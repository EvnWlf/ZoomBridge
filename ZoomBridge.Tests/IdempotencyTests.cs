using System.IO;
using Xunit;
using ZoomBridge.Core.Storage;

namespace ZoomBridge.Tests
{
    public class IdempotencyTests
    {
        [Fact]
        public async Task RecordingFolder_WhenSavedInDatabase_IsIdentifiedAsProcessed()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ZB_TestDb_" + Guid.NewGuid().ToString("N"));
            try
            {
                var db = new DatabaseContext(tempDir);
                var testFolder = Path.Combine(tempDir, "Meeting_2026-11-10");

                // 1. Inicialmente no debe estar procesada
                bool initiallyProcessed = await db.IsFolderProcessedAsync(testFolder);
                Assert.False(initiallyProcessed);

                // 2. Insertar registro simulado
                var record = new ProcessedRecording
                {
                    FolderName = Path.GetFileName(testFolder),
                    OriginalFolderPath = testFolder,
                    DestinationFolderPath = testFolder + "_Dest",
                    FileCount = 3,
                    TotalSizeBytes = 1048576,
                    Status = RecordingStatus.Completed,
                    ProcessedAt = DateTime.UtcNow
                };
                await db.AddRecordAsync(record);

                // 3. Ahora debe retornar true para evitar duplicados (Idempotencia)
                bool isProcessed = await db.IsFolderProcessedAsync(testFolder);
                Assert.True(isProcessed);

                // 4. Actualizar registro no debe fallar (ON CONFLICT DO UPDATE)
                record.FileCount = 5;
                await db.AddRecordAsync(record);

                var recent = await db.GetRecentAsync(10);
                Assert.Contains(recent, r => r.OriginalFolderPath == testFolder && r.FileCount == 5);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }
    }
}
