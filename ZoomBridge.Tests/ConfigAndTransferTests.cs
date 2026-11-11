using System.IO;
using Xunit;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Storage;
using ZoomBridge.Core.Transfer;

namespace ZoomBridge.Tests
{
    public class ConfigAndTransferTests
    {
        [Fact]
        public async Task TransferService_TransfersFilesCleanly_AndSupportsResync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "ZB_TransferTest_" + Guid.NewGuid().ToString("N"));
            var sourceDir = Path.Combine(tempRoot, "SourceMeeting_2026-11-10");
            var destDir = Path.Combine(tempRoot, "CloudSyncDestination");
            var dataDir = Path.Combine(tempRoot, "AppData");

            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(destDir);
            Directory.CreateDirectory(dataDir);

            try
            {
                var configMgr = new ConfigManager(dataDir, manageAutoStart: false);
                var db = new DatabaseContext(dataDir);
                var transferService = new MeetingTransferService(configMgr, db);

                // Crear archivos simulados de reunión Zoom
                var mp4Path = Path.Combine(sourceDir, "zoom_0.mp4");
                var m4aPath = Path.Combine(sourceDir, "audio_only.m4a");
                var txtPath = Path.Combine(sourceDir, "chat.txt");

                await File.WriteAllBytesAsync(mp4Path, new byte[2048]);
                await File.WriteAllBytesAsync(m4aPath, new byte[1024]);
                await File.WriteAllTextAsync(txtPath, "Hola mundo desde el chat");

                // Configurar destino
                configMgr.CurrentConfig.DestinationDirectory = destDir;
                configMgr.CurrentConfig.RenamePromptEnabled = false;
                configMgr.CurrentConfig.DeleteSourceAfterMove = false; // Conservar origen para probar resync

                // 1. Transferir
                bool success = await transferService.TransferMeetingFolderAsync(sourceDir, interactive: false);
                Assert.True(success);

                // Validar destino
                var targetMeetingDir = Path.Combine(destDir, "SourceMeeting_2026-11-10");
                Assert.True(Directory.Exists(targetMeetingDir));
                Assert.True(File.Exists(Path.Combine(targetMeetingDir, "zoom_0.mp4")));

                // Validar idempotencia en base de datos
                bool isRecorded = await db.IsFolderProcessedAsync(sourceDir);
                Assert.True(isRecorded);

                // 2. Probar Resync (Volver a sincronizar)
                var records = await db.GetRecentAsync(5);
                var record = records.First(r => r.OriginalFolderPath == sourceDir);

                var resyncOutcome = await transferService.ResyncAsync(record);
                Assert.True(resyncOutcome == ResyncOutcome.Copied || resyncOutcome == ResyncOutcome.Verified);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try { Directory.Delete(tempRoot, true); } catch { }
                }
            }
        }
    }
}
