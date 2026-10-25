using System.IO.Compression;
using System.Text;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public enum SnapKind : byte { Key, Value, Dir, File }

public sealed record SnapEntry(SnapKind Kind, string Path, string? Name = null, uint Type = 0, byte[]? Data = null, long Size = 0, long Time = 0)
{
    public bool IsRegistry => Kind <= SnapKind.Value;
    public string Display => Name == null ? Path : $"{Path} → {(Name == "" ? L.T("(По умолчанию)") : Name)}";
    public string KindText => Kind switch { SnapKind.Key => L.T("Раздел"), SnapKind.Value => L.T("Значение"), SnapKind.Dir => L.T("Папка"), _ => L.T("Файл") };
}

public enum ChangeKind : byte { Added, Removed, Modified }

/// <summary>Entry is the new state for Added, the old state otherwise (what a rollback restores).</summary>
public sealed record SnapChange(ChangeKind Kind, SnapEntry Entry)
{
    public bool Checked { get; set; } = true;
    public string KindText => Kind switch { ChangeKind.Added => L.T("Добавлено"), ChangeKind.Removed => L.T("Удалено"), _ => L.T("Изменено") } + ": " + Entry.KindText.ToLowerInvariant();
    public string Display => Entry.Display;
}

public sealed record SnapInfo(string File, string Note, DateTime Date, string[] Roots)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(File);
    public bool IsTrace => File.EndsWith(".rktrace", StringComparison.OrdinalIgnoreCase);
    public string SizeText => Sys.Size(new FileInfo(File).Length);
    public string RootsText => string.Join("; ", Roots.Select(r => r.StartsWith("HKEY_") ? Reg.Short(r) : r));
}

/// <summary>
/// Gzip stream of registry/file entries written in one canonical order (depth-first, siblings sorted, a key's values right
/// after it). The same comparer drives the sort and the diff, so two snapshots are diffed by a single merge pass
/// without loading either into memory.
/// </summary>
public static class Snapshot
{
    const string Magic = "RKSNAP1";
    const byte End = 255;

    public static readonly string[] RegRoots = { @"HKEY_CURRENT_USER\Software", @"HKEY_LOCAL_MACHINE\SOFTWARE" };

    public static string[] FileRoots => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    // ponytail: only temp folders are skipped; browser caches etc. show up as noise in diffs. Add an exclude list if it bothers.
    static readonly string[] SkipDirs = { System.IO.Path.GetTempPath().TrimEnd('\\'), AppPaths.Data };

    // ---- ordering

    /// <summary>Case-insensitive ordinal compare where '\' sorts below every other char = segment-by-segment order.</summary>
    public static int ComparePath(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var x = a[i] == '\\' ? '\0' : char.ToUpperInvariant(a[i]);
            var y = b[i] == '\\' ? '\0' : char.ToUpperInvariant(b[i]);
            if (x != y) return x.CompareTo(y);
        }
        return a.Length.CompareTo(b.Length);
    }

    static readonly Comparer<string> PathOrder = Comparer<string>.Create(ComparePath);

    public static int Compare(SnapEntry x, SnapEntry y)
    {
        if (x.IsRegistry != y.IsRegistry) return x.IsRegistry ? -1 : 1;
        var c = ComparePath(x.Path, y.Path);
        if (c != 0) return c;
        if (x.Name == null || y.Name == null) return x.Name == null ? (y.Name == null ? 0 : -1) : 1;
        return ComparePath(x.Name, y.Name);
    }

    static bool Same(SnapEntry x, SnapEntry y) => x.Kind == y.Kind && x.Kind switch
    {
        SnapKind.Value => x.Type == y.Type && x.Data.AsSpan().SequenceEqual(y.Data),
        SnapKind.File => x.Size == y.Size && x.Time == y.Time,
        _ => true,
    };

    // ---- taking

    public static void Take(string file, string note, IEnumerable<string> roots, CancellationToken ct = default)
    {
        var all = roots.Select(r => r.StartsWith("HK", StringComparison.OrdinalIgnoreCase) ? Reg.Normalize(r) : r.TrimEnd('\\')).ToList();
        var reg = all.Where(r => r.StartsWith("HKEY_")).Order(PathOrder).ToList();
        var dirs = all.Except(reg).Where(Directory.Exists).Order(PathOrder).ToList();
        try
        {
            using var w = Writer(file, note, reg.Concat(dirs));
            foreach (var root in reg)
            {
                using var k = Reg.Open(root);
                if (k != null) WalkKey(w, k, root, ct);
            }
            foreach (var dir in dirs) WalkDir(w, new DirectoryInfo(dir), ct);
            w.Write(End);
        }
        catch { File.Delete(file); throw; }
    }

    // buffered both ways: BinaryWriter/Reader hand gzip a few bytes per call, and each call is a round trip into zlib
    const int BufferSize = 1 << 16;

    static BinaryWriter Writer(string file, string note, IEnumerable<string> roots)
    {
        var w = new BinaryWriter(new BufferedStream(new GZipStream(File.Create(file), CompressionLevel.Fastest), BufferSize), Encoding.UTF8);
        w.Write(Magic);
        w.Write(note);
        w.Write(DateTime.Now.Ticks);
        var list = roots.ToList();
        w.Write(list.Count);
        list.ForEach(w.Write);
        return w;
    }

    static void WalkKey(BinaryWriter w, RegistryKey k, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Write(w, new SnapEntry(SnapKind.Key, path));
        foreach (var v in Reg.Values(k).OrderBy(v => v.Name, PathOrder))
            Write(w, new SnapEntry(SnapKind.Value, path, v.Name, v.Type, v.Data));
        foreach (var name in Reg.SubKeys(k).Order(PathOrder))
        {
            RegistryKey? sub;
            try { sub = k.OpenSubKey(name); } catch { continue; }
            if (sub == null) continue;
            using (sub) WalkKey(w, sub, path + "\\" + name, ct);
        }
    }

    static readonly EnumerationOptions DirOpts = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    static void WalkDir(BinaryWriter w, DirectoryInfo dir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (SkipDirs.Contains(dir.FullName.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase)) return;
        Write(w, new SnapEntry(SnapKind.Dir, dir.FullName));
        FileSystemInfo[] items;
        try { items = dir.GetFileSystemInfos("*", DirOpts); } catch { return; }
        foreach (var i in items.OrderBy(i => i.Name, PathOrder))
            if (i is DirectoryInfo d) WalkDir(w, d, ct);
            else if (i is FileInfo f) Write(w, new SnapEntry(SnapKind.File, f.FullName, Size: f.Length, Time: f.LastWriteTimeUtc.Ticks));
    }

    static void Write(BinaryWriter w, SnapEntry e)
    {
        w.Write((byte)e.Kind);
        w.Write(e.Path);
        if (e.Kind == SnapKind.Value)
        {
            w.Write(e.Name!);
            w.Write(e.Type);
            w.Write(e.Data!.Length);
            w.Write(e.Data);
        }
        else if (e.Kind == SnapKind.File)
        {
            w.Write(e.Size);
            w.Write(e.Time);
        }
    }

    static SnapEntry? ReadEntry(BinaryReader r)
    {
        var kind = r.ReadByte();
        if (kind == End) return null;
        var path = r.ReadString();
        return (SnapKind)kind switch
        {
            SnapKind.Value => new SnapEntry(SnapKind.Value, path, r.ReadString(), r.ReadUInt32(), r.ReadBytes(r.ReadInt32())),
            SnapKind.File => new SnapEntry(SnapKind.File, path, Size: r.ReadInt64(), Time: r.ReadInt64()),
            var k => new SnapEntry(k, path),
        };
    }

    // ---- reading

    static BinaryReader Reader(string file, out SnapInfo info)
    {
        var r = new BinaryReader(new BufferedStream(new GZipStream(File.OpenRead(file), CompressionMode.Decompress), BufferSize), Encoding.UTF8);
        if (r.ReadString() != Magic) { r.Dispose(); throw new InvalidDataException(L.T("Не снимок RegKeeper: ") + file); }
        var note = r.ReadString();
        var date = new DateTime(r.ReadInt64());
        var roots = new string[r.ReadInt32()];
        for (var i = 0; i < roots.Length; i++) roots[i] = r.ReadString();
        info = new SnapInfo(file, note, date, roots);
        return r;
    }

    public static SnapInfo Info(string file)
    {
        using var r = Reader(file, out var info);
        return info;
    }

    public static IEnumerable<SnapEntry> Read(string file)
    {
        using var r = Reader(file, out _);
        while (ReadEntry(r) is { } e) yield return e;
    }

    public static IEnumerable<SnapChange> Diff(IEnumerable<SnapEntry> before, IEnumerable<SnapEntry> after)
    {
        using var a = before.GetEnumerator();
        using var b = after.GetEnumerator();
        bool hasA = a.MoveNext(), hasB = b.MoveNext();
        while (hasA || hasB)
        {
            var c = !hasA ? 1 : !hasB ? -1 : Compare(a.Current, b.Current);
            if (c < 0) { yield return new(ChangeKind.Removed, a.Current); hasA = a.MoveNext(); }
            else if (c > 0) { yield return new(ChangeKind.Added, b.Current); hasB = b.MoveNext(); }
            else
            {
                if (!Same(a.Current, b.Current)) yield return new(ChangeKind.Modified, a.Current);
                hasA = a.MoveNext();
                hasB = b.MoveNext();
            }
        }
    }

    // ---- install traces: the diff saved in the same entry format, prefixed with the change kind

    public static void SaveChanges(string file, string note, IEnumerable<SnapChange> changes)
    {
        using var w = Writer(file, note, Array.Empty<string>());
        foreach (var c in changes)
        {
            w.Write((byte)c.Kind);
            Write(w, c.Entry);
        }
        w.Write(End);
    }

    public static List<SnapChange> LoadChanges(string file)
    {
        using var r = Reader(file, out _);
        var list = new List<SnapChange>();
        for (byte kind; (kind = r.ReadByte()) != End;) list.Add(new((ChangeKind)kind, ReadEntry(r)!));
        return list;
    }

    /// <summary>Undoes changes: registry through the transaction (so the rollback itself can be undone), added files and
    /// folders to the Recycle Bin. Modified/removed files can't be brought back - they're reported in tx.Errors.</summary>
    public static void Rollback(IEnumerable<SnapChange> changes, UndoTx tx)
    {
        var list = changes.OrderBy(c => c.Entry, Comparer<SnapEntry>.Create(Compare)).ToList();
        var added = list.Where(c => c.Kind == ChangeKind.Added && c.Entry.Kind is SnapKind.Key or SnapKind.Dir)
            .Select(c => c.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool UnderAdded(string path)
        {
            for (var p = Parent(path); p != ""; p = Parent(p))
                if (added.Contains(p)) return true;
            return false;
        }

        foreach (var c in list.Where(c => c.Kind != ChangeKind.Added && c.Entry.IsRegistry))
            if (c.Entry.Kind == SnapKind.Key) tx.CreateKey(c.Entry.Path);
            else tx.SetValue(c.Entry.Path, new RawValue(c.Entry.Name!, c.Entry.Type, c.Entry.Data!));

        var files = new List<string>();
        foreach (var c in list.Where(c => c.Kind == ChangeKind.Added))
        {
            var e = c.Entry;
            if (e.Kind == SnapKind.Value ? added.Contains(e.Path) : UnderAdded(e.Path)) continue; // goes with its parent
            switch (e.Kind)
            {
                case SnapKind.Key: tx.DeleteKey(e.Path); break;
                case SnapKind.Value: tx.DeleteValue(e.Path, e.Name!); break;
                default: if (File.Exists(e.Path) || Directory.Exists(e.Path)) files.Add(e.Path); break;
            }
        }
        if (files.Count > 0)
        {
            if (Sys.Recycle(files.ToArray())) files.ForEach(f => tx.Note(L.T("В корзину: ") + f));
            else tx.Errors.Add(L.T("Не удалось отправить в корзину часть файлов"));
        }
        foreach (var c in list.Where(c => c.Kind != ChangeKind.Added && !c.Entry.IsRegistry && c.Entry.Kind == SnapKind.File))
            tx.Errors.Add(L.T($"Файл не восстановить ({(c.Kind == ChangeKind.Removed ? L.T("удалён") : L.T("изменён"))}): {c.Entry.Path}"));
    }

    static string Parent(string path) => path.LastIndexOf('\\') is var i and > 0 ? path[..i] : "";
}
