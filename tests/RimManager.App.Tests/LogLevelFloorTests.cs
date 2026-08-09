using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using RimManager.App.ViewModels;
using RimManager.Core.Abstractions;
using RimManager.Core.Diagnostics;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The log-level floor, after the unchosen-Error incident: the owner's settings.json
/// carried <c>logLevelIndex: 0</c> with no click behind it, and the floor it set
/// silenced the very announcement that would have named the change — four days of a
/// quiet log misread as a dead app. Two guards came out of it: the choice fires only
/// from a command (selection state is display, never a trigger), and the announcement
/// goes out at a level the new floor admits.
/// </summary>
public sealed class LogLevelFloorTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private static ActivityLog NewLog() => new(new FixedClock());

    // --- LogLevels.ApplyFloor -------------------------------------------------

    [Fact]
    public void Setting_the_floor_to_Error_still_announces_itself()
    {
        var log = NewLog();

        LogLevels.ApplyFloor(log, 0);

        log.MinimumLevel.Should().Be(LogLevel.Error);
        log.Snapshot().Should().ContainSingle(e => e.Message == "Log level set to Error",
            "a floor change that silences its own announcement is how an unchosen "
            + "Error floor went unexplained for four days");
    }

    [Fact]
    public void The_announcement_survives_every_transition()
    {
        for (var from = 0; from < LogLevels.Choices.Length; from++)
        {
            for (var to = 0; to < LogLevels.Choices.Length; to++)
            {
                if (from == to) continue;

                var log = NewLog();
                LogLevels.ApplyFloor(log, from);
                LogLevels.ApplyFloor(log, to);

                log.Snapshot().Last().Message.Should().Be(
                    $"Log level set to {LogLevels.Label(to)}",
                    $"the {LogLevels.Label(from)}→{LogLevels.Label(to)} transition must "
                    + "leave a witness — Warn→Error is the case a fixed Info level loses");
            }
        }
    }

    [Fact]
    public void A_loose_floor_announces_at_Info_not_louder()
    {
        var log = NewLog();

        LogLevels.ApplyFloor(log, LogLevels.Choices.Length - 1); // Trace

        log.Snapshot().Last().Level.Should().Be(LogLevel.Info,
            "the announcement is not an error and only borrows loudness when the "
            + "new floor would otherwise drop it");
    }

    // --- LogLevelChoiceViewModel ----------------------------------------------

    [Fact]
    public void Selection_state_alone_fires_nothing()
    {
        int? fired = null;
        var choice = new LogLevelChoiceViewModel(0, "Error", i => fired = i);

        choice.IsSelected = true;

        fired.Should().BeNull(
            "IsSelected is display state — when it was a trigger, everything a radio "
            + "group checks uninvited (arrow-key focus moves, binding writes) wrote "
            + "the preference, which is the shipped unchosen-0 bug");
    }

    [Fact]
    public void Choosing_fires_the_callback_with_its_index()
    {
        int? fired = null;
        var choice = new LogLevelChoiceViewModel(3, "Debug", i => fired = i);

        choice.ChooseCommand.Execute(null);

        fired.Should().Be(3);
    }

    // --- markup ---------------------------------------------------------------

    /// <summary>
    /// The segments must stay Buttons. A radio group checks segments as arrow-key
    /// focus moves through them, and this is the one segmented control whose misfire
    /// is invisible — so the radio form coming back would quietly reopen the bug.
    /// </summary>
    [Fact]
    public void The_log_level_segments_are_buttons_not_a_radio_group()
    {
        var markup = File.ReadAllText(Path.Combine(RepoPaths.AppProject, "SettingsWindow.axaml"));

        markup.Should().NotContain("GroupName=\"loglevel\"",
            "the radio form is the bug's habitat: TwoWay IsChecked plus a group manager");
        markup.Should().Contain("Command=\"{Binding ChooseCommand}\"",
            "the choice fires from a click and nothing else");
        markup.Should().Contain("Classes.on=\"{Binding IsSelected}\"",
            "the selected segment lights from state the resync writes, N4g's chip shape");
    }
}
