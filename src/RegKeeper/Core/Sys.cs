using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RegKeeper.Core;

/// <summary>Command lines, file paths, recycle bin.</summary>
public static partial class Sys
{
    [GeneratedRegex(@"^(.+?\.(exe|dll|com|bat|cmd|ocx|sys|cpl|msc|scr|vbs|js|ps1|lnk|jar|ax|drv|tlb|olb|msi))(?=$|[\s,""/\\])",RegexOptions.IgnoreCase)]
    private static partial Regex ExeRx();

    /// <summary>Extracts the executable/file path from a command line or icon reference.
    /// Handles quotes, env vars, "@dll,-123", "\??\", "\SystemRoot\", rundll32 and unquoted paths with spaces.</summary>
    public static string? ExtractPath(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        var s = Environment.ExpandEnvironmentVariables(cmd.Trim()).TrimStart('@');
        if (s.StartsWith(@"\??\")) s = s[4..];
        if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            s = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + s[11..];
        else if (s.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
            s = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), s);

        string path, rest;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            path = end < 0 ? s[1..] : s[1..end];
            rest = end < 0 ? "" : s[(end + 1)..];
        }
        else if (ExeRx().Match(s) is { Success: true } m)
        {
            path = m.Groups[1].Value;
            rest = s[path.Length..];
        }
        else
        {
            var cut = s.IndexOfAny(new[] { ',', ' ' });
            path = File.Exists(s) || Directory.Exists(s) || cut < 0 ? s : s[..cut];
            rest = "";
        }

        path = path.Trim();
        var leaf = Path.GetFileName(path).ToLowerInvariant();
        if (leaf is "rundll32.exe" or "rundll32" or "regsvr32.exe" && !string.IsNullOrWhiteSpace(rest))
        {
            var arg = rest.Trim();
            while (arg.StartsWith('/') || arg.StartsWith('-')) arg = arg.Contains(' ') ? arg[(arg.IndexOf(' ') + 1)..].Trim() : "";
            return arg == "" ? path : ExtractPath(arg.Split(',')[0]);
        }
        return path;
    }

    /// <summary>True only when we can say for sure the referenced file is gone: rooted path, no file, no dir,
    /// and not present under the WOW64 twin of system32.</summary>
    public static bool IsMissing(string? pathOrCmd)
    {
        var p = ExtractPath(pathOrCmd);
        if (string.IsNullOrEmpty(p) || !Path.IsPathFullyQualified(p) || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        if (p.StartsWith(@"\\")) return false; // network paths: don't judge, may be offline
        if (File.Exists(p) || Directory.Exists(p)) return false;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys32 = Path.Combine(win, "System32");
        if (p.StartsWith(sys32, StringComparison.OrdinalIgnoreCase))
        {
            var wow = Path.Combine(win, "SysWOW64") + p[sys32.Length..];
            if (File.Exists(wow) || Directory.Exists(wow)) return false;
        }
        // drive not present (removable/unmapped) - can't tell
        var root = Path.GetPathRoot(p);
        return root != null && Directory.Exists(root);
    }

    public static string Company(string? path)
    {
        try { return path != null && File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "" : ""; }
        catch { return ""; }
    }

    public static bool IsMicrosoft(string? path) => Company(path).Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} МБ",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} ГБ"
    };

    public static long DirSize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    /// <summary>Moves files/folders to the Recycle Bin (silent). Returns false if the shell refused.</summary>
    public static bool Recycle(params string[] paths)
    {
        paths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (paths.Length == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }

    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public static void ShowInExplorer(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

    public static string? ShortcutTarget(string lnk)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            var s = shell.CreateShortcut(lnk);
            return ((string)s.TargetPath + " " + (string)s.Arguments).Trim();
        }
        catch { return null; }
    }

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
}
