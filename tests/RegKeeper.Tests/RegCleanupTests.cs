using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class RegCleanupTests
{
    [Fact]
    public void BrokenOpenWith_FoundFixedAndUndone()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Classes\Applications\RegKeeperTest.exe";
        using (var tx = UndoTx.Begin("test add cleanup"))
        {
            tx.CreateKey(key + @"\shell\open\command");
            tx.SetValue(key + @"\shell\open\command", RawValue.String("", @"""C:\nope dir\x.exe"" ""%1"""));
        }
        try
        {
            var issue = RegScanner.Scan().Single(i => i.Key == key);
            Assert.Contains(@"C:\nope dir\x.exe", issue.Problem);

            Assert.Empty(RegScanner.Fix(new[] { issue }));
            Assert.False(Reg.Exists(key));
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "Чистка реестра"));
            Assert.True(Reg.Exists(key + @"\shell\open\command"));
        }
        finally
        {
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test add cleanup"));
            Assert.False(Reg.Exists(key));
        }
    }
}
