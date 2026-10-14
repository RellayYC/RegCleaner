using System.Text.RegularExpressions;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public enum HitKind { Key, Name, Data }

public sealed record SearchHit(HitKind Kind, string Key, string? ValueName, string Text)
{
    public bool Checked { get; set; }
    public string KindText => Kind switch { HitKind.Key => "Раздел", HitKind.Name => "Имя", _ => "Данные" };
    public string Display => ValueName == null ? Reg.Short(Key) : $"{Reg.Short(Key)} → {(ValueName == "" ? "(По умолчанию)" : ValueName)}";
}

public sealed record SearchOptions(string Pattern, bool Keys = true, bool Names = true, bool Data = true,
    bool MatchCase = false, bool WholeWord = false, bool IsRegex = false)
{
    public Regex Build()
    {
        var p = IsRegex ? Pattern : Regex.Escape(Pattern);
        if (WholeWord) p = $@"(?<!\w)(?:{p})(?!\w)";
        return new Regex(p, (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant);
    }

    /// <summary>Literal mode replaces literally: "$" in the replacement must not be read as a group reference.</summary>
    public string Replacement(string text) => IsRegex ? text : text.Replace("$", "$$");
}

/// <summary>Walks the registry depth-first through parent handles (much cheaper than opening each path from the root).</summary>
public sealed class RegSearch
{
    public static readonly string[] DefaultRoots = { "HKEY_LOCAL_MACHINE", "HKEY_CURRENT_USER" };
    public long Scanned;

    public IEnumerable<SearchHit> Run(IEnumerable<string> roots, SearchOptions o, CancellationToken ct)
    {
        var rx = o.Build();
        foreach (var root in roots)
        {
            using var k = Reg.Open(root);
            if (k == null) continue;
            foreach (var hit in Walk(k, Reg.Normalize(root), rx, o, ct)) yield return hit;
        }
    }

    IEnumerable<SearchHit> Walk(RegistryKey k, string path, Regex rx, SearchOptions o, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Interlocked.Increment(ref Scanned);
        if (o.Keys && path.Contains('\\') && rx.IsMatch(Reg.Leaf(path))) yield return new(HitKind.Key, path, null, Reg.Leaf(path));
        if (o.Names || o.Data)
            foreach (var v in Reg.Values(k))
            {
                if (o.Names && v.Name != "" && rx.IsMatch(v.Name)) yield return new(HitKind.Name, path, v.Name, v.Name);
                if (o.Data && DataText(v) is { } text && rx.IsMatch(text)) yield return new(HitKind.Data, path, v.Name, text);
            }
        foreach (var name in Reg.SubKeys(k))
        {
            RegistryKey? sub;
            try { sub = k.OpenSubKey(name); }
            catch { continue; } // access denied (SAM, SECURITY, some SYSTEM keys)
            if (sub == null) continue;
            using (sub)
                foreach (var hit in Walk(sub, path + "\\" + name, rx, o, ct)) yield return hit;
        }
    }

    static string? DataText(RawValue v) => v.Type switch
    {
        RawValue.Sz or RawValue.ExpandSz => v.AsString(),
        RawValue.MultiSz => string.Join("\n", v.AsMulti()),
        _ => null,
    };

    /// <summary>Data, then value names, then keys deepest-first: a rename never invalidates a pending hit.</summary>
    static IEnumerable<SearchHit> SafeOrder(IEnumerable<SearchHit> hits) =>
        hits.OrderByDescending(h => h.Kind).ThenByDescending(h => h.Key.Length);

    public static void Delete(IEnumerable<SearchHit> hits, UndoTx tx)
    {
        foreach (var h in SafeOrder(hits))
            if (h.Kind == HitKind.Key) tx.DeleteKey(h.Key);
            else tx.DeleteValue(h.Key, h.ValueName!);
    }

    public static void Replace(IEnumerable<SearchHit> hits, SearchOptions o, string replacement, UndoTx tx)
    {
        var rx = o.Build();
        var repl = o.Replacement(replacement);
        foreach (var h in SafeOrder(hits))
        {
            try
            {
                switch (h.Kind)
                {
                    case HitKind.Key:
                        var leaf = rx.Replace(Reg.Leaf(h.Key), repl);
                        if (leaf != Reg.Leaf(h.Key) && leaf != "") tx.RenameKey(h.Key, leaf);
                        break;
                    case HitKind.Name:
                        var name = rx.Replace(h.ValueName!, repl);
                        if (name == h.ValueName || Reg.Read(h.Key, h.ValueName!) is not { } old) break;
                        if (Reg.Read(h.Key, name) != null) { tx.Errors.Add($"{Reg.Short(h.Key)} → {name}: уже существует"); break; }
                        tx.SetValue(h.Key, old with { Name = name });
                        tx.DeleteValue(h.Key, h.ValueName!);
                        break;
                    case HitKind.Data:
                        if (Reg.Read(h.Key, h.ValueName!) is not { } v) break;
                        tx.SetValue(h.Key, v.Type == RawValue.MultiSz
                            ? RawValue.Multi(v.Name, v.AsMulti().Select(l => rx.Replace(l, repl)))
                            : RawValue.String(v.Name, rx.Replace(v.AsString(), repl), v.Type));
                        break;
                }
            }
            catch (Exception e) { tx.Errors.Add($"{h.Display}: {e.Message}"); }
        }
    }
}
