using System.IO;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The rule editor's target picker must stay SEARCHABLE. It offers every installed
/// mod — about 560 on a real library — and it shipped in beta.2 as a plain ComboBox
/// with no filter, which made reaching any mod past the alphabet's middle genuinely
/// hard: type-ahead did not jump and the popup dismissed on long scrolls. The mod
/// list beside it has a filter box; the picker gets the same treatment.
/// </summary>
public sealed class RuleEditorMarkupTests
{
    private static string Markup => File.ReadAllText(Path.Combine(
        RepoPaths.AppProject, "Views", "Dialogs", "RuleEditorWindow.axaml"));

    [Fact]
    public void The_rule_target_picker_is_searchable()
    {
        Markup.Should().Contain("AutoCompleteBox",
            "a 560-item list without a filter is unusable past the alphabet's middle");
        Markup.Should().Contain("FilterMode=\"Contains\"",
            "prefix-only matching fails everyone who thinks of a mod by its middle word");
    }

    [Fact]
    public void No_plain_ComboBox_offers_the_mod_list()
    {
        Markup.Should().NotContain("<ComboBox",
            "the unsearchable picker must not quietly return");
    }
}
