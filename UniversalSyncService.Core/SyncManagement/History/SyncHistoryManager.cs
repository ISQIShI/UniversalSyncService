using System.Text;
using System.Text.Json;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.History;

namespace UniversalSyncService.Core.SyncManagement.History;

/// <summary>
/// 基于 SQLite 的同步历史管理器。
/// 这里保留了对旧 JSON 文件的自动迁移，避免已有测试数据丢失。
/// </summary>
public sealed class SyncHistoryManager : ISyncHistoryManager
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<SyncHistoryManager> _logger;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _isInitialized;

    public SyncHistoryManager(
        IConfigurationManagementService configurationManagementService,
        IHostEnvironment hostEnvironment,
        ILogger<SyncHistoryManager> logger)
    {
        _configurationManagementService = configurationManagementService;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task<long> GetLatestVersionAsync(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(SyncVersion), 0) FROM SyncHistoryEntries WHERE PlanId = $planId;";
        command.Parameters.AddWithValue("$planId", planId);
        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<SyncHistoryEntry>> GetPreviousSyncHistoryAsync(string planId, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(nodeId);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, PlanId, TaskId, NodeId,
       MetadataId, MetadataName, MetadataPath, MetadataSize,
       MetadataCreatedAt, MetadataModifiedAt, MetadataChecksum, MetadataContentType,
       State, SyncTimestamp, SyncVersion
FROM SyncHistoryEntries
WHERE PlanId = $planId AND NodeId = $nodeId
ORDER BY SyncVersion DESC, MetadataPath ASC;";
        command.Parameters.AddWithValue("$planId", planId);
        command.Parameters.AddWithValue("$nodeId", nodeId);

        return await ReadEntriesAsync(command, CancellationToken.None);
    }

    public async Task<SyncHistoryEntry?> GetFileHistoryAsync(string planId, string nodeId, string filePath)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(filePath);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, PlanId, TaskId, NodeId,
       MetadataId, MetadataName, MetadataPath, MetadataSize,
       MetadataCreatedAt, MetadataModifiedAt, MetadataChecksum, MetadataContentType,
       State, SyncTimestamp, SyncVersion
FROM SyncHistoryEntries
WHERE PlanId = $planId AND NodeId = $nodeId AND MetadataPath = $filePath
ORDER BY SyncVersion DESC
LIMIT 1;";
        command.Parameters.AddWithValue("$planId", planId);
        command.Parameters.AddWithValue("$nodeId", nodeId);
        command.Parameters.AddWithValue("$filePath", filePath);

        return (await ReadEntriesAsync(command, CancellationToken.None)).FirstOrDefault();
    }

    public async Task SaveHistoryAsync(IEnumerable<SyncHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        var bufferedEntries = entries.ToList();
        if (bufferedEntries.Count == 0)
        {
            return;
        }

        var keepVersions = Math.Max(1, _configurationManagementService.GetSyncOptions().HistoryRetentionVersions);
        var planIds = bufferedEntries.Select(entry => entry.PlanId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        foreach (var entry in bufferedEntries)
        {
            await InsertHistoryEntryAsync(connection, transaction, entry, CancellationToken.None);
        }

        foreach (var planId in planIds)
        {
            await TrimHistoryAsync(connection, transaction, planId, keepVersions, CancellationToken.None);
        }

        await transaction.CommitAsync(CancellationToken.None);
    }

    public async Task CleanupOldHistoryAsync(string planId, int keepVersions)
    {
        ArgumentNullException.ThrowIfNull(planId);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);
        await TrimHistoryAsync(connection, transaction, planId, Math.Max(1, keepVersions), CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    public async Task DeletePlanHistoryAsync(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

        await using (var deleteHistory = connection.CreateCommand())
        {
            deleteHistory.Transaction = (SqliteTransaction)transaction;
            deleteHistory.CommandText = "DELETE FROM SyncHistoryEntries WHERE PlanId = $planId;";
            deleteHistory.Parameters.AddWithValue("$planId", planId);
            await deleteHistory.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await transaction.CommitAsync(CancellationToken.None);
    }

    public async Task<IReadOnlyList<SyncHistoryEntry>> GetRecentHistoryAsync(string? planId, int limit)
    {
        await EnsureDatabaseReadyAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT Id, PlanId, TaskId, NodeId,
       MetadataId, MetadataName, MetadataPath, MetadataSize,
       MetadataCreatedAt, MetadataModifiedAt, MetadataChecksum, MetadataContentType,
       State, SyncTimestamp, SyncVersion
FROM SyncHistoryEntries
WHERE ($planId = '' OR PlanId = $planId)
ORDER BY SyncTimestamp DESC, SyncVersion DESC
LIMIT $limit;";
        command.Parameters.AddWithValue("$planId", planId ?? string.Empty);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        return await ReadEntriesAsync(command, CancellationToken.None);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            var databasePath = ResolveHistoryStorePath();
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(databasePath) && await IsLegacyJsonStoreAsync(databasePath, cancellationToken))
            {
                await MigrateLegacyJsonStoreAsync(databasePath, cancellationToken);
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnableWalModeAsync(connection, cancellationToken);
            await CreateTablesAsync(connection, cancellationToken);

            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={ResolveHistoryStorePath()}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnableWalModeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CreateTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS SyncHistoryEntries (
    Id TEXT NOT NULL PRIMARY KEY,
    PlanId TEXT NOT NULL,
    TaskId TEXT NOT NULL,
    NodeId TEXT NOT NULL,
    MetadataId TEXT NOT NULL,
    MetadataName TEXT NOT NULL,
    MetadataPath TEXT NOT NULL,
    MetadataSize INTEGER NOT NULL,
    MetadataCreatedAt TEXT NULL,
    MetadataModifiedAt TEXT NULL,
    MetadataChecksum TEXT NULL,
    MetadataContentType TEXT NULL,
    State INTEGER NOT NULL,
    SyncTimestamp TEXT NOT NULL,
    SyncVersion INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_SyncHistoryEntries_PlanId_SyncVersion ON SyncHistoryEntries(PlanId, SyncVersion DESC);
CREATE INDEX IF NOT EXISTS IX_SyncHistoryEntries_PlanId_NodeId_Path ON SyncHistoryEntries(PlanId, NodeId, MetadataPath, SyncVersion DESC);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<SyncHistoryEntry>> ReadEntriesAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var entries = new List<SyncHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new SyncHistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                new SyncItemMetadata(
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt64(7),
                    ParseNullableDateTimeOffset(reader.GetString(8)),
                    ParseNullableDateTimeOffset(reader.GetString(9)),
                    ParseNullableString(reader.GetString(10)),
                    ParseNullableString(reader.GetString(11))),
                (FileHistoryState)reader.GetInt32(12),
                DateTimeOffset.Parse(reader.GetString(13)),
                reader.GetInt64(14)));
        }

        return entries;
    }

    private static async Task InsertHistoryEntryAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        SyncHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = @"
INSERT OR REPLACE INTO SyncHistoryEntries (
    Id, PlanId, TaskId, NodeId,
    MetadataId, MetadataName, MetadataPath, MetadataSize,
    MetadataCreatedAt, MetadataModifiedAt, MetadataChecksum, MetadataContentType,
    State, SyncTimestamp, SyncVersion)
VALUES (
    $id, $planId, $taskId, $nodeId,
    $metadataId, $metadataName, $metadataPath, $metadataSize,
    $metadataCreatedAt, $metadataModifiedAt, $metadataChecksum, $metadataContentType,
    $state, $syncTimestamp, $syncVersion);";
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$planId", entry.PlanId);
        command.Parameters.AddWithValue("$taskId", entry.TaskId);
        command.Parameters.AddWithValue("$nodeId", entry.NodeId);
        command.Parameters.AddWithValue("$metadataId", entry.Metadata.Id);
        command.Parameters.AddWithValue("$metadataName", entry.Metadata.Name);
        command.Parameters.AddWithValue("$metadataPath", entry.Metadata.Path);
        command.Parameters.AddWithValue("$metadataSize", entry.Metadata.Size);
        command.Parameters.AddWithValue("$metadataCreatedAt", entry.Metadata.CreatedAt?.ToString("O") ?? string.Empty);
        command.Parameters.AddWithValue("$metadataModifiedAt", entry.Metadata.ModifiedAt?.ToString("O") ?? string.Empty);
        command.Parameters.AddWithValue("$metadataChecksum", entry.Metadata.Checksum ?? string.Empty);
        command.Parameters.AddWithValue("$metadataContentType", entry.Metadata.ContentType ?? string.Empty);
        command.Parameters.AddWithValue("$state", (int)entry.State);
        command.Parameters.AddWithValue("$syncTimestamp", entry.SyncTimestamp.ToString("O"));
        command.Parameters.AddWithValue("$syncVersion", entry.SyncVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TrimHistoryAsync(
        SqliteConnection connection,
        DbTransaction transaction,
        string planId,
        int keepVersions,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = @"
DELETE FROM SyncHistoryEntries
WHERE PlanId = $planId
  AND SyncVersion NOT IN (
      SELECT SyncVersion
      FROM SyncHistoryEntries
      WHERE PlanId = $planId
      GROUP BY SyncVersion
      ORDER BY SyncVersion DESC
      LIMIT $keepVersions
  );";
        command.Parameters.AddWithValue("$planId", planId);
        command.Parameters.AddWithValue("$keepVersions", keepVersions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> IsLegacyJsonStoreAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private async Task MigrateLegacyJsonStoreAsync(string databasePath, CancellationToken cancellationToken)
    {
        var jsonContent = await File.ReadAllTextAsync(databasePath, cancellationToken);
        var legacyStore = JsonSerializer.Deserialize<LegacyPersistedHistoryStore>(jsonContent, LegacyJsonOptions) ?? new LegacyPersistedHistoryStore();
        var backupPath = $"{databasePath}.legacy.json";
        File.Move(databasePath, backupPath, overwrite: true);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnableWalModeAsync(connection, cancellationToken);
        await CreateTablesAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var legacyEntry in legacyStore.Entries)
        {
            await InsertHistoryEntryAsync(connection, transaction, ToHistoryEntry(legacyEntry), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("已完成同步历史从 JSON 到 SQLite 的迁移。备份文件={BackupPath}", backupPath);
    }

    private string ResolveHistoryStorePath()
    {
        var configuredPath = _configurationManagementService.GetSyncOptions().HistoryStorePath;
        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static string? ParseNullableString(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : DateTimeOffset.Parse(value);
    }

    private static SyncHistoryEntry ToHistoryEntry(LegacyPersistedHistoryEntry legacyEntry)
    {
        return new SyncHistoryEntry(
            legacyEntry.Id,
            legacyEntry.PlanId,
            legacyEntry.TaskId,
            legacyEntry.NodeId,
            new SyncItemMetadata(
                legacyEntry.Metadata.Id,
                legacyEntry.Metadata.Name,
                legacyEntry.Metadata.Path,
                legacyEntry.Metadata.Size,
                legacyEntry.Metadata.CreatedAt,
                legacyEntry.Metadata.ModifiedAt,
                legacyEntry.Metadata.Checksum,
                legacyEntry.Metadata.ContentType),
            legacyEntry.State,
            legacyEntry.SyncTimestamp,
            legacyEntry.SyncVersion);
    }

    private sealed class LegacyPersistedHistoryStore
    {
        public List<LegacyPersistedHistoryEntry> Entries { get; set; } = [];
    }

    private sealed class LegacyPersistedHistoryEntry
    {
        public string Id { get; set; } = string.Empty;

        public string PlanId { get; set; } = string.Empty;

        public string TaskId { get; set; } = string.Empty;

        public string NodeId { get; set; } = string.Empty;

        public LegacyPersistedSyncItemMetadata Metadata { get; set; } = new();

        public FileHistoryState State { get; set; }

        public DateTimeOffset SyncTimestamp { get; set; }

        public long SyncVersion { get; set; }
    }

    private sealed class LegacyPersistedSyncItemMetadata
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public long Size { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? ModifiedAt { get; set; }

        public string? Checksum { get; set; }

        public string? ContentType { get; set; }
    }

}
