using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class UndoView : UserControl
{
    // what an undo item says it did (the start of its text, see UndoTx), how the list marks it, how the bars name it
    static readonly (string Start, string Sign, bool Danger, string Label)[] KindsOf =
    [
        ("Удалён ключ ", "−", true, "Удалённые ключи"), // i18n: keep (keys, translated where used)
        ("Удалено значение {0} → {1}", "−", true, "Удалённые значения"), // i18n: keep (keys, translated where used)
        ("Значение {0} → {1}", "~", false, "Изменённые значения"), // i18n: keep (keys, translated where used)
        ("Создан ключ ", "+", false, "Созданные ключи"), // i18n: keep (keys, translated where used)
        ("Создана задача ", "+", false, "Созданные задачи"), // i18n: keep (keys, translated where used)
        ("Задача ", "~", false, "Задачи планировщика"), // i18n: keep (keys, translated where used)
    ];

    public sealed record Line(string Sign, bool Danger, string Text);
    public sealed record Kind(string Label, int Count, GridLength Fill, GridLength Rest);

    public UndoView()
    {
        InitializeComponent();
        L.Apply(this);
        IsVisibleChanged += (_, _) => { if (IsVisible) Reload(); };
        Reload();
    }

    void Reload()
    {
        var list = UndoCenter.List();
        Timeline.ItemsSource = list;
        if (list.Count > 0) Timeline.SelectedIndex = 0;
        CountText.Text = L.T($"записей: {list.Count}");
        SizeText.Text = Sys.Size(Sys.DirSize(AppPaths.Undo));
    }

    List<UndoInfo> Selected => Timeline.SelectedItems.Cast<UndoInfo>().ToList();

    static int KindOf(string item)
    {
        static string Head(string s) => s.Split("{0}")[0];
        return Array.FindIndex(KindsOf, k => item.StartsWith(Head(k.Start)) || item.StartsWith(Head(L.T(k.Start))));
    }

    /// <summary>The details card: title, the restore count, kinds as bars and every item with its sign.</summary>
    void Timeline_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var sel = Selected;
        Details.Visibility = sel.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sel.Count == 0) return;
        var items = sel.OrderByDescending(i => i.Date).SelectMany(i => i.Items).ToList();
        DetailTitle.Text = sel.Count == 1 ? sel[0].Title : L.T($"Выбрано записей: {sel.Count}");
        DetailDate.Text = sel.Count == 1 ? sel[0].Date.ToString("dd.MM.yyyy HH:mm:ss") : $"{sel.Min(i => i.Date):dd.MM.yyyy HH:mm} - {sel.Max(i => i.Date):dd.MM.yyyy HH:mm}";
        RestoreText.Text = L.T($"Восстановить всё {items.Count}");
        var kinds = items.Select(KindOf).ToList();
        var max = Math.Max(1, kinds.GroupBy(k => k).Select(g => g.Count()).DefaultIfEmpty(0).Max());
        Kinds.ItemsSource = kinds.GroupBy(k => k).OrderBy(g => g.Key < 0 ? int.MaxValue : g.Key).Select(g =>
        {
            var f = (double)g.Count() / max;
            return new Kind(g.Key < 0 ? L.T("Другое") : L.T(KindsOf[g.Key].Label), g.Count(), new(f, GridUnitType.Star), new(1 - f, GridUnitType.Star));
        }).ToList();
        Items.ItemsSource = items.Zip(kinds, (t, k) => k < 0 ? new Line("·", false, t) : new Line(KindsOf[k].Sign, KindsOf[k].Danger, t)).ToList();
    }

    async void Restore_Click(object s, RoutedEventArgs e)
    {
        // newest first: older changes may sit underneath newer ones on the same keys
        var list = Selected.OrderByDescending(i => i.Date).ToList();
        if (list.Count == 0) return;
        if (!Ui.Confirm(L.T("Отменить изменения?\n") + string.Join("\n", list.Select(i => $"{i.Date:dd.MM HH:mm}  {i.Title}")))) return;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            foreach (var i in list)
                try { UndoCenter.Restore(i); }
                catch (Exception ex) { errors.Add($"{i.Title}: {ex.Message}"); }
        }));
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        Reload();
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        var list = Selected;
        if (list.Count == 0 || !Ui.Confirm(L.T($"Удалить записей: {list.Count}? Эти изменения больше нельзя будет отменить."))) return;
        list.ForEach(UndoCenter.Delete);
        Reload();
    }

    void DeleteOld_Click(object s, RoutedEventArgs e)
    {
        var old = UndoCenter.List().Where(i => i.Date < DateTime.Now.AddDays(-30)).ToList();
        if (old.Count == 0) { Ui.Info(L.T("Записей старше 30 дней нет.")); return; }
        if (!Ui.Confirm(L.T($"Удалить записей старше 30 дней: {old.Count}?"))) return;
        old.ForEach(UndoCenter.Delete);
        Reload();
    }

    void OpenReg_Click(object s, RoutedEventArgs e)
    {
        if (Timeline.SelectedItem is UndoInfo i) Sys.ShowInExplorer(Path.Combine(i.Dir, "undo.reg"));
    }

    void Refresh_Click(object s, RoutedEventArgs e) => Reload();
}
