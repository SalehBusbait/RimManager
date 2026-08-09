using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// Catches the costliest silent failure this project has: a computed property that
/// reads observable state with nothing raising <c>PropertyChanged</c> for it.
/// <para>
/// A control bound to one renders whatever the expression evaluated to when the view
/// was built and never changes again. It compiles, it passes every test, and the only
/// way to see it is to look at the window and notice a number that should have moved.
/// <c>HasCommunityRules</c> survived four phases that way — zone 2's tick could not be
/// turned on by a rules sync. <c>SnapshotDangerDetail</c> was the same shape with worse
/// consequences: it is the line stating what "Delete all snapshots" would destroy, and
/// selecting a different instance left the previous one's count beside the button.
/// </para>
/// <para>
/// Source analysis rather than reflection, because the failure is the ABSENCE of a
/// notification — see the memory note "assert the notification, not the value". A test
/// that reads the property gets the right answer and proves nothing.
/// </para>
/// </summary>
public sealed class NotifyCoverageTests
{
    [Fact]
    public void Every_bound_computed_property_is_announced_by_the_state_it_reads()
    {
        var markup = string.Concat(
            Directory.EnumerateFiles(RepoPaths.AppProject, "*.axaml", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(
                     RepoPaths.AppProject, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var src = File.ReadAllText(path);

            // [ObservableProperty] private T _name;  ->  the generated property Name
            var observable = Regex
                .Matches(src, @"\[ObservableProperty\][^;]*?private\s+[\w<>?\[\], .]+?\s+_(\w+)\s*[;=]",
                    RegexOptions.Singleline)
                .Select(m => char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value[1..])
                .ToHashSet(StringComparer.Ordinal);

            if (observable.Count == 0) continue;

            var announced = Announced(src);

            foreach (Match m in Regex.Matches(
                         src, @"public\s+(?:static\s+)?[\w<>?\[\], .]+?\s+(\w+)\s*=>\s*([^;]+);"))
            {
                var name = m.Groups[1].Value;
                if (observable.Contains(name) || announced.Contains(name)) continue;

                var reads = Regex.Matches(m.Groups[2].Value, @"\b(\w+)\b")
                    .Select(t => t.Groups[1].Value)
                    .Where(observable.Contains)
                    .Distinct()
                    .ToList();

                // Only a property something actually BINDS can strand a control. An
                // unannounced helper read from code is re-evaluated on every call.
                var bound = @"Binding\s+" + Regex.Escape(name) + @"\s*[}},.]".Replace("}}", "}");
                if (reads.Count == 0 || !Regex.IsMatch(markup, bound)) continue;

                offenders.Add($"{Path.GetFileName(path)}: {name} reads {string.Join(", ", reads)}");
            }
        }

        offenders.Should().BeEmpty(
            "a bound computed property whose sources never announce it leaves its " +
            "control frozen at whatever it showed when the view was built; add it to " +
            "the source's [NotifyPropertyChangedFor]");
    }

    /// <summary>
    /// Names raised anywhere in the file — <c>[NotifyPropertyChangedFor(...)]</c> or an
    /// explicit <c>OnPropertyChanged(nameof(X))</c>.
    /// <para>
    /// The attribute's arguments contain nested parentheses, so a lazy <c>[^)]*</c>
    /// stops at the inner one and captures nothing — which made a first draft of this
    /// analysis report 123 offenders instead of 4. Scan for the balanced close instead.
    /// </para>
    /// </summary>
    private static HashSet<string> Announced(string src)
    {
        var announced = Regex.Matches(src, @"OnPropertyChanged\(nameof\((\w+)\)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (Match open in Regex.Matches(src, @"NotifyPropertyChangedFor\("))
        {
            var i = open.Index + open.Length;
            var depth = 1;
            var start = i;

            while (i < src.Length && depth > 0)
            {
                if (src[i] == '(') depth++;
                else if (src[i] == ')') depth--;
                i++;
            }

            foreach (Match n in Regex.Matches(src[start..i], @"nameof\((\w+)\)"))
                announced.Add(n.Groups[1].Value);
        }

        return announced;
    }
}
