namespace RimManager.Core.Domain;

/// <summary>Severity of a <see cref="ModWarning"/>.</summary>
public enum WarningSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// A non-fatal problem found while scanning a mod. Malformed/missing
/// <c>About.xml</c> is common (domain primer §3): we never crash, we surface it.
/// </summary>
/// <param name="Code">Stable machine code, e.g. <c>about.missing-packageId</c>.</param>
/// <param name="Message">Human-readable detail.</param>
/// <param name="Subject">
/// The mod the warning is about, when the warning is raised somewhere that knows it.
/// A warning found while parsing one mod's About.xml is already attached to that mod;
/// scan-level warnings such as a duplicate packageId are not, and the Warnings dock
/// needs a mod for its "mod" column. Optional so every existing call site still
/// compiles, and defaulted to null rather than to a sentinel id.
/// </param>
public sealed record ModWarning(
    WarningSeverity Severity, string Code, string Message, ModId? Subject = null);
