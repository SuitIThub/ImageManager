using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;
using System.Diagnostics;
using System.Globalization;

namespace ImageManager.Services;

public sealed class CompressionService : ICompressionService
{
    public Task<int> CompressStageToWebpAsync(LibraryConfig config, bool highQuality, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureEncoder(config);
        var count = 0;
        var failed = new List<(string Path, string Error)>();
        var sw = Stopwatch.StartNew();
        long totalInputBytesOk = 0;
        long totalOutputBytesOk = 0;
        var configuredTrackExtensions = config.TrackExtensions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.StartsWith('.') ? x : $".{x}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var configuredCompressionExtensions = config.CompressionInputExtensions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.StartsWith('.') ? x : $".{x}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (configuredCompressionExtensions.Count == 0)
        {
            configuredCompressionExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"];
        }
        var activeCompressionExtensions = configuredTrackExtensions
            .Where(ext => configuredCompressionExtensions.Contains(ext))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ignoredTrackExtensions = configuredTrackExtensions
            .Where(ext => !configuredCompressionExtensions.Contains(ext))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = Directory.EnumerateFiles(config.StageRoot, "*.*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var ext = Path.GetExtension(file);
                return activeCompressionExtensions.Contains(ext);
            })
            .ToList();

        progress?.Report(
            $"Stage → WebP started. Root: {config.StageRoot}{Environment.NewLine}" +
            $"Configured TrackExtensions: {string.Join(", ", configuredTrackExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}{Environment.NewLine}" +
            $"Configured CompressionInputExtensions: {string.Join(", ", configuredCompressionExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}{Environment.NewLine}" +
            $"Active compression extensions: {string.Join(", ", activeCompressionExtensions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}{Environment.NewLine}" +
            (ignoredTrackExtensions.Count == 0
                ? string.Empty
                : $"Ignored TrackExtensions for this step: {string.Join(", ", ignoredTrackExtensions)}{Environment.NewLine}") +
            $"Found {candidates.Count} candidate images. Mode: {(highQuality ? "high-quality/lossless default" : $"lossy default (q={config.DefaultLossyQuality})")}.");

        string? lastFile = null;
        var lastQuality = 0;

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = candidates[i];
            var output = Path.ChangeExtension(file, ".webp");
            var quality = ResolveQualityForPath(config, file, highQuality);
            lastFile = file;
            lastQuality = quality;
            try
            {
                var beforeBytes = File.Exists(file) ? new FileInfo(file).Length : 0L;
                RunEncoder(config.WebpEncoderPath, file, output, quality);
                var afterBytes = File.Exists(output) ? new FileInfo(output).Length : 0L;
                count++;
                totalInputBytesOk += beforeBytes;
                totalOutputBytesOk += afterBytes;
            }
            catch (Exception ex)
            {
                failed.Add((file, ex.Message));
            }

            if ((i + 1) % 25 == 0 || i == 0)
            {
                var done = i + 1;
                var elapsed = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                var rate = done / elapsed;
                var remaining = candidates.Count - done;
                var etaSeconds = rate > 0.01 ? remaining / rate : double.NaN;
                var etaText = double.IsFinite(etaSeconds)
                    ? TimeSpan.FromSeconds(Math.Clamp(etaSeconds, 0, 24 * 60 * 60)).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture)
                    : "n/a";

                var qText = lastQuality < 0 ? "lossless (-1)" : $"q={lastQuality}";
                var savedBytes = Math.Max(0L, totalInputBytesOk - totalOutputBytesOk);
                var savedPct = totalInputBytesOk > 0 ? (savedBytes * 100.0 / totalInputBytesOk) : 0.0;
                progress?.Report(
                    $"Stage → WebP {done}/{candidates.Count} ({(done * 100.0 / Math.Max(1, candidates.Count)):0.0}%). " +
                    $"OK={count}, failed={failed.Count}, saved={FormatBytes(savedBytes)} ({savedPct:0.0}%), {rate:0.0} files/s, ETA {etaText}.{Environment.NewLine}" +
                    $"Current: {lastFile} ({qText})");
            }
        }

        if (failed.Count == 0)
        {
            progress?.Report($"Stage → WebP complete: {count}/{candidates.Count} converted successfully in {sw.Elapsed:hh\\:mm\\:ss}.");
        }
        else
        {
            var logPath = TryWriteFailureLog("stage-webp", failed);
            var preview = string.Join(Environment.NewLine, failed.Take(5).Select(f => $"- {f.Path}"));
            progress?.Report(
                $"Stage → WebP complete: {count}/{candidates.Count} converted successfully in {sw.Elapsed:hh\\:mm\\:ss}. " +
                $"Skipped {failed.Count} unreadable/failed inputs.{Environment.NewLine}" +
                (string.IsNullOrWhiteSpace(logPath) ? "" : $"Failure list saved to:{Environment.NewLine}{logPath}{Environment.NewLine}") +
                $"First failures:{Environment.NewLine}{preview}");
        }
        return Task.FromResult(count);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }

    private static string? TryWriteFailureLog(string kind, List<(string Path, string Error)> failed)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ImageManager",
                "logs");
            Directory.CreateDirectory(root);

            var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{kind}-failures.txt";
            var path = Path.Combine(root, fileName);

            using var writer = new StreamWriter(path, append: false);
            writer.WriteLine($"# ImageManager failures ({kind})");
            writer.WriteLine($"# utc={DateTime.UtcNow:O}");
            writer.WriteLine("# format: <path>\\t<error>");
            foreach (var f in failed)
            {
                writer.Write(f.Path);
                writer.Write('\t');
                writer.WriteLine(f.Error.Replace('\r', ' ').Replace('\n', ' '));
            }

            return path;
        }
        catch
        {
            return null;
        }
    }

    public Task<int> CompressMainToApkBudgetAsync(LibraryConfig config, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureEncoder(config);
        var webps = Directory.EnumerateFiles(config.MainRoot, "*.webp", SearchOption.AllDirectories).ToList();
        var count = 0;

        foreach (var webp in webps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(config.MainRoot, webp);
            var backup = Path.Combine(config.PreApkCompressionBackupRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(webp, backup, overwrite: true);
            count++;
        }

        var protectedFiles = webps
            .Where(path => ResolveQualityForPath(config, path, highQuality: false) < 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adjustable = webps.Where(path => !protectedFiles.Contains(path)).ToList();

        var totalSize = GetTotalSize(webps);
        var protectedSize = GetTotalSize(protectedFiles);
        var budget = config.ApkBudgetBytes;
        if (totalSize <= budget)
        {
            progress?.Report($"APK budget already satisfied: {totalSize} / {budget} bytes.");
            return Task.FromResult(count);
        }

        if (protectedSize >= budget)
        {
            progress?.Report($"Cannot reach budget. Protected files alone are {protectedSize} bytes (budget: {budget}).");
            return Task.FromResult(count);
        }

        var quality = Math.Clamp(config.DefaultLossyQuality, config.ApkMinimumQuality, 100);
        var minQuality = Math.Clamp(config.ApkMinimumQuality, 1, quality);
        var lastTotal = totalSize;
        progress?.Report($"Starting APK optimization: total={totalSize}, budget={budget}, protected={protectedSize}, adjustable={adjustable.Count} files.");

        while (totalSize > budget && quality >= minQuality && adjustable.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pressure = (double)(totalSize - budget) / Math.Max(totalSize, 1L);
            var fraction = Math.Clamp(pressure * 2.0, 0.15, 1.0);
            var candidates = adjustable
                .OrderByDescending(path => new FileInfo(path).Length)
                .Take(Math.Max(1, (int)Math.Ceiling(adjustable.Count * fraction)))
                .ToList();

            foreach (var webp in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunEncoder(config.WebpEncoderPath, webp, webp, quality);
            }

            totalSize = GetTotalSize(webps);
            var improved = lastTotal - totalSize;
            progress?.Report($"APK pass q={quality}, files={candidates.Count}, size={totalSize} bytes, reduced={improved}.");

            if (improved < Math.Max(1024 * 1024, lastTotal / 200))
            {
                quality -= 10;
            }
            else
            {
                quality -= 5;
            }

            lastTotal = totalSize;
        }

        if (totalSize > budget && adjustable.Count > 0)
        {
            foreach (var webp in adjustable.OrderByDescending(path => new FileInfo(path).Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RunEncoder(config.WebpEncoderPath, webp, webp, minQuality);
            }

            totalSize = GetTotalSize(webps);
            progress?.Report($"Final minimum-quality sweep completed. size={totalSize} bytes, budget={budget}.");
        }

        progress?.Report($"Backed up {count} webp files before APK budget compression.");
        return Task.FromResult(count);
    }

    private static long GetTotalSize(IEnumerable<string> files) => files.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0L);

    private static int ResolveQualityForPath(LibraryConfig config, string sourcePath, bool highQuality)
    {
        foreach (var rule in config.ConversionOverrides)
        {
            if (!string.IsNullOrWhiteSpace(rule.FolderContains) && sourcePath.Contains(rule.FolderContains, StringComparison.OrdinalIgnoreCase))
            {
                return rule.UseLossless ? -1 : rule.Quality;
            }
        }

        return highQuality ? -1 : config.DefaultLossyQuality;
    }

    private static void RunEncoder(string encoderPath, string inputFile, string outputFile, int quality)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

        // For in-place recompression (input == output), write to a temp output then replace.
        var inputFull = Path.GetFullPath(inputFile);
        var outputFull = Path.GetFullPath(outputFile);
        var inPlace = string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase);
        var effectiveOutput = outputFile;
        string? tempOutput = null;
        if (inPlace)
        {
            var dir = Path.GetDirectoryName(outputFull)!;
            tempOutput = Path.Combine(dir, $".imagemanager-tmp-{Guid.NewGuid():N}{Path.GetExtension(outputFull)}");
            effectiveOutput = tempOutput;
        }
        else
        {
            // Ensure overwrite of existing outputs regardless of encoder quirks.
            try
            {
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
            catch
            {
                // Ignore delete failures; encoder still has -overwrite.
            }
        }

        var result = RunEncoderOnce(encoderPath, inputFile, effectiveOutput, quality);
        if (result.ExitCode == 0)
        {
            if (inPlace && tempOutput is not null)
            {
                ReplaceFile(tempOutput, outputFull);
            }
            return;
        }

        // Some environments (mapped/network drives, partial writes, antivirus) cause NConvert to fail reading a file
        // even though the image is valid. Retry once from a local temp copy when we see the "can't read picture" signature.
        if (LooksLikeUnreadableImage(result.Stderr))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ImageManager", "encoder-retry");
            Directory.CreateDirectory(tempDir);
            var tempInput = Path.Combine(tempDir, $"{Guid.NewGuid():N}{Path.GetExtension(inputFile)}");
            try
            {
                File.Copy(inputFile, tempInput, overwrite: true);
                var retry = RunEncoderOnce(encoderPath, tempInput, effectiveOutput, quality);
                if (retry.ExitCode == 0)
                {
                    if (inPlace && tempOutput is not null)
                    {
                        ReplaceFile(tempOutput, outputFull);
                    }
                    return;
                }

                throw new InvalidOperationException(
                    BuildEncoderFailureMessage(encoderPath, inputFile, quality, result) +
                    Environment.NewLine + Environment.NewLine +
                    "Retry from temp copy also failed." + Environment.NewLine +
                    BuildEncoderFailureDetails(encoderPath, tempInput, outputFile, quality, retry));
            }
            finally
            {
                try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { /* ignore cleanup */ }
            }
        }

        throw new InvalidOperationException(BuildEncoderFailureMessage(encoderPath, inputFile, quality, result));
    }

    private static bool LooksLikeUnreadableImage(string? stderr)
    {
        return !string.IsNullOrWhiteSpace(stderr)
            && stderr.Contains("Don't know how to read this picture", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEncoderFailureMessage(string encoderPath, string inputFile, int quality, EncoderRunResult result)
    {
        return $"Encoder failed for '{inputFile}' with quality {quality}.{Environment.NewLine}" +
               BuildEncoderFailureDetails(encoderPath, result.InputFile, result.OutputFile, quality, result);
    }

    private static string BuildEncoderFailureDetails(string encoderPath, string inputFile, string outputFile, int quality, EncoderRunResult result)
    {
        var args = string.Join(" ", result.Args.Select(QuoteArg));
        return string.Join(Environment.NewLine, new[]
        {
            $"Encoder: {encoderPath}",
            $"Args: {args}",
            $"ExitCode: {result.ExitCode}",
            string.IsNullOrWhiteSpace(result.Stderr) ? null : $"STDERR: {result.Stderr.Trim()}",
            string.IsNullOrWhiteSpace(result.Stdout) ? null : $"STDOUT: {result.Stdout.Trim()}",
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private sealed record EncoderRunResult(
        int ExitCode,
        string InputFile,
        string OutputFile,
        IReadOnlyList<string> Args,
        string Stdout,
        string Stderr);

    private static EncoderRunResult RunEncoderOnce(string encoderPath, string inputFile, string outputFile, int quality)
    {
        var encoderDir = Path.GetDirectoryName(encoderPath);
        var psi = new ProcessStartInfo
        {
            FileName = encoderPath,
            WorkingDirectory = string.IsNullOrWhiteSpace(encoderDir) ? Environment.CurrentDirectory : encoderDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var args = new List<string>();
        void Add(string a) { psi.ArgumentList.Add(a); args.Add(a); }

        Add("-overwrite");
        Add("-out");
        Add("webp");
        Add("-q");
        Add(quality.ToString(CultureInfo.InvariantCulture));
        Add("-o");
        Add(outputFile);
        Add(inputFile);

        using var process = Process.Start(psi);
        if (process is null)
        {
            throw new InvalidOperationException($"Failed to start encoder process for '{inputFile}'.");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new EncoderRunResult(process.ExitCode, inputFile, outputFile, args, stdout, stderr);
    }

    private static void ReplaceFile(string tempOutput, string outputFull)
    {
        // Replace existing output atomically when possible.
        try
        {
            if (File.Exists(outputFull))
            {
                File.Replace(tempOutput, outputFull, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempOutput, outputFull, overwrite: true);
            }
        }
        catch
        {
            // Fallback: best-effort copy+overwrite then delete temp.
            File.Copy(tempOutput, outputFull, overwrite: true);
            try { File.Delete(tempOutput); } catch { /* ignore */ }
        }
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        return arg.IndexOfAny([' ', '\t', '\n', '\r', '"']) >= 0
            ? $"\"{arg.Replace("\"", "\\\"")}\""
            : arg;
    }

    private static void EnsureEncoder(LibraryConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebpEncoderPath) || !File.Exists(config.WebpEncoderPath))
        {
            throw new InvalidOperationException("WebP encoder path is not configured. Set Configuration -> WebP Encoder Path.");
        }
    }
}
