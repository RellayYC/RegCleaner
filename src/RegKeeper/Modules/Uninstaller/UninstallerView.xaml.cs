using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class UninstallerView : UserControl
{
    List<InstalledApp> _all = new();
    string? _pendingFocus;
    readonly Action _bar;

    public UninstallerView()
    {
        InitializeComponent();
        L.Apply(this);
        _bar = Ui.Bar(Grid, Bar, BarText, () => Targets.Count, n => L.T($"Выбрано: {n}") + " · " + Sys.Size(Targets.Sum(a => a.SizeBytes)));
        RoutedEventHandler redraw = (_, _) => Dispatcher.BeginInvoke(DrawMap);
        Grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, redraw, true);
        Grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, redraw, true);
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
                var runs = LastRuns();
                foreach (var a in apps)
                {
                    a.Icon = Inventory.LoadIcon(a.DisplayIcon != "" ? a.DisplayIcon : a.UninstallString);
                    a.Favorite = favs.Contains(a.KeyPath);
                    if (a.MainExe is { } exe && runs.TryGetValue(Path.GetFileName(exe), out var t)) a.LastRun = t;
                }
                return apps;
            });
            ApplyFilter();
        });
        if (_pendingFocus != null) FocusOn(_pendingFocus);
    }

    /// <summary>Last run per exe file name: the newest Prefetch file "NAME.EXE-HASH.pf" (needs admin; empty without it).</summary>
    static Dictionary<string, DateTime> LastRuns()
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")).EnumerateFiles("*.pf"))
                if (f.Name.LastIndexOf('-') is > 0 and var dash && f.Name[..dash] is var exe
                    && (!map.TryGetValue(exe, out var t) || t < f.LastWriteTime)) map[exe] = f.LastWriteTime;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { }
        return map;
    }

    string _chip = "all";

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var mine = "HKEY_CURRENT_USER";
        var list = _all.Where(a => q == "" || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))
                       .Where(a => _chip switch
                       {
                           "big" => a.SizeBytes >= 1L << 30,
                           "new" => a.InstallDate >= DateTime.Today.AddDays(-30),
                           "mine" => a.KeyPath.StartsWith(mine, StringComparison.OrdinalIgnoreCase),
                           _ => true,
                       })
                       .OrderByDescending(a => a.Favorite).ToList(); // stable: favourites first, each part keeps name order
        Grid.ItemsSource = list;
        CountText.Text = L.T($"{list.Count} программ · {Sys.Size(list.Sum(a => a.SizeBytes))}");
        DrawMap();
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    void Chip_Click(object s, RoutedEventArgs e)
    {
        var chip = (CheckBox)s;
        foreach (var c in Chips.Children.OfType<CheckBox>()) c.IsChecked = c == chip;
        _chip = (string)chip.Tag;
        ApplyFilter();
    }

    void MapMode_Checked(object s, RoutedEventArgs e) { if (Map != null) DrawMap(); }
    void Map_SizeChanged(object s, SizeChangedEventArgs e) => DrawMap();

    /// <summary>Treemap of the listed programs by size (or only the ones idle 90+ days); the rest merge into one block.
    /// Clicking a block ticks its program, the same tick as the row.</summary>
    void DrawMap()
    {
        Map.Children.Clear();
        if (Map.ActualWidth < 10 || Grid.ItemsSource is not IEnumerable<InstalledApp> listed) return;
        var idle = DateTime.Today.AddDays(-90);
        var apps = listed.Where(a => a.SizeBytes > 0 && (BySize.IsChecked == true || a.LastRun < idle)).OrderByDescending(a => a.SizeBytes).ToList();
        if (apps.Count == 0)
        {
            Map.Children.Add(new TextBlock { Text = L.T("Нет программ с известным размером"), Style = (Style)FindResource("Caption") });
            return;
        }
        var top = apps.Take(14).Select(a => (App: (InstalledApp?)a, Size: a.SizeBytes)).ToList();
        if (apps.Skip(14).Sum(a => a.SizeBytes) is > 0 and var rest) top.Add((null, rest));
        Split(top, new Rect(0, 0, Map.ActualWidth, Map.ActualHeight));
    }

    // ponytail: halves by size along the longer side, not squarified; fine for ~15 blocks
    void Split(List<(InstalledApp? App, long Size)> items, Rect r)
    {
        if (items.Count == 1) { Block(items[0].App, items[0].Size, r); return; }
        var total = (double)items.Sum(i => i.Size);
        int cut = 1; double left = items[0].Size;
        while (cut < items.Count - 1 && left + items[cut].Size <= total / 2) left += items[cut++].Size;
        var share = left / total;
        var (a, b) = r.Width >= r.Height
            ? (new Rect(r.X, r.Y, r.Width * share, r.Height), new Rect(r.X + r.Width * share, r.Y, r.Width * (1 - share), r.Height))
            : (new Rect(r.X, r.Y, r.Width, r.Height * share), new Rect(r.X, r.Y + r.Height * share, r.Width, r.Height * (1 - share)));
        Split(items.GetRange(0, cut), a);
        Split(items.GetRange(cut, items.Count - cut), b);
    }

    void Block(InstalledApp? app, long size, Rect r)
    {
        var on = app?.Checked == true;
        var big = r.Width * r.Height > 9000;
        var text = new StackPanel { Margin = new Thickness(10, 8, 26, 0) };
        if (r.Width > 70 && r.Height > 36)
        {
            text.Children.Add(new TextBlock { Text = app?.Name ?? L.T("Остальные"), FontSize = 12.5, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            if (r.Height > 52) text.Children.Add(new TextBlock { Text = Sys.Size(size), FontSize = 12, Opacity = 0.65 });
        }
        var cell = new Grid();
        cell.Children.Add(text);
        if (on)
            cell.Children.Add(new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 7, 7, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)FindResource("OnPrimaryBrush"),
                Child = new TextBlock { Text = "\uE73E", FontFamily = (FontFamily)FindResource("Icons"), FontSize = 10, Foreground = (Brush)FindResource("PrimaryBrush"),
                                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        var b = new Border
        {
            Width = Math.Max(r.Width - 4, 0), Height = Math.Max(r.Height - 4, 0), CornerRadius = new CornerRadius(8), Child = cell,
            Background = (Brush)FindResource(on ? "PrimaryBrush" : big ? "Surface3Brush" : "Surface2Brush"),
            ToolTip = (app?.Name ?? L.T("Остальные")) + " · " + Sys.Size(size),
            Cursor = app != null ? Cursors.Hand : null,
        };
        System.Windows.Documents.TextElement.SetForeground(b, (Brush)FindResource(on ? "OnPrimaryBrush" : "TextBrush"));
        if (app != null)
            b.MouseLeftButtonUp += (_, _) =>
            {
                app.Checked = !app.Checked;
                Grid.Items.Refresh();
                _bar();
                DrawMap();
            };
        Canvas.SetLeft(b, r.X + 2);
        Canvas.SetTop(b, r.Y + 2);
        Map.Children.Add(b);
    }

    void RowMore_Click(object s, RoutedEventArgs e)
    {
        Grid.SelectedItem = ((FrameworkElement)s).DataContext;
        if (!PrepareMenu()) return;
        Grid.ContextMenu.PlacementTarget = (UIElement)s;
        Grid.ContextMenu.IsOpen = true;
    }

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
        if (!PrepareMenu()) e.Handled = true;
    }

    bool PrepareMenu()
    {
        if (Selected is not { } app) return false;
        MenuCheck.Header = app.Checked ? L.T("Снять выбор") : L.T("Выбрать");
        MenuFav.Header = app.Favorite ? L.T("Убрать из избранного") : L.T("В избранное");
        MenuFav.Tag = app.Favorite ? "\uE735" : "\uE734";
        var n = _all.Count(a => a.Checked);
        MenuUninstall.Header = n > 0 ? L.T($"Удалить отмеченные ({n})") : L.T("Удалить");
        MenuRun.IsEnabled = app.MainExe != null;
        return true;
    }

    void Check_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        app.Checked = !app.Checked;
        Grid.Items.Refresh();
        _bar();
        DrawMap();
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
            Dialog.Show(L.T("Когда деинсталлятор закончит работу, нажмите OK, чтобы найти оставшиеся следы."), [L.T("Отмена"), "OK"], 1) != 1)
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
        // cancelled or failed: nothing offered, the list still reloads (the uninstaller may have run)
        var ok = await Ui.Busy(this, async ct => found = await Task.Run(() => Leftovers.Find(app, _all, ct)));
        if (ok && found.Count == 0) Ui.Info(L.T("Следов программы не найдено."));
        else if (ok && ChecklistWindow.Ask(L.T("Оставшиеся следы: ") + app.Name,
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
        var r = Dialog.Show(L.T($"Программа запущена ({names}). Закрыть её процессы?"), [L.T("Отмена"), L.T("Нет"), L.T("Да")], 2);
        if (r <= 0) return false;
        if (r == 2)
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
