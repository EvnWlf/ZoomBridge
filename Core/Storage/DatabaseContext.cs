using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ZoomBridge.Core.Storage
{
    public class DatabaseContext
    {
        public const string DatabaseFileName = "zoombridge.db";
        private readonly string _connectionString;

        public string DatabaseFilePath { get; }

        public DatabaseContext(string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            DatabaseFilePath = Path.Combine(dataDirectory, DatabaseFileName);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                // Acceso esporádico: sin pool se libera memoria y el archivo no queda bloqueado
                Pooling = false
            }.ToString();

            Initialize();
        }

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private void Initialize()
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS ProcessedRecordings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FolderName TEXT NOT NULL,
                    OriginalFolderPath TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    DestinationFolderPath TEXT NOT NULL,
                    FileCount INTEGER NOT NULL,
                    TotalSizeBytes INTEGER NOT NULL,
                    Status TEXT NOT NULL,
                    ProcessedAt TEXT NOT NULL,
                    ErrorMessage TEXT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Idempotencia: una carpeta se considera atendida si ya se respaldó o si el usuario
        /// decidió omitirla. Las fallidas se reintentan.
        /// </summary>
        public async Task<bool> IsFolderProcessedAsync(string folderPath)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(1) FROM ProcessedRecordings
                                WHERE OriginalFolderPath = $path AND Status IN ('Completed', 'Skipped', 'Missing');";
            cmd.Parameters.AddWithValue("$path", folderPath);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        }

        public async Task AddRecordAsync(ProcessedRecording record)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ProcessedRecordings
                    (FolderName, OriginalFolderPath, DestinationFolderPath, FileCount, TotalSizeBytes, Status, ProcessedAt, ErrorMessage)
                VALUES ($folderName, $originalPath, $destPath, $fileCount, $totalSize, $status, $processedAt, $error)
                ON CONFLICT(OriginalFolderPath) DO UPDATE SET
                    FolderName = excluded.FolderName,
                    DestinationFolderPath = excluded.DestinationFolderPath,
                    FileCount = excluded.FileCount,
                    TotalSizeBytes = excluded.TotalSizeBytes,
                    Status = excluded.Status,
                    ProcessedAt = excluded.ProcessedAt,
                    ErrorMessage = excluded.ErrorMessage;";
            cmd.Parameters.AddWithValue("$folderName", record.FolderName);
            cmd.Parameters.AddWithValue("$originalPath", record.OriginalFolderPath);
            cmd.Parameters.AddWithValue("$destPath", record.DestinationFolderPath);
            cmd.Parameters.AddWithValue("$fileCount", record.FileCount);
            cmd.Parameters.AddWithValue("$totalSize", record.TotalSizeBytes);
            cmd.Parameters.AddWithValue("$status", record.Status);
            cmd.Parameters.AddWithValue("$processedAt", record.ProcessedAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("$error", (object?)record.ErrorMessage ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<ProcessedRecording>> GetRecentAsync(int limit = 500)
        {
            var list = new List<ProcessedRecording>();
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, FolderName, OriginalFolderPath, DestinationFolderPath, FileCount, TotalSizeBytes, Status, ProcessedAt, ErrorMessage
                FROM ProcessedRecordings ORDER BY ProcessedAt DESC, Id DESC LIMIT $limit;";
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProcessedRecording
                {
                    Id = reader.GetInt64(0),
                    FolderName = reader.GetString(1),
                    OriginalFolderPath = reader.GetString(2),
                    DestinationFolderPath = reader.GetString(3),
                    FileCount = reader.GetInt32(4),
                    TotalSizeBytes = reader.GetInt64(5),
                    Status = reader.GetString(6),
                    ProcessedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8)
                });
            }
            return list;
        }

        public async Task DeleteRecordAsync(long id)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ProcessedRecordings WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateRecordFolderAsync(long id, string newFolderName, string newDestPath)
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE ProcessedRecordings SET FolderName = $name, DestinationFolderPath = $dest WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$name", newFolderName);
            cmd.Parameters.AddWithValue("$dest", newDestPath);
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<(int totalCount, long totalBytes, DateTime? lastProcessed)> GetStatisticsAsync()
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(1), COALESCE(SUM(TotalSizeBytes), 0), MAX(ProcessedAt)
                                FROM ProcessedRecordings WHERE Status = 'Completed';";
            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (0, 0, null);

            DateTime? last = reader.IsDBNull(2)
                ? null
                : DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return (Convert.ToInt32(reader.GetInt64(0)), reader.GetInt64(1), last);
        }

        public async Task ClearHistoryAsync()
        {
            using var connection = Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ProcessedRecordings;";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
