using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Analysis;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

public sealed class ConflictsPresenterTests
{
    private static ModConflict Conflict(ConflictKind kind, string key) => new(
        kind, key, [ModId.From("a.mod"), ModId.From("b.mod")], ModId.From("b.mod"));

    // Order and the grouped table went with the 2c tab (N6c); the summary orders its
    // clauses by the same most-impactful-first rule, pinned below.

    [Fact]
    public void Summarize_groups_and_labels_by_kind()
    {
        var input = new[]
        {
            Conflict(ConflictKind.HarmonyPatch, "m1"),
            Conflict(ConflictKind.HarmonyPatch, "m2"),
            Conflict(ConflictKind.DefOverride, "d1"),
            Conflict(ConflictKind.TextureCollision, "t1"),
        };

        ConflictsPresenter.Summarize(input).Should().Be("2 Harmony · 1 Def override · 1 Texture");
    }

    [Fact]
    public void Summarize_empty_reports_no_conflicts()
    {
        ConflictsPresenter.Summarize(System.Array.Empty<ModConflict>())
            .Should().Be("No conflicts detected.");
    }

}
