using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class UndoView : UserControl
{
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
        Grid.ItemsSource = list;
        Status.Text = L.T($"Записей: {list.Count}   Размер: {Sys.Size(Sys.DirSize(AppPaths.Undo))}");
    }

    List<UndoInfo> Selected => Grid.SelectedItems.Cast<UndoInfo>().ToList();

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
        if (Grid.SelectedItem is UndoInfo i) Sys.ShowInExplorer(Path.Combine(i.Dir, "undo.reg"));
    }

    void Refresh_Click(object s, RoutedEventArgs e) => Reload();
}
