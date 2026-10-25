using System.Collections.Specialized;
using System.Diagnostics;
using RegKeeper.Core;
using RegKeeper.Modules;

namespace RegKeeper.Tests;

/// <summary>Sizes that used to hang the app (a 1 MB value took over a minute to parse back) and the bulk paths that replaced
/// one-by-one work. Registry-free, so they run anywhere.</summary>
public class PerfTests
{
    static byte[] Random(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    [Fact]
    public void RegFile_LargeBinaryValue_ParsesBackExactly()
    {
        var v = new RawValue("blob", RawValue.Binary, Random(1 << 20, 1));
        var text = $"{RegFile.Header}\r\n\r\n[HKEY_CURRENT_USER\\Software\\RegKeeperPerfTest]\r\n{RegFile.FormatValue(v)}\r\n\"after\"=dword:00000001\r\n";
        var sw = Stopwatch.StartNew();
        var ops = RegFile.Parse(text);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"parse took {sw.Elapsed}");
        Assert.Equal(3, ops.Count);
        Assert.True(ops[1].Value!.SameAs(v));
        Assert.Equal("after", ops[2].Value!.Name); // the continuation stops at the value's last line
    }

    [Fact]
    public void RegFile_WrapsHexLikeRegedit()
    {
        var v = new RawValue("bin", 0x20000, Random(300, 2));
        var lines = RegFile.FormatValue(v).Split("\r\n");
        Assert.StartsWith("\"bin\"=hex(20000):", lines[0]);
        Assert.All(lines, l => Assert.True(l.Length <= 80, l));
        Assert.All(lines[..^1], l => Assert.EndsWith(",\\", l));
        Assert.All(lines[1..], l => Assert.Matches("^  [0-9a-f]{2}(,[0-9a-f]{2})*,?\\\\?$", l));
        var hex = string.Concat(lines.Select((l, i) => (i == 0 ? l[l.IndexOf(':')..][1..] : l.Trim()).TrimEnd('\\')));
        Assert.Equal(Convert.ToHexString(v.Data).ToLowerInvariant(), hex.Replace(",", ""));
    }

    [Fact]
    public void Snapshot_Changes_RoundTrip()
    {
        var changes = Enumerable.Range(0, 5000).Select(i => new SnapChange((ChangeKind)(i % 3), (i % 4) switch
        {
            0 => new SnapEntry(SnapKind.Key, $@"HKEY_CURRENT_USER\Software\T\K{i}"),
            1 => new SnapEntry(SnapKind.Value, $@"HKEY_CURRENT_USER\Software\T\K{i}", i % 5 == 0 ? "" : $"v{i} ключ", (uint)(i % 12), Random(i % 300, i)),
            2 => new SnapEntry(SnapKind.Dir, $@"C:\T\D{i}"),
            _ => new SnapEntry(SnapKind.File, $@"C:\T\D{i}\f.bin", Size: i * 7919L, Time: i * 104729L),
        })).ToList();
        var file = Path.GetTempFileName();
        try
        {
            Snapshot.SaveChanges(file, "perf", changes);
            var back = Snapshot.LoadChanges(file);
            Assert.Equal(changes.Count, back.Count);
            for (var i = 0; i < changes.Count; i++)
            {
                Assert.Equal(changes[i].Kind, back[i].Kind);
                var (a, b) = (changes[i].Entry, back[i].Entry);
                Assert.Equal((a.Kind, a.Path, a.Name, a.Type, a.Size, a.Time), (b.Kind, b.Path, b.Name, b.Type, b.Size, b.Time));
                Assert.Equal(a.Data, b.Data);
            }
            Assert.Equal("perf", Snapshot.Info(file).Note);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void HitList_RemoveAll_KeepsTheRestInOrder_OneReset()
    {
        var hits = Enumerable.Range(0, 1000).Select(i => new SearchHit(HitKind.Data, $@"HKEY_CURRENT_USER\Software\T\K{i / 10}", $"v{i}", "x")).ToList();
        var twin = hits[500] with { }; // equal by value, a different hit: only the instances passed in go
        var list = new HitList();
        hits.ForEach(list.Add);
        list.Add(twin);
        var events = new List<NotifyCollectionChangedAction>();
        list.CollectionChanged += (_, e) => events.Add(e.Action);

        list.RemoveAll(hits.Where((_, i) => i % 3 != 0));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Reset }, events);
        Assert.Equal(hits.Where((_, i) => i % 3 == 0).Append(twin), list);
        Assert.Same(twin, list[^1]);

        list.RemoveAll(Array.Empty<SearchHit>());
        Assert.Single(events); // nothing removed, nothing raised
    }
}
