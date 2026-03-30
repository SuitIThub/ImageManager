namespace ImageManager.Core.Models;

public sealed class CountSummary
{
    public string LocationName { get; set; } = string.Empty;
    public int ImageCount { get; set; }
    public int VideoCount { get; set; }
    public long TotalBytes { get; set; }
}
