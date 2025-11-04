using System;
using System.Windows.Forms;

namespace DisableWindowsUpdates;

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
            }

            return;
        }

        using var notifier = new TrayNotifier("Disable Windows Updates");
        try
        {
            var stateStore = new StateRepository();
            var manager = new WindowsUpdateManager(stateStore, notifier);
            var currentState = manager.GetCurrentState();

            if (currentState == WindowsUpdateState.Disabled)
            {
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
                    notifier.FlushAndDispose(5000);
                    return;
                }

                if (promptResult == DialogResult.Yes)
                {
                    notifier.ShowInfo("Creating system restore point...");
                    if (SystemRestoreManager.TryCreateRestorePoint("Before disabling Windows Update services", out var error))
                    {
                        notifier.ShowInfo("System restore point created successfully.");
                    }
                    else if (!string.IsNullOrEmpty(error))
                    {
                        notifier.ShowWarning($"Failed to create a system restore point: {error}");
                    }
                    else
                    {
                        notifier.ShowWarning("Failed to create a system restore point.");
                    }
                }

                manager.DisableUpdates();
            }
        }
        catch (Exception ex)
        {
            notifier.ShowError($"Failed: {ex.Message}");
        }

        notifier.FlushAndDispose(5000);
    }
}
