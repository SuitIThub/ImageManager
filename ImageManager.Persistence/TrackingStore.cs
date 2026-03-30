using ImageManager.Core.Interfaces;
using ImageManager.Core.Models;
using Microsoft.Data.Sqlite;

namespace ImageManager.Persistence;

public sealed class TrackingStore : ITrackingStore
{
    private string _dbPath;
    private readonly SemaphoreSlim _sync = new(1, 1);

    public TrackingStore(string? dbPath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _dbPath = dbPath ?? Path.Combine(appData, "ImageManager", "tracking.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_dbPath)!;
        Directory.CreateDirectory(dir);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 26) // SQLITE_NOTADB
        {
            RecoverInvalidDatabaseFile();
            await InitializeCoreAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken);

        var needsRecreate = await NeedsRecreateAsync(connection, cancellationToken);
        if (needsRecreate)
        {
            await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS Files;", cancellationToken);
        }

        await ExecuteNonQueryAsync(connection, """
CREATE TABLE IF NOT EXISTS Files (
  SourceRoot TEXT NOT NULL,
  RelativePath TEXT NOT NULL,
  Extension TEXT NOT NULL,
  SizeBytes INTEGER NOT NULL,
  LastWriteUtc TEXT NOT NULL,
  IsSelected INTEGER NOT NULL,
  VersionId TEXT NOT NULL,
  QualityTier TEXT NOT NULL,
  PRIMARY KEY(SourceRoot, RelativePath)
);
""", cancellationToken);

        await ExecuteNonQueryAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Files_IsSelected ON Files(IsSelected);", cancellationToken);
        await ExecuteNonQueryAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Files_Extension ON Files(Extension);", cancellationToken);
        await ExecuteNonQueryAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Files_VersionId ON Files(VersionId);", cancellationToken);
        await ExecuteNonQueryAsync(connection, "CREATE INDEX IF NOT EXISTS IX_Files_QualityTier ON Files(QualityTier);", cancellationToken);
    }

    private void RecoverInvalidDatabaseFile()
    {
        if (!File.Exists(_dbPath))
        {
            return;
        }

        var backupPath = $"{_dbPath}.invalid-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
        var moved = false;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Move(_dbPath, backupPath, overwrite: true);
                moved = true;
                break;
            }
            catch (IOException)
            {
                if (attempt < 5)
                {
                    Thread.Sleep(150 * attempt);
                }
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt < 5)
                {
                    Thread.Sleep(150 * attempt);
                }
            }
        }

        if (moved)
        {
            return;
        }

        // If migration file is still locked by another process, continue with a fresh DB file.
        var dir = Path.GetDirectoryName(_dbPath)!;
        var fileName = Path.GetFileNameWithoutExtension(_dbPath);
        var extension = Path.GetExtension(_dbPath);
        _dbPath = Path.Combine(dir, $"{fileName}.recovered-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}");
    }

    public async Task UpsertBatchAsync(IEnumerable<FileRecord> records, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
INSERT INTO Files(SourceRoot, RelativePath, Extension, SizeBytes, LastWriteUtc, IsSelected, VersionId, QualityTier)
VALUES ($sourceRoot, $relativePath, $extension, $sizeBytes, $lastWriteUtc, $isSelected, $versionId, $qualityTier)
ON CONFLICT(SourceRoot, RelativePath) DO UPDATE SET
  SourceRoot=excluded.SourceRoot,
  Extension=excluded.Extension,
  SizeBytes=excluded.SizeBytes,
  LastWriteUtc=excluded.LastWriteUtc,
  IsSelected=excluded.IsSelected,
  VersionId=excluded.VersionId,
  QualityTier=excluded.QualityTier;
""";
            var pSourceRoot = command.CreateParameter();
            pSourceRoot.ParameterName = "$sourceRoot";
            command.Parameters.Add(pSourceRoot);
            var pRelativePath = command.CreateParameter();
            pRelativePath.ParameterName = "$relativePath";
            command.Parameters.Add(pRelativePath);
            var pExtension = command.CreateParameter();
            pExtension.ParameterName = "$extension";
            command.Parameters.Add(pExtension);
            var pSizeBytes = command.CreateParameter();
            pSizeBytes.ParameterName = "$sizeBytes";
            command.Parameters.Add(pSizeBytes);
            var pLastWriteUtc = command.CreateParameter();
            pLastWriteUtc.ParameterName = "$lastWriteUtc";
            command.Parameters.Add(pLastWriteUtc);
            var pIsSelected = command.CreateParameter();
            pIsSelected.ParameterName = "$isSelected";
            command.Parameters.Add(pIsSelected);
            var pVersionId = command.CreateParameter();
            pVersionId.ParameterName = "$versionId";
            command.Parameters.Add(pVersionId);
            var pQualityTier = command.CreateParameter();
            pQualityTier.ParameterName = "$qualityTier";
            command.Parameters.Add(pQualityTier);

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pSourceRoot.Value = record.SourceRoot;
                pRelativePath.Value = record.RelativePath;
                pExtension.Value = record.Extension;
                pSizeBytes.Value = record.SizeBytes;
                pLastWriteUtc.Value = record.LastWriteUtc.ToString("O");
                pIsSelected.Value = record.IsSelected ? 1 : 0;
                pVersionId.Value = record.VersionId;
                pQualityTier.Value = record.QualityTier;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<FileRecord>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken);
            return await ReadManyAsync(connection, "SELECT SourceRoot, RelativePath, Extension, SizeBytes, LastWriteUtc, IsSelected, VersionId, QualityTier FROM Files ORDER BY RelativePath;", cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<IReadOnlyList<FileRecord>> ReadSelectedAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken);
            return await ReadManyAsync(connection, "SELECT SourceRoot, RelativePath, Extension, SizeBytes, LastWriteUtc, IsSelected, VersionId, QualityTier FROM Files WHERE IsSelected=1 ORDER BY RelativePath;", cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SetSelectionAsync(ISet<string> selectedKeys, CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            await using var connection = Open();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var reset = connection.CreateCommand())
            {
                reset.Transaction = transaction;
                reset.CommandText = "UPDATE Files SET IsSelected=0;";
                await reset.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Files SET IsSelected=1 WHERE SourceRoot=$sourceRoot AND RelativePath=$relativePath;";
            var pSource = update.CreateParameter();
            pSource.ParameterName = "$sourceRoot";
            update.Parameters.Add(pSource);
            var pRelative = update.CreateParameter();
            pRelative.ParameterName = "$relativePath";
            update.Parameters.Add(pRelative);

            foreach (var key in selectedKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var split = key.Split('|', 2);
                if (split.Length != 2)
                {
                    continue;
                }

                pSource.Value = split[0];
                pRelative.Value = split[1];
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, bool ignoreErrors = false)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch when (ignoreErrors)
        {
        }
    }

    private static async Task<bool> NeedsRecreateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(Files);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<(string Name, int Pk)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add((reader.GetString(1), reader.GetInt32(5)));
        }

        if (columns.Count == 0)
        {
            return false;
        }

        var hasSourceRoot = columns.Any(c => string.Equals(c.Name, "SourceRoot", StringComparison.OrdinalIgnoreCase));
        var hasCompositePk = columns.Count(c => c.Pk > 0) >= 2;
        return !hasSourceRoot || !hasCompositePk;
    }

    private static async Task<List<FileRecord>> ReadManyAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<FileRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FileRecord
            {
                SourceRoot = reader.GetString(0),
                RelativePath = reader.GetString(1),
                Extension = reader.GetString(2),
                SizeBytes = reader.GetInt64(3),
                LastWriteUtc = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                IsSelected = reader.GetInt64(5) != 0,
                VersionId = reader.GetString(6),
                QualityTier = reader.GetString(7)
            });
        }

        return list;
    }
}
