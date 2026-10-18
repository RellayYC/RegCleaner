using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class SmokeTests
{
    /// <summary>Opens every module in both themes and in English, lets their on-open loading finish; any dialog or exception fails.</summary>
    [Fact]
    public void EveryView_OpensInBothThemes() => Sta(() =>
    {
        var problems = new List<string>();
        Ui.Headless = problems.Add;
        Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => { problems.Add(e.Exception.ToString()); e.Handled = true; };
        // App.xaml's dictionaries on a bare Application: App itself opens a MainWindow once the dispatcher runs
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; // closing the Light window must not end the run
        foreach (var d in new[] { "Light", "Controls" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/RegKeeper;component/Themes/{d}.xaml") });
        Motion.Reduce(app.Resources.MergedDictionaries[1]); // as App does with animations off: screenshots show settled states
        var backgrounds = new List<Color>();
        try
        {
            foreach (var (theme, lang) in new[] { (Theme.Light, L.Ru), (Theme.Dark, L.Ru), (Theme.Dark, L.En) })
            {
                Theme.Apply(theme);
                L.Current = lang;
                var win = new MainWindow(Array.Empty<string>())
                    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, ShowActivated = false, ShowInTaskbar = false };
                app.MainWindow = win;
                win.Show();
                win.BackupCard.Visibility = Visibility.Visible; // the shots show the guard card even when the user hid it
                var nav = (ListBox)win.FindName("Nav");
                var host = (ContentControl)win.FindName("Host");
                var views = new List<UserControl>();
                for (var i = 0; i < nav.Items.Count; i++)
                {
                    nav.SelectedIndex = i;
                    Pump();
                    var view = (UserControl)host.Content;
                    Assert.True(nav.SelectedIndex == i && view.GetType() == ((NavItem)nav.Items[i]).View, $"{theme}: nav item {i} did not switch the view");
                    Assert.True(view.IsVisible && view.ActualWidth > 0, $"{theme}: {view.GetType().Name} not shown");
                    views.Add(view);
                }
                Assert.Equal(12, views.Count);
                // Ui.Busy disables a view while it loads (inventory, autoruns, store apps...)
                var sw = Stopwatch.StartNew();
                while (views.Any(v => !v.IsEnabled) && sw.Elapsed < TimeSpan.FromSeconds(120)) { Thread.Sleep(50); Pump(); }
                Assert.True(views.All(v => v.IsEnabled), "still loading: " + string.Join(", ", views.Where(v => !v.IsEnabled).Select(v => v.GetType().Name)));
                ShowScan(views.OfType<RegCleanupView>().Single());
                ShowSearch(views.OfType<RegSearchView>().Single());
                UpdateToastComesBack(win, $"{theme} {lang}");
                SnapLayoutsHitTest(win);
                if (theme == Theme.Dark) DialogKeys(win, lang);
                if (lang == L.En && Russian(win, views) is { Count: > 0 } ru) Assert.Fail("untranslated:\n" + string.Join("\n", ru));
                if (theme == Theme.Light) EditorNavigatesToNewSibling(views.OfType<RegEditorView>().Single());
                if (theme == Theme.Light) FilterFollowsAutomationToggle(views.OfType<StartupView>().Single());
                if (theme == Theme.Light) StickyGroupTitle(nav, views.IndexOf(views.OfType<TweaksView>().Single()), host);
                if (lang == L.Ru) UninstallerMenuAndDetails(nav, host, $"{theme}");
                if (lang == L.Ru) CursorLight(win, nav, views.IndexOf(views.OfType<DiskCleanupView>().Single()), $"{theme}");
                var store = views.OfType<StoreAppsView>().Single();
                nav.SelectedIndex = views.IndexOf(store); Pump();
                var storeGrid = (DataGrid)store.FindName("Grid");
                if (storeGrid.Items.Count > 0)
                {
                    storeGrid.SelectedIndex = 0; Pump();
                    Assert.True(((FrameworkElement)store.FindName("DetailsBody")).IsVisible, $"{theme}: store details stay empty after a selection");
                }
                if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir)
                {
                    for (var i = 0; i < views.Count; i++) { nav.SelectedIndex = i; Pump(); Shot(win, Path.Combine(dir, $"{lang}-{theme}-{i:00}-{views[i].GetType().Name}.png")); }
                    nav.SelectedIndex = views.IndexOf(store); Pump();
                    Pump();
                    Shot(win, Path.Combine(dir, $"{lang}-{theme}-01b-StoreAppsView-details.png"));
                    // the narrowest window: text wraps, nothing is cut
                    win.Width = win.MinWidth; Pump();
                    for (var i = 0; i < views.Count; i++) { nav.SelectedIndex = i; Pump(); Shot(win, Path.Combine(dir, $"narrow-{lang}-{theme}-{i:00}-{views[i].GetType().Name}.png")); }
                    win.Width = 1400; Pump();
                }
                Assert.True(problems.Count == 0, $"{theme}:\n" + string.Join("\n", problems));
                if (lang == L.Ru) backgrounds.Add(((SolidColorBrush)win.Background).Color);
                Assert.Equal((Color)app.Resources["Bg"], backgrounds[^1]);
                win.Close();
            }
            Assert.NotEqual(backgrounds[0], backgrounds[1]); // the swap really reached the window
        }
        finally
        {
            Ui.Headless = null;
            L.Current = L.Ru;
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });

    /// <summary>Untranslated interface text: the literal text L.Apply translates, plus the nav titles. Row data is not checked.</summary>
    static List<string> Russian(Window win, List<UserControl> views)
    {
        var found = new List<string>();
        void Check(object? o, string where) { if (o is string s && s != "Русский" && s.Any(c => c is >= 'А' and <= 'я' or 'ё' or 'Ё')) found.Add($"{where}: {s}"); }
        void Walk(object node)
        {
            if (node is FrameworkElement { Tag: "data" }) return; // row data drawn outside a grid (treemap blocks)
            if (node is Window w) Check(w.Title, "Title");
            if (node is TextBlock t) Check(t.Text, "Text");
            if (node is DataGrid g) foreach (var c in g.Columns) Check(c.Header, "Column");
            if (node is HeaderedContentControl hc) Check(hc.Header, "Header");
            if (node is HeaderedItemsControl hi) Check(hi.Header, "Header");
            if (node is ContentControl cc) Check(cc.Content, node.GetType().Name);
            if (node is FrameworkElement fe) { Check(fe.ToolTip, "ToolTip"); if (fe.ContextMenu != null) Walk(fe.ContextMenu); }
            if (node is DependencyObject d) foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
        }
        // template literals (cells, items): only the text written in XAML, not the bound data
        void Visual(DependencyObject d)
        {
            if (d is FrameworkElement { Tag: "data" }) return;
            if (d is TextBlock { TemplatedParent: not null } t && !BindingOperations.IsDataBound(t, TextBlock.TextProperty))
                foreach (var r in t.Inlines.OfType<System.Windows.Documents.Run>().Where(r => !BindingOperations.IsDataBound(r, System.Windows.Documents.Run.TextProperty)))
                    Check(r.Text, "Template");
            if (d is FrameworkElement { TemplatedParent: not null } fe && !BindingOperations.IsDataBound(fe, FrameworkElement.ToolTipProperty)) Check(fe.ToolTip, "Template ToolTip");
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Visual(VisualTreeHelper.GetChild(d, i));
        }
        Walk(win);
        views.ForEach(Walk);
        var nav = (ListBox)win.FindName("Nav");
        for (var i = 0; i < views.Count; i++) { nav.SelectedIndex = i; Pump(); Visual(views[i]); }
        foreach (NavItem n in nav.Items) Check(n.Title, "Nav");
        return found;
    }

    /// <summary>B9: a key created after its parent was loaded (copy target) must get selected, not its parent.</summary>
    static void EditorNavigatesToNewSibling(RegEditorView ed)
    {
        const string parent = @"HKEY_CURRENT_USER\Software\RegKeeperTest";
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\RegKeeperTest", false);
        Registry.CurrentUser.CreateSubKey(@"Software\RegKeeperTest\A").Dispose();
        try
        {
            ed.Navigate(parent + @"\A");
            Registry.CurrentUser.CreateSubKey(@"Software\RegKeeperTest\B").Dispose();
            ed.Navigate(parent + @"\B");
            Assert.Equal(parent + @"\B", ((TextBox)ed.FindName("Address")).Text);
            // B14: a missing or unreadable key says so instead of silently stopping at its parent
            var said = new List<string>();
            var prev = Ui.Headless;
            Ui.Headless = said.Add;
            try { ed.Navigate(parent + @"\Missing\Deeper"); }
            finally { Ui.Headless = prev; }
            Assert.Contains(said, m => m.Contains(@"RegKeeperTest\Missing\Deeper"));
            Assert.Equal(parent, ((TextBox)ed.FindName("Address")).Text);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\RegKeeperTest", false); }
    }

    /// <summary>B12: a source segment selected through UI Automation (screen readers, scripts) must refilter the list.</summary>
    static void FilterFollowsAutomationToggle(StartupView view)
    {
        var registry = (RadioButton)view.FindName("KindRegistry");
        var all = (RadioButton)((Panel)view.FindName("Kinds")).Children[0];
        var grid = (DataGrid)view.FindName("Grid");
        Assert.Contains(grid.Items.Cast<StartupItem>(), i => i.Kind == StartupKind.Service);
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(registry)).Select();
        Pump();
        try { Assert.DoesNotContain(grid.Items.Cast<StartupItem>(), i => i.Kind == StartupKind.Service); }
        finally { ((ISelectionItemProvider)new RadioButtonAutomationPeer(all)).Select(); Pump(); }
    }

    /// <summary>Scrolled into a group, the grouped tweaks grid pins that group's title; at the top it shows none.</summary>
    static void StickyGroupTitle(ListBox nav, int index, ContentControl host)
    {
        nav.SelectedIndex = index;
        Pump();
        var grid = (DataGrid)((UserControl)host.Content).FindName("Grid");
        var sv = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid);
        var bar = (Border)sv.Template.FindName("Sticky", sv);
        sv.ScrollToVerticalOffset(sv.ScrollableHeight / 2);
        Pump();
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.IsAssignableFrom<CollectionViewGroup>(((ContentPresenter)bar.Child).Content);
        sv.ScrollToVerticalOffset(0);
        Pump();
        Assert.Equal(Visibility.Collapsed, bar.Visibility);
    }

    /// <summary>Hovering a plain button, the delete button and a sidebar item lights their overlays (reduced motion: at once,
    /// in the middle); leaving puts them out.</summary>
    static void CursorLight(MainWindow win, ListBox nav, int index, string theme)
    {
        nav.SelectedIndex = index;
        Pump();
        static IEnumerable<DependencyObject> Down(DependencyObject d)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                yield return c;
                foreach (var g in Down(c)) yield return g;
            }
        }
        static List<Border> Lights(DependencyObject host) => Down(host).OfType<Border>().Where(b => Motion.GetSpot(b).A > 0).ToList();
        var buttons = Down(win).OfType<Button>().Where(b => b.IsVisible && b.IsEnabled).ToList();
        var danger = buttons.First(b => b.Style?.BasedOn == win.FindResource("DangerBtn")); // SmallDanger in the selection bar
        var plain = buttons.First(b => b.Style == win.FindResource(typeof(Button)));
        var item = (ListBoxItem)nav.ItemContainerGenerator.ContainerFromIndex(1);
        var hosts = new UIElement[] { plain, danger, item };
        Assert.Equal(new[] { 2, 2, 1 }, hosts.Select(h => Lights(h).Count));
        // even a zero-length fade lands on a render tick, which an offscreen window gets late
        bool Settle(double to)
        {
            var sw = Stopwatch.StartNew();
            while (!hosts.SelectMany(Lights).All(b => b.Opacity == to) && sw.Elapsed < TimeSpan.FromSeconds(2)) { Thread.Sleep(20); Pump(); }
            return hosts.SelectMany(Lights).All(b => b.Opacity == to);
        }
        foreach (var h in hosts) h.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
        Assert.True(Settle(1), $"{theme}: hovered lights not on");
        if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir) Shot(win, Path.Combine(dir, $"light-{theme}.png"));
        foreach (var h in hosts) h.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
        Assert.True(Settle(0), $"{theme}: lights stay on after leaving");
    }

    /// <summary>The uninstaller's right-click menu opens themed, and "Подробнее" shows the selected program.</summary>
    static void UninstallerMenuAndDetails(ListBox nav, ContentControl host, string theme)
    {
        nav.SelectedIndex = 0;
        Pump();
        var grid = (DataGrid)((UserControl)host.Content).FindName("Grid");
        var app = grid.Items.Cast<InstalledApp>().First(a => a.Publisher != "");
        grid.SelectedItem = app;
        var menu = grid.ContextMenu;
        menu.PlacementTarget = grid;
        menu.IsOpen = true;
        Pump();
        try
        {
            Assert.Equal(7, menu.Items.OfType<MenuItem>().Count());
            Assert.True(menu.ActualWidth >= 210, "menu template not applied");
            if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir) ShotOf(menu, Path.Combine(dir, $"menu-{theme}.png"));
        }
        finally { menu.IsOpen = false; }

        var details = new AppDetailsWindow(app) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, ShowActivated = false, ShowInTaskbar = false };
        details.Show();
        Pump();
        try
        {
            Assert.Equal(app.Publisher, ((TextBlock)details.FindName("Author")).Text);
            Assert.NotEqual("", ((TextBlock)details.FindName("FilePath")).Text);
            if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir) Shot(details, Path.Combine(dir, $"details-{theme}.png"));
        }
        finally { details.Close(); }
    }

    // the update card sits top right, its cross hides it, and the next found update brings it back
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    // snap layouts need the maximize button to answer WM_NCHITTEST as HTMAXBUTTON
    static void SnapLayoutsHitTest(MainWindow win)
    {
        var max = (Button)win.FindName("MaxBtn");
        var p = max.PointToScreen(new Point(max.ActualWidth / 2, max.ActualHeight / 2));
        var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        var lp = (IntPtr)(((int)p.Y << 16) | ((int)p.X & 0xFFFF));
        Assert.Equal(9, (int)SendMessage(hwnd, 0x84, IntPtr.Zero, lp));
    }

    static void UpdateToastComesBack(MainWindow win, string where)
    {
        var toast = (Border)win.FindName("UpdateToast");
        Assert.False(toast.IsVisible, $"{where}: update card shown with nothing to offer");
        for (var i = 0; i < 2; i++)
        {
            win.OfferUpdate($"9.9.{i}", null, @"C:\none.msi");
            Pump();
            Assert.True(toast.IsVisible, $"{where}: update card not shown on offer {i}");
            Assert.Contains($"9.9.{i}", ((TextBlock)win.FindName("UpdateTitle")).Text);
            var page = (FrameworkElement)win.Content;
            var corner = toast.TranslatePoint(new Point(toast.ActualWidth, 0), page); // under the 34px title bar
            Assert.True(page.ActualWidth - corner.X < 40 && corner.Y < 60, $"{where}: update card at {corner}, not top right of {page.ActualWidth}");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)win.FindName("UpdateClose")).GetPattern(PatternInterface.Invoke)).Invoke();
            Pump();
            Assert.False(toast.IsVisible, $"{where}: the cross did not hide the update card");
        }
        win.OfferUpdate("9.9.9", null, @"C:\none.msi"); // left open: the English pass checks its texts are translated
    }

    // at 125-150 % the 1400x900 window outgrew the screen and centring put its title bar above the top edge
    [Fact]
    public void Fit_KeepsAnOversizedWindowOnScreen() => Sta(() =>
    {
        var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var win = new Window { WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false,
            Left = work.Left - 200, Top = work.Top - 300, Width = 8000, Height = 6000, MinWidth = 980, MinHeight = 8000 };
        win.Show();
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            Theme.GetWindowRect(h, out var r);
            Assert.True(r.Top < work.Top && r.Bottom - r.Top > work.Height, $"positive control: {r.Left},{r.Top},{r.Right},{r.Bottom} already fits {work}");
            Theme.Fit(win);
            Theme.GetWindowRect(h, out r);
            Assert.True(r.Left >= work.Left && r.Top >= work.Top && r.Right <= work.Right && r.Bottom <= work.Bottom, $"{r.Left},{r.Top},{r.Right},{r.Bottom} outside {work}");
            Assert.True(win.MinHeight < 8000, "MinHeight still forces the window past the screen");
        }
        finally { win.Close(); }
    });

    [Fact]
    public void Busy_EscCancelsWithoutError() => Sta(() =>
    {
        // continuations must come back to this thread, as they do in the app
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var problems = new List<string>();
        Ui.Headless = problems.Add;
        var owner = new Button();
        var win = new Window { Title = "T", Content = owner, Left = -20000, ShowInTaskbar = false };
        win.Show();
        var done = false;
        string? busyTitle = null;
        _ = Run();
        async Task Run()
        {
            await Ui.Busy(owner, async ct => { busyTitle = win.Title; await Task.Delay(-1, ct); });
            done = true;
        }
        Pump();
        var target = (IInputElement?)Keyboard.FocusedElement ?? win;
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(win), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Pump();
        try
        {
            Assert.True(done, "Esc did not cancel");
            Assert.Contains("Esc", busyTitle);
            Assert.Equal("T", win.Title);
            Assert.True(owner.IsEnabled);
            Assert.Empty(problems);
        }
        finally
        {
            Ui.Headless = null;
            win.Close();
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });

    [Fact]
    public void Theme_PersistsAndResolves()
    {
        const string key = @"HKEY_CURRENT_USER\Software\RegKeeper";
        var before = Reg.GetString(key, "Theme");
        try
        {
            Theme.Saved = Theme.Dark;
            Assert.Equal(Theme.Dark, Theme.Saved);
            Assert.Equal(Theme.Dark, Theme.Resolve(Theme.Saved));
            Assert.Equal(Theme.Light, Theme.Resolve(Theme.Light));
            Registry.SetValue(key, "Theme", "Purple");
            Assert.Equal(Theme.System, Theme.Saved);
            Assert.Contains(Theme.Resolve(Theme.System), new[] { Theme.Light, Theme.Dark });
        }
        finally
        {
            if (before != null) Registry.SetValue(key, "Theme", before);
            else Registry.CurrentUser.OpenSubKey(@"Software\RegKeeper", true)?.DeleteValue("Theme", false);
        }
    }

    /// <summary>RK_SHOTS=<folder> dumps every view as PNG for a visual review.</summary>
    /// <summary>The compaction dialog over the main window: Esc and × give -1, the primary button its index and is the default (Enter).</summary>
    static void DialogKeys(Window win, string lang)
    {
        Dialog Open()
        {
            var d = new Dialog(L.T("Сжать реестр?"), [L.T("Отмена"), L.T("Сжать")], 1, "20,1 МБ", L.T("освободится в 5 разделах"),
                (121_600_000, 101_600_000), L.T("Закройте все программы. Файлы заменятся при перезагрузке.")) { ShowActivated = false };
            d.Cover(win);
            d.Show();
            Pump();
            return d;
        }
        var d = Open();
        var buttons = ((StackPanel)d.FindName("Buttons")).Children.OfType<Button>().ToList();
        Assert.True(buttons[1].IsDefault && !buttons[0].IsDefault, "Enter must press the primary button");
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation((DependencyObject)d.FindName("Card")));
        Assert.Equal(win.ActualWidth, d.Width, 1);
        if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir)
        {
            ((FrameworkElement)d.FindName("Root")).BeginAnimation(UIElement.OpacityProperty, null);
            ((FrameworkElement)d.FindName("Root")).Opacity = 1;
            Pump();
            ShotOf((FrameworkElement)d.Content, Path.Combine(dir, $"dialog-{lang}-compaction.png"));
        }
        d.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(d), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Assert.False(d.IsVisible, "Esc must close the dialog");
        Assert.Equal(-1, d.Result);
        d = Open();
        ((Button)d.FindName("X")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(-1, d.Result);
        d = Open();
        ((StackPanel)d.FindName("Buttons")).Children.OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, d.Result);
        Assert.False(d.IsVisible);
    }

    static void Shot(Window win, string file)
    {
        var content = (FrameworkElement)win.Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual(); // the window background is not part of its content
        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            dc.DrawRectangle(win.Background, null, rect);
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, Viewbox = rect, ViewboxUnits = BrushMappingMode.Absolute }, null, rect);
        }
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var f = File.Create(file);
        png.Save(f);
    }

    static void ShotOf(FrameworkElement e, string file)
    {
        var bmp = new RenderTargetBitmap((int)e.ActualWidth, (int)e.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var f = File.Create(file);
        png.Save(f);
    }

    /// <summary>The cleanup list filled with a real (read-only) scan, without the Scan button: that one saves the result
    /// and the time into the user's own settings.</summary>
    static void ShowScan(RegCleanupView view)
    {
        var t = typeof(RegCleanupView);
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetField("_all", F)!.SetValue(view, RegScanner.Scan());
        t.GetField("_scanned", F)!.SetValue(view, true);
        t.GetMethod("ApplyFilter", F)!.Invoke(view, null);
        Pump();
    }

    /// <summary>A real read-only search in one small branch, so the shot shows grouped hits with the match lit.</summary>
    static void ShowSearch(RegSearchView view)
    {
        T<TextBox>(view, "Pattern").Text = "Explorer";
        T<CheckBox>(view, "Custom").IsChecked = true;
        typeof(RegSearchView).GetMethod("Custom_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, [null!, null!]);
        T<TextBox>(view, "Roots").Text = @"HKCR\Folder; HKCR\Directory";
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); // the await comes back here, as in the app
        view.Search_Click(view, new RoutedEventArgs());
        var sw = Stopwatch.StartNew();
        while (!Equals(T<Button>(view, "SearchBtn").Content, L.T("Искать")) && sw.Elapsed < TimeSpan.FromSeconds(30)) { Thread.Sleep(50); Pump(); }
        Assert.True(T<DataGrid>(view, "Grid").Items.Count > 0, "search found nothing in Explorer keys");
    }

    static T T<T>(FrameworkElement v, string name) where T : class => (T)v.FindName(name);

    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    static void Sta(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }
}
