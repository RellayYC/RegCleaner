using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class Winapp2Tests : IDisposable
{
    readonly string _base = Path.Combine(Path.GetTempPath(), "RegKeeperTest_" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_base)) Directory.Delete(_base, true); }

    void Touch(string rel, int size = 10)
    {
        var p = Path.Combine(_base, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    [Fact]
    public void Analyze_Clean_RespectsPatternsExcludesAndRemoveSelf()
    {
        Touch(@"App\Cache\a.tmp");
        Touch(@"App\Cache\sub\b.dat");
        Touch(@"App\Cache\keep.db");
        Touch(@"App\logs\x.log");
        Touch(@"App\logs\x.txt");
        Touch(@"App\logs\deep\y.log");
        var rule = Winapp2.Parse($"""
            [Test *]
            LangSecRef=3024
            DetectFile={_base}\Ap?
            FileKey1={_base}\A*\Cache|*|REMOVESELF
            FileKey2={_base}\App\logs|*.log
            ExcludeKey1=FILE|{_base}\App\Cache\|keep.db
            """).Single();

        Assert.Equal("Test", rule.Name);
        Assert.Equal("Утилиты", rule.Category);
        Assert.True(Winapp2.Applies(rule));

        Winapp2.Analyze(rule, Array.Empty<string>());
        Assert.Equal(3, rule.Count); // a.tmp, sub\b.dat, x.log; not keep.db, x.txt, deep\y.log
        Assert.Equal(30, rule.Size);

        using (var tx = RegKeeper.Core.UndoTx.Begin("test cleanup")) Winapp2.Clean(new[] { rule }, tx);
        Assert.True(File.Exists(Path.Combine(_base, @"App\Cache\keep.db")));
        Assert.False(Directory.Exists(Path.Combine(_base, @"App\Cache\sub")));
        Assert.True(File.Exists(Path.Combine(_base, @"App\logs\x.txt")));
        Assert.True(File.Exists(Path.Combine(_base, @"App\logs\deep\y.log")));
        Assert.False(File.Exists(Path.Combine(_base, @"App\logs\x.log")));
    }

    [Fact]
    public void ForceRule_RemovesWholeFolder_DropsJunctionsWithoutTouchingTargets()
    {
        Touch(@"Outside\precious.txt");
        Touch(@"Old\Windows\a.dll");
        Touch(@"Old\Users\ro.txt");
        File.SetAttributes(Path.Combine(_base, @"Old\Users\ro.txt"), FileAttributes.ReadOnly);
        var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            new[] { "/c", "mklink", "/J", Path.Combine(_base, @"Old\Users\Link"), Path.Combine(_base, "Outside") }) { CreateNoWindow = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        var rule = Winapp2.Parse($"""
            [Old]
            Force=1
            FileKey1={_base}\Old|*|REMOVESELF
            """).Single();

        Winapp2.Analyze(rule, Array.Empty<string>());
        Assert.Equal(2, rule.Count); // the junction's target is not walked
        using (var tx = RegKeeper.Core.UndoTx.Begin("test cleanup")) Assert.Equal(0, Winapp2.Clean(new[] { rule }, tx).Failed);
        Assert.False(Directory.Exists(Path.Combine(_base, "Old")));
        Assert.True(File.Exists(Path.Combine(_base, @"Outside\precious.txt")));
    }

    [Fact]
    public void ComponentStoreReport_Parses()
    {
        const string report = """
            Windows Explorer Reported Size of Component Store : 7.66 GB

            Actual Size of Component Store : 7.43 GB

                Shared with Windows : 5.68 GB
                Backups and Disabled Features : 1.5 GB
                Cache and Temporary Data :  156.33 MB

            Number of Reclaimable Packages : 3
            Component Store Cleanup Recommended : Yes
            """;
        Assert.Equal(((long)(1.5 * (1L << 30)), 3L), Winapp2.ParseComponentStore(report));
        Assert.Equal((0L, 1L), Winapp2.ParseComponentStore("Number of Reclaimable Packages : 0\nComponent Store Cleanup Recommended : Yes"));
        Assert.Equal((0L, 0L), Winapp2.ParseComponentStore("Error: 740"));
    }

    [Fact]
    public void ExpandVars_ProgramFilesMeansBoth() =>
        Assert.Equal(2, Winapp2.ExpandVars(@"%ProgramFiles%\X").Count());

    [Fact]
    public void UpstreamFile_ParsesFully()
    {
        if (!File.Exists(Winapp2.IniPath)) Winapp2.Download().GetAwaiter().GetResult();
        var text = File.ReadAllText(Winapp2.IniPath);
        var rules = Winapp2.Parse(text);
        var headers = text.Split('\n').Count(l => l.TrimStart().StartsWith('['));
        Assert.Equal(headers, rules.Count);
        Assert.Equal(text.Split('\n').Count(l => l.StartsWith("FileKey")), rules.Sum(r => r.Files.Count));
        Assert.All(rules, r => Assert.NotEqual("", r.Name));
        Assert.NotEmpty(Winapp2.Load()); // detection over the whole base doesn't throw
    }
}
