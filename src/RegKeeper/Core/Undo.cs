using System.Text;
using System.Text.Json;
using Microsoft.Win32.TaskScheduler;

namespace RegKeeper.Core;

public static class AppPaths
{
    public static readonly string Data = Dir(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RegKeeper");
    public static readonly string Undo = Dir(Data, "Undo");
    public static readonly string Traces = Dir(Data, "Traces");
    public static readonly string Snapshots = Dir(Data, "Snapshots");

    static string Dir(params string[] parts)
    {
        var p = Path.Combine(parts);
        Directory.CreateDirectory(p);
        return p;
    }
}

public sealed record UndoInfo(string Title, DateTime Date, int Count, List<string> Items)
{
    public string Dir { get; init; } = "";
}

/// <summary>
/// Every registry change in the app goes through here. Before touching a key/value the current state is appended to
/// undo.reg as a restore block; restore applies those blocks in reverse order. Blocks are written immediately, so a
/// crash mid-operation still leaves a usable backup.
/// </summary>
public sealed class UndoTx : IDisposable
{
    const string BlockMark = "; ---- block";
    readonly string _dir, _regFile;
    readonly UndoInfo _info;
    int _tasks;

    UndoTx(string title)
    {
        // timestamp keeps folders sortable; the suffix keeps two transactions in the same millisecond apart
        _dir = Path.Combine(AppPaths.Undo, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(_dir);
        _regFile = Path.Combine(_dir, "undo.reg");
        File.WriteAllText(_regFile, RegFile.Header + "\r\n", Encoding.Unicode);
        _info = new UndoInfo(title, DateTime.Now, 0, new List<string>());
        SaveInfo();
    }

    public static UndoTx Begin(string title) => new(title);

    public int Count => _info.Items.Count;
    public List<string> Errors { get; } = new();

    void Block(string text, string what)
    {
        File.AppendAllText(_regFile, $"\r\n{BlockMark}\r\n{text}", Encoding.Unicode);
        _info.Items.Add(what);
    }

    /// <summary>Restore block that deletes the highest missing ancestor of <paramref name="key"/> (null if key exists).</summary>
    static string? MissingAncestor(string key)
    {
        if (Reg.Exists(key)) return null;
        var top = key;
        for (var p = Reg.Parent(key); p.Contains('\\') && !Reg.Exists(p); p = Reg.Parent(p)) top = p;
        return top;
    }

    public void CreateKey(string key)
    {
        key = Reg.Normalize(key);
        if (MissingAncestor(key) is not { } top) return;
        Block($"[-{top}]\r\n", "Создан ключ " + Reg.Short(key));
        Reg.Create(key).Dispose();
    }

    public void SetValue(string key, RawValue v)
    {
        key = Reg.Normalize(key);
        if (MissingAncestor(key) is { } top) Block($"[-{top}]\r\n", "Создан ключ " + Reg.Short(key));
        else
        {
            var old = Reg.Read(key, v.Name);
            if (v.SameAs(old)) return;
            var restore = old == null ? RegFile.FormatName(v.Name) + "=-" : RegFile.FormatValue(old);
            Block($"[{key}]\r\n{restore}\r\n", $"Значение {Reg.Short(key)} → {DisplayName(v.Name)}");
        }
        using var k = Reg.Create(key);
        Reg.Write(k, v);
    }

    public void DeleteValue(string key, string name)
    {
        key = Reg.Normalize(key);
        if (Protect.IsProtectedValue(key)) { Errors.Add($"{Reg.Short(key)} → {name}: защищённый системный раздел, не удалено"); return; }
        using var k = Reg.Open(key, true);
        if (k == null || Reg.Read(k, name) is not { } old) return;
        Block($"[{key}]\r\n{RegFile.FormatValue(old)}\r\n", $"Удалено значение {Reg.Short(key)} → {DisplayName(name)}");
        try { k.DeleteValue(name); }
        catch (Exception e) { Errors.Add($"{Reg.Short(key)} → {name}: {e.Message}"); }
    }

    public void DeleteKey(string key)
    {
        key = Reg.Normalize(key);
        if (Protect.IsProtectedKey(key)) { Errors.Add($"{Reg.Short(key)}: защищённый системный раздел, не удалён"); return; }
        if (!Reg.Exists(key)) return;
        var sb = new StringBuilder();
        RegFile.AppendKey(sb, key, true);
        Block(sb.ToString().TrimStart() + "\r\n", "Удалён ключ " + Reg.Short(key));
        try { Reg.DeleteTree(key); }
        catch (Exception e) { Errors.Add($"{Reg.Short(key)}: {e.Message}"); }
    }

    /// <summary>Copies a key tree to a new location (used for renames), recorded so undo removes the copy.</summary>
    public void CopyKey(string from, string to)
    {
        from = Reg.Normalize(from);
        to = Reg.Normalize(to);
        using var src = Reg.Open(from) ?? throw new IOException("Ключ не найден: " + from);
        CreateKey(to);
        foreach (var v in Reg.Values(src)) SetValue(to, v);
        foreach (var sub in Reg.SubKeys(src)) CopyKey(from + "\\" + sub, to + "\\" + sub);
    }

    public void RenameKey(string from, string newLeaf)
    {
        var to = Reg.Combine(Reg.Parent(Reg.Normalize(from)), newLeaf);
        if (Reg.Exists(to)) throw new IOException("Ключ уже существует: " + to);
        CopyKey(from, to);
        DeleteKey(from);
    }

    /// <summary>Saves a scheduled task definition so restore can re-register it.</summary>
    public void BackupTask(string path, string xml)
    {
        File.WriteAllText(Path.Combine(_dir, $"task_{_tasks++}.xml"), xml);
        File.AppendAllLines(Path.Combine(_dir, "tasks.txt"), new[] { path });
        _info.Items.Add("Задача " + path);
    }

    /// <summary>Records a task this transaction registered, so restore deletes it.</summary>
    public void CreatedTask(string path)
    {
        File.AppendAllLines(Path.Combine(_dir, "created_tasks.txt"), new[] { path });
        _info.Items.Add("Создана задача " + path);
    }

    /// <summary>Free-form entry for non-registry actions that are not undoable (shown in the log only).</summary>
    public void Note(string what) => _info.Items.Add(what);

    static string DisplayName(string n) => n == "" ? "(По умолчанию)" : n;

    void SaveInfo() =>
        File.WriteAllText(Path.Combine(_dir, "info.json"), JsonSerializer.Serialize(_info with { Count = _info.Items.Count }));

    public void Dispose()
    {
        if (_info.Items.Count == 0) Directory.Delete(_dir, true);
        else SaveInfo();
    }
}

public static class UndoCenter
{
    public static List<UndoInfo> List() =>
        Directory.GetDirectories(AppPaths.Undo)
            .Select(d =>
            {
                try { return JsonSerializer.Deserialize<UndoInfo>(File.ReadAllText(Path.Combine(d, "info.json")))! with { Dir = d }; }
                catch { return null; }
            })
            .Where(i => i != null).Select(i => i!)
            .OrderByDescending(i => i.Date).ToList();

    public static void Restore(UndoInfo info)
    {
        var created = Path.Combine(info.Dir, "created_tasks.txt");
        if (File.Exists(created))
            foreach (var p in File.ReadAllLines(created))
                if (TaskService.Instance.GetTask(p) is { } t) using (t) t.Folder.DeleteTask(t.Name, false);

        var tasks = Path.Combine(info.Dir, "tasks.txt");
        if (File.Exists(tasks))
        {
            var paths = File.ReadAllLines(tasks);
            for (var i = 0; i < paths.Length; i++)
            {
                var td = TaskService.Instance.NewTask();
                td.XmlText = File.ReadAllText(Path.Combine(info.Dir, $"task_{i}.xml"));
                TaskService.Instance.RootFolder.RegisterTaskDefinition(paths[i], td);
            }
        }

        var text = File.ReadAllText(Path.Combine(info.Dir, "undo.reg"));
        var blocks = text.Split("; ---- block").Skip(1).Reverse();
        foreach (var b in blocks) RegFile.ApplyRaw(RegFile.Parse(b));
        Directory.Delete(info.Dir, true);
    }

    public static void Delete(UndoInfo info) => Directory.Delete(info.Dir, true);
}
