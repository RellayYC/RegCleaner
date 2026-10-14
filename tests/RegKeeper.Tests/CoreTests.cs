using RegKeeper.Core;

namespace RegKeeper.Tests;

public class CoreTests : IDisposable
{
    const string Root = @"HKEY_CURRENT_USER\Software\RegKeeperTest";

    public CoreTests() => Cleanup();
    public void Dispose() => Cleanup();
    static void Cleanup() { if (Reg.Exists(Root)) Reg.DeleteTree(Root); }

    static void Seed()
    {
        using var k = Reg.Create(Root + @"\Sub\Deep");
        using var r = Reg.Create(Root);
        Reg.Write(r, RawValue.String("", "default \"quoted\" C:\\path\\"));
        Reg.Write(r, RawValue.String("str", "привет"));
        Reg.Write(r, RawValue.String("exp", "%TEMP%\\x", RawValue.ExpandSz));
        Reg.Write(r, RawValue.FromDword("dw", 0xdeadbeef));
        Reg.Write(r, RawValue.FromQword("qw", 0x0102030405060708));
        Reg.Write(r, RawValue.Multi("multi", new[] { "a", "b", "c" }));
        Reg.Write(r, new RawValue("bin", RawValue.Binary, Enumerable.Range(0, 200).Select(i => (byte)i).ToArray()));
        Reg.Write(r, new RawValue("none", RawValue.None, Array.Empty<byte>()));
        Reg.Write(r, new RawValue("weird", 8, new byte[] { 1, 2, 3 }));
        Reg.Write(r, new RawValue("unterminated", RawValue.Sz, new byte[] { 0x41, 0 }));
        Reg.Write(r, RawValue.String("line\nbreak", "a\r\nb"));
        Reg.Write(k, RawValue.String("deep", "1"));
    }

    [Fact]
    public void RegFile_RoundTrip_IsExact()
    {
        Seed();
        var first = RegFile.Export(new[] { Root });
        Reg.DeleteTree(Root);
        RegFile.ApplyRaw(RegFile.Parse(first));
        Assert.Equal(first, RegFile.Export(new[] { Root }));
    }

    [Fact]
    public void Undo_RestoresEverything()
    {
        Seed();
        var before = RegFile.Export(new[] { Root });
        using (var tx = UndoTx.Begin("test"))
        {
            tx.SetValue(Root, RawValue.String("str", "changed"));
            tx.SetValue(Root, RawValue.String("new", "added"));
            tx.SetValue(Root + @"\Brand\New", RawValue.FromDword("x", 1));
            tx.DeleteValue(Root, "dw");
            tx.DeleteKey(Root + @"\Sub");
            tx.RenameKey(Root + @"\Brand", "Renamed");
        }
        Assert.NotEqual(before, RegFile.Export(new[] { Root }));
        var info = UndoCenter.List().First(i => i.Title == "test");
        UndoCenter.Restore(info);
        Assert.Equal(before, RegFile.Export(new[] { Root }));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" /silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Program Files\\App\\app.exe /silent", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Windows\\System32\\rundll32.exe C:\\Tools\\x.dll,Entry", "C:\\Tools\\x.dll")]
    [InlineData("@C:\\Windows\\System32\\shell32.dll,-21787", "C:\\Windows\\System32\\shell32.dll")]
    [InlineData("\\??\\C:\\drivers\\a.sys", "C:\\drivers\\a.sys")]
    [InlineData("MsiExec.exe /X{GUID}", "MsiExec.exe")]
    public void ExtractPath(string cmd, string expected) => Assert.Equal(expected, Sys.ExtractPath(cmd), ignoreCase: true);

    [Fact]
    public void IsMissing_OnlyForRootedAbsentFiles()
    {
        Assert.True(Sys.IsMissing(@"C:\definitely\not\here.exe"));
        Assert.False(Sys.IsMissing("notepad.exe"));
        Assert.False(Sys.IsMissing(@"%windir%\explorer.exe"));
        Assert.False(Sys.IsMissing(@"\\server\share\x.exe"));
    }
}
