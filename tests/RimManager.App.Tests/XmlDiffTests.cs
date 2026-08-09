using FluentAssertions;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The two-up diff (<c>3c</c>). The failure mode here is a diff that looks entirely
/// plausible and pairs the wrong lines — no screenshot catches that, which is why the
/// alignment is a pure function with tests rather than something the view works out.
/// </summary>
public sealed class XmlDiffTests
{
    private const string Before = """
        <ThingDef ParentName="BaseGun">
          <defName>Gun_AssaultRifle</defName>
          <RangedWeapon_Cooldown>1.6</RangedWeapon_Cooldown>
          <AccuracyMedium>0.72</AccuracyMedium>
          <Mass>3.5</Mass>
        </ThingDef>
        """;

    private const string After = """
        <ThingDef ParentName="BaseGun">
          <defName>Gun_AssaultRifle</defName>
          <RangedWeapon_Cooldown>0.9</RangedWeapon_Cooldown>
          <AccuracyMedium>0.72</AccuracyMedium>
          <Mass>3.9</Mass>
          <Bulk>5.4</Bulk>
        </ThingDef>
        """;

    [Fact]
    public void Identical_input_produces_no_changes()
    {
        var diff = XmlDiff.Compare(Before, Before);

        diff.Added.Should().Be(0);
        diff.Removed.Should().Be(0);
        diff.Left.Should().OnlyContain(r => r.Kind == DiffKind.Context);
    }

    /// <summary>
    /// The panes are side by side ONLY because the rows line up. If the two sides ever
    /// have different lengths the reader is comparing unrelated lines.
    /// </summary>
    [Fact]
    public void Both_sides_always_have_the_same_number_of_rows()
    {
        foreach (var changedOnly in new[] { false, true })
        {
            var diff = XmlDiff.Compare(Before, After, changedOnly);
            diff.Left.Length.Should().Be(diff.Right.Length, $"changedOnly: {changedOnly}");
        }
    }

    [Fact]
    public void A_changed_line_reads_as_one_removal_beside_one_addition()
    {
        var diff = XmlDiff.Compare(Before, After);

        // Two values edited (cooldown, mass) and one element added (Bulk).
        diff.Removed.Should().Be(2);
        diff.Added.Should().Be(3);

        var removedTexts = diff.Left.Where(r => r.IsRemoved).Select(r => r.Text.Trim());
        removedTexts.Should().Contain("<RangedWeapon_Cooldown>1.6</RangedWeapon_Cooldown>");

        var addedTexts = diff.Right.Where(r => r.IsAdded).Select(r => r.Text.Trim());
        addedTexts.Should().Contain("<RangedWeapon_Cooldown>0.9</RangedWeapon_Cooldown>");
        addedTexts.Should().Contain("<Bulk>5.4</Bulk>");
    }

    /// <summary>Every edited line pairs with its counterpart on the same row.</summary>
    [Fact]
    public void An_edit_puts_the_old_and_new_line_on_the_same_row()
    {
        var diff = XmlDiff.Compare(Before, After);

        var row = Enumerable.Range(0, diff.Left.Length)
            .Single(i => diff.Left[i].Text.Contains("RangedWeapon_Cooldown"));

        diff.Left[row].IsRemoved.Should().BeTrue();
        diff.Right[row].IsAdded.Should().BeTrue();
        diff.Right[row].Text.Should().Contain("0.9");
    }

    [Fact]
    public void Unchanged_lines_keep_their_own_line_numbers_on_each_side()
    {
        var diff = XmlDiff.Compare(Before, After);

        diff.Left[0].Number.Should().Be("1");
        diff.Right[0].Number.Should().Be("1");
        diff.Left[0].Text.Should().Contain("ThingDef");
    }

    [Fact]
    public void A_long_identical_run_collapses_to_one_marker_on_both_sides()
    {
        var padding = string.Join('\n', Enumerable.Range(0, 20).Select(i => $"  <Line{i}>x</Line{i}>"));
        var before = $"<Def>\n{padding}\n  <A>1</A>\n</Def>";
        var after = $"<Def>\n{padding}\n  <A>2</A>\n</Def>";

        var diff = XmlDiff.Compare(before, after, changedOnly: true);

        diff.Collapsed.Should().BeGreaterThan(0);
        diff.Left.Count(r => r.IsCollapsed).Should().Be(diff.Right.Count(r => r.IsCollapsed));
        diff.Left.Length.Should().Be(diff.Right.Length);
        diff.Summary.Should().Contain("unchanged collapsed");
    }

    /// <summary>
    /// The compact panel renders the unified column. A change that only DELETES a line
    /// used to render as no change at all while the footer counted it — the panel was
    /// showing the winner's side alone.
    /// </summary>
    [Fact]
    public void A_pure_deletion_still_appears_in_the_unified_column()
    {
        var before = "<Def>\n  <A>1</A>\n  <B>2</B>\n</Def>";
        var after = "<Def>\n  <A>1</A>\n</Def>";

        var diff = XmlDiff.Compare(before, after);

        diff.Removed.Should().Be(1);
        diff.Added.Should().Be(0);
        diff.Unified.Should().Contain(r => r.IsRemoved && r.Text.Contains("<B>2</B>"));
    }

    [Fact]
    public void The_unified_column_puts_a_removal_immediately_before_its_replacement()
    {
        var diff = XmlDiff.Compare(Before, After);
        var rows = diff.Unified;

        var removed = rows.ToList().FindIndex(r => r.IsRemoved && r.Text.Contains("Cooldown"));
        removed.Should().BeGreaterThanOrEqualTo(0);
        rows[removed + 1].IsAdded.Should().BeTrue();
        rows[removed + 1].Text.Should().Contain("Cooldown");
    }

    [Fact]
    public void Nothing_to_compare_is_an_empty_result_not_a_crash()
    {
        XmlDiff.Compare(null, null).Should().Be(XmlDiffResult.Empty);
        XmlDiff.Compare("   ", null).Left.Should().BeEmpty();
    }

    [Fact]
    public void One_sided_content_reads_entirely_as_additions()
    {
        var diff = XmlDiff.Compare(null, After);

        diff.Removed.Should().Be(0);
        diff.Added.Should().Be(7);
        diff.Left.Length.Should().Be(diff.Right.Length);
    }
}
