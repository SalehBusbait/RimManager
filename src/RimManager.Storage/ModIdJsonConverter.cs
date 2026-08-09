using System.Text.Json;
using System.Text.Json.Serialization;
using RimManager.Core.Domain;

namespace RimManager.Storage;

/// <summary>
/// Serializes <see cref="ModId"/> as its display string. Round-trips through
/// <see cref="ModId.From"/>, so the canonical lowercase form is recomputed on read.
/// </summary>
public sealed class ModIdJsonConverter : JsonConverter<ModId>
{
    public override ModId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return string.IsNullOrWhiteSpace(s) ? default : ModId.From(s);
    }

    public override void Write(Utf8JsonWriter writer, ModId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Display);
}
