using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>One category tile: what the scan checks and how many problems it found there.</summary>
public sealed record RegTile(string Name, string Glyph, int? Count)
{
    public string CountText => Count?.ToString() ?? "-";
}

public partial class RegCleanupView : UserControl
{
    List<RegIssue> _all = new();
    readonly Action _bar;

    public RegCleanupView()
    {
        InitializeComponent();
        L.Apply(this);
        // the header button counts the ticks too
        _bar = Ui.Bar(Grid, Bar, BarText, () => _all.Count(i => i.Checked), n =>
        {
            FixBtn.Content = n > 0 ? L.T($"Исправить {n}") : L.T("Исправить");
            return L.T($"Отмечено: {n}");
        });
        ApplyFilter();
    }

    bool _scanned;

    // the scanner's categories in its order
    static (string Name, string Glyph)[] Kinds => [(L.T("Деинсталляция"), ""), (L.T("Пути приложений"), ""), (L.T("Общие библиотеки"), ""),
        (L.T("Кэш имён программ"), ""), (L.T("Автозагрузка"), ""), (L.T("Папки установщика"), ""), (L.T("Открыть с помощью"), ""),
        (L.T("Правила брандмауэра"), ""), (L.T("COM-объекты"), ""), (L.T("Библиотеки типов"), "")];

    static string LastScan() =>
        DateTime.TryParse(Reg.GetString(MainWindow.Key, "LastScan"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.Date == DateTime.Today ? L.T($"Проверено сегодня в {t:HH:mm}") : L.T($"Проверено {t:d MMMM} в {t:HH:mm}")
            : L.T("Ещё не проверялось");

    // cancelled: the previous result stays as it was
    async Task Scan() => await Ui.Busy(this, async ct =>
    {
        _all = await Task.Run(() => RegScanner.Scan(ct));
        _scanned = true;
        Microsoft.Win32.Registry.SetValue(MainWindow.Key, "LastProblems", _all.Count.ToString());
        Microsoft.Win32.Registry.SetValue(MainWindow.Key, "LastScan", DateTime.Now.ToString("o"));
        MainWindow.SetBadge(typeof(RegCleanupView), _all.Count > 0 ? _all.Count.ToString() : null);
        ApplyFilter();
    });

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = q == "" ? _all : _all.Where(i => i.Category.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                   || i.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                   || i.Problem.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Grid.ItemsSource = list;
        CountText.Text = !_scanned ? "-" : list.Count != _all.Count ? $"{list.Count} / {_all.Count}" : _all.Count.ToString();
        SafeText.Text = _scanned ? L.T($"Безопасно {_all.Count(i => i.Safe)} · проверить {_all.Count(i => !i.Safe)}") : L.T("Нажмите «Сканировать»");
        Tiles.ItemsSource = Kinds.Select(k => (object)new RegTile(k.Name, k.Glyph, _scanned ? _all.Count(i => i.Category == k.Name) : null)).Append(LastScan()).ToList();
        _bar();
    }


    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    async void Scan_Click(object s, RoutedEventArgs e) => await Scan();

    async void Fix_Click(object s, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var items = _all.Where(i => i.Checked).ToList();
        if (items.Count == 0 || !Ui.Confirm(L.T($"Исправить {items.Count} записей? Резервная копия попадёт в Центр отмены."))) return;
        List<string> errors = new();
        await Ui.Busy(this, async () => errors = await Task.Run(() => RegScanner.Fix(items)));
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors.Take(20)));
        await Scan();
    }

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        _all.ForEach(i => i.Checked = v);
        Grid.Items.Refresh();
        _bar();
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
