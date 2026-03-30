using System.Text.Json;
using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;

namespace ImageManager.Persistence;

public sealed class ConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configPath;

    public ConfigStore(string? configPath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configPath = configPath ?? Path.Combine(appData, "ImageManager", "config.json");
    }

    public async Task<LibraryConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        if (!File.Exists(_configPath))
        {
            var config = new LibraryConfig();
            await SaveAsync(config, cancellationToken);
            return config;
        }

        await using var stream = File.OpenRead(_configPath);
        var configFromFile = await JsonSerializer.DeserializeAsync<LibraryConfig>(stream, JsonOptions, cancellationToken);
        return configFromFile ?? new LibraryConfig();
    }

    public async Task SaveAsync(LibraryConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions, cancellationToken);
    }
}
