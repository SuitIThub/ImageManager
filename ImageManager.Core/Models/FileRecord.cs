namespace ImageManager.Core.Models;

public sealed class FileRecord
{
    public string SourceRoot { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public bool IsSelected { get; set; } = true;
    public string VersionId { get; set; } = string.Empty;
    public string QualityTier { get; set; } = string.Empty;
}
