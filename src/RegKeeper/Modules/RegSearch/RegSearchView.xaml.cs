using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class RegSearchView : UserControl
{
    readonly ObservableCollection<SearchHit> _hits = new();
    CancellationTokenSource? _cts;
    SearchOptions? _last;

    public RegSearchView()
    {
        InitializeComponent();
        Grid.ItemsSource = _hits;
        Roots.Text = string.Join("; ", RegSearch.DefaultRoots.Select(Reg.Short));
    }

    SearchOptions Options() => new(Pattern.Text, InKeys.IsChecked == true, InNames.IsChecked == true, InData.IsChecked == true,
        MatchCase.IsChecked == true, WholeWord.IsChecked == true, IsRegex.IsChecked == true);

    void Pattern_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter && _cts == null) Search_Click(s, e); }

    /// <summary>Second click while running = cancel. Hits are buffered on the worker and flushed to the grid by a timer.</summary>
    async void Search_Click(object s, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        if (Pattern.Text == "") return;
        var o = Options();
        string[] roots;
        try { o.Build(); roots = Roots.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Reg.Normalize).ToArray(); }
        catch (Exception ex) when (ex is ArgumentException) { Ui.Info(ex.Message); return; }

        _hits.Clear();
        _last = o;
        _cts = new CancellationTokenSource();
        var search = new RegSearch();
        var buffer = new List<SearchHit>();
        void Flush()
        {
            lock (buffer) { buffer.ForEach(_hits.Add); buffer.Clear(); }
            Status.Text = $"Разделов: {search.Scanned:N0}   Найдено: {_hits.Count:N0}";
        }
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Flush(), Dispatcher);
        SearchBtn.Content = "Стоп";
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
            if (cancelled) Status.Text += "   (остановлено)";
            SearchBtn.Content = "Искать";
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
        if (!Ui.Confirm($"Заменить «{_last.Pattern}» на «{Replacement.Text}» в {hits.Count} записях? Разделы переименовываются копированием. Откат - в Центре отмены.")) return;
        Apply("Замена в реестре", tx => RegSearch.Replace(hits, _last, Replacement.Text, tx), hits);
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        var hits = Marked();
        if (hits.Count == 0 || !Ui.Confirm($"Удалить {hits.Count} найденных разделов/значений? Откат - в Центре отмены.")) return;
        Apply("Удаление найденного", tx => RegSearch.Delete(hits, tx), hits);
    }

    void Apply(string title, Action<UndoTx> work, List<SearchHit> done)
    {
        try
        {
            using var tx = UndoTx.Begin(title);
            work(tx);
            if (tx.Errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", tx.Errors.Take(20)));
        }
        catch (Exception ex) { Ui.Error(ex); }
        done.ForEach(h => _hits.Remove(h));
    }

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        foreach (var h in _hits) h.Checked = v;
        Grid.Items.Refresh();
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is SearchHit h) MainWindow.Current.Show<RegEditorView>().Navigate(h.Key);
    }
}
