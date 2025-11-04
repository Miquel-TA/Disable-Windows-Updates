using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DisableWindowsUpdates;

internal static class SystemRestoreManager
{
    private const int MaxDescriptionLength = 256;

    public static bool TryCreateRestorePoint(string description, out string? failureReason)
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

            if (!SRSetRestorePoint(ref beginInfo, out var status))
            {
                failureReason = GetLastErrorMessage();
                return false;
            }

            var endInfo = new RestorePointInfo
            {
                EventType = RestorePointEventType.EndSystemChange,
                RestorePointType = RestorePointType.ModifySettings,
                SequenceNumber = status.SequenceNumber,
                Description = sanitizedDescription
            };

            if (!SRSetRestorePoint(ref endInfo, out _))
            {
                failureReason = GetLastErrorMessage();
                return false;
            }

            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or Win32Exception)
        {
            failureReason = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static string SanitizeDescription(string description)
    {
        var trimmed = description.Trim();
        return trimmed.Length >= MaxDescriptionLength
            ? trimmed[..(MaxDescriptionLength - 1)]
            : trimmed;
    }

    private static string GetLastErrorMessage()
    {
        var errorCode = Marshal.GetLastWin32Error();
        if (errorCode == 0)
        {
            return "Unknown error while creating the restore point.";
        }

        var exception = new Win32Exception(errorCode);
        return $"{exception.Message} (0x{errorCode:X8})";
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
