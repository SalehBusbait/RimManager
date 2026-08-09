using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The sorter and the validator must see the SAME rules.
/// <para>
/// They did not. <c>MainWindowViewModel</c> built its rule graph with
/// <c>_communityRules</c> at three call sites and then validated with the three-argument
/// overload, whose optional <c>community</c> parameter defaults to null — so Sort
/// honoured 629 community rules while Warnings read About.xml alone. The dock could
/// neither report a violated community rule nor explain a move Sort had just made
/// because of one, and the same list validated differently in the GUI than in the CLI.
/// </para>
/// <para>
/// Pinned as a source check because nothing else can catch it: the overload exists, so
/// dropping the argument compiles cleanly, passes every test and launches fine. It is
/// the "wired and does nothing" shape that has cost this project more than any other,
/// and an optional parameter is its most invisible form — there is not even a missing
/// call to notice.
/// </para>
/// </summary>
public sealed class RuleSourceParityTests
{
    private static string Hub => RepoPaths.HubSource();

    [Fact]
    public void Every_validate_call_is_given_the_community_rules()
    {
        var calls = Regex.Matches(Hub, @"\.Validate\((?:[^()]|\([^()]*\))*\)", RegexOptions.Singleline);

        calls.Should().NotBeEmpty("the hub validates, so the check must be looking at something");

        foreach (Match call in calls)
        {
            call.Value.Should().Contain("_communityRules",
                "Validate's community parameter is OPTIONAL, so omitting it compiles and "
                + "silently validates against About.xml alone while Sort uses the database");
        }
    }

    /// <summary>
    /// N7's two databases are the same trap in the same place: both are OPTIONAL
    /// parameters on Validate, so a call that omits them compiles cleanly and silently
    /// validates without the suppression list and without the replacement check — the
    /// synced caches sit on disk feeding nothing.
    /// </summary>
    [Fact]
    public void Every_validate_call_is_given_the_mod_databases()
    {
        var calls = Regex.Matches(Hub, @"\.Validate\((?:[^()]|\([^()]*\))*\)", RegexOptions.Singleline);

        calls.Should().NotBeEmpty();

        foreach (Match call in calls)
        {
            call.Value.Should().Contain("_knownGood",
                "omitting the known-good list silently un-suppresses nothing — it just "
                + "never suppresses, and the feature reads as absent");
            call.Value.Should().Contain("_replacements",
                "omitting the replacements silently removes the Replaceable check");
        }
    }

    [Fact]
    public void Every_rule_graph_the_hub_builds_is_given_the_community_rules()
    {
        var calls = Regex.Matches(Hub, @"RuleGraphBuilder\.Build\([^)]*\)");

        calls.Should().NotBeEmpty();

        foreach (Match call in calls)
        {
            call.Value.Should().Contain("_communityRules",
                "a rule graph built without them sorts by About.xml alone");
        }
    }

    /// <summary>
    /// The rule editor's output is the third rule source, and it fell into exactly the
    /// trap this file exists for: the editor persisted overrides and posted "Rules
    /// changed — sort to apply them" while no call site passed them, so the promise
    /// shipped false. Optional parameter, compiles clean, launches fine.
    /// </summary>
    [Fact]
    public void Every_rule_graph_the_hub_builds_is_given_the_users_overrides()
    {
        var calls = Regex.Matches(Hub, @"RuleGraphBuilder\.Build\([^)]*\)");

        calls.Should().NotBeEmpty();

        foreach (Match call in calls)
        {
            call.Value.Should().Contain("_ruleOverrides",
                "a rule graph built without the editor's overrides makes the editor "
                + "a settings page that changes nothing");
        }
    }

    [Fact]
    public void Every_validate_call_is_given_the_users_overrides()
    {
        var calls = Regex.Matches(Hub, @"\.Validate\((?:[^()]|\([^()]*\))*\)", RegexOptions.Singleline);

        calls.Should().NotBeEmpty();

        foreach (Match call in calls)
        {
            call.Value.Should().Contain("_ruleOverrides",
                "a validator without the overrides keeps warning about rules the user "
                + "disabled and never warns about rules they wrote");
        }
    }
}
