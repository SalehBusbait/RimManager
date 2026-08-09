using FluentAssertions;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Analysis;

public sealed class CrashLogRankerTests
{
    private static ModNamespace Ns(string ns, string id) => new(ns, ModId.From(id), id);

    private const string Log = """
        Exception in FinalizeInit: System.NullReferenceException
          at RimHUD.Access.Patch.Foo (…)
          at RimHUD.Interface.Bar (…)
          at VFECore.SomeThing.Baz (…)
          at Verse.Root.Update (…)
        """;

    [Fact]
    public void Ranks_mods_by_namespace_reference_count()
    {
        var report = CrashLogRanker.Rank(Log,
        [
            Ns("RimHUD", "jaxe.rimhud"),
            Ns("VFECore", "vanillaexpanded.vfecore"),
            Ns("NotPresent", "some.other"),
        ]);

        report.Suspects.Should().HaveCount(2, "the mod whose namespace never appears is not a suspect");
        report.Suspects[0].PackageId.Should().Be(ModId.From("jaxe.rimhud"));
        report.Suspects[0].Hits.Should().Be(2, "RimHUD. appears twice");
        report.Suspects[1].PackageId.Should().Be(ModId.From("vanillaexpanded.vfecore"));
    }

    [Fact]
    public void Trailing_dot_avoids_false_substring_matches()
    {
        // "Ancot" appears as a bare word but never as a namespace ("Ancot.").
        var report = CrashLogRanker.Rank("mention of Ancot without a namespace use", [Ns("Ancot", "ancot.lib")]);
        report.Suspects.Should().BeEmpty();
    }
}
