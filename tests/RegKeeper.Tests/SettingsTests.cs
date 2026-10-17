using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class SettingsTests
{
    [Fact]
    public void ShellVerb_RegistersAndRemoves()
    {
        var was = Integration.ShellVerb;
        try
        {
            Integration.SetShellVerb(true);
            Assert.True(Integration.ShellVerb);
            var cmd = Reg.GetString(@"HKEY_CURRENT_USER\Software\Classes\lnkfile\shell\RegKeeperUninstall\command", "");
            Assert.Equal($"\"{Integration.AppExe}\" --uninstall \"%1\"", cmd);
            Integration.SetShellVerb(false);
            Assert.False(Integration.ShellVerb);
            Assert.False(Reg.Exists(@"HKEY_CURRENT_USER\Software\Classes\exefile\shell\RegKeeperUninstall"));
        }
        finally { Integration.SetShellVerb(was); }
    }

    [Fact]
    public void WatcherAutostart_RegistersAndRemoves()
    {
        var was = Integration.WatcherAutostart;
        try
        {
            Integration.SetWatcherAutostart(true);
            Assert.Contains(Autoruns.RunAndFolders(), i => i.Name == "RegKeeper Watcher" && i.Command.Contains("RegKeeper.Watcher.exe"));
            Integration.SetWatcherAutostart(false);
            Assert.False(Integration.WatcherAutostart);
        }
        finally { Integration.SetWatcherAutostart(was); }
    }

    [Fact]
    public void Update_FindsNewestNewerMsi()
    {
        var dir = Directory.CreateTempSubdirectory("RegKeeperUpdate").FullName;
        try
        {
            var cur = Update.Current;
            Assert.Null(Update.Find(dir));
            foreach (var n in new[] { $"RegKeeper-{cur}-x64.msi", "RegKeeper-0.9.0-x64.msi", "RegKeeper-junk-x64.msi" })
                File.WriteAllText(Path.Combine(dir, n), "");
            Assert.Null(Update.Find(dir)); // same, older and unparsable are not updates
            foreach (var n in new[] { $"RegKeeper-{cur.Major}.{cur.Minor}.{cur.Build + 1}-x64.msi", $"RegKeeper-{cur.Major}.{cur.Minor + 1}.0-x64.msi" })
                File.WriteAllText(Path.Combine(dir, n), "");
            Assert.Equal($"RegKeeper-{cur.Major}.{cur.Minor + 1}.0-x64.msi", Path.GetFileName(Update.Find(dir)));
        }
        finally { Directory.Delete(dir, true); }
    }
}
