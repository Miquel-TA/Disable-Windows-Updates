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
