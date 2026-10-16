using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class UninstallerView : UserControl
{
    List<InstalledApp> _all = new();
    string? _pendingFocus;

    public UninstallerView()
    {
        InitializeComponent();
        L.Apply(this);
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload()
    {
        await Ui.Busy(this, async () =>
        {
            _all = await Task.Run(() =>
            {
                var apps = Inventory.Load();
                var favs = Favorites;
                foreach (var a in apps)
                {
                    a.Icon = Inventory.LoadIcon(a.DisplayIcon != "" ? a.DisplayIcon : a.UninstallString);
                    a.Favorite = favs.Contains(a.KeyPath);
                }
                return apps;
            });
            ApplyFilter();
        });
        if (_pendingFocus != null) FocusOn(_pendingFocus);
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = _all.Where(a => q == "" || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(a => a.Favorite).ToList(); // stable: favourites first, each part keeps name order
        Grid.ItemsSource = list;
        Status.Text = L.T($"Программ: {list.Count}   Общий размер: {Sys.Size(list.Sum(a => a.SizeBytes))}");
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    InstalledApp? Selected => Grid.SelectedItem as InstalledApp;

    /// <summary>What Delete acts on: the ticked programs, else the selected one.</summary>
    List<InstalledApp> Targets => _all.Where(a => a.Checked).ToList() is { Count: > 0 } c ? c : Selected is { } s ? new() { s } : new();

    const string Key = @"HKEY_CURRENT_USER\Software\RegKeeper";

    /// <summary>Uninstall key paths the user starred, HKCU\Software\RegKeeper\Favorites.</summary>
    static HashSet<string> Favorites
    {
        get => new(Registry.GetValue(Key, "Favorites", null) as string[] ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        set => Registry.SetValue(Key, "Favorites", value.ToArray(), RegistryValueKind.MultiString);
    }

    void Grid_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (Grid.CurrentColumn is not DataGridCheckBoxColumn) Uninstall_Click(s, e);
    }

    void Grid_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        if (Selected is not { } app) { e.Handled = true; return; }
        MenuCheck.Header = app.Checked ? L.T("Снять выбор") : L.T("Выбрать");
        MenuFav.Header = app.Favorite ? L.T("Убрать из избранного") : L.T("В избранное");
        MenuFav.Tag = app.Favorite ? "\uE735" : "\uE734";
        var n = _all.Count(a => a.Checked);
        MenuUninstall.Header = n > 0 ? L.T($"Удалить отмеченные ({n})") : L.T("Удалить");
        MenuRun.IsEnabled = app.MainExe != null;
    }

    void Check_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        app.Checked = !app.Checked;
        Grid.Items.Refresh();
    }

    void Fav_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        var favs = Favorites;
        if (app.Favorite = !app.Favorite) favs.Add(app.KeyPath); else favs.Remove(app.KeyPath);
        Favorites = favs;
        ApplyFilter();
        Grid.SelectedItem = app;
        Grid.ScrollIntoView(app);
    }

    // ponytail: explorer.exe opens it as the signed-in user, not with our admin token
    void Run_Click(object s, RoutedEventArgs e)
    {
        if (Selected?.MainExe is { } exe) Process.Start("explorer.exe", $"\"{exe}\"");
    }

    void Details_Click(object s, RoutedEventArgs e)
    {
        if (Selected is { } app) new AppDetailsWindow(app).ShowDialog();
    }

    async void Uninstall_Click(object s, RoutedEventArgs e) => await Uninstall(false);
    async void Quiet_Click(object s, RoutedEventArgs e) => await Uninstall(true);

    async Task Uninstall(bool quiet)
    {
        var apps = Targets;
        if (apps.Count == 0) return;
        if (!Ui.Confirm(apps.Count == 1 ? L.T($"Удалить «{apps[0].Name}»?")
                : L.T($"Удалить отмеченные программы ({apps.Count})?") + "\n\n" + string.Join("\n", apps.Select(a => a.Name)))) return;
        foreach (var app in apps)
            if (!await UninstallOne(app, quiet)) return;
    }

    /// <summary>False when the user cancelled, which stops the rest of a batch.</summary>
    async Task<bool> UninstallOne(InstalledApp app, bool quiet)
    {
        if (!CloseRunning(app)) return false;

        if (app.Broken)
        {
            if (Ui.Confirm(L.T("Деинсталлятор программы не найден. Выполнить принудительное удаление (только следы)?")))
                await ScanAndClean(app);
            return true;
        }

        await Ui.Busy(this, async () =>
        {
            var p = Inventory.RunUninstaller(app, quiet);
            if (p != null) await p.WaitForExitAsync();
        });
        // Inno/NSIS uninstallers re-launch themselves from %TEMP% and return at once; let the user tell us when done.
        if (Reg.Exists(app.KeyPath) &&
            MessageBox.Show(L.T("Когда деинсталлятор закончит работу, нажмите OK, чтобы найти оставшиеся следы."), "RegKeeper",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return true;
        await ScanAndClean(app);
        return true;
    }

    async void Force_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        if (!Ui.Confirm(L.T($"Принудительно удалить «{app.Name}» без деинсталлятора?\nБудут найдены и удалены папки и ключи программы."))) return;
        if (!CloseRunning(app)) return;
        await ScanAndClean(app);
    }

    async Task ScanAndClean(InstalledApp app)
    {
        List<Leftover> found = new();
        await Ui.Busy(this, async () => found = await Task.Run(() => Leftovers.Find(app, _all)));
        if (found.Count == 0) Ui.Info(L.T("Следов программы не найдено."));
        else if (ChecklistWindow.Ask(L.T("Оставшиеся следы: ") + app.Name,
                     L.T("Найденные папки и ключи. Проверьте список: папки уйдут в корзину, ключи реестра можно вернуть в Центре отмены."), found))
        {
            var (done, failed) = Leftovers.Remove(found, L.T("Удаление: ") + app.Name);
            Ui.Info(L.T($"Удалено объектов: {done}") + (failed.Count > 0 ? L.T("\nНе удалось:\n") + string.Join("\n", failed.Take(15)) : ""));
        }
        await Reload();
    }

    /// <summary>Offers to kill processes running from the app folder (an open app blocks file deletion).</summary>
    static bool CloseRunning(InstalledApp app)
    {
        if (app.InstallDir is not { } dir) return true;
        var running = Process.GetProcesses().Where(p =>
        {
            try { return p.MainModule?.FileName is { } f && Leftovers.IsSameOrInside(f, dir); }
            catch { return false; }
        }).ToList();
        if (running.Count == 0) return true;
        var names = string.Join(", ", running.Select(p => p.ProcessName).Distinct());
        var r = MessageBox.Show(L.T($"Программа запущена ({names}). Закрыть её процессы?"), "RegKeeper", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            foreach (var p in running)
                try { p.Kill(true); p.WaitForExit(5000); } catch { }
        return true;
    }

    async void RemoveEntry_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app || !Ui.Confirm(L.T($"Убрать «{app.Name}» из списка установленных? Сама программа останется на диске."))) return;
        using (var tx = UndoTx.Begin(L.T("Удалена запись: ") + app.Name)) tx.DeleteKey(app.KeyPath);
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Selected?.InstallDir is { } d && Directory.Exists(d)) Sys.Open(d);
        else Ui.Info(L.T("Папка программы неизвестна или не существует."));
    }

    void Key_Click(object s, RoutedEventArgs e)
    {
        if (Selected is { } app) MainWindow.Current.Show<RegEditorView>().Navigate(app.KeyPath);
    }

    void Export_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "programs.csv" };
        if (dlg.ShowDialog() != true) return;
        static string Q(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var lines = new[] { L.T("Название;Издатель;Версия;Размер, байт;Установлено;Папка;Ключ") }
            .Concat(_all.Select(a => string.Join(';', Q(a.Name), Q(a.Publisher), Q(a.Version), a.SizeBytes, a.DateText, Q(a.InstallDir ?? ""), Q(a.KeyPath))));
        File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true));
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();

    /// <summary>Selects the program owning a file (shell menu "Удалить с RegKeeper" on a shortcut or exe).</summary>
    public void FocusOn(string file)
    {
        if (_all.Count == 0) { _pendingFocus = file; return; }
        _pendingFocus = null;
        var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Sys.ExtractPath(Sys.ShortcutTarget(file)) ?? file : file;
        var app = _all.FirstOrDefault(a => a.InstallDir is { } d && Leftovers.IsSameOrInside(target, d));
        if (app == null)
        {
            Search.Text = Path.GetFileNameWithoutExtension(file);
            return;
        }
        Search.Text = "";
        Grid.SelectedItem = app;
        Grid.ScrollIntoView(app);
    }
}
