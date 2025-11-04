using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DisableWindowsUpdates
{
    internal static class SystemRestoreManager
    {
        private const int MaxDescriptionLength = 256;

        public static bool TryCreateRestorePoint(string description, out string failureReason)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                description = "Disable Windows Updates restore point";
            }

            var sanitizedDescription = SanitizeDescription(description);

            try
            {
                var beginInfo = new RestorePointInfo
                {
                    EventType = RestorePointEventType.BeginSystemChange,
                    RestorePointType = RestorePointType.ModifySettings,
                    SequenceNumber = 0,
                    Description = sanitizedDescription
                };

                StateManagerStatus status;
                if (!SRSetRestorePoint(ref beginInfo, out status))
                {
                    failureReason = GetLastErrorMessage();
                    Logger.Warning("Failed to start system restore point creation: " + failureReason);
                    return false;
                }

                var endInfo = new RestorePointInfo
                {
                    EventType = RestorePointEventType.EndSystemChange,
                    RestorePointType = RestorePointType.ModifySettings,
                    SequenceNumber = status.SequenceNumber,
                    Description = sanitizedDescription
                };

                StateManagerStatus endStatus;
                if (!SRSetRestorePoint(ref endInfo, out endStatus))
                {
                    failureReason = GetLastErrorMessage();
                    Logger.Warning("Failed to finalize system restore point creation: " + failureReason);
                    return false;
                }

                failureReason = null;
                Logger.Info("System restore point created with description: " + sanitizedDescription);
                return true;
            }
            catch (Win32Exception ex)
            {
                failureReason = ex.Message;
                Logger.Error("System restore point creation failed due to a Win32 exception.", ex);
                return false;
            }
            catch (ExternalException ex)
            {
                failureReason = ex.Message;
                Logger.Error("System restore point creation failed due to an external exception.", ex);
                return false;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                Logger.Error("System restore point creation failed due to an unexpected exception.", ex);
                return false;
            }
        }

        private static string SanitizeDescription(string description)
        {
            var trimmed = description.Trim();
            if (trimmed.Length >= MaxDescriptionLength)
            {
                return trimmed.Substring(0, MaxDescriptionLength - 1);
            }

            return trimmed;
        }

        private static string GetLastErrorMessage()
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode == 0)
            {
                return "Unknown error while creating the restore point.";
            }

            var exception = new Win32Exception(errorCode);
            return string.Format("{0} (0x{1:X8})", exception.Message, errorCode);
        }

        [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SRSetRestorePoint(ref RestorePointInfo restorePointInfo, out StateManagerStatus status);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RestorePointInfo
        {
            public RestorePointEventType EventType;
            public RestorePointType RestorePointType;
            public long SequenceNumber;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDescriptionLength)]
            public string Description;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StateManagerStatus
        {
            public int Status;
            public long SequenceNumber;
        }

        private enum RestorePointEventType
        {
            BeginSystemChange = 100,
            EndSystemChange = 101
        }

        private enum RestorePointType
        {
            ApplicationInstall = 0,
            ApplicationUninstall = 1,
            ModifySettings = 12
        }
    }
}
