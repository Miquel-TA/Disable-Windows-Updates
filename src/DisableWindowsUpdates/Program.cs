using System;
using System.Windows.Forms;

namespace DisableWindowsUpdates
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!PrivilegeHelper.IsRunningAsAdministrator())
            {
                if (!PrivilegeHelper.TryRelaunchWithElevation())
                {
                    TrayNotifier.ShowError("Disable Windows Updates", "Administrator privileges are required.");
                    Logger.Warning("Application terminated because administrator privileges were not granted.");
                }

                return;
            }

            Logger.Info("Application started with administrative privileges.");

            var notifier = new TrayNotifier("Disable Windows Updates");
            try
            {
                var stateStore = new StateRepository();
                var manager = new WindowsUpdateManager(stateStore, notifier);
                var currentState = manager.GetCurrentState();

                if (currentState == WindowsUpdateState.Disabled)
                {
                    Logger.Info("Detected Windows Update services as disabled; initiating enable operation.");
                    manager.EnableUpdates();
                }
                else
                {
                    var promptResult = MessageBox.Show(
                        "Do you want to create a system restore point before disabling Windows Update services?",
                        "Create Restore Point",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (promptResult == DialogResult.Cancel)
                    {
                        notifier.ShowWarning("Operation cancelled. Windows Update services were not modified.");
                        Logger.Info("User cancelled the disable operation before changes were applied.");
                        notifier.FlushAndDispose(5000);
                        return;
                    }

                    if (promptResult == DialogResult.Yes)
                    {
                        notifier.ShowInfo("Creating system restore point...");
                        if (SystemRestoreManager.TryCreateRestorePoint("Before disabling Windows Update services", out var error))
                        {
                            Logger.Info("System restore point created successfully.");
                            notifier.ShowInfo("System restore point created successfully.");
                        }
                        else if (!string.IsNullOrEmpty(error))
                        {
                            Logger.Warning("Failed to create system restore point: " + error);
                            notifier.ShowWarning("Failed to create a system restore point: " + error);
                        }
                        else
                        {
                            Logger.Warning("Failed to create system restore point due to an unspecified error.");
                            notifier.ShowWarning("Failed to create a system restore point.");
                        }
                    }

                    Logger.Info("Initiating disable operation for Windows Update services.");
                    manager.DisableUpdates();
                }
            }
            catch (Exception ex)
            {
                notifier.ShowError("Failed: " + ex.Message);
                Logger.Error("Unhandled exception in application entry point.", ex);
            }
            finally
            {
                notifier.FlushAndDispose(5000);
                notifier.Dispose();
            }
        }
    }
}
