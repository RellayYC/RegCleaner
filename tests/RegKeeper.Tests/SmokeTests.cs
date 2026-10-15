using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
    /// <summary>Opens every module in both themes, lets their on-open loading finish; any dialog or exception fails.</summary>
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
        var backgrounds = new List<Color>();
        try
        {
            foreach (var theme in new[] { Theme.Light, Theme.Dark })
            {
                Theme.Apply(theme);
                var win = new MainWindow(Array.Empty<string>())
                    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, ShowActivated = false, ShowInTaskbar = false };
                app.MainWindow = win;
                win.Show();
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
                if (theme == Theme.Light) EditorNavigatesToNewSibling(views.OfType<RegEditorView>().Single());
                if (theme == Theme.Light) FilterFollowsAutomationToggle(views.OfType<StartupView>().Single());
                if (Environment.GetEnvironmentVariable("RK_SHOTS") is { Length: > 0 } dir)
                    for (var i = 0; i < views.Count; i++) { nav.SelectedIndex = i; Pump(); Shot(win, Path.Combine(dir, $"{theme}-{i:00}-{views[i].GetType().Name}.png")); }
                Assert.True(problems.Count == 0, $"{theme}:\n" + string.Join("\n", problems));
                backgrounds.Add(((SolidColorBrush)win.Background).Color);
                Assert.Equal((Color)app.Resources["Bg"], backgrounds[^1]);
                win.Close();
            }
            Assert.NotEqual(backgrounds[0], backgrounds[1]); // the swap really reached the window
        }
        finally
        {
            Ui.Headless = null;
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    });

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
            Assert.Equal(parent + @"\B", ((TreeViewItem)((TreeView)ed.FindName("Tree")).SelectedItem).Tag);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(@"Software\RegKeeperTest", false); }
    }

    /// <summary>B12: a filter checkbox toggled through UI Automation (screen readers, scripts) must refilter the list.</summary>
    static void FilterFollowsAutomationToggle(StartupView view)
    {
        var services = (CheckBox)view.FindName("ShowServices");
        var grid = (DataGrid)view.FindName("Grid");
        Assert.Contains(grid.Items.Cast<StartupItem>(), i => i.Kind == StartupKind.Service);
        ((IToggleProvider)new CheckBoxAutomationPeer(services)).Toggle();
        Pump();
        try { Assert.DoesNotContain(grid.Items.Cast<StartupItem>(), i => i.Kind == StartupKind.Service); }
        finally { ((IToggleProvider)new CheckBoxAutomationPeer(services)).Toggle(); Pump(); }
    }

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
    static void Shot(Window win, string file)
    {
        var content = (FrameworkElement)win.Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual(); // the window background is not part of its content
        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            dc.DrawRectangle(win.Background, null, rect);
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
        }
        bmp.Render(dv);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var f = File.Create(file);
        png.Save(f);
    }

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
