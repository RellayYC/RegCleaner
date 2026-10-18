using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>One group of cleanup rules: a tri-state tick over its rules, folded unless opened.</summary>
public sealed class CleanGroup : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "";
    public List<CleanRule> Items { get; init; } = new();
    /// <summary>The two columns of rules; fresh lists on every read, so a raised change re-creates the rows.</summary>
    public List<CleanRule> Left => Items.Take((Items.Count + 1) / 2).ToList();
    public List<CleanRule> Right => Items.Skip((Items.Count + 1) / 2).ToList();
    bool _open;
    public bool Open { get => _open; set { _open = value; Raise(nameof(Open)); } }
    public bool? State
    {
        get => Items.All(r => r.Checked) ? true : Items.Any(r => r.Checked) ? null : false;
        set { foreach (var r in Items) r.Checked = value == true; Raise(); }
    }
    long Found => Items.Where(r => r.Analyzed && r.Checked).Sum(r => r.Size);
    public string SizeText => Items.Any(r => r.Analyzed) ? Sys.Size(Found) : "-";
    public string Summary => L.T($"Правил: {Items.Count} · отмечено {Items.Count(r => r.Checked)}");
    public void Raise(params string[] names) { foreach (var n in names.Append(nameof(State)).Append(nameof(SizeText)).Append(nameof(Summary))) PropertyChanged?.Invoke(this, new(n)); }
}

public partial class DiskCleanupView : UserControl
{
    List<CleanRule> _all = new();
    List<CleanRule> _visible = new();
    List<CleanGroup> _groups = new();
    readonly HashSet<string> _open = new() { L.T("Windows") };
    readonly Action _bar;

    public DiskCleanupView()
    {
        InitializeComponent();
        L.Apply(this);
        _bar = Ui.Bar(Groups, Bar, BarText, () => _all.Count(r => r.Checked),
                      n => L.T($"Отмечено: {n}") + (Found() is { } f ? " · " + Sys.Size(f) : ""));
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload() => await Ui.Busy(this, async () =>
    {
        _all = await Task.Run(Winapp2.Load);
        ApplyFilter();
        Status.Visibility = File.Exists(Winapp2.IniPath) ? Visibility.Collapsed : Visibility.Visible;
    });

    // ponytail: four fixed groups by category name; Parse folds "<name> Web Browser" sections into Browsers
    static (string Name, string Glyph)[] Kinds => [(L.T("Windows"), ""), (L.T("Браузеры"), ""), (L.T("Приложения"), ""), (L.T("Игры"), "")];
    static string Kind(CleanRule r) =>
        r.Category == L.T("Windows") || r.ComponentStore || r.RecycleBin ? Kinds[0].Name
        : r.Category == L.T("Браузеры") || r.Category == L.T("Интернет") || r.Category is "Firefox" or "Google Chrome" or "Microsoft Edge" ? Kinds[1].Name
        : r.Category == L.T("Игры") ? Kinds[3].Name
        : Kinds[2].Name;

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        _visible = q == "" ? _all : _all.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                    || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _groups = Kinds.Select(k => new CleanGroup { Name = k.Name, Glyph = k.Glyph, Items = _visible.Where(r => Kind(r) == k.Name).ToList(), Open = q != "" || _open.Contains(k.Name) })
                       .Where(g => g.Items.Count > 0).ToList();
        Groups.ItemsSource = _groups;
        UpdateStatus();
    }

    long? Found() => _all.Any(r => r.Analyzed) ? _all.Where(r => r.Analyzed && r.Checked).Sum(r => r.Size) : null;

    void UpdateStatus()
    {
        var free = Found();
        FreeText.Text = free is { } f ? Sys.Size(f) : "-";
        FreeHint.Text = free == null ? L.T("Размер появится после анализа") : L.T($"По отмеченным правилам: {_all.Count(r => r.Checked)}");
        CleanBtn.Content = free is > 0 ? L.T($"Очистить {Sys.Size(free.Value)}") : L.T("Очистить");
        Tiles.ItemsSource = Kinds.Select(k => new CleanGroup { Name = k.Name, Glyph = k.Glyph, Items = _all.Where(r => Kind(r) == k.Name).ToList() }).ToList();
        _bar?.Invoke();
        if (free == null) return;
        // the sidebar counter and the Windows Apps "can be freed" card read this
        Microsoft.Win32.Registry.SetValue(MainWindow.Key, "LastFreeable", free.Value.ToString());
        MainWindow.SetBadge(typeof(DiskCleanupView), free > 0 ? Sys.Size(free.Value) : null);
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    void Group_Click(object s, RoutedEventArgs e) => UpdateStatus(); // the State binding has already ticked the rules

    void Rule_Click(object s, RoutedEventArgs e)
    {
        var r = (CleanRule)((FrameworkElement)s).DataContext;
        _groups.FirstOrDefault(g => g.Items.Contains(r))?.Raise();
        UpdateStatus();
    }

    void Fold_Click(object s, RoutedEventArgs e)
    {
        var g = (CleanGroup)((FrameworkElement)s).DataContext;
        g.Open = !g.Open;
        if (!_open.Remove(g.Name) && g.Open) _open.Add(g.Name);
    }

    /// <summary>Group sums and the hero after sizes or ticks changed; the rows follow their rules themselves.</summary>
    void Refresh()
    {
        foreach (var g in _groups) g.Raise();
        UpdateStatus();
    }

    /// <summary>
    /// Only the action buttons wait (Actions disabled): the list, the search and the other tabs stay usable.
    /// Each rule shows its size as soon as its own walk ends.
    /// </summary>
    async Task Analyze(List<CleanRule> rules)
    {
        Winapp2.SaveChecked(_all);
        foreach (var r in rules) r.Scanning = true;
        // rows update themselves; the sums and the hero redraw once per idle moment, not once per rule
        var pending = 0;
        void Done()
        {
            if (Interlocked.Exchange(ref pending, 1) == 0)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => { pending = 0; Refresh(); });
        }
        // Esc stops starting new rules; rules already being walked finish, the rest stay unanalyzed.
        // DISM first (minutes), and no chunking: one big folder must not hold back the rules queued behind it.
        await Ui.Busy(Actions, ct => Task.Run(() =>
        {
            var ignore = Winapp2.LoadIgnore();
            var queue = System.Collections.Concurrent.Partitioner.Create(rules.OrderByDescending(r => r.ComponentStore), System.Collections.Concurrent.EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(queue, new ParallelOptions { CancellationToken = ct }, r =>
            {
                Winapp2.Analyze(r, ignore);
                Done();
            });
        }));
        foreach (var r in rules) r.Scanning = false;
        Refresh();
    }

    async void Analyze_Click(object s, RoutedEventArgs e) { if (Actions.IsEnabled) await Analyze(_all.Where(r => r.Checked).ToList()); }
    // DISM takes minutes and would hold the buttons: it joins "all" only when ticked
    async void AnalyzeAll_Click(object s, RoutedEventArgs e) { if (Actions.IsEnabled) await Analyze(_all.Where(r => !r.ComponentStore || r.Checked).ToList()); }

    async void Clean_Click(object s, RoutedEventArgs e)
    {
        if (!Actions.IsEnabled) return; // the dock's button while a run goes
        if (_all.Where(r => r.Checked && !r.Analyzed).ToList() is { Count: > 0 } fresh) await Analyze(fresh);
        var rules = _all.Where(r => r.Checked && r.Analyzed && r.Count > 0).ToList();
        if (rules.Count == 0) { Ui.Info(L.T("Нечего удалять.")); return; }
        var warn = rules.Where(r => r.Warning != null).Select(r => $"• {r.Name}: {r.Warning}").ToList();
        if (!Ui.Confirm(L.T($"Удалить {rules.Sum(r => r.Count)} объектов ({Sys.Size(rules.Sum(r => r.Size))}) по {rules.Count} правилам?\n") +
                        L.T("Файлы удаляются без корзины.") + (warn.Count > 0 ? L.T("\n\nВнимание:\n") + string.Join("\n", warn) : ""))) return;

        (long Freed, int Failed) res = default;
        await Ui.Busy(Actions, () => Task.Run(() =>
        {
            using var tx = UndoTx.Begin(L.T("Очистка диска: ") + string.Join(", ", rules.Select(r => r.Name).Take(10)));
            res = Winapp2.Clean(rules, tx);
        }));
        Ui.Info(L.T($"Освобождено: {Sys.Size(res.Freed)}") + (res.Failed > 0 ? L.T($"\nПропущено (заняты или нет доступа): {res.Failed}") : ""));
        Refresh();
    }

    void Details_Click(object s, RoutedEventArgs e)
    {
        if (((FrameworkElement)s).DataContext is not CleanRule { Analyzed: true } r) return; // "…": still being walked
        if (r.Found.Count == 0) { Ui.Info(L.T("Ничего не найдено.")); return; }
        ChecklistWindow.Ask(r.Name, L.T("Снимите галочки с того, что нужно оставить. Папки удаляются, только если опустели."), r.Found, L.T("Готово"));
        r.Size = r.Found.Where(l => l.Checked).Sum(l => l.Size);
        Refresh();
    }

    void All_Click(object s, RoutedEventArgs e) => SetChecked(_visible, true);
    void None_Click(object s, RoutedEventArgs e) => SetChecked(_all, false);

    void SetChecked(List<CleanRule> rules, bool v)
    {
        rules.ForEach(r => r.Checked = v);
        Refresh();
    }

    async void Download_Click(object s, RoutedEventArgs e)
    {
        var count = 0;
        await Ui.Busy(this, async () => count = await Winapp2.Download());
        if (count == 0) return;
        Ui.Info(L.T($"winapp2.ini обновлён, правил в базе: {count}."));
        await Reload();
    }

    void Ignore_Click(object s, RoutedEventArgs e)
    {
        if (!File.Exists(Winapp2.IgnorePath))
            File.WriteAllText(Winapp2.IgnorePath, L.T("; Пути (папки или файлы), которые очистка никогда не трогает. По одному на строку.\r\n"));
        Sys.Open(Winapp2.IgnorePath);
    }
}
