using System.Windows;
using System.Windows.Controls;
using RegKeeper.Modules;

namespace RegKeeper;

public sealed record NavItem(string Title, string Glyph, Type View)
{
    public UserControl? Instance { get; set; }
}

public partial class MainWindow : Window
{
    readonly List<NavItem> _items = new()
    {
        new("Деинсталлятор", "", typeof(UninstallerView)),
        new("Приложения Windows", "", typeof(StoreAppsView)),
        new("Автозагрузка", "", typeof(StartupView)),
        new("Очистка диска", "", typeof(DiskCleanupView)),
        new("Чистка реестра", "", typeof(RegCleanupView)),
        new("Редактор реестра", "", typeof(RegEditorView)),
        new("Поиск и замена", "", typeof(RegSearchView)),
        new("Снимки и трассы", "", typeof(SnapshotsView)),
        new("Твики", "", typeof(TweaksView)),
        new("Сжатие реестра", "", typeof(CompactionView)),
        new("Центр отмены", "", typeof(UndoView)),
        new("Настройки", "", typeof(SettingsView)),
    };

    public static MainWindow Current => (MainWindow)Application.Current.MainWindow;

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Nav.ItemsSource = _items;
        // --uninstall "<file>" from the shell menu, --startup from the watcher toast
        if (args.Length >= 2 && args[0] == "--uninstall") Loaded += (_, _) => Show<UninstallerView>().FocusOn(args[1]);
        else if (args.Length >= 1 && args[0] == "--startup") Nav.SelectedIndex = 2;
        else Nav.SelectedIndex = 0;
    }

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        item.Instance ??= (UserControl)Activator.CreateInstance(item.View)!;
        Host.Content = item.Instance;
    }

    /// <summary>Switches to a module and returns it (e.g. "open in registry editor" from search results).</summary>
    public T Show<T>() where T : UserControl
    {
        var item = _items.First(i => i.View == typeof(T));
        Nav.SelectedItem = item;
        return (T)item.Instance!;
    }
}
