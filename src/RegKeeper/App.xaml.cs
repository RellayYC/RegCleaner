using System.Windows;
using System.Windows.Threading;

namespace RegKeeper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Ui.Error(ex.Exception);
            ex.Handled = true;
        };
        if (!Motion.On) Motion.Reduce(Resources.MergedDictionaries[1]);
        Theme.Apply(Theme.Saved);
        var w = new MainWindow(e.Args);
        Theme.TitleBar(w);
        w.Show();
    }
}
