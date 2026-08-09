using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimManager.Core.Sharing;

/// <summary>Serializes/parses <c>.rwlist</c> manifests and manages the checksum (docs/rwlist-v1.md).</summary>
public static class RwListSerializer
{
    private static readonly JsonSerializerOptions Pretty = Build(indented: true);
    private static readonly JsonSerializerOptions Canonical = Build(indented: false);

    private static JsonSerializerOptions Build(bool indented) => new()
    {
        WriteIndented = indented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a list, stamping a fresh checksum computed over the checksum-less form.</summary>
    public static string Serialize(RwList list)
    {
        var stamped = list with { Checksum = "sha256:" + ComputeChecksum(list) };
        return JsonSerializer.Serialize(stamped, Pretty);
    }

    public static RwList Parse(string json) =>
        JsonSerializer.Deserialize<RwList>(json, Pretty)
        ?? throw new FormatException("Not a valid .rwlist document.");

    /// <summary>True if the manifest has no checksum, or its checksum matches the content.</summary>
    public static bool VerifyChecksum(RwList list)
    {
        if (string.IsNullOrEmpty(list.Checksum)) return true;
        return string.Equals(list.Checksum, "sha256:" + ComputeChecksum(list), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SHA-256 (hex) over the canonical JSON with the checksum field removed.</summary>
    private static string ComputeChecksum(RwList list)
    {
        var canonical = JsonSerializer.Serialize(list with { Checksum = null }, Canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
