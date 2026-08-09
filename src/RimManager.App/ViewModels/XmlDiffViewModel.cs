using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RimManager.Core.Analysis;

namespace RimManager.App.ViewModels;

/// <summary>
/// Backs the two-up XML diff viewer (<c>3c</c>): non-modal, read-only, opened from a
/// selected conflict.
/// <para>
/// Read-only on purpose. The viewer answers "what does this mod change about the
/// contested element"; editing a mod's XML from a mod manager would put the user's
/// change one Workshop update away from being silently overwritten.
/// </para>
/// </summary>
public sealed partial class XmlDiffViewModel : ObservableObject
{
    private readonly string? _leftXml;
    private readonly string? _rightXml;

    public XmlDiffViewModel(
        string target,
        string subtitle,
        string leftHeader,
        string rightHeader,
        string? leftXml,
        string? rightXml)
    {
        Target = target;
        Subtitle = subtitle;
        LeftHeader = leftHeader;
        RightHeader = rightHeader;
        _leftXml = leftXml;
        _rightXml = rightXml;
        Refresh();
    }

    /// <summary>Builds the viewer for a selected conflict, or null when it has no two
    /// providers carrying XML to compare.</summary>
    public static XmlDiffViewModel? For(
        ModConflict conflict, Func<RimManager.Core.Domain.ModId, int?> positionOf,
        IReadOnlyDictionary<RimManager.Core.Domain.ModId, string> names)
    {
        if (conflict.DiffPair() is not { } pair) return null;

        return new XmlDiffViewModel(
            conflict.Key,
            $"{ConflictsPresenter.KindLabel(conflict.Kind)} · {conflict.ProvidersOrEmpty.Length} providers",
            Header(pair.Overwritten.ModId, "overwritten", positionOf, names),
            Header(pair.Wins.ModId, "wins", positionOf, names),
            pair.Overwritten.Xml,
            pair.Wins.Xml);
    }

    private static string Header(
        RimManager.Core.Domain.ModId id, string state,
        Func<RimManager.Core.Domain.ModId, int?> positionOf,
        IReadOnlyDictionary<RimManager.Core.Domain.ModId, string> names)
    {
        var name = names.TryGetValue(id, out var n) ? n : id.Display;
        var position = positionOf(id);
        return position is { } p ? $"{name}   #{p} · {state}" : $"{name}   · {state}";
    }

    public string Target { get; }
    public string Subtitle { get; }
    public string LeftHeader { get; }
    public string RightHeader { get; }

    public ObservableCollection<DiffRow> Left { get; } = [];
    public ObservableCollection<DiffRow> Right { get; } = [];

    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>
    /// Collapses long unchanged runs. On by default because the contested element is
    /// usually mostly identical and the eye should land on the difference.
    /// </summary>
    [ObservableProperty] private bool _changedOnly = true;

    partial void OnChangedOnlyChanged(bool value) => Refresh();

    /// <summary>The whole diff as text, for pasting into an issue.</summary>
    public string AsText()
    {
        var lines = new List<string> { $"{Target} — {Subtitle}", LeftHeader, RightHeader, string.Empty };
        for (var i = 0; i < Math.Max(Left.Count, Right.Count); i++)
        {
            var l = i < Left.Count ? Left[i] : null;
            var r = i < Right.Count ? Right[i] : null;
            if (l is { Kind: DiffKind.Removed }) lines.Add($"- {l.Text}");
            if (r is { Kind: DiffKind.Added }) lines.Add($"+ {r.Text}");
            if (l is { Kind: DiffKind.Context } && l.Text.Length > 0) lines.Add($"  {l.Text}");
            if (l is { Kind: DiffKind.Collapsed }) lines.Add($"  … {l.Text}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void Refresh()
    {
        var result = XmlDiff.Compare(_leftXml, _rightXml, ChangedOnly);

        Left.Clear();
        Right.Clear();
        foreach (var row in result.Left) Left.Add(row);
        foreach (var row in result.Right) Right.Add(row);
        Summary = result.Summary;
    }
}
