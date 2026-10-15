using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ShellVerb.IsChecked = Integration.ShellVerb;
        Watcher.IsChecked = Integration.WatcherAutostart;
        Watcher.IsEnabled = File.Exists(Integration.WatcherExe);
        DataInfo.Text = $"{AppPaths.Data}\nЦентр отмены, снимки и трассы установок: {Sys.Size(Sys.DirSize(AppPaths.Data))}";
        About.Text = $"RegKeeper {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)}, .NET {Environment.Version}";
    }

    void ShellVerb_Click(object s, RoutedEventArgs e)
    {
        try { Integration.SetShellVerb(ShellVerb.IsChecked == true); }
        catch (Exception ex) { Ui.Error(ex); }
        ShellVerb.IsChecked = Integration.ShellVerb;
    }

    void Watcher_Click(object s, RoutedEventArgs e)
    {
        try
        {
            Integration.SetWatcherAutostart(Watcher.IsChecked == true);
            if (Watcher.IsChecked == true) Integration.StartWatcher();
        }
        catch (Exception ex) { Ui.Error(ex); }
        Watcher.IsChecked = Integration.WatcherAutostart;
    }

    void Data_Click(object s, RoutedEventArgs e) => Sys.Open(AppPaths.Data);
}
