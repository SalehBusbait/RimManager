using FluentAssertions;
using RimManager.Core.Domain;
using Xunit;

namespace RimManager.Core.Tests.Domain;

/// <summary>
/// Which Config files travel with a modlist. The names here are taken from a real
/// 544-mod install's Config folder — 418 files — not invented.
/// </summary>
public sealed class ModSettingsFilesTests
{
    /// <summary>
    /// The reason this is a deny-list. An allow-list of Mod_*.xml covers 397 of 418 files
    /// and silently drops every mod that writes its own filename.
    /// </summary>
    [Theory]
    [InlineData("Mod_1055485938_Megafauna_Mod.xml")]
    [InlineData("Mod_1157085076_LocksMod.xml")]
    [InlineData("CameraPlusColors.txt")]
    [InlineData("CameraPlusDefaultRules.xml")]
    [InlineData("VanillaBackgroundsExpanded_LoadingSettings.txt")]
    [InlineData("ModSettingsFrameworkMod_Settings.xml")]
    [InlineData("MapPreviewCompatCache.xml")]
    [InlineData("TrueTerrainColorsCache.xml")]
    public void Mod_authored_files_travel_with_the_list(string name) =>
        ModSettingsFiles.ShouldCapture(name).Should().BeTrue();

    /// <summary>
    /// Restoring these on a switch would change the player's screen resolution, volume
    /// and keybindings, or fight the apply pipeline for the load order.
    /// </summary>
    [Theory]
    [InlineData("ModsConfig.xml")]
    [InlineData("Prefs.xml")]
    [InlineData("KeyPrefs.xml")]
    [InlineData("LastPlayedVersion.txt")]
    [InlineData("Knowledge.xml")]
    public void Game_owned_files_never_do(string name) =>
        ModSettingsFiles.ShouldCapture(name).Should().BeFalse();

    [Fact]
    public void Backups_are_not_settings()
    {
        ModSettingsFiles.ShouldCapture("ModsConfig.xml.20260725T133751Z.bak").Should().BeFalse();
        ModSettingsFiles.ShouldCapture("Mod_123_Thing.xml.20260725T133751Z.bak").Should().BeFalse();
    }

    /// <summary>
    /// The stem guard: a future backup naming scheme that does not end in .bak must still
    /// not smuggle the load order into a settings snapshot.
    /// </summary>
    [Fact]
    public void Anything_prefixed_with_a_game_owned_name_is_refused()
    {
        ModSettingsFiles.ShouldCapture("ModsConfig.xml.old").Should().BeFalse();
        ModSettingsFiles.ShouldCapture("Prefs.xml.1").Should().BeFalse();
    }

    /// <summary>Three platforms, one of which cares about case.</summary>
    [Fact]
    public void Matching_is_case_insensitive()
    {
        ModSettingsFiles.ShouldCapture("modsconfig.xml").Should().BeFalse();
        ModSettingsFiles.ShouldCapture("PREFS.XML").Should().BeFalse();
    }

    [Fact]
    public void A_name_that_merely_contains_a_game_owned_name_is_still_captured() =>
        ModSettingsFiles.ShouldCapture("MyMod_ModsConfig.xml").Should().BeTrue(
            "the guard is a prefix, not a substring — a mod may legitimately name its file that");

    [Fact]
    public void Empty_names_are_refused()
    {
        ModSettingsFiles.ShouldCapture("").Should().BeFalse();
        ModSettingsFiles.ShouldCapture("   ").Should().BeFalse();
    }
}
