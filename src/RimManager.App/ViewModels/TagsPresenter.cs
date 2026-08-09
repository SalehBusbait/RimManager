using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimManager.Core.Domain;

namespace RimManager.App.ViewModels;

/// <summary>
/// The wording of Settings ▸ Tags &amp; metadata (<c>2g</c>). Pure, so the counts and the
/// condition text can be pinned without a window.
/// </summary>
public static class TagsPresenter
{
    /// <summary>"7 · used on 141 mods", or the honest empty form.</summary>
    public static string Header(int tagCount, int taggedMods)
    {
        if (tagCount == 0) return "none yet";

        // "used on 0 mods" reads as a failure; "not used yet" reads as a next step.
        return taggedMods == 0
            ? $"{tagCount} · not used yet"
            : $"{tagCount} · used on {taggedMods} mod{(taggedMods == 1 ? "" : "s")}";
    }


    /// <summary>The RULES column: how many auto-assign conditions the tag carries.</summary>
    public static string RulesLabel(int conditionCount) =>
        conditionCount == 0 ? "—" : conditionCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// One auto-assign condition in the mono form <c>2g</c> shows —
    /// <c>author contains "Oskar"</c>, <c>size &gt; 100 MB</c>.
    /// <para>
    /// Quoted for the text conditions and bare for the numeric one, because the quotes
    /// are what make a trailing space visible. A condition matching <c>"Oskar "</c> and
    /// silently never firing is the bug this spelling exists to prevent.
    /// </para>
    /// </summary>
    public static string ConditionText(TagCondition condition) => condition.Kind switch
    {
        TagConditionKind.AuthorContains => $"author contains \"{condition.Value}\"",
        TagConditionKind.NameContains => $"name contains \"{condition.Value}\"",
        TagConditionKind.PackageIdContains => $"packageId contains \"{condition.Value}\"",
        TagConditionKind.SourceIs => $"source is \"{condition.Value}\"",
        TagConditionKind.SizeOverMb => $"size > {condition.Value} MB",
        _ => condition.Value,
    };

    /// <summary>The condition-kind dropdown, in the order <c>2g</c> lists them.</summary>
    public static IReadOnlyList<string> ConditionKinds { get; } =
        ["author contains", "name contains", "packageId contains", "source is", "size over (MB)"];

    public static TagConditionKind KindFromIndex(int index) => index switch
    {
        1 => TagConditionKind.NameContains,
        2 => TagConditionKind.PackageIdContains,
        3 => TagConditionKind.SourceIs,
        4 => TagConditionKind.SizeOverMb,
        _ => TagConditionKind.AuthorContains,
    };

    public static int IndexFromKind(TagConditionKind kind) => kind switch
    {
        TagConditionKind.NameContains => 1,
        TagConditionKind.PackageIdContains => 2,
        TagConditionKind.SourceIs => 3,
        TagConditionKind.SizeOverMb => 4,
        _ => 0,
    };

    /// <summary>
    /// A condition is only kept if it can match something. An empty value would match
    /// every mod ever scanned — a rule that tags the entire library the next time the
    /// folder is read, which is the worst possible silent outcome here.
    /// </summary>
    public static bool IsUsable(TagCondition condition) =>
        !string.IsNullOrWhiteSpace(condition.Value)
        && (condition.Kind != TagConditionKind.SizeOverMb
            || double.TryParse(condition.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _));

    /// <summary>
    /// A name for a new tag that does not collide. "New tag", then "New tag 2" — a
    /// duplicate name is not fatal (tags are keyed by id) but it is unusable in a list
    /// where the name is all you can see.
    /// </summary>
    public static string UniqueName(IEnumerable<Tag> existing, string basis = "New tag")
    {
        var taken = existing.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(basis)) return basis;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{basis} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return basis;
    }


    /// <summary>"metadata.json · keyed by packageId · 341 entries · 84 KB".</summary>
    public static string StorageLine(string path, int entries, long bytes) =>
        $"{path} · keyed by packageId · {entries} entr{(entries == 1 ? "y" : "ies")} · "
        + UpdatesPresenter.Size(bytes);
}
