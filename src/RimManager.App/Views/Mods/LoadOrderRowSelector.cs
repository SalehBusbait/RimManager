using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using RimManager.App.ViewModels;

namespace RimManager.App.Views.Mods;

/// <summary>
/// Picks the mod-row or separator template for the active pane, which holds a
/// single flat collection of both.
/// <para>
/// AVALONIA_GUIDE.md §4: "Use one ItemsSource of a common base type and a
/// DataTemplateSelector that returns SeparatorTemplate or ModRowTemplate. Do NOT
/// use nested TreeView groups — collapse is a filter operation over a flat list,
/// which keeps drag indices simple and keeps virtualization working."
/// </para>
/// </summary>
public sealed class LoadOrderRowSelector : IDataTemplate
{
    /// <summary>Template for an ordinary mod row.</summary>
    [Content]
    public IDataTemplate? ModTemplate { get; set; }

    /// <summary>Template for a separator row (always 22px, regardless of density).</summary>
    public IDataTemplate? SeparatorTemplate { get; set; }

    public bool Match(object? data) => data is RowViewModel;

    public Control? Build(object? param) => param switch
    {
        SeparatorRowViewModel => SeparatorTemplate?.Build(param),
        ModRowViewModel => ModTemplate?.Build(param),
        _ => null,
    };
}
