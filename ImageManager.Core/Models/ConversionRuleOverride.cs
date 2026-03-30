namespace ImageManager.Core.Models;

public sealed class ConversionRuleOverride
{
    public string FolderContains { get; set; } = string.Empty;
    public bool UseLossless { get; set; }
    public int Quality { get; set; } = 90;
}
