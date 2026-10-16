using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class StoreAppsTests
{
    [Fact]
    public void Parse_SingleObjectAndFiltering()
    {
        var one = StoreApps.Parse("""{"Name":"A.B","PackageFullName":"A.B_1_x64__x","Publisher":"CN=Foo Inc, O=\"Foo, Ltd\", C=US","IsFramework":false,"NonRemovable":false,"SignatureKind":"Store"}""");
        Assert.Single(one);
        Assert.False(one[0].IsSystem);

        var many = StoreApps.Parse("""
            [{"Name":"fw","IsFramework":true,"SignatureKind":"Store","Publisher":"CN=Microsoft Corporation"},
             {"Name":"nr","NonRemovable":true,"SignatureKind":"Store"},
             {"Name":"sys","SignatureKind":"System"},
             {"Name":"dev","SignatureKind":"Developer","InstallLocation":null}]
            """);
        Assert.Equal(new[] { "dev" }, many.Where(a => !a.IsSystem).Select(a => a.Name));
        Assert.Equal("Microsoft Corporation", many[0].PublisherName);
        Assert.Equal("dev", many[3].Title);
        Assert.Empty(StoreApps.Parse(""));
    }

    [Fact]
    public void Load_RealSystem_And_RemovalKeepsNameOutOfScript()
    {
        var apps = StoreApps.Load();
        Assert.Contains(apps, a => a.IsSystem);
        Assert.All(apps, a => Assert.False(string.IsNullOrEmpty(a.PackageFullName)));
        Assert.Contains(apps, a => a.DisplayName != "" && !a.DisplayName.StartsWith('@'));
        // B7: bare "ms-resource:" references in the repository are resolved, not shown raw
        Assert.DoesNotContain(apps, a => a.Title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase));

        const string evil = "x'; Remove-Item C:\\ -Recurse; '";
        var psi = StoreApps.RemoveStartInfo(evil);
        Assert.DoesNotContain(psi.ArgumentList, a => a.Contains(evil));
        Assert.Contains(StoreApps.RemoveScript, psi.ArgumentList);
        Assert.Equal(evil, psi.Environment["RK_PACKAGE"]);

        Assert.NotNull(StoreApps.Remove(apps.First(a => a.IsSystem))); // guarded, never reaches PowerShell
    }

    [Fact]
    public void Aumid_FamilyNameAndFirstApplication()
    {
        var dir = Directory.CreateTempSubdirectory("rk-aumid").FullName;
        var app = new StoreApp { PackageFullName = "Foo.Bar_1.2.3.0_x64__8wekyb3d8bbwe", InstallLocation = dir };
        Assert.Null(StoreApps.Aumid(app)); // no manifest
        File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"), """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Applications><Application Id="App" /><Application Id="Second" /></Applications>
            </Package>
            """);
        Assert.Equal("Foo.Bar_8wekyb3d8bbwe!App", StoreApps.Aumid(app));
        File.WriteAllText(Path.Combine(dir, "AppxManifest.xml"), "<Package><broken");
        Assert.Null(StoreApps.Aumid(app));
        Directory.Delete(dir, true);
    }
}
