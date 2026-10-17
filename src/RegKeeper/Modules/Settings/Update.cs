using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>
/// Online updates from GitHub Releases: UpdateUrl is .../releases/latest/download, where installer\release.mjs puts the MSI
/// and latest.txt, the release JSON (version, MSI name, SHA-256) plus its ECDSA P-256 signature. The files are public;
/// the signature is the guard, and nothing it does not cover is run.
/// URL and public key come from build.ps1 (%USERPROFILE%\.regkeeper\update.json); a build without them has no channel.
/// </summary>
public static class Update
{
    public record Release(string Version, string File, string Sha256);

    static string? Meta(string name) =>
        typeof(Update).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == name)?.Value is { Length: > 0 } v ? v : null;

    public static bool Online => Meta("UpdateUrl") != null;

    public static Version Current => Version.Parse(typeof(Update).Assembly.GetName().Version!.ToString(3));

    /// <summary>Without a channel the button looks in a folder the user picked (W:\RegMsi, a share, OneDrive...).</summary>
    public static string? Source
    {
        get => Reg.GetString(MainWindow.Key, "UpdateSource") is { Length: > 0 } s && Directory.Exists(s) ? s : null;
        set => Microsoft.Win32.Registry.SetValue(MainWindow.Key, "UpdateSource", value ?? "");
    }

    /// <summary>"RegKeeper-1.2.3-x64.msi" gives 1.2.3.</summary>
    internal static Version? VersionOf(string msi) =>
        Path.GetFileNameWithoutExtension(msi).Split('-') is [_, var v, _] && Version.TryParse(v, out var ver) ? ver : null;

    /// <summary>The newest MSI in the folder that is newer than this build, or null.</summary>
    public static string? Find(string folder) => Directory.EnumerateFiles(folder, "RegKeeper-*-x64.msi")
        .Select(f => (File: f, Version: VersionOf(f))).Where(m => m.Version > Current)
        .OrderByDescending(m => m.Version).Select(m => m.File).FirstOrDefault();

    static string Dir => AppContext.BaseDirectory.TrimEnd('\\');

    /// <summary>This copy is the one the MSI put in place (HKLM\Software\RegKeeper\Setup); any other is portable.</summary>
    public static bool Installed =>
        Reg.GetString(@"HKEY_LOCAL_MACHINE\Software\RegKeeper\Setup", "InstallDir") is { } d && d.TrimEnd('\\').Equals(Dir, StringComparison.OrdinalIgnoreCase);

    static async Task<HttpResponseMessage> Get(string name, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // follows GitHub's redirect to its file storage
        HttpResponseMessage res;
        try { res = await http.GetAsync(Meta("UpdateUrl")!.TrimEnd('/') + "/" + name, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException e) { throw new IOException(L.T("Не удалось связаться с сервером обновлений. Проверьте подключение к интернету."), e); }
        if (res.IsSuccessStatusCode) return res;
        res.Dispose();
        throw new IOException(L.T($"Сервер обновлений ответил ошибкой {(int)res.StatusCode}. Попробуйте позже."));
    }

    /// <summary>The signed release if it is newer than this build, else null.</summary>
    public static async Task<Release?> Check(CancellationToken ct)
    {
        using var res = await Get("latest.txt", ct);
        var lines = (await res.Content.ReadAsStringAsync(ct)).Split('\n');
        var r = lines.Length >= 2 ? Verify(Encoding.UTF8.GetBytes(lines[0]), Convert.FromBase64String(lines[1].Trim()), Meta("UpdatePub")!) : null;
        if (r == null) throw new InvalidDataException(L.T("Подпись обновления не сходится, обновление отклонено."));
        return Version.Parse(r.Version) > Current ? r : null;
    }

    internal static Release? Verify(byte[] json, byte[] sig, string publicKey)
    {
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
        return ec.VerifyData(json, sig, HashAlgorithmName.SHA256)
            ? JsonSerializer.Deserialize<Release>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            : null;
    }

    /// <summary>Where the MSI waits: ProgramData, so users cannot swap it before the elevated msiexec runs. Emptied on every start after an update.</summary>
    public static string Cache => Path.Combine(AppPaths.Data, "Update");

    /// <summary>Downloads the MSI and checks its hash; nothing is left behind if that fails.</summary>
    public static async Task<string> Download(Release r, IProgress<int> percent, CancellationToken ct)
    {
        if (Directory.Exists(Cache)) Directory.Delete(Cache, true);
        var path = Path.Combine(Directory.CreateDirectory(Cache).FullName, Path.GetFileName(r.File));
        try
        {
            using (var res = await Get(r.File, ct))
            {
                var total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(path);
                var buf = new byte[1 << 16];
                for (int n; (n = await src.ReadAsync(buf, ct)) > 0;)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    if (total > 0) percent.Report((int)(dst.Length * 100 / total));
                }
            }
            string hash;
            await using (var f = File.OpenRead(path)) hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
            if (!hash.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T("Скачанный файл повреждён: контрольная сумма не совпала."));
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    // waits for RegKeeper to exit, then: installed copy - MSI upgrade with a progress bar only; portable copy - files pulled
    // out of the MSI (administrative install) over the old ones. Either way RegKeeper starts again and is told how it went.
    // Paths reach the script through the environment, never through its text.
    const string Script = """
        Wait-Process -Id $env:RK_PID -ErrorAction SilentlyContinue
        $msi = '"' + $env:RK_MSI + '"'
        if ($env:RK_PORTABLE) {
            $t = Join-Path $env:TEMP ('RegKeeper-' + [guid]::NewGuid())
            $p = Start-Process msiexec.exe -ArgumentList '/a', $msi, '/qb', ('TARGETDIR="' + $t + '"') -Wait -PassThru
            $exe = Get-ChildItem $t -Recurse -Filter RegKeeper.exe -ErrorAction SilentlyContinue | Select-Object -First 1
            $ok = $false
            if ($p.ExitCode -eq 0 -and $exe) { robocopy $exe.DirectoryName $env:RK_DIR /E /R:5 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null; $ok = $LASTEXITCODE -lt 8 }
            Remove-Item $t -Recurse -Force -ErrorAction SilentlyContinue
        } else {
            $p = Start-Process msiexec.exe -ArgumentList '/i', $msi, '/passive', '/norestart' -Wait -PassThru
            $ok = $p.ExitCode -in 0, 3010
        }
        Start-Process (Join-Path $env:RK_DIR 'RegKeeper.exe') -ArgumentList $(if ($ok) { '--updated' } else { '--update-failed' })
        """;

    /// <summary>Hands the MSI to the script above; the caller quits right after. The watcher goes too, its exe is replaced.</summary>
    public static void Run(string msi)
    {
        foreach (var p in Process.GetProcessesByName("RegKeeper.Watcher")) using (p) p.Kill();
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", Script } };
        psi.Environment["RK_MSI"] = msi;
        psi.Environment["RK_DIR"] = Dir;
        psi.Environment["RK_PID"] = Environment.ProcessId.ToString();
        psi.Environment["RK_PORTABLE"] = Installed ? "" : "1";
        Process.Start(psi);
    }

    /// <summary>First start after the script: drop the downloaded MSI, bring the watcher back if it is meant to run.</summary>
    public static void Finish()
    {
        try { if (Directory.Exists(Cache)) Directory.Delete(Cache, true); } catch (IOException) { }
        if (Integration.WatcherAutostart) Integration.StartWatcher();
    }
}
