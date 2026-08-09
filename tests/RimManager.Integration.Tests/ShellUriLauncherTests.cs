using FluentAssertions;
using RimManager.Storage;
using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Guards the launcher's scheme allowlist. Only the rejection path is exercised — it
/// must never actually launch anything — so these run safely in CI. The success path
/// (opening a real steam:// link) is user-verified, since it pops the Steam client.
/// </summary>
public sealed class ShellUriLauncherTests
{
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("mailto:x@y.z")]
    [InlineData("ftp://host/x")]
    public void Refuses_non_allowlisted_schemes(string uri)
    {
        var launcher = new ShellUriLauncher();
        var act = () => launcher.Launch(uri);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_empty_input(string uri)
    {
        var act = () => new ShellUriLauncher().Launch(uri);
        act.Should().Throw<ArgumentException>();
    }
}
