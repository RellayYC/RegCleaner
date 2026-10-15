using System.Diagnostics;
using System.Text.Json;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>One registry value of a tweak. On/Off: JSON number or string; null (or absent) = the value must not exist.</summary>
public sealed class TweakValue
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "dword";
    public JsonElement? On { get; init; }
    public JsonElement? Off { get; init; }
    /// <summary>Turning off removes this key (an ancestor of Key the tweak itself creates) instead of just the value.</summary>
    public string? OffDeleteKey { get; init; }

    public RawValue? Raw(bool on)
    {
        var el = on ? On : Off;
        if (el is not { ValueKind: not JsonValueKind.Null } e) return null;
        return Type switch
        {
            "dword" => RawValue.FromDword(Name, e.GetUInt32()),
            "qword" => RawValue.FromQword(Name, e.GetUInt64()),
            "sz" => RawValue.String(Name, e.GetString()!),
            "expand" => RawValue.String(Name, e.GetString()!, RawValue.ExpandSz),
            _ => throw new InvalidDataException($"Неизвестный тип {Type} у {Key} → {Name}"),
        };
    }
}

public sealed class Tweak
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>null, "explorer", "logoff" or "reboot".</summary>
    public string? Restart { get; init; }
    public List<TweakValue> Values { get; init; } = new();

    public bool IsOn { get; private set; }
    public bool Wanted { get; set; }
    public string RestartText => Restart switch { "explorer" => "перезапуск Проводника", "logoff" => "выход из системы", "reboot" => "перезагрузка", _ => "" };

    public void Refresh() => Wanted = IsOn = Values.All(v => v.Raw(true) is { } raw ? raw.SameAs(Reg.Read(v.Key, v.Name)) : Reg.Read(v.Key, v.Name) == null);

    public void Apply(bool on, UndoTx tx)
    {
        foreach (var v in Values)
            if (v.Raw(on) is { } raw) tx.SetValue(v.Key, raw);
            else if (!on && v.OffDeleteKey != null) tx.DeleteKey(v.OffDeleteKey);
            else tx.DeleteValue(v.Key, v.Name);
    }
}

public static class Tweaks
{
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "Data", "tweaks.json");

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    public static List<Tweak> Load(string? path = null)
    {
        var list = JsonSerializer.Deserialize<List<Tweak>>(File.ReadAllText(path ?? FilePath), Json)!;
        list.ForEach(t => t.Refresh());
        return list;
    }

    /// <summary>Applies every tweak whose checkbox differs from the current state. Returns the strongest restart needed.</summary>
    public static (string? Restart, List<string> Errors) ApplyChanged(IEnumerable<Tweak> tweaks)
    {
        var changed = tweaks.Where(t => t.Wanted != t.IsOn).ToList();
        if (changed.Count == 0) return (null, new());
        using var tx = UndoTx.Begin("Твики: " + string.Join(", ", changed.Select(t => (t.Wanted ? "+" : "-") + t.Name)));
        foreach (var t in changed)
        {
            try { t.Apply(t.Wanted, tx); }
            catch (Exception e) { tx.Errors.Add($"{t.Name}: {e.Message}"); }
            t.Refresh();
        }
        var order = new[] { "explorer", "logoff", "reboot" };
        return (changed.Select(t => t.Restart).Where(r => r != null).MaxBy(r => Array.IndexOf(order, r)), tx.Errors);
    }

    /// <summary>Kills the shell; winlogon respawns it (AutoRestartShell). Starting it ourselves would give an elevated shell.</summary>
    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } }
    }
}
