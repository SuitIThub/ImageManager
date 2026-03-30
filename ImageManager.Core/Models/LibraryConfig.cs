namespace ImageManager.Core.Models;

public sealed class LibraryConfig
{
    public string MainRoot { get; set; } = string.Empty;
    public List<string> MainRoots { get; set; } = [];
    public string StageRoot { get; set; } = string.Empty;
    public string BackupRoot { get; set; } = string.Empty;
    public string FullBackupRoot { get; set; } = string.Empty;
    public string ArchiveRoot { get; set; } = string.Empty;
    public string PreApkCompressionBackupRoot { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = "dev";
    public string CurrentQualityTier { get; set; } = "lossless";
    public long ApkBudgetBytes { get; set; } = 1_700_000_000;
    public string WebpEncoderPath { get; set; } = string.Empty;
    public int DefaultLossyQuality { get; set; } = 90;
    public int ApkMinimumQuality { get; set; } = 40;
    public List<string> TrackExtensions { get; set; } = [".png", ".webp", ".webm"];
    public List<string> ArchiveExtensions { get; set; } = [".png", ".webp", ".webm"];
    public List<string> VideoExtensions { get; set; } = [".webm", ".mp4"];
    public List<string> FinishedExtensions { get; set; } = [".webp", ".webm"];
    public List<string> CompressionInputExtensions { get; set; } = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"];
    public string DiscrepancyMapSourceExtension { get; set; } = ".png";
    public string DiscrepancyMapTargetExtension { get; set; } = ".webp";
    public List<ConversionRuleOverride> ConversionOverrides { get; set; } = [];
    public ConflictPolicy ConflictPolicy { get; set; } = new();
}
