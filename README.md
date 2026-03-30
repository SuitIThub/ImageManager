# ImageManager

Desktop application for managing tracked image and media libraries on Windows: scanning, backups, staging, archiving, WebP compression, discrepancy checks, and related workflows. Built with WPF on .NET 8.

## Requirements

- Windows
- [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) (runtime for published builds; SDK for development)

## Build

```bash
dotnet build ImageManager.sln -c Release
```

## Run (development)

```bash
dotnet run --project ImageManager.App/ImageManager.App.csproj
```

## Releases

Published versions are built from GitHub Actions when the application version in `ImageManager.App/ImageManager.App.csproj` is increased. See [Releases](https://github.com/SuitIThub/ImageManager/releases) for installers and zip packages.

The app can check for updates from the toolbar (**Check for updates**) or on startup (when not running under a debugger).

## Version

The shipping version is defined by the `<Version>` property in `ImageManager.App/ImageManager.App.csproj`.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
