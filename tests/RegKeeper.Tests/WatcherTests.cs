using Microsoft.Win32;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class WatcherTests
{
    [Fact]
    public void DetectsNewRunEntry_IgnoresKnown()
    {
        const string name = "RegKeeperWatchTest";
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!;
        run.DeleteValue(name, false);
        var before = StartupWatch.Ids(Autoruns.RunAndFolders());
        Assert.Empty(StartupWatch.Added(Autoruns.RunAndFolders(), before));
        try
        {
            run.SetValue(name, @"C:\nowhere\x.exe /y");
            var added = StartupWatch.Added(Autoruns.RunAndFolders(), before);
            Assert.Equal(name, Assert.Single(added).Name);

            run.SetValue(name, @"C:\nowhere\x.exe /z"); // changed command counts as new
            Assert.Single(StartupWatch.Added(Autoruns.RunAndFolders(), before));
        }
        finally { run.DeleteValue(name, false); }
    }
}
