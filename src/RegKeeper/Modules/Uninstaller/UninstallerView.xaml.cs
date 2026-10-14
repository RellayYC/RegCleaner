using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
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
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload()
    {
        await Ui.Busy(this, async () =>
        {
            _all = await Task.Run(() =>
            {
                var apps = Inventory.Load();
                foreach (var a in apps) a.Icon = Inventory.LoadIcon(a.DisplayIcon != "" ? a.DisplayIcon : a.UninstallString);
                return apps;
            });
            ApplyFilter();
        });
        if (_pendingFocus != null) FocusOn(_pendingFocus);
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = q == "" ? _all : _all.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                   || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Grid.ItemsSource = list;
        Status.Text = $"Программ: {list.Count}   Общий размер: {Sys.Size(list.Sum(a => a.SizeBytes))}";
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    InstalledApp? Selected => Grid.SelectedItem as InstalledApp;

    async void Uninstall_Click(object s, RoutedEventArgs e) => await Uninstall(false);
    async void Quiet_Click(object s, RoutedEventArgs e) => await Uninstall(true);

    async Task Uninstall(bool quiet)
    {
        if (Selected is not { } app) return;
        if (!Ui.Confirm($"Удалить «{app.Name}»?")) return;
        if (!CloseRunning(app)) return;

        if (app.Broken)
        {
            if (Ui.Confirm("Деинсталлятор программы не найден. Выполнить принудительное удаление (только следы)?"))
                await ScanAndClean(app);
            return;
        }

        await Ui.Busy(this, async () =>
        {
            var p = Inventory.RunUninstaller(app, quiet);
            if (p != null) await p.WaitForExitAsync();
        });
        // Inno/NSIS uninstallers re-launch themselves from %TEMP% and return at once; let the user tell us when done.
        if (Reg.Exists(app.KeyPath) &&
            MessageBox.Show("Когда деинсталлятор закончит работу, нажмите OK, чтобы найти оставшиеся следы.", "RegKeeper",
                MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
            return;
        await ScanAndClean(app);
    }

    async void Force_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        if (!Ui.Confirm($"Принудительно удалить «{app.Name}» без деинсталлятора?\nБудут найдены и удалены папки и ключи программы.")) return;
        if (!CloseRunning(app)) return;
        await ScanAndClean(app);
    }

    async Task ScanAndClean(InstalledApp app)
    {
        List<Leftover> found = new();
        await Ui.Busy(this, async () => found = await Task.Run(() => Leftovers.Find(app, _all)));
        if (found.Count == 0) Ui.Info("Следов программы не найдено.");
        else if (ChecklistWindow.Ask("Оставшиеся следы: " + app.Name,
                     "Найденные папки и ключи. Проверьте список: папки уйдут в корзину, ключи реестра можно вернуть в Центре отмены.", found))
        {
            var (done, failed) = Leftovers.Remove(found, "Удаление: " + app.Name);
            Ui.Info($"Удалено объектов: {done}" + (failed.Count > 0 ? "\nНе удалось:\n" + string.Join("\n", failed.Take(15)) : ""));
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
        var r = MessageBox.Show($"Программа запущена ({names}). Закрыть её процессы?", "RegKeeper", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes)
            foreach (var p in running)
                try { p.Kill(true); p.WaitForExit(5000); } catch { }
        return true;
    }

    async void RemoveEntry_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app || !Ui.Confirm($"Убрать «{app.Name}» из списка установленных? Сама программа останется на диске.")) return;
        using (var tx = UndoTx.Begin("Удалена запись: " + app.Name)) tx.DeleteKey(app.KeyPath);
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Selected?.InstallDir is { } d && Directory.Exists(d)) Sys.Open(d);
        else Ui.Info("Папка программы неизвестна или не существует.");
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
        var lines = new[] { "Название;Издатель;Версия;Размер, байт;Установлено;Папка;Ключ" }
            .Concat(_all.Select(a => string.Join(';', Q(a.Name), Q(a.Publisher), Q(a.Version), a.SizeBytes, a.DateText, Q(a.InstallDir ?? ""), Q(a.KeyPath))));
        File.WriteAllLines(dlg.FileName, lines, new UTF8Encoding(true));
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();

    /// <summary>Selects the program owning a file (shell menu "Удалить с RegKeeper" on a shortcut or exe).</summary>
    public void FocusOn(string file)
    {
        if (_all.Count == 0) { _pendingFocus = file; return; }
        _pendingFocus = null;
        var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) ?? file : file;
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

    static string? ResolveShortcut(string lnk)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            return (string)shell.CreateShortcut(lnk).TargetPath;
        }
        catch { return null; }
    }
}
