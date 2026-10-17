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

    // signed by Node the way installer/release.mjs signs (P-256, IEEE P1363), so the two formats stay in step
    [Fact]
    public void Update_AcceptsOnlySignedManifest()
    {
        const string pub = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEZdhov6nLyV1tgq4opwt+xzHQ6oDMl8OKQCSc2LmpVKipB7YLGSTL8f9T7ZVTQotEJ+e/FbsgWxaI0Zp++nFLnA==";
        var sig = Convert.FromBase64String("g8DVpG3iAOkfRS4umZ3+UCWg03LRK1aubdtgtBp+bGMmkfVkP/yAzCULhE2czew8N9+Kqq08Dwg81hIk0/SbrA==");
        var json = "{\"version\":\"9.9.9\",\"file\":\"RegKeeper-9.9.9-x64.msi\",\"sha256\":\"ab12\"}"u8.ToArray();
        Assert.Equal(new Update.Release("9.9.9", "RegKeeper-9.9.9-x64.msi", "ab12"), Update.Verify(json, sig, pub));
        json[13] = (byte)'8'; // 9.8.9
        Assert.Null(Update.Verify(json, sig, pub));
    }

    [Fact]
    public void Update_FindsTheNewestMsiAboveThisBuild()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Null(Update.Find(dir));
            foreach (var n in new[] { "RegKeeper-0.0.1-x64.msi", "RegKeeper-99.0.0-x64.msi", "RegKeeper-100.0.0-x64.msi", "RegKeeper-x-x64.msi", "Other-200.0.0-x64.msi" })
                File.WriteAllText(Path.Combine(dir, n), "");
            Assert.Equal("RegKeeper-100.0.0-x64.msi", Path.GetFileName(Update.Find(dir)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Update_UnmasksTheBuildSeed_AndSignsLikeTheWorker()
    {
        // seed made by build.ps1's masking, HMAC by node's crypto.createHmac (what worker.js verifies)
        const string pub = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEZdhov6nLyV1tgq4opwt+xzHQ6oDMl8OKQCSc2LmpVKipB7YLGSTL8f9T7ZVTQotEJ+e/FbsgWxaI0Zp++nFLnA==";
        var key = Update.Unmask("JMMMQTkW+sGCVAFgieghxSWMMDDn7i5BowemSgdK6kUkwwxBORb6wYJUAWCJ6CHF", pub);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef", System.Text.Encoding.UTF8.GetString(key));
        Assert.Equal("1700000000.13f610556385980b034cfb1fca98554bbf358de995306e1c8c3cebe67de29bbf", Update.Auth(key, "latest.txt", 1700000000));
    }
}
