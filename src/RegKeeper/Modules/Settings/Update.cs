using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>
/// Online updates without a public download: a Cloudflare Worker (installer\worker.js) hands the files of a private
/// Backblaze B2 bucket only to requests carrying the key built into the app. latest.json names the MSI and its SHA-256
/// and is signed (ECDSA P-256) by installer\release.mjs; the app takes nothing the signature does not cover.
/// URL, key and public key come from build.ps1 (%USERPROFILE%\.regkeeper\update.json); a build without them has no channel.
/// </summary>
public static class Update
{
    public record Release(string Version, string File, string Sha256);

    static string? Meta(string name) =>
        typeof(Update).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == name)?.Value is { Length: > 0 } v ? v : null;

    public static bool Online => Meta("UpdateUrl") != null;

    public static Version Current => Version.Parse(typeof(Update).Assembly.GetName().Version!.ToString(3));

    /// <summary>Set by the MSI (HKLM\Software\RegKeeper\Setup); a portable copy has none, so the MSI installs beside it.</summary>
    public static bool Installed => Reg.GetString(@"HKEY_LOCAL_MACHINE\Software\RegKeeper\Setup", "InstallDir") != null;

    static HttpClient Http()
    {
        var http = new HttpClient { BaseAddress = new Uri(Meta("UpdateUrl")!.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Add("X-RegKeeper-Key", Meta("UpdateKey"));
        return http;
    }

    /// <summary>The signed release if it is newer than this build, else null.</summary>
    public static async Task<Release?> Check(CancellationToken ct)
    {
        using var http = Http();
        var json = await http.GetByteArrayAsync("latest.json", ct);
        var sig = Convert.FromBase64String(await http.GetStringAsync("latest.sig", ct));
        var r = Verify(json, sig, Meta("UpdatePub")!) ?? throw new InvalidDataException(L.T("Подпись обновления не сходится, обновление отклонено."));
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

    /// <summary>Downloads the MSI into ProgramData (users cannot swap it there before the elevated msiexec runs) and checks its hash.</summary>
    public static async Task<string> Download(Release r, IProgress<int> percent, CancellationToken ct)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(AppPaths.Data, "Update")).FullName, Path.GetFileName(r.File));
        using var http = Http();
        using (var res = await http.GetAsync(r.File, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
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
        if (hash.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase)) return path;
        File.Delete(path);
        throw new InvalidDataException(L.T("Скачанный файл повреждён: контрольная сумма не совпала."));
    }

    /// <summary>Starts the MSI wizard; the caller quits so its files are free. The watcher is stopped for the same reason.</summary>
    public static void Run(string msi)
    {
        foreach (var p in Process.GetProcessesByName("RegKeeper.Watcher")) using (p) p.Kill();
        Process.Start(new ProcessStartInfo("msiexec.exe") { ArgumentList = { "/i", msi } });
    }
}
