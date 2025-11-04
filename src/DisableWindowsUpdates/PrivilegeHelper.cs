using System;
using System.Diagnostics;
using System.Security.Principal;

namespace DisableWindowsUpdates
{
    internal static class PrivilegeHelper
    {
        public static bool IsRunningAsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            if (identity == null)
            {
                return false;
            }

            using (identity)
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool TryRelaunchWithElevation()
        {
            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    var executablePath = currentProcess.MainModule != null ? currentProcess.MainModule.FileName : null;
                    if (string.IsNullOrEmpty(executablePath))
                    {
                        executablePath = Environment.ProcessPath;
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
                    Logger.Info("Application relaunched with elevation request.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to relaunch application with elevation.", ex);
                return false;
            }
        }
    }
}
