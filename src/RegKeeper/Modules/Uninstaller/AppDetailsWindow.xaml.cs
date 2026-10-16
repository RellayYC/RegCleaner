using System.Windows;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>Uninstaller "Подробнее": the program's exe with a jump to it, sizes, who it is installed for and its publisher.</summary>
public partial class AppDetailsWindow : Window
{
    readonly string? _target;

    public AppDetailsWindow(InstalledApp app)
    {
        InitializeComponent();
        L.Apply(this);
        Theme.TitleBar(this);
        Owner = Application.Current.MainWindow;
        var unknown = L.T("неизвестно");
        Title = app.Name;
        Ico.Source = app.Icon;
        AppName.Text = app.Name;
        AppVersion.Text = app.Version;

        var exe = app.MainExe;
        _target = exe ?? (app.InstallDir is { } d && Directory.Exists(d) ? d : null);
        FilePath.Text = _target ?? unknown;
        Go.IsEnabled = _target != null;
        FileSize.Text = exe != null ? Sys.Size(new FileInfo(exe).Length) : unknown;
        AppSize.Text = app.SizeText is { Length: > 0 } size ? size : unknown;
        ForUser.Text = app.KeyPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
            ? L.T($"{Environment.UserName} (только этот пользователь)") : app.Scope;
        Author.Text = app.Publisher is { Length: > 0 } p ? p : exe != null && Sys.Company(exe) is { Length: > 0 } c ? c : unknown;
        Date.Text = app.DateText is { Length: > 0 } t ? t : unknown;
        KeyPath.Text = app.KeyPath;
    }

    void Go_Click(object s, RoutedEventArgs e)
    {
        if (File.Exists(_target)) Sys.ShowInExplorer(_target);
        else if (_target != null) Sys.Open(_target);
    }
}
