using Xunit;

namespace RimManager.Integration.Tests;

/// <summary>
/// Seven tests in this project call third-party services for real — Steam's Workshop
/// API, GitHub's releases API, and the two community databases. They are worth having:
/// a parser that satisfies its unit tests and then fails against the live response
/// shape is the failure mode they exist to catch.
/// <para>
/// They must not decide whether CI is green. Each already skips when the endpoint is
/// unreachable, but that only covers a failure to connect — a service that answers with
/// something unexpected (rate limiting, a changed result code, a redirect) fails the
/// assertion instead. On a repository whose CI runs on every push, and whose release
/// workflow will not publish unless the suite passes, that hands a third party a veto
/// over the build. It is also simply not what CI is measuring.
/// </para>
/// <para>
/// So they run locally, where the author can see and judge the result, and skip on a
/// runner. Detected through <c>CI</c>, which GitHub Actions sets to <c>true</c> and
/// which most other CI systems set as well.
/// </para>
/// </summary>
internal static class LiveEndpoints
{
    /// <summary>Call as the first statement of any test that reaches the network.</summary>
    public static void SkipInCi() =>
        Skip.If(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")),
            "Live endpoint test: runs locally, skipped on CI so a third-party service "
            + "cannot fail the build.");
}
