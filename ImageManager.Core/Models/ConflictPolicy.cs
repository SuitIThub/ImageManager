namespace ImageManager.Core.Models;

public enum ConflictAction
{
    Ask = 0,
    Overwrite = 1,
    Skip = 2,
    Cancel = 3
}

public sealed class ConflictPolicy
{
    public ConflictAction DefaultAction { get; set; } = ConflictAction.Ask;
    public Dictionary<string, ConflictAction> ExtensionOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ApplyToAllRemaining { get; set; }
    public bool OnlyExistingTargets { get; set; }
}
