using System.Diagnostics.Eventing.Reader;
using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class StartupView : UserControl
{
    List<StartupItem> _all = new();
    double? _boot;
    StartupKind? _kind;

    public StartupView()
    {
        InitializeComponent();
        L.Apply(this);
        Ui.Bar(Grid, Bar, BarText, () => Grid.SelectedItems.Count);
        AxisEnd.Text = L.T("40 с"); // column headers sit outside the tree L.Apply walks
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload() => await Ui.Busy(this, async () =>
    {
        (_all, _boot) = await Task.Run(() =>
        {
            var items = Autoruns.Load();
            var (boot, apps) = BootTimes();
            foreach (var i in items)
                if (Path.GetFileName(Sys.ExtractPath(i.Command) ?? "") is { Length: > 0 } exe && apps.TryGetValue(exe, out var sec)) i.Seconds = sec;
            return (items, boot);
        });
        ApplyFilter();
    });

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = _all.Where(i => (_kind == null || i.Kind == _kind)
                                   && (q == "" || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                               || i.Command.Contains(q, StringComparison.OrdinalIgnoreCase)
                                               || i.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = list;
        int on = list.Count(i => i.Enabled), missing = list.Count(i => i.Missing);
        CountText.Text = L.T($"{list.Count} записей · включено {on} · битых {missing}");
        BootText.Text = _boot is { } b ? L.T($"{b:0} с") : "-";
        BootHint.Visibility = _boot == null ? Visibility.Visible : Visibility.Collapsed;
        var saved = _all.Where(i => !i.Enabled).Sum(i => i.Seconds ?? 0);
        OffText.Text = saved > 0 ? L.T($"Отключено вами −{saved:0} с") : L.T($"Отключено: {_all.Count(i => !i.Enabled)}");
    }

    /// <summary>Windows' own boot measurements: event 100 is the last boot to a ready desktop, event 101 a program that
    /// slowed it (its total start time). Reading the log needs admin; without it both come back empty.</summary>
    static (double? Boot, Dictionary<string, double> Apps) BootTimes()
    {
        var apps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double? boot = null;
        try
        {
            var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID=100 or EventID=101)]]") { ReverseDirection = true };
            using var r = new EventLogReader(q);
            var at100 = new EventLogPropertySelector(new[] { "Event/EventData/Data[@Name='BootTime']" });
            var at101 = new EventLogPropertySelector(new[] { "Event/EventData/Data[@Name='FileName']", "Event/EventData/Data[@Name='TotalTime']" });
            for (int n = 0; n < 500 && r.ReadEvent() is EventLogRecord e; n++)
                using (e)
                    if (e.Id == 100) boot ??= Convert.ToDouble(e.GetPropertyValues(at100)[0]) / 1000;
                    else if (e.GetPropertyValues(at101) is [string file, var ms]) apps.TryAdd(file, Convert.ToDouble(ms) / 1000); // newest first
        }
        catch (Exception e) when (e is EventLogException or UnauthorizedAccessException or InvalidCastException or FormatException) { }
        return (boot, apps);
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();
    // Checked, not Click: UI Automation selection does not raise Click; IsChecked="True" fires it while the XAML is parsed
    void Kind_Checked(object s, RoutedEventArgs e)
    {
        _kind = Enum.TryParse<StartupKind>((string)((FrameworkElement)s).Tag, out var k) ? k : null;
        if (IsInitialized) ApplyFilter();
    }

    List<StartupItem> Selected => Grid.SelectedItems.Cast<StartupItem>().ToList();

    /// <summary>The switch in a row toggles that row alone.</summary>
    void Switch_Click(object s, RoutedEventArgs e)
    {
        Grid.SelectedItem = ((FrameworkElement)s).DataContext;
        Toggle_Click(s, e);
    }

    void Toggle_Click(object s, RoutedEventArgs e)
    {
        var items = Selected.Where(i => i.CanToggle).ToList();
        if (items.Count == 0) { if (Selected.Count > 0) Ui.Info(L.T("Однократный запуск (RunOnce) нельзя отключить, только удалить.")); return; }
        var on = !items[0].Enabled;
        var errors = new List<string>();
        using (var tx = UndoTx.Begin((on ? L.T("Включено в автозагрузке: ") : L.T("Отключено в автозагрузке: ")) + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.SetEnabled(i, on, tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        Grid.Items.Refresh();
        ApplyFilter();
    }

    async void Delay_Click(object s, RoutedEventArgs e)
    {
        var items = Selected.Where(i => i.Kind is StartupKind.Registry or StartupKind.Folder && i.Approved != null && i.Enabled).ToList();
        if (items.Count == 0) { Ui.Info(L.T("Выберите включённые записи реестра или файлы папки автозагрузки.")); return; }
        if (Ui.Prompt(L.T("Отложенный запуск"), L.T("Задержка после входа в Windows, секунд:"), "60") is not { } text) return;
        if (!int.TryParse(text, out var sec) || sec is < 1 or > 3600) { Ui.Info(L.T("Нужно число от 1 до 3600.")); return; }
        var errors = new List<string>();
        using (var tx = UndoTx.Begin(L.T($"Отложено на {sec} с: ") + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.Delay(i, TimeSpan.FromSeconds(sec), tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        await Reload();
    }

    async void Delete_Click(object s, RoutedEventArgs e)
    {
        var items = Selected;
        if (items.Count == 0 || !Ui.Confirm(L.T("Удалить из автозагрузки:\n") + string.Join("\n", items.Select(i => i.Name)) +
                                            L.T("\n\nЗаписи реестра и задачи можно вернуть в Центре отмены, файлы уйдут в корзину."))) return;
        var errors = new List<string>();
        using (var tx = UndoTx.Begin(L.T("Удалено из автозагрузки: ") + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.Delete(i, tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        await Reload();
    }

    async void Add_Click(object s, RoutedEventArgs e)
    {
        if (Ui.Prompt(L.T("Добавить в автозагрузку"), L.T("Команда (путь к программе и аргументы):")) is not { Length: > 0 } cmd) return;
        if (Ui.Prompt(L.T("Добавить в автозагрузку"), L.T("Название записи:"), Path.GetFileNameWithoutExtension(Sys.ExtractPath(cmd) ?? cmd)) is not { Length: > 0 } name) return;
        var allUsers = Ui.Confirm(L.T("Запускать для всех пользователей? (Нет - только для текущего)"));
        using (var tx = UndoTx.Begin(L.T("Добавлено в автозагрузку: ") + name)) Autoruns.Add(name, cmd, allUsers, tx);
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not StartupItem i) return;
        var path = i.Kind == StartupKind.Folder ? i.Location : Sys.ExtractPath(i.Command);
        if (path != null && File.Exists(path)) Sys.ShowInExplorer(path);
        else Ui.Info(L.T("Файл не найден: ") + path);
    }

    void Goto_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not StartupItem i) return;
        switch (i.Kind)
        {
            case StartupKind.Registry or StartupKind.Service: MainWindow.Current.Show<RegEditorView>().Navigate(i.Location); break;
            case StartupKind.Folder: Sys.ShowInExplorer(i.Location); break;
            case StartupKind.Task: Sys.Open("taskschd.msc"); break;
        }
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();
}
