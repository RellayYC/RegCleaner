using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegKeeper;

/// <summary>Small UI helpers shared by modules.</summary>
public static class Ui
{
    /// <summary>Set by headless runs (smoke tests): messages go here instead of a MessageBox and Confirm answers no.</summary>
    public static Action<string>? Headless;

    public static bool Confirm(string text, string title = "RegKeeper")
    {
        if (Headless != null) { Headless("confirm: " + text); return false; }
        return MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public static void Info(string text)
    {
        if (Headless != null) Headless("info: " + text);
        else MessageBox.Show(text, "RegKeeper", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void Error(Exception e)
    {
        var text = e is AggregateException a ? a.InnerException?.Message ?? a.Message : e.Message;
        if (Headless != null) Headless("error: " + e);
        else MessageBox.Show(text, "RegKeeper", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Runs work off the UI thread with a wait cursor and the given control disabled; errors are shown.</summary>
    public static async Task Busy(FrameworkElement owner, Func<Task> work)
    {
        owner.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.AppStarting;
        try { await work(); }
        catch (Exception e) { Error(e); }
        finally
        {
            Mouse.OverrideCursor = null;
            owner.IsEnabled = true;
        }
    }

    /// <summary>Busy that Esc cancels (the window title says so); a cancelled run is not an error.</summary>
    public static async Task Busy(FrameworkElement owner, Func<CancellationToken, Task> work)
    {
        using var cts = new CancellationTokenSource();
        var w = Window.GetWindow(owner);
        KeyEventHandler esc = (_, e) => { if (e.Key == Key.Escape) cts.Cancel(); };
        var title = w?.Title;
        if (w != null)
        {
            w.PreviewKeyDown += esc;
            w.Title = title + " - Esc: отмена";
        }
        try
        {
            await Busy(owner, async () =>
            {
                if (w != null) Keyboard.Focus(w); // the focused control is disabled now; keys must still reach the window
                try { await work(cts.Token); }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            });
        }
        finally
        {
            if (w != null)
            {
                w.PreviewKeyDown -= esc;
                w.Title = title;
            }
        }
    }

    /// <summary>Text prompt; multiline for REG_MULTI_SZ and the like.</summary>
    public static string? Prompt(string title, string label, string initial = "", bool multiline = false)
    {
        var box = new TextBox { Text = initial, MinWidth = 380, Margin = new Thickness(0, 6, 0, 12), AcceptsReturn = multiline, Height = multiline ? 220 : double.NaN, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var ok = new Button { Content = "OK", IsDefault = true, Style = (Style)Application.Current.Resources["Primary"] };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0) };
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
