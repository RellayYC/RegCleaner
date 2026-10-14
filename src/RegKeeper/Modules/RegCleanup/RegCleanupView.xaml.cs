using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class RegCleanupView : UserControl
{
    List<RegIssue> _all = new();

    public RegCleanupView() => InitializeComponent();

    async Task Scan() => await Ui.Busy(this, async () =>
    {
        _all = await Task.Run(RegScanner.Scan);
        ApplyFilter();
    });

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = q == "" ? _all : _all.Where(i => i.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                   || i.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                   || i.Problem.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Grid.ItemsSource = list;
        Status.Text = $"Проблем: {_all.Count}" + (list.Count != _all.Count ? $"   Показано: {list.Count}" : "");
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    async void Scan_Click(object s, RoutedEventArgs e) => await Scan();

    async void Fix_Click(object s, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var items = _all.Where(i => i.Checked).ToList();
        if (items.Count == 0 || !Ui.Confirm($"Исправить {items.Count} записей? Резервная копия попадёт в Центр отмены.")) return;
        List<string> errors = new();
        await Ui.Busy(this, async () => errors = await Task.Run(() => RegScanner.Fix(items)));
        if (errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", errors.Take(20)));
        await Scan();
    }

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        _all.ForEach(i => i.Checked = v);
        Grid.Items.Refresh();
    }

    void Ignore_Click(object s, RoutedEventArgs e)
    {
        var items = Grid.SelectedItems.Cast<RegIssue>().ToList();
        if (items.Count == 0) return;
        RegScanner.AddIgnore(items);
        _all = _all.Except(items).ToList();
        ApplyFilter();
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is RegIssue i) MainWindow.Current.Show<RegEditorView>().Navigate(i.Key);
    }

    void IgnoreList_Click(object s, RoutedEventArgs e)
    {
        if (!File.Exists(RegScanner.IgnorePath)) File.WriteAllText(RegScanner.IgnorePath, "");
        Sys.Open(RegScanner.IgnorePath);
    }
}
