using System.Collections.ObjectModel;

namespace ImageManager.App.ViewModels;

public sealed class FileTreeViewModel : ObservableObject
{
    public ObservableCollection<FileTreeNodeViewModel> Roots { get; } = [];

    public void Clear() => Roots.Clear();

    public void SetExpandedAll(bool expanded)
    {
        foreach (var r in Roots)
        {
            r.SetExpandedRecursive(expanded);
        }
    }
}
