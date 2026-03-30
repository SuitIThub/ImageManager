using ImageManager.Core.Models;
using ImageManager.Core.Interfaces;

namespace ImageManager.Services;

internal static class FileTransferService
{
    public static bool ShouldProcess(ConflictPolicy policy, string sourcePath, string destinationPath, IConflictPrompt? prompt = null)
    {
        if (!File.Exists(destinationPath))
        {
            return !policy.OnlyExistingTargets;
        }

        var extension = Path.GetExtension(destinationPath);
        if (policy.ExtensionOverrides.TryGetValue(extension, out var action))
        {
            return action switch
            {
                ConflictAction.Overwrite => true,
                ConflictAction.Skip => false,
                ConflictAction.Cancel => false,
                _ => true
            };
        }

        if (policy.DefaultAction == ConflictAction.Overwrite)
        {
            return true;
        }

        if (policy.DefaultAction == ConflictAction.Skip || policy.DefaultAction == ConflictAction.Cancel)
        {
            return false;
        }

        if (prompt is null)
        {
            return true;
        }

        var decision = prompt.Ask(sourcePath, destinationPath, extension);
        switch (decision)
        {
            case ConflictPromptDecision.Overwrite:
                return true;
            case ConflictPromptDecision.Skip:
                return false;
            case ConflictPromptDecision.Cancel:
                policy.DefaultAction = ConflictAction.Cancel;
                return false;
            case ConflictPromptDecision.OverwriteAll:
                policy.DefaultAction = ConflictAction.Overwrite;
                return true;
            case ConflictPromptDecision.SkipAll:
                policy.DefaultAction = ConflictAction.Skip;
                return false;
            case ConflictPromptDecision.SkipAllForExtension:
                policy.ExtensionOverrides[extension] = ConflictAction.Skip;
                return false;
            default:
                return true;
        }
    }

    public static void CopyOrMove(string sourcePath, string destinationPath, bool move)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (move)
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(sourcePath, destinationPath);
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }
}
