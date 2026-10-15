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
    public void Delay_MovesEntryToDelayedTask_AndUndoRemovesIt()
    {
        var (run, _) = Autoruns.RunKeys[0];
        const string name = "RegKeeperTestDelay";
        using (var tx = UndoTx.Begin("test add delay")) Autoruns.Add(name, @"""C:\nope\my app.exe"" /tray -x", false, tx);
        try
        {
            StartupItem Find() => Autoruns.RunAndFolders().Single(i => i.Location == run && i.Name == name);
            string task;
            using (var tx = UndoTx.Begin("test delay")) task = Autoruns.Delay(Find(), TimeSpan.FromSeconds(45), tx);
            Assert.False(Find().Enabled);
            using (var t = Microsoft.Win32.TaskScheduler.TaskService.Instance.GetTask(task))
            {
                Assert.NotNull(t);
                Assert.Equal(TimeSpan.FromSeconds(45), ((Microsoft.Win32.TaskScheduler.LogonTrigger)t.Definition.Triggers[0]).Delay);
                var a = (Microsoft.Win32.TaskScheduler.ExecAction)t.Definition.Actions[0];
                Assert.Equal((@"C:\nope\my app.exe", "/tray -x"), (a.Path, a.Arguments));
            }
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test delay"));
            Assert.True(Find().Enabled);
            Assert.Null(Microsoft.Win32.TaskScheduler.TaskService.Instance.GetTask(task));
        }
        finally { UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test add delay")); }
    }

    [Theory]
    [InlineData(@"""C:\a b\x.exe"" -q", @"C:\a b\x.exe", "-q")]
    [InlineData(@"C:\a b\x.exe -q", @"C:\a b\x.exe", "-q")]
    [InlineData(@"rundll32.exe C:\x.dll,Run", "rundll32.exe", @"C:\x.dll,Run")]
    [InlineData(@"C:\x.exe", @"C:\x.exe", "")]
    public void SplitCommand_SeparatesProgramAndArgs(string cmd, string exe, string args) =>
        Assert.Equal((exe, args), Autoruns.SplitCommand(cmd));

    [Fact]
    public void Load_DoesNotThrow() => Assert.NotNull(Autoruns.Load());
}
