using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public class StoreApp
{
    public string Name { get; set; } = "";
    public string PackageFullName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string? InstallLocation { get; set; }
    public bool IsFramework { get; set; }
    public bool NonRemovable { get; set; }
    public string SignatureKind { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long SizeBytes { get; set; }
    public ImageSource? Icon { get; set; }

    public bool Checked { get; set; }
    public bool Favorite { get; set; }
    /// <summary>Size against the largest app in the list, 0..1: the length of the bar in the Size column.</summary>
    public double Rel { get; set; }

    public string SizeText => SizeBytes > 0 ? Sys.Size(SizeBytes) : "";
    public bool IsSystem => IsFramework || NonRemovable || SignatureKind == "System";
    public string Title => DisplayName != "" ? DisplayName : Name;
    public string Letter => Title.FirstOrDefault(char.IsLetterOrDigit) is var c and not '\0' ? char.ToUpper(c).ToString() : "?";
    public bool Heavy => SizeBytes > 500L * 1024 * 1024;
    // in a DataTemplate L.Apply never sees the text, so it comes translated from here
    public string HeavyLabel => L.T("Тяжёлое");
    /// <summary>Not heavy but at least 9% of the largest: its bar is drawn bright.</summary>
    public bool Big => !Heavy && Rel >= 0.09;
    // a 2% stub so even a tiny app shows a bar
    public System.Windows.GridLength BarFill => new(Math.Max(Rel, 0.02), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarRest => new(1 - Math.Max(Rel, 0.02), System.Windows.GridUnitType.Star);

    /// <summary>"CN=Foo Inc, O=Foo Inc, C=US" → "Foo Inc".</summary>
    public string PublisherName
    {
        get
        {
            var parts = Publisher.Split(',', StringSplitOptions.TrimEntries);
            var o = parts.FirstOrDefault(p => p.StartsWith("O=")) ?? parts.FirstOrDefault(p => p.StartsWith("CN="));
            return o?[(o.IndexOf('=') + 1)..].Trim('"') ?? Publisher;
        }
    }
}

public static class StoreApps
{
    static readonly string PowerShell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    // Fixed scripts: user data never becomes script text. The package name for removal travels in an env variable.
    const string ListScript =
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
        "@(Get-AppxPackage | Select-Object Name,PackageFullName,Publisher,Version,InstallLocation,IsFramework,NonRemovable," +
        "@{n='SignatureKind';e={\"$($_.SignatureKind)\"}}) | ConvertTo-Json -Compress";
    public const string RemoveScript = "$ErrorActionPreference='Stop'; Remove-AppxPackage -Package $env:RK_PACKAGE";

    public static List<StoreApp> Parse(string json)
    {
        json = json.Trim();
        if (json == "") return new();
        if (json.StartsWith('{')) json = "[" + json + "]"; // ConvertTo-Json unwraps a single item
        return JsonSerializer.Deserialize<List<StoreApp>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public static List<StoreApp> Load()
    {
        var (code, output, error) = Run(ListScript, null);
        if (code != 0) throw new InvalidOperationException("Get-AppxPackage: " + error.Trim());
        var apps = Parse(output);
        foreach (var a in apps) a.DisplayName = ResolveDisplayName(a.PackageFullName);
        return apps;
    }

    /// <summary>The package logo named in AppxManifest (Properties/Logo); on disk it is usually only scale variants. Frozen.</summary>
    public static ImageSource? Logo(string? dir)
    {
        if (dir == null || !File.Exists(Path.Combine(dir, "AppxManifest.xml"))) return null;
        try
        {
            var logo = XDocument.Load(Path.Combine(dir, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Logo" && e.Parent?.Name.LocalName == "Properties")?.Value;
            if (string.IsNullOrWhiteSpace(logo)) return null;
            var full = Path.Combine(dir, logo);
            var file = File.Exists(full) ? full
                : Directory.EnumerateFiles(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + ".*" + Path.GetExtension(full))
                    .OrderBy(f => f.Contains("contrast-") ? 3 : f.Contains("scale-100") ? 0 : f.Contains("targetsize") ? 1 : 2).FirstOrDefault();
            if (file == null) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(file);
            img.DecodePixelWidth = 64; // drawn at 26 in the list and 44 in the details card
            img.CacheOption = BitmapCacheOption.OnLoad; // no file lock on the package
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or NotSupportedException or FormatException) { return null; }
    }

    /// <summary>App user model id for "shell:AppsFolder\..." (family name + the first Application Id in the manifest); null when the package has no app.</summary>
    public static string? Aumid(StoreApp app)
    {
        var parts = app.PackageFullName.Split('_');
        if (parts.Length < 2 || app.InstallLocation is not { } dir || !File.Exists(Path.Combine(dir, "AppxManifest.xml"))) return null;
        try
        {
            var id = XDocument.Load(Path.Combine(dir, "AppxManifest.xml")).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Application")?.Attribute("Id")?.Value;
            return string.IsNullOrEmpty(id) ? null : $"{parts[0]}_{parts[^1]}!{id}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return null; }
    }

    public static void FillSizes(IEnumerable<StoreApp> apps)
    {
        foreach (var a in apps)
            if (a.InstallLocation is { } dir && Directory.Exists(dir)) a.SizeBytes = Sys.DirSize(dir);
    }

    public static ProcessStartInfo RemoveStartInfo(string packageFullName)
    {
        var psi = StartInfo(RemoveScript);
        psi.Environment["RK_PACKAGE"] = packageFullName;
        return psi;
    }

    /// <summary>Removes the package for the current user. Returns null on success, else the PowerShell error text.</summary>
    public static string? Remove(StoreApp app)
    {
        if (app.IsSystem) return L.T("Системный пакет или фреймворк, удаление запрещено.");
        var (code, _, error) = Run(RemoveScript, app.PackageFullName);
        return code == 0 ? null : error.Trim();
    }

    static ProcessStartInfo StartInfo(string script) => new(PowerShell)
    {
        ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
    };

    static (int Code, string Output, string Error) Run(string script, string? package)
    {
        var psi = package == null ? StartInfo(script) : RemoveStartInfo(package);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, err.Result);
    }

    const string Repository = @"HKEY_CURRENT_USER\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages\";

    static string ResolveDisplayName(string fullName)
    {
        var raw = Reg.GetString(Repository + fullName, "DisplayName");
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.StartsWith('@')) return Load(raw) ?? "";
        if (!raw.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return raw;
        // the repository often keeps the bare manifest reference; relative keys live under Resources/ or the package root
        var key = raw[12..];
        if (key.StartsWith("//")) return Load($"@{{{fullName}?{raw}}}") ?? "";
        var name = fullName.Split('_')[0];
        key = key.TrimStart('/');
        return Load($"@{{{fullName}?ms-resource://{name}/Resources/{key}}}") ?? Load($"@{{{fullName}?ms-resource://{name}/{key}}}") ?? "";
    }

    static string? Load(string indirect)
    {
        var sb = new StringBuilder(512);
        return SHLoadIndirectString(indirect, sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int SHLoadIndirectString(string source, StringBuilder output, int size, IntPtr reserved);
}
