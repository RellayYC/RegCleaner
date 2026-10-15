using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed class InstalledApp
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string DisplayIcon { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime? InstallDate { get; init; }
    public string KeyPath { get; init; } = "";
    public bool IsMsi { get; init; }
    public string Scope { get; init; } = "";
    public ImageSource? Icon { get; set; }

    public string SizeText => SizeBytes > 0 ? Sys.Size(SizeBytes) : "";
    public string DateText => InstallDate?.ToString("dd.MM.yyyy") ?? "";
    public string ProductCode => Reg.Leaf(KeyPath);
    public bool Broken => !IsMsi && Sys.IsMissing(UninstallString);

    /// <summary>Best guess of the install folder: InstallLocation, else folder of the icon/uninstaller if it is not a system path.</summary>
    public string? InstallDir
    {
        get
        {
            foreach (var candidate in new[] { InstallLocation, Path.GetDirectoryName(Sys.ExtractPath(DisplayIcon) ?? ""), Path.GetDirectoryName(Sys.ExtractPath(UninstallString) ?? "") })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                var d = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')).TrimEnd('\\');
                if (Leftovers.IsProtectedDir(d) || !Path.IsPathFullyQualified(d)) continue;
                return d;
            }
            return null;
        }
    }
}

public static class Inventory
{
    const string UninstallSub = @"Microsoft\Windows\CurrentVersion\Uninstall";

    static readonly (string Root, string Scope)[] Roots =
    {
        ($@"HKEY_LOCAL_MACHINE\SOFTWARE\{UninstallSub}", "Все пользователи"), // i18n: keep - translated where used
        ($@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\{UninstallSub}", "Все пользователи, 32-бит"), // i18n: keep - translated where used
        ($@"HKEY_CURRENT_USER\Software\{UninstallSub}", "Текущий пользователь"), // i18n: keep - translated where used
    };

    public static List<InstalledApp> Load()
    {
        var list = new List<InstalledApp>();
        foreach (var (root, scope) in Roots)
        {
            foreach (var sub in Reg.SubKeys(root))
            {
                var path = root + "\\" + sub;
                using var k = Reg.Open(path);
                if (k == null) continue;
                var name = Reg.GetString(k, "DisplayName");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                if (Reg.GetString(k, "ParentKeyName") != null) continue;
                var release = Reg.GetString(k, "ReleaseType") ?? "";
                if (release.Contains("Update") || release == "Hotfix") continue;

                var uninst = Reg.GetString(k, "UninstallString") ?? "";
                var isMsi = k.GetValue("WindowsInstaller") is int wi && wi == 1 || uninst.Contains("msiexec", StringComparison.OrdinalIgnoreCase);
                list.Add(new InstalledApp
                {
                    Name = name.Trim(),
                    Version = Reg.GetString(k, "DisplayVersion") ?? "",
                    Publisher = (Reg.GetString(k, "Publisher") ?? "").Trim(),
                    InstallLocation = Reg.GetString(k, "InstallLocation") ?? "",
                    UninstallString = uninst,
                    QuietUninstallString = Reg.GetString(k, "QuietUninstallString") ?? "",
                    DisplayIcon = Reg.GetString(k, "DisplayIcon") ?? "",
                    SizeBytes = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0,
                    InstallDate = DateTime.TryParseExact(Reg.GetString(k, "InstallDate"), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                    KeyPath = path,
                    IsMsi = isMsi,
                    Scope = L.T(scope),
                });
            }
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Splits the uninstall command into exe + args and starts it. MSI entries use msiexec /x {code}.</summary>
    public static Process? RunUninstaller(InstalledApp app, bool quiet)
    {
        if (app.IsMsi && app.ProductCode.StartsWith('{'))
            return Process.Start(new ProcessStartInfo("msiexec.exe", $"/x {app.ProductCode}" + (quiet ? " /qb" : "")) { UseShellExecute = true });
        var cmd = quiet && app.QuietUninstallString != "" ? app.QuietUninstallString : app.UninstallString;
        var exe = Sys.ExtractPath(cmd) ?? throw new InvalidOperationException(L.T("Пустая строка удаления"));
        var trimmed = cmd.Trim();
        var args = trimmed.StartsWith('"') ? trimmed[(trimmed.IndexOf('"', 1) + 1)..]
            : trimmed.Length > exe.Length && trimmed.StartsWith(exe, StringComparison.OrdinalIgnoreCase) ? trimmed[exe.Length..] : "";
        return Process.Start(new ProcessStartInfo(exe, args.Trim()) { UseShellExecute = true });
    }

    /// <summary>Loads the app icon (frozen, safe to create off the UI thread).</summary>
    public static ImageSource? LoadIcon(string? iconRef)
    {
        var path = Sys.ExtractPath(iconRef);
        if (path == null || !File.Exists(path)) return null;
        var index = 0;
        var comma = iconRef!.LastIndexOf(',');
        if (comma > 0) int.TryParse(iconRef[(comma + 1)..], out index);
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) index = 0;
        if (ExtractIconEx(path, index, IntPtr.Zero, out var small, 1) == 0 || small == IntPtr.Zero)
            if (ExtractIconEx(path, 0, IntPtr.Zero, out small, 1) == 0 || small == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(small, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(small); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr h);
}
