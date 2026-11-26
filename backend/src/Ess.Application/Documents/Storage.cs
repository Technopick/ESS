namespace Ess.Application.Documents;

/// <summary>Stores file bytes encrypted at rest. Keys are opaque; callers never build paths.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(byte[] content, CancellationToken ct);
    Task<byte[]> ReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public enum ScanVerdict { Clean = 0, Infected = 1, NotScanned = 2 }

public sealed record ScanResult(ScanVerdict Verdict, string? Detail);

public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(byte[] content, CancellationToken ct);
}

/// <summary>Which uploads are accepted. The type is decided from the file's first bytes, never from its name or the browser's claim.</summary>
public static class FileRules
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
    };

    /// <summary>Returns the content type for allowed files, or null.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head, string fileName)
    {
        if (head.StartsWith("%PDF-"u8)) return "application/pdf";
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (head.StartsWith("PK\u0003\u0004"u8))
        {
            // Office Open XML is a zip; the extension decides which (macro-enabled formats are not accepted).
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                _ => null
            };
        }
        return null;
    }

    /// <summary>Keeps a safe display name: no path, no control characters, at most 120 characters.</summary>
    public static string SafeName(string fileName, string contentType)
    {
        var name = Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/').Split('/').Last());
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim();
        if (name.Length == 0) name = "document";
        if (name.Length > 110) name = name[..110];
        return name + Extensions[contentType];
    }
}
