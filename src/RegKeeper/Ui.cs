using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegKeeper;

/// <summary>Small UI helpers shared by modules.</summary>
public static class Ui
{
    /// <summary>Set by headless runs (smoke tests): messages go here instead of a dialog and Confirm answers no.</summary>
    public static Action<string>? Headless;

    /// <summary>Shadow a button casts from a copy of its body (Controls.xaml), so the label itself keeps ClearType.</summary>
    public static readonly DependencyProperty GlowProperty = DependencyProperty.RegisterAttached(
        "Glow", typeof(System.Windows.Media.Effects.Effect), typeof(Ui));
    public static System.Windows.Media.Effects.Effect? GetGlow(DependencyObject o) => (System.Windows.Media.Effects.Effect?)o.GetValue(GlowProperty);
    public static void SetGlow(DependencyObject o, System.Windows.Media.Effects.Effect? v) => o.SetValue(GlowProperty, v);

    /// <summary>A "⋯" button: a click opens its ContextMenu under it, where the rarer commands live.</summary>
    public static readonly DependencyProperty OverflowProperty = DependencyProperty.RegisterAttached(
        "Overflow", typeof(bool), typeof(Ui), new(false, (o, e) =>
        {
            if (o is Button b && (bool)e.NewValue) b.Click += (_, _) =>
            {
                if (b.ContextMenu is not { } m) return;
                m.PlacementTarget = b;
                m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                m.IsOpen = true;
            };
        }));
    public static bool GetOverflow(DependencyObject o) => (bool)o.GetValue(OverflowProperty);
    public static void SetOverflow(DependencyObject o, bool v) => o.SetValue(OverflowProperty, v);

    /// <summary>Shows a selection bar (Controls.xaml SelectionBar) while count() is above zero and writes the count into text.
    /// Re-counts on selection and on any tick in the grid; the returned action re-counts after code changes the ticks.</summary>
    public static Action Bar(ItemsControl grid, FrameworkElement bar, TextBlock text, Func<int> count, Func<int, string>? label = null)
    {
        void Update()
        {
            var n = count();
            bar.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            text.Text = label?.Invoke(n) ?? L.T($"Выбрано: {n}");
        }
        RoutedEventHandler later = (_, _) => grid.Dispatcher.BeginInvoke(Update); // after the tick reaches the item
        grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, later, true);
        grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, later, true);
        if (grid is System.Windows.Controls.Primitives.Selector s) s.SelectionChanged += (_, _) => Update();
        Update();
        return Update;
    }

    public static bool Confirm(string text, string title = "RegKeeper")
    {
        if (Headless != null) { Headless("confirm: " + text); return false; }
        return Dialog.Show(text, [L.T("Отмена"), L.T("Да")], 1, head: title) == 1;
    }

    public static void Info(string text)
    {
        if (Headless != null) Headless("info: " + text);
        else Dialog.Show(text, ["OK"], 0);
    }

    public static void Error(Exception e)
    {
        var text = e is AggregateException a ? a.InnerException?.Message ?? a.Message : e.Message;
        if (Headless != null) Headless("error: " + e);
        else Dialog.Show(text, ["OK"], 0, head: L.T("Ошибка"));
    }

    /// <summary>Runs work off the UI thread with a wait cursor and the given control disabled; errors are shown.</summary>
    public static async Task Busy(FrameworkElement owner, Func<Task> work)
    {
        owner.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.AppStarting;
        var main = Application.Current is { } app && app.CheckAccess() ? app.MainWindow as MainWindow : null; // tests run Busy on their own thread
        main?.SetBusy(true);
        try { await work(); }
        catch (Exception e) { Error(e); }
        finally
        {
            main?.SetBusy(false);
            Mouse.OverrideCursor = null;
            owner.IsEnabled = true;
        }
    }

    /// <summary>Busy that Esc or the window's "Отмена" pill cancels; a cancelled run is not an error.
    /// True when the work ran to the end (not cancelled, no error).</summary>
    public static async Task<bool> Busy(FrameworkElement owner, Func<CancellationToken, Task> work)
    {
        using var cts = new CancellationTokenSource();
        var w = Window.GetWindow(owner);
        KeyEventHandler esc = (_, e) => { if (e.Key == Key.Escape) cts.Cancel(); };
        var main = w as MainWindow;
        if (w != null) w.PreviewKeyDown += esc;
        main?.AddCancel(cts);
        var done = false;
        try
        {
            await Busy(owner, async () =>
            {
                if (w != null) Keyboard.Focus(w); // the focused control is disabled now; keys must still reach the window
                try { await work(cts.Token); done = true; }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            });
        }
        finally
        {
            if (w != null) w.PreviewKeyDown -= esc;
            main?.RemoveCancel(cts);
        }
        return done;
    }

    /// <summary>Text prompt; multiline for REG_MULTI_SZ and the like.</summary>
    public static string? Prompt(string title, string label, string initial = "", bool multiline = false)
    {
        var box = new TextBox { Text = initial, MinWidth = 380, Margin = new Thickness(0, 6, 0, 12), AcceptsReturn = multiline, Height = multiline ? 220 : double.NaN, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.Resources["Primary"] };
        var cancel = new Button { Content = L.T("Отмена"), IsCancel = true, Margin = new Thickness(0) };
        var w = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock { Text = label }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } }
                }
            }
        };
        ok.Click += (_, _) => w.DialogResult = true;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        Theme.TitleBar(w);
        return w.ShowDialog() == true ? box.Text : null;
    }
}
