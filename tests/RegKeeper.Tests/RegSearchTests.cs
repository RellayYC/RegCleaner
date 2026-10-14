using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class RegSearchTests
{
    const string Root = @"HKEY_CURRENT_USER\Software\RegKeeperSearchTest";

    [Fact]
    public void FindReplaceUndo()
    {
        using (var tx = UndoTx.Begin("test add"))
        {
            tx.SetValue(Root + @"\Sub_Foo", RawValue.String("FooName", "a foo b"));
            tx.SetValue(Root + @"\Sub_Foo\Deeper_foo", RawValue.Multi("m", new[] { "foo", "x$1" }));
            tx.SetValue(Root, RawValue.String("other", "food"));
        }
        try
        {
            var o = new SearchOptions("foo", WholeWord: true);
            var hits = new RegSearch().Run(new[] { Root }, o, default).ToList();
            // only "a foo b" and the multi line "foo": Sub_Foo, FooName, food are not whole words
            Assert.Equal(new[] { HitKind.Data, HitKind.Data }, hits.Select(h => h.Kind));

            o = new SearchOptions("foo");
            hits = new RegSearch().Run(new[] { Root }, o, default).ToList();
            Assert.Equal(6, hits.Count); // 2 keys, 1 name, 3 data

            using (var tx = UndoTx.Begin("test replace")) RegSearch.Replace(hits, o, "$0X", tx);
            Assert.Equal("a $0X b", Reg.GetString(Root + @"\Sub_$0X", "$0XName"));
            Assert.Equal(new[] { "$0X", "x$1" }, Reg.Read(Root + @"\Sub_$0X\Deeper_$0X", "m")!.AsMulti());
            Assert.Equal("$0Xd", Reg.GetString(Root, "other"));

            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test replace"));
            Assert.Equal("a foo b", Reg.GetString(Root + @"\Sub_Foo", "FooName"));
            Assert.False(Reg.Exists(Root + @"\Sub_$0X"));
        }
        finally
        {
            UndoCenter.Restore(UndoCenter.List().First(u => u.Title == "test add"));
            Assert.False(Reg.Exists(Root));
        }
    }
}
