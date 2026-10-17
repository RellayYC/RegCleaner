using System.Diagnostics;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>
/// Updates without publishing anything: the new RegKeeper-x.y.z-x64.msi comes from a folder the user picks
/// (network share, OneDrive...) or a file they choose. The MSI's MajorUpgrade replaces the installed version in place.
/// </summary>
public static class Update
{
    const string Key = @"HKEY_CURRENT_USER\Software\RegKeeper";

    public static string? Source
    {
        get => Reg.GetString(Key, "UpdateSource") is { Length: > 0 } s ? s : null;
        set => Registry.SetValue(Key, "UpdateSource", value ?? "");
    }

    public static Version Current => Version.Parse(typeof(Update).Assembly.GetName().Version!.ToString(3));

    /// <summary>Set by the MSI (HKLM\Software\RegKeeper\Setup); a portable copy has none, so the MSI installs beside it.</summary>
    public static bool Installed => Reg.GetString(@"HKEY_LOCAL_MACHINE\Software\RegKeeper\Setup", "InstallDir") != null;

    /// <summary>"RegKeeper-1.2.3-x64.msi" gives 1.2.3.</summary>
    public static Version? VersionOf(string msi) =>
        Path.GetFileNameWithoutExtension(msi).Split('-') is [_, var v, _] && Version.TryParse(v, out var ver) ? ver : null;

    /// <summary>The newest MSI in the folder that is newer than this build.</summary>
    public static string? Find(string folder) =>
        Directory.EnumerateFiles(folder, "RegKeeper-*-x64.msi")
            .Select(f => (File: f, Version: VersionOf(f)))
            .Where(m => m.Version > Current)
            .OrderByDescending(m => m.Version).Select(m => m.File).FirstOrDefault();

    /// <summary>Starts the MSI wizard; the caller quits so its files are free. The watcher is stopped for the same reason.</summary>
    public static void Run(string msi)
    {
        foreach (var p in Process.GetProcessesByName("RegKeeper.Watcher")) using (p) p.Kill();
        Process.Start(new ProcessStartInfo("msiexec.exe") { ArgumentList = { "/i", msi } });
    }
}
