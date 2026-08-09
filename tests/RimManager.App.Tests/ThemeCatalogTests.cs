using System;
using System.Linq;
using Avalonia.Styling;
using FluentAssertions;
using RimManager.App.Themes;
using RimManager.App.ViewModels;
using Xunit;

namespace RimManager.App.Tests;

/// <summary>
/// The theme roster (design handoff v2): one catalog, ten themes, and a legacy
/// mapping that keeps a chosen theme chosen across the v1 → v2 settings format.
/// </summary>
public sealed class ThemeCatalogTests
{
    [Fact]
    public void Every_theme_except_follow_system_has_a_catalog_entry()
    {
        var catalogued = ThemeCatalog.All.Select(t => t.Theme).ToHashSet();

        foreach (var theme in Enum.GetValues<AppTheme>().Where(t => t != AppTheme.FollowSystem))
        {
            catalogued.Should().Contain(theme,
                "an enum member without a catalog entry is a theme the Settings list, "
                + "ApplyTheme and the parity tests cannot see");
        }

        ThemeCatalog.All.Should().HaveCount(10).And.OnlyHaveUniqueItems(t => t.Theme);
    }

    [Fact]
    public void Legacy_names_map_to_the_drop_pods_pair()
    {
        ThemeCatalog.Parse("Light").Should().Be(AppTheme.DropPodsLight,
            "an install that chose Light must keep a light theme, not reset");
        ThemeCatalog.Parse("Dark").Should().Be(AppTheme.DropPodsDark);
    }

    [Fact]
    public void Unknown_names_fall_back_to_follow_system()
    {
        ThemeCatalog.Parse(null).Should().Be(AppTheme.FollowSystem);
        ThemeCatalog.Parse("").Should().Be(AppTheme.FollowSystem);
        ThemeCatalog.Parse("NotATheme").Should().Be(AppTheme.FollowSystem);
    }

    [Fact]
    public void Current_names_parse_to_themselves()
    {
        foreach (var info in ThemeCatalog.All)
            ThemeCatalog.Parse(info.Theme.ToString()).Should().Be(info.Theme);
    }

    [Fact]
    public void Follow_system_defers_to_the_platform()
    {
        ThemeCatalog.VariantOf(AppTheme.FollowSystem).Should().Be(ThemeVariant.Default);
    }

    [Fact]
    public void The_drop_pods_pair_rides_the_built_in_variants()
    {
        // This is what makes follow-system work with no code: the OS picks Light or
        // Dark, and those keys ARE the pair's dictionaries.
        ThemeCatalog.VariantOf(AppTheme.DropPodsDark).Should().Be(ThemeVariant.Dark);
        ThemeCatalog.VariantOf(AppTheme.DropPodsLight).Should().Be(ThemeVariant.Light);
    }

    [Fact]
    public void Flavoured_themes_use_custom_variants_inheriting_a_base()
    {
        foreach (var info in ThemeCatalog.All.Where(
            t => t.Theme is not (AppTheme.DropPodsDark or AppTheme.DropPodsLight)))
        {
            info.Variant.Should().NotBe(ThemeVariant.Dark).And.NotBe(ThemeVariant.Light,
                $"{info.DisplayName} is a custom variant, or its dictionary key would collide with the pair's");
            ThemeCatalog.VariantOf(info.Theme).Should().Be(info.Variant);
        }
    }
}
