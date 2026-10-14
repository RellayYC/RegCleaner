using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class StartupTests
{
    [Fact]
    public void RunEntry_DisableDeleteUndo_RoundTrips()
    {
        var (run, approved) = Autoruns.RunKeys[0];
        const string name = "RegKeeperTestEntry";
        using (var tx = UndoTx.Begin("test add")) Autoruns.Add(name, @"C:\nope\missing.exe /x", false, tx);
        try
        {
            StartupItem Find() => Autoruns.RunAndFolders().Single(i => i.Location == run && i.Name == name);
            Assert.True(Find().Enabled);

            using (var tx = UndoTx.Begin("test off")) Autoruns.SetEnabled(Find(), false, tx);
            Assert.False(Find().Enabled);
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test off"));
            Assert.True(Find().Enabled);
            Assert.Null(Reg.Read(approved!, name));

            using (var tx = UndoTx.Begin("test del")) Autoruns.Delete(Find(), tx);
            Assert.Null(Reg.Read(run, name));
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test del"));
            Assert.Equal(@"C:\nope\missing.exe /x", Find().Command);
        }
        finally
        {
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test add"));
            Assert.Null(Reg.Read(run, name));
        }
    }

    [Fact]
    public void Load_DoesNotThrow() => Assert.NotNull(Autoruns.Load());
}
