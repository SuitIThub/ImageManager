using System.Collections.ObjectModel;

namespace ImageManager.App.ViewModels;

public sealed class FileTreeNodeViewModel : ObservableObject
{
    private bool? _isChecked = true;
    private bool _isExpanded;

    public FileTreeNodeViewModel(string name, string relativePath, bool isFolder)
    {
        Name = name;
        RelativePath = relativePath;
        IsFolder = isFolder;
    }

    /// <summary>Parent folder node, or null for roots.</summary>
    public FileTreeNodeViewModel? Parent { get; set; }

    public string Name { get; }
    public string RelativePath { get; }
    public bool IsFolder { get; }
    public ObservableCollection<FileTreeNodeViewModel> Children { get; } = [];

    /// <summary>Bound to TreeViewItem.IsExpanded for expand/collapse and expand-all actions.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Sets expansion on this node (if it has children) and all descendants.</summary>
    public void SetExpandedRecursive(bool expanded)
    {
        if (Children.Count > 0)
        {
            IsExpanded = expanded;
        }

        foreach (var child in Children)
        {
            child.SetExpandedRecursive(expanded);
        }
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            ApplyCheckToDescendants(value);
            Parent?.NotifyCheckChangedFromSubtree();
        }
    }

    /// <summary>
    /// Sets the check state without notifying parents (used when wiring trees where order of construction would cause noisy updates).
    /// </summary>
    internal void SetCheckedWithoutNotify(bool? value)
    {
        ApplyCheckToDescendants(value);
    }

    /// <summary>Recomputes folder checkboxes from children after bulk load (post-order).</summary>
    internal void RefreshAggregateFromChildrenAfterLoad()
    {
        foreach (var c in Children)
        {
            c.RefreshAggregateFromChildrenAfterLoad();
        }

        if (Children.Count == 0)
        {
            return;
        }

        var newVal = ComputeAggregate();
        _ = SetProperty(ref _isChecked, newVal);
    }

    private void ApplyCheckToDescendants(bool? value)
    {
        SetProperty(ref _isChecked, value);
        foreach (var child in Children)
        {
            child.ApplyCheckToDescendants(value);
        }
    }

    private void NotifyCheckChangedFromSubtree()
    {
        if (Children.Count == 0)
        {
            return;
        }

        var newVal = ComputeAggregate();
        if (SetProperty(ref _isChecked, newVal))
        {
            Parent?.NotifyCheckChangedFromSubtree();
        }
    }

    private bool? ComputeAggregate()
    {
        if (Children.Count == 0)
        {
            return _isChecked;
        }

        bool? first = null;
        foreach (var c in Children)
        {
            if (first is null)
            {
                first = c.IsChecked;
            }
            else if (!Nullable.Equals(c.IsChecked, first))
            {
                return null;
            }
        }

        return first;
    }
}
