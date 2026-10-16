using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        L.Apply(this);
        (Theme.Saved switch { Theme.Light => ThemeLight, Theme.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        foreach (var p in Theme.Scales)
        {
            var r = new RadioButton { Content = p + " %", Tag = p, IsChecked = p == Theme.Scale };
            r.Checked += Scale_Checked;
            ScaleChoices.Children.Add(r);
        }
        Loaded += (_, _) => Motion.Slide(ScalePill, ScaleChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true));
        (L.Saved switch { L.Ru => LangRu, L.En => LangEn, _ => LangAuto }).IsChecked = true;
        ShellVerb.IsChecked = Integration.ShellVerb;
        Watcher.IsChecked = Integration.WatcherAutostart;
        Watcher.IsEnabled = File.Exists(Integration.WatcherExe);
        if (!Watcher.IsEnabled)
        {
            Watcher.ToolTip = L.T("Не найден ") + Integration.WatcherExe;
            ToolTipService.SetShowOnDisabled(Watcher, true);
        }
        DataInfo.Text = L.T($"{AppPaths.Data}\nЦентр отмены, снимки и трассы установок: {Sys.Size(Sys.DirSize(AppPaths.Data))}");
        About.Text = $"RegKeeper {typeof(SettingsView).Assembly.GetName().Version?.ToString(3)}, .NET {Environment.Version}";
    }

    void Theme_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(ThemePill, (FrameworkElement)s);
        var mode = (string)((RadioButton)s).Tag;
        if (mode == Theme.Saved) return; // the initial IsChecked from the constructor
        Theme.Saved = mode;
        Theme.Apply(Theme.Saved);
    }

    void Scale_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(ScalePill, (FrameworkElement)s);
        Theme.ApplyScale(Theme.Scale = (int)((RadioButton)s).Tag);
    }

    void Lang_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(LangPill, (FrameworkElement)s);
        var mode = (string)((RadioButton)s).Tag;
        if (mode == L.Saved) return;
        L.Saved = mode;
        if (IsLoaded) Ui.Info(L.T("Язык интерфейса сменится после перезапуска RegKeeper."));
    }

    // Checked/Unchecked (UI Automation toggles raise no Click); also fired by the constructor and the reverts below
    void ShellVerb_Changed(object s, RoutedEventArgs e)
    {
        if (ShellVerb.IsChecked == Integration.ShellVerb) return;
        try { Integration.SetShellVerb(ShellVerb.IsChecked == true); }
        catch (Exception ex) { Ui.Error(ex); }
        ShellVerb.IsChecked = Integration.ShellVerb;
    }

    void Watcher_Changed(object s, RoutedEventArgs e)
    {
        if (Watcher.IsChecked == Integration.WatcherAutostart) return;
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
