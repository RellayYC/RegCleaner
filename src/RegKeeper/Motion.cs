using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace RegKeeper;

/// <summary>Code-driven motion: page swap, sliding pill, row stagger, count-up.
/// Durations and easings are the T*/Ease* tokens in Themes/Controls.xaml; template storyboards read the same keys.</summary>
public static class Motion
{
    static readonly string[] Times = { "TPress", "THover", "TFast", "TBase", "TSlow", "TCount", "TStagger" };

    /// <summary>Off when Windows animations are off (the reduced-motion switch) and in headless runs.</summary>
    public static bool On => SystemParameters.ClientAreaAnimation && Ui.Headless == null;

    /// <summary>Zeroes the duration tokens so template storyboards jump to their end; call before any window exists.</summary>
    public static void Reduce(ResourceDictionary controls)
    {
        foreach (var k in Times) controls[k] = KeyTime.FromTimeSpan(TimeSpan.Zero);
    }

    static TimeSpan T(string key) => Application.Current?.TryFindResource(key) is KeyTime k ? k.TimeSpan : TimeSpan.Zero;
    static KeySpline S(string key) => Application.Current?.TryFindResource(key) as KeySpline ?? new KeySpline();

    /// <summary>Keyframes to <paramref name="to"/>: holds <paramref name="from"/> through the delay, then eases.</summary>
    static DoubleAnimationUsingKeyFrames A(double to, string time, string ease, double? from = null, TimeSpan delay = default)
    {
        var a = new DoubleAnimationUsingKeyFrames();
        if (from is double f)
        {
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(f, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (delay > TimeSpan.Zero) a.KeyFrames.Add(new DiscreteDoubleKeyFrame(f, KeyTime.FromTimeSpan(delay)));
        }
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + T(time)), S(ease)));
        return a;
    }

    // ---- page transition: the old page lifts away as a snapshot, the new one is in Host at once (callers read Content right after)

    public static void SwapPage(ContentControl host, Image ghost, object next)
    {
        if (On && host.Content != null && host.ActualWidth > 0 && host.ActualHeight > 0)
        {
            var shot = Snapshot(host);
            ghost.Source = shot;
            ghost.Visibility = Visibility.Visible;
            var lift = new TranslateTransform();
            ghost.RenderTransform = lift;
            var fade = A(0, "TFast", "EaseExit", 1);
            fade.Completed += (_, _) =>
            {
                if (ghost.Source != shot) return; // a newer swap owns the ghost now
                ghost.Source = null;
                ghost.Visibility = Visibility.Collapsed;
            };
            ghost.BeginAnimation(UIElement.OpacityProperty, fade);
            lift.BeginAnimation(TranslateTransform.YProperty, A(-4, "TFast", "EaseExit", 0));
        }
        host.Content = next;
        if (!On) return;
        var rise = new TranslateTransform();
        var blur = new BlurEffect { Radius = 4, KernelType = KernelType.Gaussian };
        host.RenderTransform = rise;
        host.Effect = blur;
        var show = A(1, "TSlow", "EaseEnter", 0);
        show.Completed += (_, _) => { if (host.Effect == blur) host.Effect = null; }; // blur off: text gets ClearType back
        host.BeginAnimation(UIElement.OpacityProperty, show);
        rise.BeginAnimation(TranslateTransform.YProperty, A(0, "TSlow", "EaseEnter", 8));
        blur.BeginAnimation(BlurEffect.RadiusProperty, A(0, "TSlow", "EaseEnter", 4));
    }

    static BitmapSource Snapshot(FrameworkElement e)
    {
        // through a DrawingVisual: rendering the element itself bakes in its layout offset (the Margin)
        var dpi = VisualTreeHelper.GetDpi(e);
        double w = e.ActualWidth, h = e.ActualHeight;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(e) { Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute }, null, new Rect(0, 0, w, h));
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    // ---- sliding pill: lives in a Canvas laid over the items, moves onto the selected one

    public static void Slide(FrameworkElement pill, FrameworkElement? target)
    {
        if (target == null) { pill.Visibility = Visibility.Hidden; return; }
        if (target.ActualWidth == 0)
        {
            SizeChangedEventHandler? later = null;
            later = (_, _) => { target.SizeChanged -= later; Slide(pill, target); };
            target.SizeChanged += later;
            return;
        }
        if (VisualTreeHelper.GetParent(pill) is not Visual canvas || canvas.FindCommonVisualAncestor(target) == null) return;
        var p = target.TransformToVisual(canvas).Transform(new Point());
        if (pill.RenderTransform is not TranslateTransform tt) pill.RenderTransform = tt = new TranslateTransform();
        var jump = !On || pill.Visibility != Visibility.Visible;
        pill.Visibility = Visibility.Visible;
        Go(tt, TranslateTransform.XProperty, p.X, jump);
        Go(tt, TranslateTransform.YProperty, p.Y, jump);
        Go(pill, FrameworkElement.WidthProperty, target.ActualWidth, jump);
        Go(pill, FrameworkElement.HeightProperty, target.ActualHeight, jump);
    }

    static void Go(IAnimatable o, DependencyProperty p, double to, bool jump)
    {
        if (!jump) { o.BeginAnimation(p, A(to, "TBase", "EaseMove")); return; }
        o.BeginAnimation(p, null);
        ((DependencyObject)o).SetValue(p, to);
    }

    // ---- row stagger: rows of a freshly filled grid rise in one after another, the whole wave capped at 400 ms

    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
        "Stagger", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnStagger));
    public static bool GetStagger(DependencyObject o) => (bool)o.GetValue(StaggerProperty);
    public static void SetStagger(DependencyObject o, bool v) => o.SetValue(StaggerProperty, v);

    static readonly DependencyProperty ArmedProperty = DependencyProperty.RegisterAttached("Armed", typeof(DateTime?), typeof(Motion));
    static readonly DependencyProperty FilledProperty = DependencyProperty.RegisterAttached("Filled", typeof(bool), typeof(Motion));
    static readonly TimeSpan Wave = TimeSpan.FromMilliseconds(400);

    static void OnStagger(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid g || !(bool)e.NewValue) return;
        g.Loaded += (_, _) => ThemeChecks(g);
        // ponytail: the descriptor holds the grid for the app's lifetime; fine for cached views, a dialog grid leaks a few KB
        DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid)).AddValueChanged(g, (_, _) =>
        {
            // only a list that appears where there was none staggers; refreshing a filled list must not replay the wave
            var filled = g.Items.Count > 0;
            if (filled && !(bool)g.GetValue(FilledProperty)) g.SetValue(ArmedProperty, DateTime.UtcNow);
            g.SetValue(FilledProperty, filled);
        });
        g.LoadingRow += (_, r) => Row(g, r.Row);
    }

    static void Row(DataGrid g, DataGridRow row)
    {
        if (row.RenderTransform is not TranslateTransform tt) row.RenderTransform = tt = new TranslateTransform();
        var armed = g.GetValue(ArmedProperty) as DateTime?;
        if (!On || armed == null || DateTime.UtcNow - armed > Wave)
        {
            // a recycled row may still carry its entrance; drop it so scrolling shows rows as they are
            row.BeginAnimation(UIElement.OpacityProperty, null);
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }
        var delay = TimeSpan.FromTicks(Math.Min(T("TStagger").Ticks * row.GetIndex(), Wave.Ticks));
        row.BeginAnimation(UIElement.OpacityProperty, A(1, "TBase", "EaseEnter", 0, delay));
        tt.BeginAnimation(TranslateTransform.YProperty, A(0, "TBase", "EaseEnter", 8, delay));
    }

    /// <summary>DataGridCheckBoxColumn brings its own element styles, which skip the implicit CheckBox; swap in the themed ones.</summary>
    static void ThemeChecks(DataGrid g)
    {
        foreach (var c in g.Columns.OfType<DataGridCheckBoxColumn>())
        {
            if (c.ElementStyle == DataGridCheckBoxColumn.DefaultElementStyle && g.TryFindResource("GridCheck") is Style s) c.ElementStyle = s;
            if (c.EditingElementStyle == DataGridCheckBoxColumn.DefaultEditingElementStyle && g.TryFindResource("GridCheckEdit") is Style es) c.EditingElementStyle = es;
        }
    }

    // ---- count-up: when a loaded status line gets new numbers, they run up from zero

    public static readonly DependencyProperty CountUpProperty = DependencyProperty.RegisterAttached(
        "CountUp", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnCountUp));
    public static bool GetCountUp(DependencyObject o) => (bool)o.GetValue(CountUpProperty);
    public static void SetCountUp(DependencyObject o, bool v) => o.SetValue(CountUpProperty, v);

    static readonly Regex Digits = new(@"\d{1,15}");
    static readonly Dictionary<TextBlock, EventHandler> Counting = new();
    static readonly HashSet<TextBlock> Writing = new();

    static void OnCountUp(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb && (bool)e.NewValue)
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(tb, (_, _) => Count(tb));
    }

    static void Count(TextBlock tb)
    {
        if (Writing.Contains(tb)) return;
        if (Counting.Remove(tb, out var old)) CompositionTarget.Rendering -= old; // a newer text wins
        var target = tb.Text;
        if (!On || !tb.IsLoaded || !Digits.IsMatch(target)) return;
        var dur = T("TCount");
        var ease = S("EaseEnter");
        var start = DateTime.UtcNow;
        EventHandler tick = null!;
        tick = (_, _) =>
        {
            var p = dur <= TimeSpan.Zero ? 1 : Math.Min(1, (DateTime.UtcNow - start) / dur);
            var k = ease.GetSplineProgress(p);
            var text = p >= 1 ? target : Digits.Replace(target, m => Math.Round(long.Parse(m.Value) * k).ToString());
            Writing.Add(tb);
            tb.SetCurrentValue(TextBlock.TextProperty, text); // SetCurrentValue keeps a binding on Text alive
            Writing.Remove(tb);
            if (p < 1) return;
            CompositionTarget.Rendering -= tick;
            Counting.Remove(tb);
        };
        Counting[tb] = tick;
        tick(null, EventArgs.Empty); // zeros before the first frame paints the final value
        CompositionTarget.Rendering += tick;
    }
}
