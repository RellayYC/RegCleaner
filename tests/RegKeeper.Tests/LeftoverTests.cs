using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class LeftoverTests : IDisposable
{
    readonly string _base = Path.Combine(Path.GetTempPath(), "RegKeeperTest_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_base)) Directory.Delete(_base, true); }

    string Dir(string rel)
    {
        var p = Path.Combine(_base, rel);
        Directory.CreateDirectory(p);
        return p;
    }

    [Theory]
    [InlineData("Mozilla Firefox 128.0.3 (x64 ru)", "Mozilla", "mozillafirefox", "firefox")]
    [InlineData("Notepad++ (64-bit x64)", "Notepad++ Team", "notepad", null)]
    [InlineData("7-Zip 24.08 (x64)", "Igor Pavlov", "7zip", null)]
    [InlineData("Discord", "Discord Inc.", "discord", null)]
    public void NameTokens(string name, string pub, string expected, string? alt)
    {
        var t = Leftovers.NameTokens(name, pub);
        Assert.Contains(expected, t);
        if (alt != null) Assert.Contains(alt, t);
    }

    [Fact]
    public void Folders_StrictMatch_AndVendorOnlyWhenExclusive()
    {
        var widget = Dir(@"Acme\Widget");
        var exclusive = Dir(@"Solo");
        Dir(@"Solo\Widget");
        Dir(@"Acme\Other");
        Dir(@"WidgetPro");          // prefix only: must not match
        var direct = Dir(@"Widget");

        var names = Leftovers.NameTokens("Widget 2.1", "Acme Software");
        var acme = Leftovers.FindFolders(new[] { _base }, names, Leftovers.PublisherTokens("Acme Software"));
        Assert.Contains(widget, acme);
        Assert.Contains(direct, acme);
        Assert.DoesNotContain(Path.Combine(_base, "Acme"), acme);
        Assert.DoesNotContain(Path.Combine(_base, "WidgetPro"), acme);

        var solo = Leftovers.FindFolders(new[] { _base }, names, Leftovers.PublisherTokens("Solo"));
        Assert.Contains(exclusive, solo);

        var soloShared = Leftovers.FindFolders(new[] { _base }, names, Leftovers.PublisherTokens("Solo"), vendorShared: true);
        Assert.DoesNotContain(exclusive, soloShared);
        Assert.Contains(Path.Combine(exclusive, "Widget"), soloShared);
    }

    [Fact]
    public void MainExe_SkipsUninstaller_PrefersIconThenName()
    {
        var d = Dir("App");
        foreach (var f in new[] { "unins000.exe", "helper.exe", "crash.exe", "Widget.exe" }) File.WriteAllText(Path.Combine(d, f), "");
        var app = new InstalledApp { Name = "Widget 2.1", InstallLocation = d, DisplayIcon = Path.Combine(d, "unins000.exe") + ",0" };
        Assert.Equal(Path.Combine(d, "Widget.exe"), app.MainExe);
        var byIcon = new InstalledApp { Name = "Widget", InstallLocation = d, DisplayIcon = Path.Combine(d, "helper.exe") + ",0" };
        Assert.Equal(Path.Combine(d, "helper.exe"), byIcon.MainExe); // the icon beats the name
        File.Delete(Path.Combine(d, "Widget.exe"));
        Assert.Null(app.MainExe); // two exes left, neither named like the app
        File.Delete(Path.Combine(d, "crash.exe"));
        Assert.Equal(Path.Combine(d, "helper.exe"), app.MainExe); // the only one
    }

    [Fact]
    public void SharedVendorFolderNeverOffered()
    {
        Dir(@"Microsoft\Widget");
        var found = Leftovers.FindFolders(new[] { _base }, Leftovers.NameTokens("Widget", "Microsoft Corporation"),
            Leftovers.PublisherTokens("Microsoft Corporation"));
        Assert.Equal(new[] { Path.Combine(_base, @"Microsoft\Widget") }, found);
    }
}
