using System.IO;
using System.Threading;
using System.Windows;
using Xunit;
using ZoomBridge.Core.Config;
using ZoomBridge.Core.Monitoring;
using ZoomBridge.Core.Storage;
using ZoomBridge.Core.Transfer;
using ZoomBridge.UI;

namespace ZoomBridge.Tests
{
    public class WindowInitTests
    {
        [Fact]
        public void Test_WindowInitialization()
        {
            Exception? caught = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (Application.Current == null)
                    {
                        new Application();
                    }

                    var tempDir = Path.Combine(Path.GetTempPath(), "ZB_UI_Test_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    var cfg = new ConfigManager(tempDir, manageAutoStart: false);
                    var db = new DatabaseContext(tempDir);
                    var agent = new AgentState();
                    var watcher = new ZoomWatcherService(cfg, db, status: agent);
                    var transfer = new MeetingTransferService(cfg, db, agent);

                    var flyout = new TrayFlyoutWindow(agent, cfg, watcher);
                    var main = new MainWindow(cfg, db, watcher, transfer, agent);
                    var onboarding = new OnboardingWindow(cfg);
                    var rename = new RenamePromptWindow("Test", 30);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (caught != null)
            {
                throw new Exception($"Caught window init exception: {caught}", caught);
            }
        }
    }
}
