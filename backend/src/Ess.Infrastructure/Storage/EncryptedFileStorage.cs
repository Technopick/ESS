using System.Net.Sockets;
using Ess.Application.Documents;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ess.Infrastructure.Storage;

/// <summary>
/// Files on disk (or a mounted share) under Storage:RootPath, encrypted with ASP.NET Core Data Protection so a copy of
/// the folder alone reveals nothing. Keys are random and sharded into sub-folders.
/// </summary>
public sealed class EncryptedFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly IDataProtector _protector;

    public EncryptedFileStorage(IConfiguration config, IHostEnvironment env, IDataProtectionProvider dp)
    {
        _root = Path.GetFullPath(config["Storage:RootPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "files"));
        Directory.CreateDirectory(_root);
        _protector = dp.CreateProtector("Ess.Files.v1");
    }

    public async Task<string> SaveAsync(byte[] content, CancellationToken ct)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, _protector.Protect(content), ct);
        return key;
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken ct) => _protector.Unprotect(await File.ReadAllBytesAsync(PathFor(key), ct));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (key.Length != 32 || !key.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid storage key.");
        return Path.Combine(_root, key[..2], key[2..4], key);
    }
}

/// <summary>
/// Scans uploads with ClamAV (clamd INSTREAM over TCP) when Storage:ClamAv:Host is set. Without it, files are marked
/// "not scanned"; Production refuses unscanned uploads unless Storage:AllowUnscanned is explicitly true.
/// </summary>
public sealed class ClamAvScanner : IMalwareScanner
{
    private readonly string? _host;
    private readonly int _port;
    private readonly ILogger<ClamAvScanner> _log;

    public ClamAvScanner(IConfiguration config, ILogger<ClamAvScanner> log)
    {
        _host = config["Storage:ClamAv:Host"];
        _port = config.GetValue("Storage:ClamAv:Port", 3310);
        _log = log;
    }

    public async Task<ScanResult> ScanAsync(byte[] content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_host)) return new ScanResult(ScanVerdict.NotScanned, "No virus scanner is configured.");
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await client.ConnectAsync(_host, _port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            for (var offset = 0; offset < content.Length; offset += 64 * 1024)
            {
                var len = Math.Min(64 * 1024, content.Length - offset);
                var size = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(len));
                await stream.WriteAsync(size, timeout.Token);
                await stream.WriteAsync(content.AsMemory(offset, len), timeout.Token);
            }
            await stream.WriteAsync(new byte[4], timeout.Token);
            using var reader = new StreamReader(stream);
            var reply = (await reader.ReadToEndAsync(timeout.Token)).Trim('\0', ' ', '\n');
            if (reply.EndsWith("OK")) return new ScanResult(ScanVerdict.Clean, null);
            if (reply.EndsWith("FOUND")) return new ScanResult(ScanVerdict.Infected, reply);
            _log.LogWarning("Unexpected ClamAV reply: {Reply}", reply);
            return new ScanResult(ScanVerdict.NotScanned, "The virus scanner gave an unexpected answer.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            _log.LogWarning(ex, "ClamAV scan failed");
            return new ScanResult(ScanVerdict.NotScanned, "The virus scanner could not be reached.");
        }
    }
}

/// <summary>
/// Scans uploads with Microsoft Defender's command-line scanner (MpCmdRun.exe), built into Windows Server 2016 and
/// later, so a Windows install has a scanner without ClamAV. Each file is written to a private temporary folder under
/// the storage root, scanned, and deleted. Defender's real-time protection may remove a malicious file as soon as it is
/// written; a file that disappears is treated as infected.
/// </summary>
public sealed class DefenderScanner : IMalwareScanner
{
    private readonly string _exe;
    private readonly string _scanDir;
    private readonly ILogger<DefenderScanner> _log;

    public DefenderScanner(string exe, string scanDir, ILogger<DefenderScanner> log)
    {
        _exe = exe;
        _scanDir = scanDir;
        _log = log;
    }

    public static string? Locate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        return File.Exists(path) ? path : null;
    }

    public async Task<ScanResult> ScanAsync(byte[] content, CancellationToken ct)
    {
        Directory.CreateDirectory(_scanDir);
        var file = Path.Combine(_scanDir, Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await File.WriteAllBytesAsync(file, content, ct);
            var psi = new System.Diagnostics.ProcessStartInfo(_exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var a in new[] { "-Scan", "-ScanType", "3", "-File", file, "-DisableRemediation" }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new IOException("MpCmdRun did not start.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
            await p.WaitForExitAsync(timeout.Token);
            if (p.ExitCode == 0 && File.Exists(file)) return new ScanResult(ScanVerdict.Clean, null);
            if (p.ExitCode == 2 || !File.Exists(file)) return new ScanResult(ScanVerdict.Infected, "Microsoft Defender found a threat.");
            _log.LogWarning("Microsoft Defender scan returned {Code}: {Output}", p.ExitCode, await output);
            return new ScanResult(ScanVerdict.NotScanned, "The virus scanner gave an unexpected answer.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            _log.LogWarning(ex, "Microsoft Defender scan failed");
            return new ScanResult(ScanVerdict.NotScanned, "The virus scanner could not be run.");
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { /* Defender may still hold it; the folder is private */ }
        }
    }
}
