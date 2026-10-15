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

    /// <summary>Admin only (passes vacuously otherwise): files owned by TrustedInstaller with read-only access for
    /// Administrators, as in a real Windows.old, go away under a Force rule. Run elevated: gate G26.</summary>
    [Fact]
    public void ForceRule_DeletesTrustedInstallerFiles_AsAdmin()
    {
        if (!Compaction.IsAdmin) return;
        var old = Path.Combine(_base, "Old");
        Touch(@"Old\Windows\System32\a.dll");
        try
        {
            foreach (var p in new[] { @"Windows\System32\a.dll", @"Windows\System32", "Windows", "" }.Select(r => Path.Combine(old, r)))
            {
                Icacls(p, "/setowner", "*" + TrustedInstaller);
                Icacls(p, "/inheritance:r", "/grant:r", $"*{TrustedInstaller}:F", "*S-1-5-32-544:RX", "*S-1-5-32-545:RX");
            }
            Assert.Throws<UnauthorizedAccessException>(() => File.Delete(Path.Combine(old, @"Windows\System32\a.dll"))); // the ACL really bites
            var rule = Winapp2.Parse($"""
                [Old]
                Force=1
                FileKey1={old}|*|REMOVESELF
                """).Single();
            Winapp2.Analyze(rule, Array.Empty<string>());
            Assert.Equal(1, rule.Count);
            using (var tx = RegKeeper.Core.UndoTx.Begin("test cleanup")) Assert.Equal(0, Winapp2.Clean(new[] { rule }, tx).Failed);
            Assert.False(Directory.Exists(old));
        }
        finally
        {
            if (Directory.Exists(old)) { Icacls(old, "/setowner", "*S-1-5-32-544", "/t", "/c"); Icacls(old, "/reset", "/t", "/c"); }
        }
    }

    const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    static void Icacls(string path, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("icacls.exe") { CreateNoWindow = true, UseShellExecute = false };
        psi.ArgumentList.Add(path);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"icacls {path} {string.Join(' ', args)}: {p.ExitCode}");
    }

    /// <summary>Admin only: the real DISM report on this machine parses (a format change would read as 0 silently).</summary>
    [Fact]
    public void ComponentStore_RealReportParses_AsAdmin()
    {
        if (!Compaction.IsAdmin) return;
        var report = Winapp2.Dism("/AnalyzeComponentStore");
        Assert.Matches(@"Backups and Disabled Features\s*:\s*[\d.]+\s*(bytes|KB|MB|GB|TB)", report);
        Assert.Matches(@"Component Store Cleanup Recommended\s*:\s*(Yes|No)", report);
        var (size, _) = Winapp2.ParseComponentStore(report);
        Assert.True(size > 0 || report.Contains(": 0 bytes"), report);
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
