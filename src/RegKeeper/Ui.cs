using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegKeeper;

/// <summary>Small UI helpers shared by modules.</summary>
public static class Ui
{
    public static bool Confirm(string text, string title = "RegKeeper") =>
        MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string text) => MessageBox.Show(text, "RegKeeper", MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(Exception e) =>
        MessageBox.Show(e is AggregateException a ? a.InnerException?.Message ?? a.Message : e.Message,
            "RegKeeper", MessageBoxButton.OK, MessageBoxImage.Error);

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

    /// <summary>Single-line text prompt.</summary>
    public static string? Prompt(string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial, MinWidth = 380, Margin = new Thickness(0, 6, 0, 12) };
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
        return w.ShowDialog() == true ? box.Text : null;
    }
}
