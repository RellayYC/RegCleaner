using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class TweaksView : UserControl
{
    List<Tweak> _all = new();

    public TweaksView()
    {
        InitializeComponent();
        L.Apply(this);
        Load();
    }

    void Load()
    {
        try { _all = Tweaks.Load(); }
        catch (Exception e) { Ui.Error(e); }
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = q == "" ? _all : _all.Where(t => (t.Name + " " + t.Description + " " + t.Category).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var view = new ListCollectionView(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Tweak.Category)));
        Grid.ItemsSource = view;
        Status.Text = L.T($"Включено: {_all.Count(t => t.IsOn)} из {_all.Count}");
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    void Reset_Click(object s, RoutedEventArgs e)
    {
        _all.ForEach(t => t.Refresh());
        ApplyFilter();
    }

    async void Apply_Click(object s, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var changed = _all.Where(t => t.Wanted != t.IsOn).ToList();
        if (changed.Count == 0) { Ui.Info(L.T("Нечего применять: отметки совпадают с текущим состоянием.")); return; }
        if (!Ui.Confirm(L.T("Применить:\n") + string.Join("\n", changed.Select(t => (t.Wanted ? "+ " : "- ") + t.Name)))) return;
        (string? Restart, List<string> Errors) r = (null, new());
        await Ui.Busy(this, () => Task.Run(() => r = Tweaks.ApplyChanged(changed)));
        ApplyFilter();
        if (r.Errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", r.Errors));
        switch (r.Restart)
        {
            case "explorer" when Ui.Confirm(L.T("Перезапустить Проводник, чтобы изменения вступили в силу?")):
                Tweaks.RestartExplorer();
                break;
            case "logoff":
                Ui.Info(L.T("Изменения вступят в силу после выхода из системы и повторного входа."));
                break;
            case "reboot" when Ui.Confirm(L.T("Изменения вступят в силу после перезагрузки. Перезагрузить сейчас?")):
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
                break;
        }
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is Tweak t) MainWindow.Current.Show<RegEditorView>().Navigate(t.Values[0].Key);
    }
}
