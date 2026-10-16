using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RegKeeper.Modules;

namespace RegKeeper;

// a class, not a record: Selector hashes its items, and value equality over the mutable Instance broke selection
public sealed class NavItem(string title, string glyph, Type view)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public Type View { get; } = view;
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    readonly List<NavItem> _items = new()
    {
        new(L.T("Деинсталлятор"), "", typeof(UninstallerView)),
        new(L.T("Приложения Windows"), "", typeof(StoreAppsView)),
        new(L.T("Автозагрузка"), "", typeof(StartupView)),
        new(L.T("Очистка диска"), "", typeof(DiskCleanupView)),
        new(L.T("Чистка реестра"), "", typeof(RegCleanupView)),
        new(L.T("Редактор реестра"), "", typeof(RegEditorView)),
        new(L.T("Поиск и замена"), "", typeof(RegSearchView)),
        new(L.T("Снимки и трассы"), "", typeof(SnapshotsView)),
        new(L.T("Твики"), "", typeof(TweaksView)),
        new(L.T("Сжатие реестра"), "", typeof(CompactionView)),
        new(L.T("Центр отмены"), "", typeof(UndoView)),
        new(L.T("Настройки"), "", typeof(SettingsView)),
    };

    public static MainWindow Current => (MainWindow)Application.Current.MainWindow;

    public MainWindow(string[] args)
    {
        InitializeComponent();
        L.Apply(this);
        Nav.ItemsSource = _items;
        Loaded += (_, _) => SlideNav();
        // --uninstall "<file>" from the shell menu, --startup from the watcher toast
        if (args.Length >= 2 && args[0] == "--uninstall") Loaded += (_, _) => Show<UninstallerView>().FocusOn(args[1]);
        else if (args.Length >= 1 && args[0] == "--startup") Nav.SelectedIndex = 2;
        else Nav.SelectedIndex = 0;
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
            Motion.Slide(pill, Nav.ItemContainerGenerator.ContainerFromItem(Nav.SelectedItem) as FrameworkElement);
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

    /// <summary>Switches to a module and returns it (e.g. "open in registry editor" from search results).</summary>
    public T Show<T>() where T : UserControl
    {
        var item = _items.First(i => i.View == typeof(T));
        Nav.SelectedItem = item;
        return (T)item.Instance!;
    }
}
