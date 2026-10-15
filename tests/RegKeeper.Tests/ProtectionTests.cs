using RegKeeper.Core;

namespace RegKeeper.Tests;

public class ProtectionTests
{
    [Fact]
    public void SystemKeys_AreRefused()
    {
        foreach (var k in new[]
                 {
                     "HKLM", @"HKLM\SOFTWARE", @"HKLM\SOFTWARE\Microsoft", @"HKCU\Software", @"HKLM\SAM",
                     @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", @"HKLM\SYSTEM\CurrentControlSet\Services",
                     @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager", @"HKCU\Software\Classes\CLSID",
                 })
            Assert.True(Protect.IsProtectedKey(k), k);

        foreach (var k in new[]
                 {
                     @"HKCU\Software\RegKeeperTest", @"HKLM\SOFTWARE\SomeVendor\App", @"HKLM\SYSTEM\CurrentControlSet\Services\SomeSvc",
                     @"HKCU\Software\Classes\CLSID\{00000000-1111-2222-3333-444444444444}",
                 })
            Assert.False(Protect.IsProtectedKey(k), k);

        Assert.True(Protect.IsProtectedValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"));
        Assert.False(Protect.IsProtectedValue(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run"));

        // the guard sits in UndoTx, so every module inherits it
        using (var tx = UndoTx.Begin("ProtectionTests"))
        {
            tx.DeleteKey(@"HKCU\Software\Microsoft");
            Assert.Single(tx.Errors);
            Assert.Equal(0, tx.Count);
        }
        Assert.True(Reg.Exists(@"HKCU\Software\Microsoft"));
        Assert.DoesNotContain(UndoCenter.List(), u => u.Title == "ProtectionTests");
    }

    [Fact]
    public void SystemFolders_AreRefused()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var p in new[]
                 {
                     "", @"C:\", win, Path.Combine(win, "System32"), Path.Combine(win, @"System32\drivers\x.sys"),
                     Path.Combine(win, "explorer.exe"), Path.Combine(win, @"WinSxS\a\b.txt"), Path.GetTempPath(),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))!,
                 })
            Assert.True(Protect.IsProtectedPath(p), p);

        foreach (var p in new[]
                 {
                     Path.Combine(Path.GetTempPath(), @"RegKeeperProtTest\a.txt"), Path.Combine(win, @"Temp\foo.log"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SomeVendor"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SomeVendor"),
                 })
            Assert.False(Protect.IsProtectedPath(p), p);

        Assert.False(Sys.Recycle(win));
        var dir = Path.Combine(Path.GetTempPath(), "RegKeeperProtTest");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "a.txt");
        File.WriteAllText(file, "x");
        Assert.False(Sys.Recycle(file, Path.GetTempPath())); // one protected path blocks the whole batch
        Assert.True(File.Exists(file));
        Assert.True(Sys.Recycle(dir));
        Assert.False(Directory.Exists(dir));
    }
}
