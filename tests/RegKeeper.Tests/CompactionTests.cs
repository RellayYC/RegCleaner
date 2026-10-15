using RegKeeper.Modules;

namespace RegKeeper.Tests;

public class CompactionTests
{
    [Fact]
    public void Plan_FindsMainHives()
    {
        var plan = Compaction.Plan();
        var dump = string.Join("\n", plan.Select(h => $"{h.Key} = {h.File}"));
        Assert.True(plan.Any(h => h.Key == @"HKEY_LOCAL_MACHINE\SOFTWARE" && h.File.EndsWith(@"\config\SOFTWARE", StringComparison.OrdinalIgnoreCase)), dump);
        Assert.Contains(plan, h => h.Key == @"HKEY_LOCAL_MACHINE\SYSTEM");
        Assert.Contains(plan, h => h.File.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NTUSER.DAT"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan, h => h.Key.EndsWith("_Classes"));
        Assert.DoesNotContain(plan, h => h.Key.Contains("SAM") || h.Key.Contains("SECURITY") || h.Key.Contains("BCD") || h.Key.Contains("Silo"));
        Assert.All(plan.Where(h => Compaction.IsAdmin || h.Key.StartsWith(@"HKEY_USERS\S-")), h => Assert.True(h.Size > 0 && File.Exists(h.File), h.File));
    }

    [Fact]
    public void Replace_RefusesWithoutBackupOrAdmin()
    {
        var sw = Compaction.Plan().First(h => h.Key.EndsWith("SOFTWARE"));
        var h = new HiveInfo { Key = sw.Key, File = sw.File, Size = 100_000_000 }; // real size is hidden without admin
        var dir = Path.Combine(Path.GetTempPath(), "RegKeeperCompactTest");
        Assert.Equal(Path.GetPathRoot(h.File), Path.GetPathRoot(dir), ignoreCase: true); // temp must share the hive's volume
        Directory.CreateDirectory(dir);
        try
        {
            h.Compacted = Path.Combine(dir, "SOFTWARE");
            File.WriteAllBytes(h.Compacted, new byte[16]);
            h.CompactedSize = 16;
            var backup = Path.Combine(dir, "SOFTWARE.old");

            Assert.Throws<ArgumentException>(() => Compaction.Replace(h, ""));
            File.WriteAllText(backup, "x");
            Assert.Throws<IOException>(() => Compaction.Replace(h, backup));
            File.Delete(backup);
            Assert.Throws<IOException>(() => Compaction.Replace(h, @"Q:\SOFTWARE.old")); // other volume

            var noGain = new HiveInfo { Key = h.Key, File = h.File, Size = 16, Compacted = h.Compacted };
            Assert.Throws<InvalidOperationException>(() => Compaction.Replace(noGain, backup));

            if (!Compaction.IsAdmin)
            {
                Assert.Throws<UnauthorizedAccessException>(() => Compaction.Replace(h, backup));
                Assert.Throws<UnauthorizedAccessException>(() => Compaction.Analyze(new[] { h }));
            }
            Assert.False(File.Exists(backup));
        }
        finally { Directory.Delete(dir, true); }
    }
}
