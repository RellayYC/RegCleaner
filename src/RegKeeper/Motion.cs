using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace RegKeeper;

/// <summary>Code-driven motion: page swap, sliding pill, row stagger, count-up, smooth wheel, sticky group titles.
/// Durations and easings are the T*/Ease* tokens in Themes/Controls.xaml; template storyboards read the same keys.</summary>
public static class Motion
{
    static readonly string[] Times = { "TPress", "THover", "TFast", "TBase", "TSlow", "TCount", "TStagger", "TSpotIn", "TSpotOut" };

    /// <summary>Off when Windows animations are off (the reduced-motion switch), in light mode and in headless runs.</summary>
    public static bool On => SystemParameters.ClientAreaAnimation && Ui.Headless == null && !Lite;

    /// <summary>Light mode (Settings): no animation, cursor light, glows or shadows. Read once: it takes effect on the next start.</summary>
    public static readonly bool Lite = LiteSaved;
    public static bool LiteSaved
    {
        get => Core.Reg.GetString(MainWindow.Key, "Lite") == "1";
        set => Microsoft.Win32.Registry.SetValue(MainWindow.Key, "Lite", value ? "1" : "0");
    }

    /// <summary>Drops the effect and glow resources from a dictionary (Controls once, each theme in Theme.Apply): a missing
    /// DynamicResource leaves the property empty, while a null value would just fall through to the next dictionary.</summary>
    public static void Strip(ResourceDictionary d)
    {
        foreach (var k in new[] { "GlowLight", "GlowHot", "CardShadow", "HotCardShadow", "DangerShadow", "BarShadow", "SpotBlur", "BgGlowBrush", "BgHotBrush" }) d.Remove(k);
    }

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
        g.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => Stick(g, e)));
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

    /// <summary>DataGridCheckBoxColumn brings its own element styles, which skip the implicit CheckBox; swap in the themed ones.
    /// Plain text columns get GridText, which wraps, so a narrow window grows the row instead of cutting the text;
    /// star columns get at least ColumnMin, so wrapping never comes down to a letter per line, and fixed ones keep their width
    /// (short of room the grid would squeeze them to 20 px rather than scroll).</summary>
    static void ThemeChecks(DataGrid g)
    {
        foreach (var c in g.Columns.Where(c => c.ReadLocalValue(DataGridColumn.MinWidthProperty) == DependencyProperty.UnsetValue))
            if (c.Width.IsStar) c.MinWidth = D("ColumnMin", 150);
            else if (c.Width.IsAbsolute) c.MinWidth = c.Width.Value;
        foreach (var c in g.Columns.OfType<DataGridTextColumn>())
            if (c.ElementStyle == DataGridTextColumn.DefaultElementStyle && g.TryFindResource("GridText") is Style t) c.ElementStyle = t;
        foreach (var c in g.Columns.OfType<DataGridCheckBoxColumn>())
        {
            if (c.ElementStyle == DataGridCheckBoxColumn.DefaultElementStyle && g.TryFindResource("GridCheck") is Style s) c.ElementStyle = s;
            if (c.EditingElementStyle == DataGridCheckBoxColumn.DefaultEditingElementStyle && g.TryFindResource("GridCheckEdit") is Style es) c.EditingElementStyle = es;
        }
    }

    // ---- sticky group titles: in a grouped grid the title of the group the top rows belong to stays pinned,
    // and the next group's title pushes it out (the Sticky slot in the DataGrid template)

    static void Stick(DataGrid g, ScrollChangedEventArgs e)
    {
        if (g.GroupStyle.Count == 0 || e.OriginalSource is not ScrollViewer sv || sv.TemplatedParent != g
            || sv.Template.FindName("Sticky", sv) is not Border bar || bar.Child is not ContentPresenter title
            || sv.Template.FindName("PART_ScrollContentPresenter", sv) is not UIElement view) return;
        GroupItem? top = null;
        var next = double.PositiveInfinity;
        foreach (var gi in Groups(view))
        {
            var y = gi.TranslatePoint(new Point(), view).Y;
            if (y < 0) top = gi;
            else { next = y; break; }
        }
        bar.Visibility = top == null ? Visibility.Collapsed : Visibility.Visible; // a title right at the top shows itself
        if (top == null) return;
        title.Content = top.Content; // the CollectionViewGroup
        title.ContentTemplate = g.GroupStyle[0].HeaderTemplate;
        if (bar.RenderTransform is not TranslateTransform tt) bar.RenderTransform = tt = new TranslateTransform();
        tt.Y = Math.Min(0, next - bar.ActualHeight);
    }

    static IEnumerable<GroupItem> Groups(DependencyObject d)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is GroupItem gi) yield return gi;
            else foreach (var x in Groups(c)) yield return x;
        }
    }

    // ---- smooth wheel: the innermost scrollable viewer under the mouse glides to its target instead of jumping

    static readonly Dictionary<ScrollViewer, double> Targets = new();

    /// <summary>Once per process, before windows open.</summary>
    public static void SmoothWheel() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel));

    static void Wheel(object s, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)s;
        // Preview tunnels from the outermost viewer: only the one that would scroll takes it; Ctrl/Shift wheel stays stock
        if (!On || e.Handled || Keyboard.Modifiers != ModifierKeys.None || Scrollable(e.OriginalSource as DependencyObject) != sv) return;
        if (sv.CanContentScroll && sv.TemplatedParent is ItemsControl ic && VirtualizingPanel.GetScrollUnit(ic) == ScrollUnit.Item) return; // offsets are rows
        e.Handled = true;
        var running = Targets.TryGetValue(sv, out var from);
        if (!running) from = sv.VerticalOffset;
        Targets[sv] = Math.Clamp(from - e.Delta / 120.0 * SystemParameters.WheelScrollLines * 16, 0, sv.ScrollableHeight);
        if (running) return;
        var start = DateTime.UtcNow;
        var last = start;
        EventHandler tick = null!;
        tick = (_, _) =>
        {
            var now = DateTime.UtcNow;
            var goal = Math.Clamp(Targets[sv], 0, sv.ScrollableHeight);
            var cur = sv.VerticalOffset;
            // exponential approach: ~250 ms to settle, and a new notch mid-glide just moves the goal
            var step = cur + (goal - cur) * (1 - Math.Exp(-(now - last).TotalSeconds * 18));
            last = now;
            var done = Math.Abs(goal - step) < 0.5 || now - start > TimeSpan.FromSeconds(2) || !sv.IsLoaded;
            sv.ScrollToVerticalOffset(done ? goal : step);
            if (!done) return;
            CompositionTarget.Rendering -= tick;
            Targets.Remove(sv);
        };
        CompositionTarget.Rendering += tick;
    }

    static ScrollViewer? Scrollable(DependencyObject? d)
    {
        for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
        return null;
    }

    // ---- count-up: when a loaded status line gets new numbers, they run up from the ones on screen (from zero the first time,
    // or when the line has a different count of numbers), so a line updated many times a second, like a download, only climbs

    public static readonly DependencyProperty CountUpProperty = DependencyProperty.RegisterAttached(
        "CountUp", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnCountUp));
    public static bool GetCountUp(DependencyObject o) => (bool)o.GetValue(CountUpProperty);
    public static void SetCountUp(DependencyObject o, bool v) => o.SetValue(CountUpProperty, v);

    static readonly Regex Digits = new(@"\d{1,15}");
    static readonly Dictionary<TextBlock, EventHandler> Counting = new();
    static readonly HashSet<TextBlock> Writing = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, long[]> Shown = new();

    /// <summary>The numbers of target as they stand at k (0..1) on the way from the numbers in from.</summary>
    internal static string Between(string target, long[] from, double k)
    {
        var i = 0;
        return Digits.Replace(target, m => { var f = from[i++]; return Math.Round(f + (long.Parse(m.Value) - f) * k).ToString(); });
    }

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
        var count = Digits.Matches(target).Count;
        var from = Shown.TryGetValue(tb, out var s) && s.Length == count ? s : new long[count];
        if (!On || !tb.IsLoaded || count == 0) { Shown.Remove(tb); return; }
        var dur = T("TCount");
        var ease = S("EaseEnter");
        var start = DateTime.UtcNow;
        EventHandler tick = null!;
        tick = (_, _) =>
        {
            var p = dur <= TimeSpan.Zero ? 1 : Math.Min(1, (DateTime.UtcNow - start) / dur);
            var text = p >= 1 ? target : Between(target, from, ease.GetSplineProgress(p));
            Shown.AddOrUpdate(tb, Digits.Matches(text).Select(m => long.Parse(m.Value)).ToArray());
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

    // ---- cursor light: a Border overlay in a template lights up around the mouse over its templated parent (Controls.xaml buttons,
    // sidebar items). The overlay's own Background, or its BorderBrush when it has a border, becomes a radial gradient of Spot.

    public static readonly DependencyProperty SpotProperty = DependencyProperty.RegisterAttached(
        "Spot", typeof(Color), typeof(Motion), new PropertyMetadata(default(Color), OnSpot));
    public static Color GetSpot(DependencyObject o) => (Color)o.GetValue(SpotProperty);
    public static void SetSpot(DependencyObject o, Color v) => o.SetValue(SpotProperty, v);

    public static readonly DependencyProperty SpotSizeProperty = DependencyProperty.RegisterAttached(
        "SpotSize", typeof(double), typeof(Motion), new PropertyMetadata(120.0));
    public static double GetSpotSize(DependencyObject o) => (double)o.GetValue(SpotSizeProperty);
    public static void SetSpotSize(DependencyObject o, double v) => o.SetValue(SpotSizeProperty, v);

    /// <summary>The colours a button hands to its overlays: the spotlight and the outer halo (transparent = none).</summary>
    public static readonly DependencyProperty LightProperty = DependencyProperty.RegisterAttached("Light", typeof(Color), typeof(Motion));
    public static Color GetLight(DependencyObject o) => (Color)o.GetValue(LightProperty);
    public static void SetLight(DependencyObject o, Color v) => o.SetValue(LightProperty, v);
    public static readonly DependencyProperty HaloProperty = DependencyProperty.RegisterAttached("Halo", typeof(Color), typeof(Motion));
    public static Color GetHalo(DependencyObject o) => (Color)o.GetValue(HaloProperty);
    public static void SetHalo(DependencyObject o, Color v) => o.SetValue(HaloProperty, v);

    static void OnSpot(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || Lite) return;
        if ((b.BorderThickness.Left > 0 ? b.BorderBrush : b.Background) is RadialGradientBrush old) { Tint(old, (Color)e.NewValue, 1); return; }
        var br = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute };
        br.GradientStops.Add(new GradientStop(default, 0));
        br.GradientStops.Add(new GradientStop(default, 1));
        Tint(br, (Color)e.NewValue, 1);
        if (b.BorderThickness.Left > 0) b.BorderBrush = br; else b.Background = br;
        b.Opacity = 0;
        b.IsHitTestVisible = false;
        b.Loaded += (_, _) => { if (b.TemplatedParent is UIElement host) _ = new Light(b, br, host); };
    }

    /// <summary>Centre colour times <paramref name="k"/> in alpha, fading to the same colour fully transparent.</summary>
    static void Tint(RadialGradientBrush br, Color c, double k)
    {
        br.GradientStops[0].Color = Color.FromArgb((byte)Math.Min(255, c.A * k), c.R, c.G, c.B);
        br.GradientStops[1].Color = Color.FromArgb(0, c.R, c.G, c.B);
    }

    static double D(string key, double fallback) => Application.Current?.TryFindResource(key) is double v ? v : fallback;

    sealed class Light
    {
        readonly Border b; readonly RadialGradientBrush br; readonly UIElement host;
        Point at, to;
        bool tracking;

        public Light(Border b, RadialGradientBrush br, UIElement host)
        {
            (this.b, this.br, this.host) = (b, br, host);
            Size(1, true);
            host.MouseEnter += Enter; host.MouseMove += Move; host.MouseLeave += Leave;
            host.PreviewMouseLeftButtonDown += Down; host.PreviewMouseLeftButtonUp += Up;
            host.IsEnabledChanged += Enabled;
            b.Unloaded += Gone;
        }

        void Gone(object s, RoutedEventArgs e)
        {
            host.MouseEnter -= Enter; host.MouseMove -= Move; host.MouseLeave -= Leave;
            host.PreviewMouseLeftButtonDown -= Down; host.PreviewMouseLeftButtonUp -= Up;
            host.IsEnabledChanged -= Enabled;
            b.Unloaded -= Gone;
            Track(false);
        }

        // reduced motion: a still light in the middle, no following
        Point Pos(MouseEventArgs e) => On ? e.GetPosition(b) : new Point(b.ActualWidth / 2, b.ActualHeight / 2);

        void Enter(object s, MouseEventArgs e)
        {
            if (e.StylusDevice != null || !host.IsEnabled || GetSpot(b).A == 0) return;
            at = to = Pos(e);
            Place();
            b.BeginAnimation(UIElement.OpacityProperty, A(1, "TSpotIn", "EaseEnter"));
            Track(On);
        }

        void Move(object s, MouseEventArgs e) { if (e.StylusDevice == null) to = Pos(e); }

        void Leave(object s, MouseEventArgs e) => Off();

        void Enabled(object s, DependencyPropertyChangedEventArgs e) { if (!host.IsEnabled) Off(); }

        void Off()
        {
            b.BeginAnimation(UIElement.OpacityProperty, A(0, "TSpotOut", "EaseExit"));
            Track(false);
            Size(1, false);
        }

        // pressed: a tighter, 10% brighter light; back on release
        void Down(object s, MouseButtonEventArgs e) { if (e.StylusDevice == null) Size(D("SpotPress", 0.67), false); }
        void Up(object s, MouseButtonEventArgs e) => Size(1, false);

        void Size(double k, bool jump)
        {
            var r = GetSpotSize(b) * k;
            Go(br, RadialGradientBrush.RadiusXProperty, r, jump);
            Go(br, RadialGradientBrush.RadiusYProperty, r, jump);
            Tint(br, GetSpot(b), k < 1 ? 1.1 : 1);
        }

        void Track(bool on)
        {
            if (on == tracking) return;
            tracking = on;
            if (on) CompositionTarget.Rendering += Frame; else CompositionTarget.Rendering -= Frame;
        }

        void Frame(object? s, EventArgs e)
        {
            at += (to - at) * D("SpotLerp", 0.2);
            Place();
        }

        void Place() => br.Center = br.GradientOrigin = at;
    }
}
