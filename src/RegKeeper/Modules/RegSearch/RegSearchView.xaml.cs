using System.Windows;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class RegSearchView : UserControl
{
    readonly HitList _hits = new();
    readonly Action _bar;
    CancellationTokenSource? _cts;
    SearchOptions? _last;
    Regex? _rx;

    public RegSearchView()
    {
        InitializeComponent();
        L.Apply(this);
        _bar = Ui.Bar(Grid, Bar, Bar, () => _hits.Count(h => h.Checked), n => L.T($"Отмечено: {n}"));
        Grid.ItemsSource = _hits;
        CollectionViewSource.GetDefaultView(_hits).GroupDescriptions.Add(new PropertyGroupDescription(nameof(SearchHit.ShortKey)));
        // ponytail: grouped rows leave a star column at its minimum, so the match column takes the rest by hand
        Grid.SizeChanged += (_, _) => MatchCol.Width = Math.Max(300, Grid.ActualWidth - Grid.Columns.Where(c => c != MatchCol).Sum(c => c.ActualWidth) - 24);
        var at = 0;
        foreach (var r in Reg.RootNames)
            Hives.Children.Insert(at++, new CheckBox { Content = Reg.Short(r), Tag = r, Style = (Style)FindResource("Chip"),
                Margin = new Thickness(0, 0, 6, 6), IsChecked = RegSearch.DefaultRoots.Contains(r) });
        Roots.Text = string.Join("; ", RegSearch.DefaultRoots.Select(Reg.Short));
    }

    SearchOptions Options() => new(Pattern.Text, InKeys.IsChecked == true, InNames.IsChecked == true, InData.IsChecked == true,
        MatchCase.IsChecked == true, WholeWord.IsChecked == true, IsRegex.IsChecked == true);

    IEnumerable<CheckBox> HiveChips => Hives.Children.OfType<CheckBox>().Where(c => c.Tag is string);

    /// <summary>"Own path" swaps the hive chips for a free list of keys.</summary>
    void Custom_Click(object s, RoutedEventArgs e)
    {
        var on = Custom.IsChecked == true;
        Roots.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        foreach (var c in HiveChips) c.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) Roots.Focus();
    }

    /// <summary>The match in White on Ink; a long text starts a little before its first match so the hit stays in view.</summary>
    void Match_DataContextChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        var t = (TextBlock)s;
        t.Inlines.Clear();
        if (e.NewValue is not SearchHit h || _rx == null) return;
        var ms = _rx.Matches(h.Text).Take(20).Where(m => m.Length > 0).ToList();
        var pos = ms.Count > 0 && ms[0].Index > 40 ? ms[0].Index - 20 : 0;
        if (pos > 0) t.Inlines.Add("…");
        void Plain(int end) { if (end > pos) t.Inlines.Add(h.Text[pos..end].Replace('\n', ' ')); }
        foreach (var m in ms)
        {
            if (m.Index < pos) continue;
            Plain(m.Index);
            var r = new Run(m.Value.Replace('\n', ' '));
            r.SetResourceReference(TextElement.BackgroundProperty, "PrimaryBrush");
            r.SetResourceReference(TextElement.ForegroundProperty, "OnPrimaryBrush");
            t.Inlines.Add(r);
            pos = m.Index + m.Length;
        }
        Plain(h.Text.Length);
    }

    void Pattern_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter && _cts == null) Search_Click(s, e); }

    /// <summary>Second click while running = cancel. Hits are buffered on the worker and flushed to the grid by a timer.</summary>
    internal async void Search_Click(object s, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        if (Pattern.Text == "") return;
        var o = Options();
        string[] roots;
        try { o.Build(); roots = (Custom.IsChecked == true
                ? Roots.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : HiveChips.Where(c => c.IsChecked == true).Select(c => (string)c.Tag)).Select(Reg.Normalize).ToArray(); }
        catch (Exception ex) when (ex is ArgumentException) { Ui.Info((ex is System.Text.RegularExpressions.RegexParseException ? L.T("Неверное регулярное выражение:\n") : "") + ex.Message); return; }

        _hits.Clear();
        _last = o;
        _rx = o.Build();
        _cts = new CancellationTokenSource();
        var search = new RegSearch();
        var buffer = new List<SearchHit>();
        void Flush()
        {
            List<SearchHit> batch;
            lock (buffer) { batch = buffer.ToList(); buffer.Clear(); } // the grid takes them outside the lock, the walk never waits on it
            batch.ForEach(_hits.Add);
            ScannedText.Text = search.Scanned.ToString("N0");
            FoundText.Text = _hits.Count.ToString("N0");
            _bar();
        }
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Flush(), Dispatcher);
        SearchBtn.Content = L.T("Стоп");
        var token = _cts.Token;
        var cancelled = false;
        try
        {
            await Task.Run(() =>
            {
                foreach (var hit in search.Run(roots, o, token))
                    lock (buffer) buffer.Add(hit);
            });
        }
        catch (OperationCanceledException) { cancelled = true; }
        finally
        {
            timer.Stop();
            Flush();
            Stopped.Visibility = cancelled ? Visibility.Visible : Visibility.Collapsed;
            SearchBtn.Content = L.T("Искать");
            _cts.Dispose();
            _cts = null;
        }
    }

    List<SearchHit> Marked()
    {
        Grid.CommitEdit();
        return _hits.Where(h => h.Checked).ToList();
    }

    void Replace_Click(object s, RoutedEventArgs e)
    {
        var hits = Marked();
        if (_last == null || hits.Count == 0) return;
        if (!Ui.Confirm(L.T($"Заменить «{_last.Pattern}» на «{Replacement.Text}» в {hits.Count} записях? Разделы переименовываются копированием. Откат - в Центре отмены."))) return;
        Apply(L.T("Замена в реестре"), tx => RegSearch.Replace(hits, _last, Replacement.Text, tx), hits);
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        var hits = Marked();
        if (hits.Count == 0 || !Ui.Confirm(L.T($"Удалить {hits.Count} найденных разделов/значений? Откат - в Центре отмены."))) return;
        Apply(L.T("Удаление найденного"), tx => RegSearch.Delete(hits, tx), hits);
    }

    void Apply(string title, Action<UndoTx> work, List<SearchHit> done)
    {
        try
        {
            using var tx = UndoTx.Begin(title);
            work(tx);
            if (tx.Errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", tx.Errors.Take(20)));
        }
        catch (Exception ex) { Ui.Error(ex); }
        _hits.RemoveAll(done);
        _bar();
    }

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        foreach (var h in _hits) h.Checked = v;
        Grid.Items.Refresh();
        _bar();
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is SearchHit h) MainWindow.Current.Show<RegEditorView>().Navigate(h.Key);
    }
}
