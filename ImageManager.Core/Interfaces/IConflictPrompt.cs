namespace ImageManager.Core.Interfaces;

public enum ConflictPromptDecision
{
    Overwrite,
    Skip,
    Cancel,
    OverwriteAll,
    SkipAll,
    SkipAllForExtension
}

public interface IConflictPrompt
{
    ConflictPromptDecision Ask(string sourcePath, string destinationPath, string extension);
}
