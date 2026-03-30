using System.Windows.Forms;
using System.IO;

namespace ImageManager.App.Services;

public sealed class FolderDialogService
{
    public string? PickFolder(string? initialPath, string title)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.SelectedPath = initialPath!;
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? PickFile(string? initialPath, string title, string filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*")
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            if (File.Exists(initialPath))
            {
                dialog.FileName = initialPath;
            }
            else if (Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }
        }

        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
    }
}

