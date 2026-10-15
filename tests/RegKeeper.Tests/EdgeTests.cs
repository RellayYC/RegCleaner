using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class EdgeTests : IDisposable
{
    const string Root = @"HKEY_CURRENT_USER\Software\RegKeeperTest";
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "RegKeeperTest");

    public EdgeTests() => Cleanup();
    public void Dispose() => Cleanup();
    static void Cleanup()
    {
        if (Reg.Exists(Root)) Reg.DeleteTree(Root);
        if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
    }

    static string LongDir()
    {
        var d = Dir;
        while (d.Length < 300) d = Path.Combine(d, new string('x', 40));
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "f.txt"), "x");
        return d;
    }

    [Fact]
    public void LongPath_SnapshotSeesTheFile()
    {
        var d = LongDir();
        var snap = Path.Combine(Path.GetTempPath(), "rk_edge.rksnap");
        try
        {
            Snapshot.Take(snap, "edge", new[] { Dir });
            Assert.Contains(Snapshot.Read(snap), e => e.Path.EndsWith("f.txt"));
        }
        finally { File.Delete(snap); }
    }

    [Fact]
    public void LongPath_RecycleRemovesTheFolder()
    {
        LongDir();
        Assert.True(Sys.Recycle(Dir));
        Assert.False(Directory.Exists(Dir));
    }

    [Fact]
    public void Snapshot_Cancelled_LeavesNoFile()
    {
        var snap = Path.Combine(Path.GetTempPath(), "rk_cancel.rksnap");
        Assert.Throws<OperationCanceledException>(() => Snapshot.Take(snap, "c", Snapshot.RegRoots, new CancellationToken(true)));
        Assert.False(File.Exists(snap));
    }

    [Fact]
    public void Undo_InterruptedTransaction_ListsItsItems()
    {
        // a transaction that never reaches Dispose (crash) must still show what it holds
        var tx = UndoTx.Begin("interrupted");
        tx.SetValue(Root, RawValue.String("a", "1"));
        var info = UndoCenter.List().First(i => i.Title == "interrupted");
        Assert.Equal(1, info.Count);
        UndoCenter.Restore(info);
        Assert.False(Reg.Exists(Root));
    }

    [Fact]
    public void Undo_RestoringTwice_FailsCleanly()
    {
        using (var tx = UndoTx.Begin("twice")) tx.SetValue(Root, RawValue.String("a", "1"));
        var info = UndoCenter.List().First(i => i.Title == "twice");
        UndoCenter.Restore(info);
        Assert.ThrowsAny<IOException>(() => UndoCenter.Restore(info));
        Assert.False(Reg.Exists(Root));
    }
}
