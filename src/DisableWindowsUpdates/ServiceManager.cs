using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32.SafeHandles;

namespace DisableWindowsUpdates
{
    internal static class ServiceManager
    {
        private const SecurityInfos SecurityInfoFlags = SecurityInfos.Owner | SecurityInfos.Group | SecurityInfos.DiscretionaryAcl;

        public static uint GetStartType(string serviceName)
        {
            using (var controller = new ServiceController(serviceName))
            {
                var serviceHandle = controller.ServiceHandle.DangerousGetHandle();
                var queryResult = QueryConfig(serviceHandle);
                return queryResult.dwStartType;
            }
        }

        public static void SetStartType(string serviceName, ServiceStartType startType)
        {
            using (var controller = new ServiceController(serviceName))
            {
                ChangeStartType(controller.ServiceHandle.DangerousGetHandle(), startType);
            }
        }

        public static void StopService(string serviceName, TimeSpan timeout)
        {
            using (var controller = new ServiceController(serviceName))
            {
                if (controller.Status == ServiceControllerStatus.Stopped)
                {
                    return;
                }

                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
        }

        public static void StartService(string serviceName, TimeSpan timeout)
        {
            using (var controller = new ServiceController(serviceName))
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
            }
        }

        public static byte[] GetSecurityDescriptor(string serviceName)
        {
            using (var handle = OpenServiceHandle(serviceName, ServiceAccessRights.ReadControl))
            {
                return GetServiceSecurityDescriptor(handle);
            }
        }

        public static void RestoreSecurityDescriptor(string serviceName, byte[] descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            using (var handle = OpenServiceHandle(serviceName, ServiceAccessRights.ReadControl | ServiceAccessRights.WriteDac))
            {
                SetServiceSecurityDescriptor(handle, descriptor);
            }
        }

        public static void ApplyLockdown(string serviceName)
        {
            using (var handle = OpenServiceHandle(serviceName, ServiceAccessRights.ReadControl | ServiceAccessRights.WriteDac))
            {
                var descriptorBytes = GetServiceSecurityDescriptor(handle);
                var security = new CommonSecurityDescriptor(false, false, descriptorBytes, 0);
                security.SetDiscretionaryAclProtection(true, false);

                var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier trustedInstallerSid = null;
                try
                {
                    trustedInstallerSid = new NTAccount("NT SERVICE", "TrustedInstaller").Translate(typeof(SecurityIdentifier)) as SecurityIdentifier;
                }
                catch (IdentityNotMappedException)
                {
                    Logger.Warning("TrustedInstaller account could not be resolved on this system.");
                }

                var lockdownAcl = new DiscretionaryAcl(false, false, 4);
                var lockdownRights = ServiceAccessRights.Start | ServiceAccessRights.ChangeConfig | ServiceAccessRights.Stop;

                lockdownAcl.AddAccess(AccessControlType.Deny, systemSid, (int)lockdownRights, InheritanceFlags.None, PropagationFlags.None);
                if (trustedInstallerSid != null)
                {
                    lockdownAcl.AddAccess(AccessControlType.Deny, trustedInstallerSid, (int)lockdownRights, InheritanceFlags.None, PropagationFlags.None);
                }

                var systemAllowRights = ServiceAccessRights.QueryStatus | ServiceAccessRights.Interrogate | ServiceAccessRights.ReadControl;
                lockdownAcl.AddAccess(AccessControlType.Allow, systemSid, (int)systemAllowRights, InheritanceFlags.None, PropagationFlags.None);
                lockdownAcl.AddAccess(AccessControlType.Allow, adminSid, (int)ServiceAccessRights.AllAccess, InheritanceFlags.None, PropagationFlags.None);

                security.DiscretionaryAcl = lockdownAcl;

                var updatedDescriptor = new byte[security.BinaryLength];
                security.GetBinaryForm(updatedDescriptor, 0);

                SetServiceSecurityDescriptor(handle, updatedDescriptor);
            }
        }

        private static SafeServiceHandle OpenServiceHandle(string serviceName, ServiceAccessRights accessRights)
        {
            var scmHandle = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (scmHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var serviceHandle = NativeMethods.OpenService(scmHandle, serviceName, (uint)accessRights);
                if (serviceHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return new SafeServiceHandle(serviceHandle);
            }
            finally
            {
                NativeMethods.CloseServiceHandle(scmHandle);
            }
        }

        private static byte[] GetServiceSecurityDescriptor(SafeServiceHandle serviceHandle)
        {
            uint bytesNeeded = 0;
            if (!NativeMethods.QueryServiceObjectSecurity(serviceHandle.DangerousGetHandle(), SecurityInfoFlags, null, 0, out bytesNeeded))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                {
                    throw new Win32Exception(error);
                }
            }

            var buffer = new byte[bytesNeeded];
            if (!NativeMethods.QueryServiceObjectSecurity(serviceHandle.DangerousGetHandle(), SecurityInfoFlags, buffer, bytesNeeded, out bytesNeeded))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return buffer;
        }

        private static void SetServiceSecurityDescriptor(SafeServiceHandle serviceHandle, byte[] descriptor)
        {
            if (!NativeMethods.SetServiceObjectSecurity(serviceHandle.DangerousGetHandle(), SecurityInfoFlags, descriptor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static void ChangeStartType(IntPtr serviceHandle, ServiceStartType startType)
        {
            if (!NativeMethods.ChangeServiceConfig(serviceHandle, NativeMethods.ServiceNoChange, (uint)startType, NativeMethods.ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        private static QUERY_SERVICE_CONFIG QueryConfig(IntPtr serviceHandle)
        {
            uint bytesNeeded = 0;
            NativeMethods.QueryServiceConfig(serviceHandle, IntPtr.Zero, 0, out bytesNeeded);
            var lastError = Marshal.GetLastWin32Error();
            if (lastError != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            {
                throw new Win32Exception(lastError);
            }

            var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                uint ignored;
                if (!NativeMethods.QueryServiceConfig(serviceHandle, buffer, bytesNeeded, out ignored))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return (QUERY_SERVICE_CONFIG)Marshal.PtrToStructure(buffer, typeof(QUERY_SERVICE_CONFIG));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeServiceHandle(IntPtr handle)
                : base(true)
            {
                SetHandle(handle);
            }

            protected override bool ReleaseHandle()
            {
                return NativeMethods.CloseServiceHandle(handle);
            }
        }

        private static class NativeMethods
        {
            public const uint ServiceNoChange = 0xFFFFFFFF;
            public const int ERROR_INSUFFICIENT_BUFFER = 122;
            public const uint SC_MANAGER_CONNECT = 0x0001;

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool ChangeServiceConfig(IntPtr hService, uint nServiceType, uint nStartType, uint nErrorControl, string lpBinaryPathName, string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword, string lpDisplayName);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool QueryServiceConfig(IntPtr hService, IntPtr lpServiceConfig, uint cbBufSize, out uint pcbBytesNeeded);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr OpenSCManager(string machineName, string databaseName, uint dwDesiredAccess);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool CloseServiceHandle(IntPtr hSCObject);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool QueryServiceObjectSecurity(IntPtr hService, SecurityInfos dwSecurityInformation, byte[] lpSecurityDescriptor, uint cbBufSize, out uint pcbBytesNeeded);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool SetServiceObjectSecurity(IntPtr hService, SecurityInfos dwSecurityInformation, byte[] lpSecurityDescriptor);
        }
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct QUERY_SERVICE_CONFIG
{
    public uint dwServiceType;
    public uint dwStartType;
    public uint dwErrorControl;
    public IntPtr lpBinaryPathName;
    public IntPtr lpLoadOrderGroup;
    public uint dwTagId;
    public IntPtr lpDependencies;
    public IntPtr lpServiceStartName;
    public IntPtr lpDisplayName;
}

internal enum ServiceStartType : uint
{
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4
}

[Flags]
internal enum ServiceAccessRights : uint
{
    QueryConfig = 0x0001,
    ChangeConfig = 0x0002,
    QueryStatus = 0x0004,
    EnumerateDependents = 0x0008,
    Start = 0x0010,
    Stop = 0x0020,
    PauseContinue = 0x0040,
    Interrogate = 0x0080,
    UserDefinedControl = 0x0100,
    Delete = 0x00010000,
    ReadControl = 0x00020000,
    WriteDac = 0x00040000,
    WriteOwner = 0x00080000,
    AllAccess = 0x000F01FF
}
