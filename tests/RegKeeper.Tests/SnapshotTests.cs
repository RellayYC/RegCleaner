using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class SnapshotTests
{
    const string Root = @"HKEY_CURRENT_USER\Software\RegKeeperSnapTest";

    [Fact]
    public void PathOrder_IsSegmentWise() =>
        Assert.True(Snapshot.ComparePath(@"A\a\z", @"A\a b") < 0 && Snapshot.ComparePath("a", "A") == 0);

    [Fact]
    public void TakeDiffRollback()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RegKeeperSnapTest");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "keep.txt"), "1");
        File.WriteAllText(Path.Combine(dir, "gone.txt"), "1");
        using (var tx = UndoTx.Begin("test add snap"))
        {
            tx.SetValue(Root, RawValue.String("same", "x"));
            tx.SetValue(Root, RawValue.FromDword("mod", 1));
            tx.SetValue(Root + @"\Old", RawValue.String("del", "y"));
        }
        string a = Path.GetTempFileName(), b = Path.GetTempFileName(), t = Path.GetTempFileName();
        try
        {
            Snapshot.Take(a, "a", new[] { Root, dir });

            Reg.Write(Reg.Open(Root, true)!, RawValue.FromDword("mod", 2));
            Reg.DeleteTree(Root + @"\Old");
            Reg.Create(Root + @"\New\Deep").Dispose();
            Reg.Write(Reg.Open(Root + @"\New", true)!, RawValue.String("v", "z"));
            File.Delete(Path.Combine(dir, "gone.txt"));
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            File.WriteAllText(Path.Combine(dir, "sub", "added.txt"), "1");

            Snapshot.Take(b, "b", new[] { "HKCU\\Software\\RegKeeperSnapTest", dir });
            Snapshot.SaveChanges(t, "trace", Snapshot.Diff(Snapshot.Read(a), Snapshot.Read(b)));
            var changes = Snapshot.LoadChanges(t);
            string Sig(SnapChange c) => $"{c.Kind} {c.Entry.Kind} {c.Entry.Path.Replace(Root, "R").Replace(dir, "D")} {c.Entry.Name}".TrimEnd();
            Assert.Equal(new[]
            {
                "Modified Value R mod",
                "Added Key R\\New", "Added Value R\\New v", "Added Key R\\New\\Deep",
                "Removed Key R\\Old", "Removed Value R\\Old del",
                "Removed File D\\gone.txt", "Added Dir D\\sub", "Added File D\\sub\\added.txt",
            }, changes.Select(Sig));

            using (var tx = UndoTx.Begin("test rollback snap")) Snapshot.Rollback(changes, tx);
            Assert.Empty(Snapshot.Diff(Snapshot.Read(a), ReadAfterTake()).Where(c => c.Entry.IsRegistry));
            Assert.False(Directory.Exists(Path.Combine(dir, "sub")));
        }
        finally
        {
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test add snap"));
            Assert.False(Reg.Exists(Root));
            Directory.Delete(dir, true);
            foreach (var f in new[] { a, b, t }) File.Delete(f);
        }

        IEnumerable<SnapEntry> ReadAfterTake()
        {
            var c = Path.GetTempFileName();
            Snapshot.Take(c, "c", new[] { Root });
            var list = Snapshot.Read(c).ToList();
            File.Delete(c);
            return list;
        }
    }
}
