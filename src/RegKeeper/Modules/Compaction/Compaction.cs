using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed class HiveInfo
{
    public string Key { get; init; } = "";
    public string File { get; init; } = "";
    public long Size { get; init; }
    public string? Compacted { get; set; }
    public long CompactedSize { get; set; }

    public long Gain => Compacted != null ? Math.Max(0, Size - CompactedSize) : 0;
    public string SizeText => Size > 0 ? Sys.Size(Size) : "нет доступа";
    public string CompactedText => Compacted != null ? Sys.Size(CompactedSize) : "";
    public string GainText => Compacted != null ? $"{Sys.Size(Gain)} ({100.0 * Gain / Math.Max(1, Size):0.#}%)" : "";
}

/// <summary>
/// Registry compaction: RegSaveKeyEx writes each hive into a fresh, defragmented file; RegReplaceKey makes Windows
/// swap it in at the next boot and keeps the old hive as a backup file.
/// </summary>
public static class Compaction
{
    public static readonly string Dir = Path.Combine(AppPaths.Data, "Compaction");
    const string HiveList = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\hivelist";

    public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Machine SOFTWARE/SYSTEM/.DEFAULT and the current user's hives. SAM, SECURITY, BCD and app silos are left alone.</summary>
    public static List<HiveInfo> Plan()
    {
        var sid = WindowsIdentity.GetCurrent().User!.Value;
        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [@"\REGISTRY\MACHINE\SOFTWARE"] = @"HKEY_LOCAL_MACHINE\SOFTWARE",
            [@"\REGISTRY\MACHINE\SYSTEM"] = @"HKEY_LOCAL_MACHINE\SYSTEM",
            [@"\REGISTRY\USER\.DEFAULT"] = @"HKEY_USERS\.DEFAULT",
            [@"\REGISTRY\USER\" + sid] = @"HKEY_USERS\" + sid,
            [@"\REGISTRY\USER\" + sid + "_Classes"] = @"HKEY_USERS\" + sid + "_Classes",
        };
        var drives = DriveMap();
        var list = new List<HiveInfo>();
        using var k = Reg.Open(HiveList);
        if (k == null) return list;
        foreach (var name in Reg.ValueNames(k))
        {
            if (!wanted.TryGetValue(name, out var key) || Reg.GetString(k, name) is not { Length: > 0 } device) continue;
            var file = drives.Where(d => device.StartsWith(d.Device + "\\", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Letter + device[d.Device.Length..]).FirstOrDefault();
            // System32\config is not even listable without admin rights: keep the hive, size unknown (0)
            if (file != null) list.Add(new HiveInfo { Key = key, File = file, Size = System.IO.File.Exists(file) ? new FileInfo(file).Length : 0 });
        }
        return list;
    }

    /// <summary>Saves compacted copies of the hives into a new folder. Needs admin (SeBackupPrivilege).</summary>
    public static void Analyze(IEnumerable<HiveInfo> hives, CancellationToken ct = default)
    {
        if (!IsAdmin) throw new UnauthorizedAccessException("Для анализа сжатия нужны права администратора.");
        // previous analyses that were never applied hold only throw-away copies
        if (Directory.Exists(Dir))
            foreach (var d in Directory.GetDirectories(Dir).Where(d => !Directory.EnumerateFiles(d, "*.old").Any()))
                Directory.Delete(d, true);
        var dir = Directory.CreateDirectory(Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd_HHmmss"))).FullName;
        EnablePrivilege("SeBackupPrivilege");
        foreach (var h in hives)
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(dir, Reg.Leaf(h.Key).TrimStart('.'));
            using var key = Reg.Open(h.Key) ?? throw new IOException("Раздел не открывается: " + h.Key);
            Check(RegSaveKeyExW(key.Handle, file, IntPtr.Zero, RegLatestFormat), "RegSaveKeyEx " + h.Key);
            h.Compacted = file;
            h.CompactedSize = new FileInfo(file).Length;
        }
    }

    /// <summary>
    /// Schedules the swap for the next boot. Refuses unless the backup target is a fresh path on the hive's volume
    /// and the compacted file is actually smaller; only then checks admin rights and calls RegReplaceKey.
    /// </summary>
    public static void Replace(HiveInfo h, string backup)
    {
        if (string.IsNullOrWhiteSpace(backup)) throw new ArgumentException("Без резервной копии замена не выполняется.", nameof(backup));
        if (System.IO.File.Exists(backup)) throw new IOException("Файл резервной копии уже существует: " + backup);
        if (h.Compacted is not { } file || !System.IO.File.Exists(file)) throw new FileNotFoundException("Нет сжатой копии раздела " + h.Key);
        if (new FileInfo(file).Length >= h.Size) throw new InvalidOperationException("Сжатие не даёт выигрыша для " + h.Key);
        if (!string.Equals(Path.GetPathRoot(backup), Path.GetPathRoot(h.File), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetPathRoot(file), Path.GetPathRoot(h.File), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Сжатая копия и резервная копия должны лежать на том же диске, что и раздел.");
        if (!IsAdmin) throw new UnauthorizedAccessException("Для сжатия реестра нужны права администратора.");

        EnablePrivilege("SeBackupPrivilege");
        EnablePrivilege("SeRestorePrivilege");
        using var key = Reg.Open(h.Key) ?? throw new IOException("Раздел не открывается: " + h.Key);
        Check(RegReplaceKeyW(key.Handle, null, file, backup), "RegReplaceKey " + h.Key);
    }

    /// <summary>System restore point before compaction. False if System Restore is off or refused.</summary>
    public static bool CreateRestorePoint(string description)
    {
        try
        {
            using var sr = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var args = sr.GetMethodParameters("CreateRestorePoint");
            args["Description"] = description;
            args["RestorePointType"] = 12;  // MODIFY_SETTINGS
            args["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
            using var r = sr.InvokeMethod("CreateRestorePoint", args, null);
            return Convert.ToUInt32(r["ReturnValue"]) == 0;
        }
        catch (ManagementException) { return false; }
    }

    static List<(string Letter, string Device)> DriveMap()
    {
        var list = new List<(string, string)>();
        var sb = new StringBuilder(1024);
        foreach (var d in Environment.GetLogicalDrives())
        {
            var letter = d.TrimEnd('\\');
            if (QueryDosDeviceW(letter, sb, sb.Capacity) > 0) list.Add((letter, sb.ToString()));
        }
        return list;
    }

    static void Check(int err, string what)
    {
        if (err != 0) throw new IOException($"{what}: {new System.ComponentModel.Win32Exception(err).Message}");
    }

    static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out var token))
            throw new System.ComponentModel.Win32Exception();
        try
        {
            var tp = new TokenPrivileges { Count = 1, Attributes = SePrivilegeEnabled };
            if (!LookupPrivilegeValueW(null, name, out tp.Luid) || !AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                || Marshal.GetLastWin32Error() != 0)
                throw new UnauthorizedAccessException("Не удалось включить " + name);
        }
        finally { CloseHandle(token); }
    }

    const int RegLatestFormat = 2, TokenAdjustPrivileges = 0x20, TokenQuery = 0x8, SePrivilegeEnabled = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegSaveKeyExW(SafeHandle key, string file, IntPtr security, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegReplaceKeyW(SafeHandle key, string? subKey, string newFile, string oldFile);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int QueryDosDeviceW(string device, StringBuilder target, int max);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int len, IntPtr prev, IntPtr retLen);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
}
