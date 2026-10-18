using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Keys = System.Windows.Input.Key;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper;

// a class, not a record: Selector hashes its items, and value equality over the mutable Instance broke selection
public sealed class NavItem(string title, string glyph, Type view, string group, string tone = "") : INotifyPropertyChanged
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public Type View { get; } = view;
    /// <summary>Sidebar section; the header shows it as is. The unnamed one sits at the bottom of the sidebar.</summary>
    public string Group { get; } = group;
    /// <summary>Counter colour: light, hot or plain (grey).</summary>
    public string Tone { get; } = tone;
    public UserControl? Instance { get; set; }

    string? _badge;
    /// <summary>Counter on the right of the item; null hides it.</summary>
    public string? Badge { get => _badge; set { _badge = value; PropertyChanged?.Invoke(this, new(nameof(Badge))); PropertyChanged?.Invoke(this, new(nameof(HasBadge))); } }
    public bool HasBadge => !string.IsNullOrEmpty(_badge);
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    readonly List<NavItem> _items;

    public static MainWindow Current => (MainWindow)Application.Current.MainWindow;

    public MainWindow(string[] args)
    {
        // section titles per window: the language can change between windows (tests run ru and en)
        string programs = L.T("Программы"), cleaning = L.T("Очистка"), registry = L.T("Реестр"), system = L.T("Система");
        _items = new()
        {
            new(L.T("Деинсталлятор"), "", typeof(UninstallerView), programs),
            new(L.T("Приложения Windows"), "", typeof(StoreAppsView), programs, "light"),
            new(L.T("Автозагрузка"), "", typeof(StartupView), programs),
            new(L.T("Очистка диска"), "", typeof(DiskCleanupView), cleaning, "hot"),
            new(L.T("Чистка реестра"), "", typeof(RegCleanupView), cleaning, "hot"),
            new(L.T("Сжатие реестра"), "", typeof(CompactionView), cleaning),
            new(L.T("Редактор реестра"), "", typeof(RegEditorView), registry),
            new(L.T("Поиск и замена"), "", typeof(RegSearchView), registry),
            new(L.T("Снимки и трассы"), "", typeof(SnapshotsView), registry),
            new(L.T("Твики"), "", typeof(TweaksView), system),
            new(L.T("Центр отмены"), "", typeof(UndoView), ""),
            new(L.T("Настройки"), "", typeof(SettingsView), ""),
        };
        InitializeComponent();
        L.Apply(this);
        Nav.ItemsSource = _items;
        Nav.Items.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Group)));
        BackupCard.Visibility = ShowBackupCard ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => { SlideNav(); _ = LoadBadges(); _ = RefreshGuard(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Keys.K && Keyboard.Modifiers == ModifierKeys.Control) { Omni.Focus(); Omni.SelectAll(); e.Handled = true; }
        };
        StateChanged += (_, _) => Chrome();
        // a bare Application is the test host: no network from tests
        if (Application.Current is App)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            timer.Tick += (_, _) => _ = CheckUpdate();
            Loaded += (_, _) => { _ = CheckUpdate(); timer.Start(); };
        }
        // --uninstall "<file>" from the shell menu, --startup from the watcher toast
        if (args.Length >= 2 && args[0] == "--uninstall") Loaded += (_, _) => Show<UninstallerView>().FocusOn(args[1]);
        else if (args.Length >= 1 && args[0] == "--startup") Nav.SelectedIndex = 2;
        else Nav.SelectedIndex = 0;
        // from the update script in Update.Run
        if (args is ["--updated", ..]) Loaded += (_, _) => { Update.Finish(); Ui.Info(L.T($"RegKeeper обновлён до версии {Update.Current}.")); };
        else if (args is ["--update-failed", ..]) Loaded += (_, _) => { Update.Finish(); Ui.Info(L.T($"Обновление не установилось, работает прежняя версия {Update.Current}.")); };
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // Windows 11 shows snap layouts only over a non-client maximize button: the hook reports MaxBtn as HTMAXBUTTON,
    // so the click and the hover arrive as NC messages and are replayed here
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle).AddHook(SnapHook);
    }

    const int HtMaxButton = 9;

    IntPtr SnapHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case 0x84: // WM_NCHITTEST
                var p = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
                var over = MaxBtn.IsVisible && new Rect(MaxBtn.PointToScreen(new Point()), MaxBtn.PointToScreen(new Point(MaxBtn.ActualWidth, MaxBtn.ActualHeight))).Contains(p);
                SetMaxHover(over);
                if (!over) return IntPtr.Zero;
                handled = true;
                return HtMaxButton;
            case 0xA1 when wParam == HtMaxButton: // WM_NCLBUTTONDOWN
                handled = true;
                break;
            case 0xA2 when wParam == HtMaxButton: // WM_NCLBUTTONUP
                handled = true;
                Maximize_Click(MaxBtn, new RoutedEventArgs());
                break;
            case 0x2A2: // WM_NCMOUSELEAVE
                SetMaxHover(false);
                break;
        }
        return IntPtr.Zero;
    }

    void SetMaxHover(bool on)
    {
        if (on) { MaxBtn.Background = (Brush)MaxBtn.Tag; MaxBtn.Foreground = (Brush)FindResource("TextBrush"); }
        else { MaxBtn.ClearValue(BackgroundProperty); MaxBtn.ClearValue(ForegroundProperty); }
    }

    // a maximized custom-chrome window hangs past the screen by its resize border; the padding pulls the content back in
    void Chrome()
    {
        var max = WindowState == WindowState.Maximized;
        Padding = max ? SystemParameters.WindowResizeBorderThickness : new Thickness();
        MaxBtn.Content = max ? "\uE923" : "\uE922";
        MaxBtn.ToolTip = max ? L.T("Восстановить") : L.T("Развернуть");
        System.Windows.Automation.AutomationProperties.SetName(MaxBtn, (string)MaxBtn.ToolTip);
    }

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        item.Instance ??= (UserControl)Activator.CreateInstance(item.View)!;
        Motion.SwapPage(Host, Ghost, item.Instance);
        SlideNav();
    }

    void SlideNav()
    {
        if (Nav.Template.FindName("Pill", Nav) is FrameworkElement pill)
            Motion.Slide(pill, Container(Nav, Nav.SelectedItem));
    }

    // grouped lists hand out containers per group, so ContainerFromItem on the list itself finds nothing
    static ListBoxItem? Container(DependencyObject d, object? item)
    {
        if (d is ListBoxItem li && li.DataContext == item) return li;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Container(VisualTreeHelper.GetChild(d, i), item) is { } found) return found;
        return null;
    }

    /// <summary>Sets the counter of a sidebar item; views call it after they load or scan.</summary>
    public static void SetBadge(Type view, string? text)
    {
        if (Application.Current?.MainWindow is MainWindow w && w._items.FirstOrDefault(i => i.View == view) is { } item) item.Badge = text;
    }

    // store apps and enabled autoruns are counted on start; cleanup figures are the ones the last scan saved
    async Task LoadBadges()
    {
        if (long.TryParse(Reg.GetString(Key, "LastFreeable"), out var free) && free > 0) SetBadge(typeof(DiskCleanupView), Sys.Size(free));
        if (int.TryParse(Reg.GetString(Key, "LastProblems"), out var problems) && problems > 0) SetBadge(typeof(RegCleanupView), problems.ToString());
        try
        {
            var startup = await Task.Run(() => Autoruns.Load().Count(i => i.Enabled));
            SetBadge(typeof(StartupView), startup.ToString());
            var apps = await Task.Run(() => StoreApps.Load().Count(a => !a.IsSystem));
            SetBadge(typeof(StoreAppsView), apps.ToString());
        }
        catch { } // a counter is decoration: the module itself reports the failure when opened
    }

    async void Backup_Click(object sender, RoutedEventArgs e) => await Ui.Busy(BackupCard, async () =>
    {
        var ok = await Task.Run(() => Compaction.CreateRestorePoint(L.T("RegKeeper: резервная копия")));
        await RefreshGuard();
        Ui.Info(ok ? L.T("Точка восстановления создана.") : L.T("Не удалось создать точку восстановления. Включите защиту системы для диска C:."));
    });

    void HideBackup_Click(object sender, RoutedEventArgs e) => ShowBackupCard = false;

    /// <summary>Guard card: how many operations Undo Center can roll back (8 bars) and when the last restore point was made.</summary>
    public async Task RefreshGuard()
    {
        var (count, point) = await Task.Run(() => (UndoCenter.List().Count, Compaction.LastRestorePoint()));
        SetBadge(typeof(UndoView), count > 0 ? count.ToString() : null);
        GuardTitle.Text = L.T($"Можно отменить {count}");
        GuardBars.Children.Clear();
        for (var i = 0; i < 8; i++)
            GuardBars.Children.Add(new Border
            {
                Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 4, 0),
                Background = (Brush)FindResource(i < count ? "PrimaryBrush" : "Surface3Brush"),
            });
        GuardPoint.Text = L.T("Точка восстановления: ") + (point is not { } p ? L.T("нет")
            : p.Date == DateTime.Today ? L.T($"сегодня в {p:HH:mm}")
            : p.Date == DateTime.Today.AddDays(-1) ? L.T($"вчера в {p:HH:mm}")
            : p.ToString("d MMM yyyy"));
    }

    // Ctrl+K: the routes hand the text to the module's own search.
    // TODO data: no unified results with counts; each route searches in its module.
    void Omni_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = Omni.Text.Trim();
        OmniList.Items.Clear();
        if (q.Length == 0) { OmniPop.IsOpen = false; return; }
        void Add(string glyph, string text, Action go) => OmniList.Items.Add(new ListBoxItem
        {
            Tag = go, Padding = new Thickness(10, 8, 10, 8),
            Content = new DockPanel { Children =
            {
                new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), Margin = new Thickness(0, 2, 10, 0) },
                new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            } },
        });
        if (q.StartsWith("HK", StringComparison.OrdinalIgnoreCase) || q.Contains('\\')) Add("", L.T($"Открыть ключ {q}"), () => Show<RegEditorView>().Navigate(q));
        Add("", L.T($"Программы: {q}"), () => Show<UninstallerView>().Search.Text = q);
        Add("", L.T($"Твики: {q}"), () => Show<TweaksView>().Search.Text = q);
        Add("", L.T($"Искать в реестре: {q}"), () => { var v = Show<RegSearchView>(); v.Pattern.Text = q; v.Search_Click(v, new RoutedEventArgs()); });
        OmniList.SelectedIndex = 0;
        OmniPop.IsOpen = true;
    }

    void OmniGo(ListBoxItem? item)
    {
        if (item?.Tag is not Action go) return;
        OmniPop.IsOpen = false;
        Omni.Clear();
        go();
    }

    void Omni_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Keys.Enter) OmniGo(OmniList.SelectedItem as ListBoxItem ?? OmniList.Items.OfType<ListBoxItem>().FirstOrDefault());
        else if (e.Key == Keys.Down && OmniPop.IsOpen) { OmniList.SelectedIndex = Math.Min(OmniList.SelectedIndex + 1, OmniList.Items.Count - 1); }
        else if (e.Key == Keys.Up && OmniPop.IsOpen) { OmniList.SelectedIndex = Math.Max(OmniList.SelectedIndex - 1, 0); }
        else if (e.Key == Keys.Escape) { OmniPop.IsOpen = false; Omni.Clear(); Keyboard.ClearFocus(); }
        else return;
        e.Handled = true;
    }

    void OmniList_Click(object sender, MouseButtonEventArgs e) =>
        OmniGo(ItemsControl.ContainerFromElement(OmniList, (DependencyObject)e.OriginalSource) as ListBoxItem);

    void OmniList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Keys.Enter) return;
        OmniGo(OmniList.SelectedItem as ListBoxItem);
        e.Handled = true;
    }

    /// <summary>The backup card at the bottom of the sidebar; its cross hides it, Settings brings it back. HKCU\Software\RegKeeper\BackupCard.</summary>
    public static bool ShowBackupCard
    {
        get => Reg.GetString(Key, "BackupCard") != "0";
        set
        {
            Registry.SetValue(Key, "BackupCard", value ? "1" : "0");
            if (Application.Current?.MainWindow is MainWindow w) w.BackupCard.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    Update.Release? _offer;
    string? _offerMsi;

    // on start and every 6 hours: the release channel, or the newest MSI in the saved folder when this build has no channel
    async Task CheckUpdate()
    {
        if (!UpdateToast.IsEnabled) return; // already downloading the one on offer
        try
        {
            if (Update.Online) { if (await Update.Check(default) is { } r) OfferUpdate(r.Version, r, null); }
            else if (Update.Source is { } folder && await Task.Run(() => Update.Find(folder)) is { } msi) OfferUpdate(Update.VersionOf(msi)!.ToString(), null, msi);
        }
        catch { } // offline or the folder is gone: the next check, or the button in Settings, reports it
    }

    /// <summary>Shows the top-right card for a found update, again after its cross was used: every check that finds one offers it.</summary>
    public void OfferUpdate(string version, Update.Release? release, string? msi)
    {
        (_offer, _offerMsi) = (release, msi);
        UpdateTitle.Text = L.T($"Доступна версия {version}");
        if (UpdateToast.Visibility == Visibility.Visible) return;
        UpdateToast.Visibility = Visibility.Visible;
        if (!Motion.On) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        UpdateToast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        UpdateToast.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    void UpdateClose_Click(object sender, RoutedEventArgs e) => UpdateToast.Visibility = Visibility.Collapsed;

    async void UpdateGo_Click(object sender, RoutedEventArgs e)
    {
        var hint = UpdateHint.Text;
        await Ui.Busy(UpdateToast, async ct =>
        {
            var msi = _offerMsi ?? await Update.Download(_offer!, new Progress<int>(p => UpdateHint.Text = L.T($"Скачивание: {p} %")), ct);
            SettingsView.Launch(msi);
        });
        UpdateHint.Text = hint;
    }

    int _busy;

    /// <summary>Counts running jobs (Ui.Busy); the 2px line at the top runs while any is going.</summary>
    public void SetBusy(bool on)
    {
        _busy = Math.Max(0, _busy + (on ? 1 : -1));
        var run = (TranslateTransform)BusyRun.RenderTransform;
        if (_busy == 0) { BusyBar.Visibility = Visibility.Hidden; run.BeginAnimation(TranslateTransform.XProperty, null); return; }
        if (BusyBar.Visibility == Visibility.Visible) return;
        BusyBar.Visibility = Visibility.Visible;
        if (!Motion.On) return; // reduced motion: a still segment says the same
        run.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-BusyRun.Width, Math.Max(BusyBar.ActualWidth, 400), TimeSpan.FromSeconds(1.2))
            { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    internal const string Key = @"HKEY_CURRENT_USER\Software\RegKeeper";

    /// <summary>Closing the window hides it to the tray instead of quitting; off by default. HKCU\Software\RegKeeper\CloseToTray.</summary>
    public static bool CloseToTray
    {
        get => Reg.GetString(Key, "CloseToTray") == "1";
        set => Registry.SetValue(Key, "CloseToTray", value ? "1" : "0");
    }

    System.Windows.Forms.NotifyIcon? _tray;
    bool _quit;

    /// <summary>Closes for real, past the close-to-tray setting.</summary>
    public void Quit() { _quit = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        // a bare Application is the test host: it must really close
        if (!_quit && CloseToTray && Application.Current is App)
        {
            e.Cancel = true;
            Hide();
            Tray().Visible = true;
            return;
        }
        _tray?.Dispose();
        base.OnClosing(e);
    }

    System.Windows.Forms.NotifyIcon Tray()
    {
        if (_tray != null) return _tray;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(L.T("Открыть"), null, (_, _) => Restore());
        menu.Items.Add(L.T("Выход"), null, (_, _) => Quit());
        _tray = new() { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), Text = "RegKeeper", ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Restore(); };
        return _tray;
    }

    void Restore()
    {
        _tray!.Visible = false;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Switches to a module and returns it (e.g. "open in registry editor" from search results).</summary>
    public T Show<T>() where T : UserControl
    {
        var item = _items.First(i => i.View == typeof(T));
        Nav.SelectedItem = item;
        return (T)item.Instance!;
    }
}
