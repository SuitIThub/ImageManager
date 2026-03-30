using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageManager.App.Services;

/// <summary>
/// Compares the running build to GitHub releases and, after download, stages a post-exit copy into the install folder.
/// </summary>
public sealed class ApplicationUpdateService
{
    private const string Owner = "SuitIThub";
    private const string Repo = "ImageManager";
    private const string UserAgent = "ImageManager (https://github.com/SuitIThub/ImageManager)";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return client;
    }

    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            var core = plus >= 0 ? info[..plus] : info;
            if (Version.TryParse(core, out var v))
            {
                return v;
            }
        }

        return asm.GetName().Version ?? new Version(0, 0);
    }

    public static string GetInstallDirectory()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            path = Assembly.GetExecutingAssembly().Location;
        }

        return Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
    }

    /// <summary>
    /// Finds the highest GitHub release whose tag parses as a version greater than <paramref name="current"/>,
    /// with a zip asset named like the CI output (ImageManager-x.y.z.zip).
    /// </summary>
    public async Task<UpdateOffer?> GetBestNewerReleaseAsync(Version current, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases?per_page=100";
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(stream, GitHubJsonOptions.Relaxed, cancellationToken).ConfigureAwait(false);
        if (releases is null || releases.Count == 0)
        {
            return null;
        }

        UpdateOffer? best = null;
        foreach (var release in releases)
        {
            if (release.Draft)
            {
                continue;
            }

            if (!TryParseReleaseVersion(release.TagName, out var v) || v <= current)
            {
                continue;
            }

            var asset = FindMatchingZip(release.Assets, v);
            if (asset is null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                continue;
            }

            if (!IsTrustedGitHubDownloadUrl(asset.BrowserDownloadUrl))
            {
                continue;
            }

            if (best is null || v > best.Version)
            {
                best = new UpdateOffer(v, ToVersionLabel(v), asset.BrowserDownloadUrl, release.HtmlUrl ?? "");
            }
        }

        return best;
    }

    public async Task DownloadUpdatePackageAsync(UpdateOffer offer, string zipPath, IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync(offer.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (!IsTrustedGitHubDownloadUrl(offer.DownloadUrl))
        {
            throw new InvalidOperationException("Blocked download URL (not a trusted GitHub host).");
        }

        var total = response.Content.Headers.ContentLength;
        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            read += n;
            if (total is > 0)
            {
                progress?.Report($"Downloading update… {read * 100 / total.Value}%");
            }
            else
            {
                progress?.Report($"Downloading update… {read / 1024} KB");
            }
        }
    }

    public void ExtractZip(string zipPath, string extractDirectory, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, recursive: true);
        }

        Directory.CreateDirectory(extractDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        ZipFile.ExtractToDirectory(zipPath, extractDirectory);
    }

    /// <summary>
    /// Writes a small cmd script that waits for this process to exit, copies the extracted build over the install folder, and restarts the app.
    /// </summary>
    public string WriteApplyScriptAndGetPath(string extractRoot, string installDirectory, string exeFullPath)
    {
        extractRoot = Path.GetFullPath(extractRoot);
        installDirectory = Path.GetFullPath(installDirectory);
        exeFullPath = Path.GetFullPath(exeFullPath);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ImageManager-apply-{Guid.NewGuid():N}.cmd");
        var pid = Environment.ProcessId;

        // Wait until this process exits, then mirror files into the install directory and restart.
        var lines = new[]
        {
            "@echo off",
            "setlocal",
            $"set \"SRC={extractRoot}\"",
            $"set \"DEST={installDirectory}\"",
            $"set \"EXE={exeFullPath}\"",
            $"set \"PID={pid}\"",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" 2>nul | find /I \"%PID%\" >nul",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >nul",
            "  goto wait",
            ")",
            "robocopy \"%SRC%\" \"%DEST%\" /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS",
            "if errorlevel 8 exit /b 1",
            "start \"\" \"%EXE%\"",
            "endlocal"
        };

        File.WriteAllLines(scriptPath, lines);
        return scriptPath;
    }

    public void StartApplyScript(string scriptPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("In-place update is only supported on Windows.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetTempPath()
        };
        Process.Start(psi);
    }

    private static bool IsTrustedGitHubDownloadUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.Equals(u.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubAssetDto? FindMatchingZip(IReadOnlyList<GitHubAssetDto> assets, Version v)
    {
        var expected = $"ImageManager-{ToVersionLabel(v)}.zip";
        foreach (var a in assets)
        {
            if (string.IsNullOrEmpty(a.Name))
            {
                continue;
            }

            if (string.Equals(a.Name, expected, StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }

        foreach (var a in assets)
        {
            if (!string.IsNullOrEmpty(a.Name) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return a;
            }
        }

        return null;
    }

    internal static bool TryParseReleaseVersion(string? tag, out Version version)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            version = new Version(0, 0);
            return false;
        }

        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
        {
            s = s[1..];
        }

        if (!Version.TryParse(s, out var parsed))
        {
            version = new Version(0, 0);
            return false;
        }

        version = parsed;
        return true;
    }

    internal static string ToVersionLabel(Version v)
    {
        if (v.Revision > 0)
        {
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }

        return $"{v.Major}.{v.Minor}.{v.Build}";
    }
}

public sealed record UpdateOffer(Version Version, string VersionLabel, string DownloadUrl, string ReleasePageUrl);

internal static class GitHubJsonOptions
{
    public static readonly JsonSerializerOptions Relaxed = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubAssetDto> Assets { get; set; } = [];
}

internal sealed class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
