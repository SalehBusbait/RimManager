using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// A separator offers the same five actions from a right-click and from its ⋮ button, and
/// the markup says so twice — a <c>MenuFlyout</c> cannot be shared between two parents, and
/// a data-driven menu sets <c>Header</c> as a local value, which this project has already
/// been bitten by.
/// <para>
/// So the duplication is guarded rather than wished away. Apply-and-launch was live on the
/// menu bar and dead on the toolbar for four phases for exactly this reason: a surface
/// copied by hand, then edited once.
/// </para>
/// </summary>
public sealed class SeparatorMenuTests
{
    private static string Markup => File.ReadAllText(
        Path.Combine(RepoPaths.AppProject, "Views", "Mods", "ModRowTemplates.axaml"));

    /// <summary>The command each menu row invokes, in order, for one menu block.</summary>
    private static string[] CommandsIn(string block) =>
        [.. Regex.Matches(block, @"Command=""\{Binding (\w+)Command\}""")
            .Select(m => m.Groups[1].Value)];

    private static string ContextMenuBlock =>
        Between(Markup, "<Border.ContextMenu>", "</Border.ContextMenu>");

    private static string KebabFlyoutBlock =>
        Between(Markup, "<MenuFlyout>", "</MenuFlyout>");

    private static string Between(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{open}' must exist in the separator template");

        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return text[start..end];
    }

    [Fact]
    public void The_right_click_menu_and_the_kebab_menu_offer_the_same_actions()
    {
        CommandsIn(ContextMenuBlock).Should().Equal(CommandsIn(KebabFlyoutBlock),
            "one of the two copies gets edited alone otherwise, and the row then behaves "
            + "differently depending on how you opened its menu");
    }

    /// <summary>
    /// Every action the menus name routes somewhere. The audit that prompted this found
    /// the opposite problem — commands that ran and then lost the change — but a row that
    /// names an action and invokes nothing is the older failure and worth pinning too.
    /// </summary>
    [Fact]
    public void Every_named_action_routes_to_a_command()
    {
        CommandsIn(ContextMenuBlock).Should().BeEquivalentTo(
            ["BeginRename", "ChooseColor", "ChooseColor", "ChooseColor",
             "ChooseColor", "ChooseColor", "ChooseColor",
             "ToggleCollapse", "Delete"]);
    }

    /// <summary>
    /// Each colour label sends the index that PAINTS that colour.
    /// <para>
    /// Three of the six did not: the rows read Blue Green Violet Amber Red Slate against
    /// parameters 0..5, so picking Violet painted amber, Amber painted red and Red
    /// painted violet — in both copies. The sibling test above compares the two copies
    /// to <em>each other</em>, which is why two identically wrong copies agreed
    /// perfectly; nothing compared a label to the thing it does. This does.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_colour_label_sends_the_index_that_paints_it()
    {
        foreach (var block in new[] { ContextMenuBlock, KebabFlyoutBlock })
        {
            var pairs = Regex.Matches(
                    block,
                    @"<MenuItem Header=""(?<name>\w+)"" Command=""\{Binding ChooseColorCommand\}"">\s*"
                    + @"<MenuItem\.CommandParameter><sys:Int32>(?<index>\d)</sys:Int32>",
                    RegexOptions.Singleline)
                .Select(m => (Name: m.Groups["name"].Value, Index: int.Parse(m.Groups["index"].Value)))
                .ToList();

            pairs.Should().HaveCount(Palette.Count, "every hue is offered");

            foreach (var (name, index) in pairs)
            {
                Palette.Names[index].Should().Be(name,
                    $"the row labelled {name} sends {index}, which paints "
                    + $"{Palette.Names[index]} — a colour menu must not lie about the colour");
            }
        }
    }

    /// <summary>
    /// The palette parameter must be a real <c>int</c>. <c>CommandParameter="0"</c> is a
    /// <b>string</b>, and a command taking an int silently refuses it — that is precisely
    /// how all six tag swatches in Settings were dead, with only the binding log to say so.
    /// </summary>
    [Fact]
    public void The_colour_choices_pass_integers_not_strings()
    {
        Markup.Should().NotMatchRegex(@"ChooseColorCommand\}""\s+CommandParameter=""",
            "a literal CommandParameter is a string; the swatches must pass sys:Int32");

        Regex.Matches(Markup, @"<sys:Int32>(\d)</sys:Int32>")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Should().HaveCount(Palette.Count,
                "every hue must be reachable — a cycle was replaced precisely so the user "
                + "does not have to click through the ones they did not want");
    }

    /// <summary>
    /// The verb has to be the one that will happen. Both menus said "Collapse" whatever the
    /// state, so on a collapsed group the row named the opposite of what it did.
    /// </summary>
    [Fact]
    public void The_collapse_row_names_what_it_will_do()
    {
        var sep = new SeparatorRowViewModel("s", "Frameworks");

        sep.CollapseMenuLabel.Should().Be("Collapse");
        sep.Collapsed = true;
        sep.CollapseMenuLabel.Should().Be("Expand");
    }

    /// <summary>
    /// "(keeps its mods)" left the label (owner's call) — but the reassurance it carried is
    /// a real question, so it moved to the tooltip rather than being deleted. A delete whose
    /// blast radius is unstated is the one people do not click.
    /// </summary>
    [Fact]
    public void Delete_still_says_what_it_does_not_touch()
    {
        Markup.Should().NotContain("Delete separator (keeps",
            "the parenthetical left the label");

        ContextMenuBlock.Should().Contain("The mods under it stay exactly where they are",
            "and survives as the tooltip");
    }
}
