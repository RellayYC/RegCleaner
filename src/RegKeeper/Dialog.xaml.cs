using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace RegKeeper;

/// <summary>The one modal message window: a dark card over the dimmed owner. Result is the clicked button's index, -1 for × and Esc.</summary>
public partial class Dialog : Window
{
    public int Result { get; private set; } = -1;

    /// <param name="title">The question; a text with line breaks puts the rest into the body.</param>
    /// <param name="bar">Sizes for the bar under the figure: now, and after (the freed part is hatched).</param>
    public Dialog(string title, string[] buttons, int primary, string? figure = null, string? caption = null,
        (long now, long after)? bar = null, string? warning = null, string? head = null)
    {
        InitializeComponent();
        var cut = title.IndexOf('\n');
        TitleText.Text = cut < 0 ? title : title[..cut];
        if (cut >= 0) { Body.Text = title[(cut + 1)..]; BodyScroll.Visibility = Visibility.Visible; }
        if (head != null) Head.Text = head;
        if (figure != null) { Figure.Text = figure; Caption.Text = caption; FigureRow.Visibility = Visibility.Visible; }
        if (bar is { now: > 0 } b)
        {
            Bar.ColumnDefinitions[0].Width = new GridLength(Math.Max(b.after, 0), GridUnitType.Star);
            Bar.ColumnDefinitions[1].Width = new GridLength(Math.Max(b.now - b.after, 0), GridUnitType.Star);
            BarRow.Visibility = Visibility.Visible;
        }
        if (warning != null) { WarnText.Text = warning; Warn.Visibility = Visibility.Visible; }
        for (var i = 0; i < buttons.Length; i++)
        {
            var n = i;
            var btn = new Button { Content = buttons[i], Style = (Style)FindResource(i == primary ? "DlgPrimaryButton" : "DlgButton"), IsDefault = i == primary };
            btn.Click += (_, _) => { Result = n; Close(); };
            Buttons.Children.Add(btn);
            if (i == primary) Loaded += (_, _) => Keyboard.Focus(btn);
        }
        L.Apply(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) => Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(110)));
    }

    void X_Click(object s, RoutedEventArgs e) => Close();

    /// <summary>Covers the owner's window so it dims; no visible owner, the card sits alone mid-screen.</summary>
    public void Cover(Window? owner)
    {
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized } && PresentationSource.FromVisual(owner) is { CompositionTarget: { } target })
        {
            Owner = owner;
            var to = target.TransformFromDevice;
            var tl = to.Transform(owner.PointToScreen(new Point()));
            var br = to.Transform(owner.PointToScreen(new Point(owner.ActualWidth, owner.ActualHeight)));
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        }
        else
        {
            Dim.Visibility = Visibility.Collapsed;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>Shows the dialog modally from any thread. Headless runs (tests) log the title and get -1.</summary>
    public static int Show(string title, string[] buttons, int primary, string? figure = null, string? caption = null,
        (long now, long after)? bar = null, string? warning = null, string? head = null)
    {
        if (Ui.Headless != null) { Ui.Headless("dialog: " + title); return -1; }
        if (Application.Current is { } app && !app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Show(title, buttons, primary, figure, caption, bar, warning, head));
        var d = new Dialog(title, buttons, primary, figure, caption, bar, warning, head);
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w is not Dialog) ?? Application.Current?.MainWindow;
        d.Cover(owner);
        d.ShowDialog();
        return d.Result;
    }
}
