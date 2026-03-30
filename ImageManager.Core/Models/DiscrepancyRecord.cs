namespace ImageManager.Core.Models;

public sealed class DiscrepancyRecord
{
    public string RelativePath { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string ExpectedLocation { get; set; } = string.Empty;
    public string ActualLocation { get; set; } = string.Empty;
}
