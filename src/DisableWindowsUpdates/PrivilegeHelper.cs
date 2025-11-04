using System;
using System.Diagnostics;
using System.Security.Principal;

namespace DisableWindowsUpdates;

internal static class PrivilegeHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRelaunchWithElevation()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            var executablePath = currentProcess.MainModule?.FileName;
            if (string.IsNullOrEmpty(executablePath))
            {
                executablePath = AppDomain.CurrentDomain.SetupInformation.ApplicationName;
            }

            if (string.IsNullOrEmpty(executablePath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
