using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class DiskCleanupView : UserControl
{
    List<CleanRule> _all = new();
    List<CleanRule> _visible = new();

    public DiskCleanupView()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload() => await Ui.Busy(this, async () =>
    {
        _all = await Task.Run(Winapp2.Load);
        ApplyFilter();
        if (!File.Exists(Winapp2.IniPath)) Status.Text += "   База winapp2.ini не загружена";
    });

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        _visible = q == "" ? _all : _all.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                                    || r.Category.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Grid.ItemsSource = _visible;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        var done = _all.Where(r => r.Analyzed && r.Checked).ToList();
        Status.Text = $"Правил: {_visible.Count}   Отмечено: {_all.Count(r => r.Checked)}" +
                      (done.Count > 0 ? $"   Найдено: {Sys.Size(done.Sum(r => r.Size))}" : "");
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    async Task Analyze()
    {
        Grid.CommitEdit();
        Winapp2.SaveChecked(_all);
        var rules = _all.Where(r => r.Checked).ToList();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            var ignore = Winapp2.LoadIgnore();
            Parallel.ForEach(rules, r => Winapp2.Analyze(r, ignore));
        }));
        Grid.Items.Refresh();
        UpdateStatus();
    }

    async void Analyze_Click(object s, RoutedEventArgs e) => await Analyze();

    async void Clean_Click(object s, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        if (_all.Any(r => r.Checked && !r.Analyzed)) await Analyze();
        var rules = _all.Where(r => r.Checked && r.Analyzed && r.Count > 0).ToList();
        if (rules.Count == 0) { Ui.Info("Нечего удалять."); return; }
        var warn = rules.Where(r => r.Warning != null).Select(r => $"• {r.Name}: {r.Warning}").ToList();
        if (!Ui.Confirm($"Удалить {rules.Sum(r => r.Count)} объектов ({Sys.Size(rules.Sum(r => r.Size))}) по {rules.Count} правилам?\n" +
                        "Файлы удаляются без корзины." + (warn.Count > 0 ? "\n\nВнимание:\n" + string.Join("\n", warn) : ""))) return;

        (long Freed, int Failed) res = default;
        await Ui.Busy(this, () => Task.Run(() =>
        {
            using var tx = UndoTx.Begin("Очистка диска: " + string.Join(", ", rules.Select(r => r.Name).Take(10)));
            res = Winapp2.Clean(rules, tx);
        }));
        Ui.Info($"Освобождено: {Sys.Size(res.Freed)}" + (res.Failed > 0 ? $"\nПропущено (заняты или нет доступа): {res.Failed}" : ""));
        Grid.Items.Refresh();
        UpdateStatus();
    }

    void Details_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not CleanRule r) return;
        if (!r.Analyzed || r.Found.Count == 0) { Ui.Info(r.Analyzed ? "Ничего не найдено." : "Сначала выполните анализ."); return; }
        ChecklistWindow.Ask(r.Name, "Снимите галочки с того, что нужно оставить. Папки удаляются, только если опустели.", r.Found, "Готово");
        r.Size = r.Found.Where(l => l.Checked).Sum(l => l.Size);
        Grid.Items.Refresh();
        UpdateStatus();
    }

    void All_Click(object s, RoutedEventArgs e) => SetChecked(_visible, true);
    void None_Click(object s, RoutedEventArgs e) => SetChecked(_all, false);

    void SetChecked(List<CleanRule> rules, bool v)
    {
        rules.ForEach(r => r.Checked = v);
        Grid.Items.Refresh();
        UpdateStatus();
    }

    async void Download_Click(object s, RoutedEventArgs e)
    {
        var count = 0;
        await Ui.Busy(this, async () => count = await Winapp2.Download());
        if (count == 0) return;
        Ui.Info($"winapp2.ini обновлён, правил в базе: {count}.");
        await Reload();
    }

    void Ignore_Click(object s, RoutedEventArgs e)
    {
        if (!File.Exists(Winapp2.IgnorePath))
            File.WriteAllText(Winapp2.IgnorePath, "; Пути (папки или файлы), которые очистка никогда не трогает. По одному на строку.\r\n");
        Sys.Open(Winapp2.IgnorePath);
    }
}
