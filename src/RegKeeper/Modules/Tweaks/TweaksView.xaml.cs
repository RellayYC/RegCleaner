using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class TweaksView : UserControl
{
    List<Tweak> _all = new();
    string _cat = "";
    Tweak? _menu;
    readonly Action _bar;

    public TweaksView()
    {
        InitializeComponent();
        L.Apply(this);
        Resources["WillOn"] = L.T("включится");
        Resources["WillOff"] = L.T("выключится");
        Cards.Tag = 3;
        _bar = Ui.Bar(Cards, Bar, BarText, () => _all.Count(t => t.Wanted != t.IsOn), n => L.T($"{n} изменений не применены"));
        Load();
    }

    void Load()
    {
        try { _all = Tweaks.Load(); }
        catch (Exception e) { Ui.Error(e); }
        foreach (var c in _all.Select(t => t.Category).Distinct())
        {
            var b = new RadioButton { Content = c, Tag = c, Style = (Style)FindResource("Segment") };
            b.Checked += Cat_Checked;
            Cats.Children.Add(b);
        }
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        Cards.ItemsSource = _all.Where(t => (_cat == "" || t.Category == _cat)
            && (q == "" || (t.Name + " " + t.Description + " " + t.Category).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        OnRun.Text = _all.Count(t => t.IsOn).ToString();
        TotalRun.Text = _all.Count.ToString();
        _bar?.Invoke();
    }

    void Cat_Checked(object s, RoutedEventArgs e)
    {
        _cat = (string)((FrameworkElement)s).Tag;
        if (IsLoaded) ApplyFilter();
    }

    /// <summary>The category strip is wider than the window at 100%: the wheel scrolls it sideways, the bar stays hidden.</summary>
    void Cats_MouseWheel(object s, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)s;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    void SearchBtn_Click(object s, RoutedEventArgs e)
    {
        var show = Search.Visibility != Visibility.Visible;
        Search.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) Search.Focus(); else Search.Text = "";
    }

    void Cards_SizeChanged(object s, SizeChangedEventArgs e)
    {
        var n = e.NewSize.Width < 760 ? 2 : 3;
        if (!Equals(Cards.Tag, n)) Cards.Tag = n;
    }

    /// <summary>The card under the right click is the one "Key in editor" opens.</summary>
    void Cards_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        _menu = (e.OriginalSource as FrameworkElement)?.DataContext as Tweak;
        if (_menu == null) e.Handled = true;
    }

    void Reset_Click(object s, RoutedEventArgs e)
    {
        _all.ForEach(t => t.Refresh());
        ApplyFilter();
    }

    async void Apply_Click(object s, RoutedEventArgs e)
    {
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
        if (_menu is { } t) MainWindow.Current.Show<RegEditorView>().Navigate(t.Values[0].Key);
    }
}
