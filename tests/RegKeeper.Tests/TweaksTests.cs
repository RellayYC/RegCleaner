using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class TweaksTests
{
    const string Root = @"HKEY_CURRENT_USER\Software\RegKeeperTweakTest";

    [Fact]
    public void ShippedCatalog_LoadsAndConverts()
    {
        var list = Tweaks.Load();
        Assert.True(list.Count >= 30);
        foreach (var v in list.SelectMany(t => t.Values))
        {
            Assert.NotNull(v.Raw(true));
            v.Raw(false); // must not throw
        }
    }

    [Fact]
    public void ApplyRefreshUndo()
    {
        var json = Path.GetTempFileName();
        File.WriteAllText(json, $$"""
        [
          { "category": "t", "name": "a", "description": "d", "values": [
              { "key": "{{Root.Replace(@"\", @"\\")}}\\Sub", "name": "n", "type": "sz", "on": "yes", "off": null, "offDeleteKey": "{{Root.Replace(@"\", @"\\")}}" },
              { "key": "{{Root.Replace(@"\", @"\\")}}\\Sub", "name": "d", "on": 1, "off": null } ] },
          { "category": "t", "name": "b", "description": "d", "restart": "reboot", "values": [
              { "key": "HKEY_CURRENT_USER\\Software\\RegKeeperTweakTest2", "name": "q", "type": "qword", "on": 5, "off": 7 } ] }
        ]
        """);
        try
        {
            var list = Tweaks.Load(json);
            Assert.All(list, t => Assert.False(t.IsOn));

            list.ForEach(t => t.Wanted = true);
            var (restart, errors) = Tweaks.ApplyChanged(list);
            Assert.Empty(errors);
            Assert.Equal("reboot", restart);
            Assert.All(Tweaks.Load(json), t => Assert.True(t.IsOn));
            Assert.Equal("yes", Reg.GetString(Root + @"\Sub", "n"));

            list = Tweaks.Load(json);
            list.ForEach(t => t.Wanted = false);
            Tweaks.ApplyChanged(list);
            Assert.False(Reg.Exists(Root)); // offDeleteKey removed the whole tree
            Assert.Equal(7UL, BitConverter.ToUInt64(Reg.Read(@"HKEY_CURRENT_USER\Software\RegKeeperTweakTest2", "q")!.Data));

            UndoCenter.Restore(UndoCenter.List().First(u => u.Title.StartsWith("Твики: -a")));
            Assert.Equal("yes", Reg.GetString(Root + @"\Sub", "n"));
        }
        finally
        {
            File.Delete(json);
            foreach (var u in UndoCenter.List().Where(u => u.Title.StartsWith("Твики: +a"))) UndoCenter.Restore(u);
            Assert.False(Reg.Exists(Root));
            Assert.False(Reg.Exists(@"HKEY_CURRENT_USER\Software\RegKeeperTweakTest2"));
        }
    }
}
