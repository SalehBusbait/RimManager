using System.Text;
using RimManager.Core.Domain;

namespace RimManager.Core.Writing;

/// <summary>
/// Serializes a <see cref="ModsConfig"/> to the exact byte format RimWorld writes,
/// so applied changes produce clean diffs (spec §3: "must match RimWorld's exact
/// formatting/indentation").
/// </summary>
/// <remarks>
/// Verified against a real file: no BOM, declaration <c>&lt;?xml version="1.0" ?&gt;</c>
/// (a space, no encoding attribute — which <see cref="System.Xml"/> can't emit),
/// CRLF line endings, 2-space then 4-space indentation, and a trailing CRLF. That
/// is why this is hand-rolled rather than using <c>XmlWriter</c>.
/// </remarks>
public static class ModsConfigWriter
{
    private const string Nl = "\r\n";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Serialize(ModsConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" ?>").Append(Nl);
        sb.Append("<ModsConfigData>").Append(Nl);
        sb.Append("  <version>").Append(Escape(config.Version)).Append("</version>").Append(Nl);

        AppendList(sb, "activeMods", config.ActiveMods);
        AppendList(sb, "knownExpansions", config.KnownExpansions);

        sb.Append("</ModsConfigData>").Append(Nl);
        return sb.ToString();
    }

    public static byte[] SerializeToBytes(ModsConfig config) => Utf8NoBom.GetBytes(Serialize(config));

    private static void AppendList(StringBuilder sb, string element, IReadOnlyList<ModId> ids)
    {
        if (ids.Count == 0)
        {
            sb.Append("  <").Append(element).Append(" />").Append(Nl);
            return;
        }

        sb.Append("  <").Append(element).Append('>').Append(Nl);
        foreach (var id in ids)
        {
            sb.Append("    <li>").Append(Escape(id.Value)).Append("</li>").Append(Nl);
        }

        sb.Append("  </").Append(element).Append('>').Append(Nl);
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
