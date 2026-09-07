using CommunityToolkit.Mvvm.ComponentModel;
using MyFinance.App.Services;
using MyFinance.Core.Help;

namespace MyFinance.App.ViewModels;

/// <summary>A task-pane link shown in the left sidebar.</summary>
public sealed partial class TaskLink : ObservableObject
{
    public required string Text { get; init; }

    public required Action Execute { get; init; }

    public bool IsEnabled { get; init; } = true;
}

/// <summary>A group of related task-pane links, e.g. "Common tasks".</summary>
public sealed class TaskGroup
{
    public required string Header { get; init; }

    public required IReadOnlyList<TaskLink> Links { get; init; }
}

/// <summary>
/// Base for anything the shell can display in its content region.
/// </summary>
public abstract partial class PageViewModel : BusyViewModel
{
    /// <summary>Title shown at the top of the content region.</summary>
    public abstract string Title { get; }

    /// <summary>Which top-level tab should light up while this page is showing.</summary>
    public abstract AppSection Section { get; }

    /// <summary>Contextual links for the left task pane. Empty hides the pane.</summary>
    public virtual IReadOnlyList<TaskGroup> TaskGroups => [];

    /// <summary>
    /// The page of the documentation this screen is about, for Help and F1.
    /// </summary>
    /// <remarks>
    /// Defaulted rather than abstract: a new page that has not thought about it opens the
    /// contents, which is a worse answer than the right one and a far better answer than a
    /// Help key that does nothing.
    /// </remarks>
    public virtual HelpTopic HelpTopic => HelpTopic.Contents;

    /// <summary>
    /// Called each time the page becomes visible. Data loading belongs here rather than in
    /// the constructor, so returning to a page picks up changes made elsewhere.
    /// </summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;
}
