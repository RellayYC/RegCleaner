using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

/// <summary>A cancelled scan throws instead of handing back a cut-short result; the same call without a token finishes.</summary>
public class CancelTests : IDisposable
{
    readonly string _base = Path.Combine(Path.GetTempPath(), "RegKeeperTest_" + Guid.NewGuid().ToString("N"));
    static readonly CancellationToken Cancelled = new(true);

    public CancelTests()
    {
        Directory.CreateDirectory(Path.Combine(_base, "App", "sub"));
        File.WriteAllBytes(Path.Combine(_base, "App", "a.tmp"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_base, "App", "sub", "b.tmp"), new byte[10]);
    }

    public void Dispose() => Directory.Delete(_base, true);

    [Fact]
    public void DiskRule_CancelledLeavesItUnanalyzed()
    {
        var rule = Winapp2.Parse($"[T *]\nLangSecRef=3024\nFileKey1={_base}\\App|*.tmp|RECURSE\n").Single();
        Assert.ThrowsAny<OperationCanceledException>(() => Winapp2.Analyze(rule, Array.Empty<string>(), Cancelled));
        Assert.False(rule.Analyzed);
        Winapp2.Analyze(rule, Array.Empty<string>());
        Assert.True(rule.Analyzed);
        Assert.Equal(2, rule.Count);
    }

    [Fact]
    public void DirSize_CancelledThrowsInsteadOfZero()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => Sys.DirSize(_base, Cancelled));
        Assert.Equal(20, Sys.DirSize(_base));
    }

    [Fact]
    public void Leftovers_Cancelled()
    {
        var app = new InstalledApp { Name = "RegKeeperCancelTest", InstallLocation = Path.Combine(_base, "App"), KeyPath = @"HKEY_CURRENT_USER\Software\RegKeeperCancelTestNone" };
        Assert.ThrowsAny<OperationCanceledException>(() => Leftovers.Find(app, new[] { app }, Cancelled));
        Assert.Contains(Leftovers.Find(app, new[] { app }), l => l.Path == app.InstallLocation && l.Size == 20);
    }

    [Fact]
    public void RegistryScan_Cancelled() => Assert.ThrowsAny<OperationCanceledException>(() => RegScanner.Scan(Cancelled));

    [Fact]
    public void Export_Cancelled()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer";
        Assert.ThrowsAny<OperationCanceledException>(() => RegFile.Export(new[] { key }, ct: Cancelled));
        Assert.StartsWith(RegFile.Header, RegFile.Export(new[] { key }));
    }
}
