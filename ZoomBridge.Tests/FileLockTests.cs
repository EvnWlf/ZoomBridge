using System.IO;
using Xunit;
using ZoomBridge.Core.Monitoring;

namespace ZoomBridge.Tests
{
    public class FileLockTests
    {
        [Fact]
        public async Task Inspector_DoesNotMarkFolderReady_WhileTempOrZoomFilesExist()
        {
            var inspector = new ProcessInspector(isTranscoderRunning: () => false, pollInterval: TimeSpan.FromMilliseconds(200));
            var testDir = Path.Combine(Path.GetTempPath(), "ZB_LockTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var zoomFile = Path.Combine(testDir, "double_click_to_convert_01.zoom");
                await File.WriteAllTextAsync(zoomFile, "dummy raw zoom stream");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                bool ready = await inspector.WaitForFolderReadyAsync(testDir, TimeSpan.FromSeconds(1), cts.Token);

                Assert.False(ready);
            }
            finally
            {
                if (Directory.Exists(testDir))
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            }
        }

        [Fact]
        public async Task Inspector_MarksFolderReady_WhenCompletedMediaFilesAreUnlockedAndStable()
        {
            var inspector = new ProcessInspector(isTranscoderRunning: () => false, pollInterval: TimeSpan.FromMilliseconds(200));
            var testDir = Path.Combine(Path.GetTempPath(), "ZB_ReadyTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var mp4File = Path.Combine(testDir, "video12345.mp4");
                await File.WriteAllBytesAsync(mp4File, new byte[1024]);

                bool ready = await inspector.WaitForFolderReadyAsync(testDir, TimeSpan.FromSeconds(6));
                Assert.True(ready);
            }
            finally
            {
                if (Directory.Exists(testDir))
                {
                    try { Directory.Delete(testDir, true); } catch { }
                }
            }
        }
    }
}
