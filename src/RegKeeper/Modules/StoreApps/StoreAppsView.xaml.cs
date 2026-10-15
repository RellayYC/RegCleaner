using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class StoreAppsView : UserControl
{
    List<StoreApp> _all = new();

    public StoreAppsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload()
    {
        try
        {
            await Ui.Busy(this, async () => _all = await Task.Run(() =>
            {
                var apps = StoreApps.Load();
                StoreApps.FillSizes(apps);
                return apps.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            }));
        }
        catch (Exception e) { Ui.Error(e); }
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = _all.Where(a => (ShowSystem.IsChecked == true || !a.IsSystem)
                                   && (q == "" || (a.Title + " " + a.Name + " " + a.PublisherName).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = list;
        Status.Text = $"Приложений: {list.Count}   Размер: {Sys.Size(list.Sum(a => a.SizeBytes))}";
    }

    void Search_TextChanged(object s, RoutedEventArgs e) => ApplyFilter();

    async void Remove_Click(object s, RoutedEventArgs e)
    {
        var apps = Grid.SelectedItems.Cast<StoreApp>().ToList();
        if (apps.Count == 0) return;
        if (apps.FirstOrDefault(a => a.IsSystem) is { } sys) { Ui.Info($"«{sys.Title}» - системный пакет, его удалять нельзя."); return; }
        if (!Ui.Confirm("Удалить приложения?\n" + string.Join("\n", apps.Select(a => a.Title)) + "\n\nВернуть их можно только из Microsoft Store.")) return;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            foreach (var a in apps)
                if (StoreApps.Remove(a) is { } err) errors.Add($"{a.Title}: {err}");
        }));
        if (errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", errors));
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is StoreApp { InstallLocation: { } dir } && Directory.Exists(dir)) Sys.Open(dir);
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();
}
