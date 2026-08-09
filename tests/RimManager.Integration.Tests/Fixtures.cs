namespace RimManager.Integration.Tests;

/// <summary>Locates the committed <c>/fixtures</c> tree by walking up from the test binary.</summary>
internal static class Fixtures
{
    public static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    public static string? ModsDir()
    {
        var root = Root();
        var mods = root is null ? null : Path.Combine(root, "mods");
        return mods is not null && Directory.Exists(mods) ? mods : null;
    }

    public static string? ModsConfig()
    {
        var root = Root();
        var path = root is null ? null : Path.Combine(root, "config", "ModsConfig.xml");
        return path is not null && File.Exists(path) ? path : null;
    }
}
