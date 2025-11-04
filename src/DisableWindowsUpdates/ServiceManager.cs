using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;

namespace DisableWindowsUpdates
{
    internal static class ServiceManager
    {
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
            using (var controller = new ServiceController(serviceName))
            {
                var security = controller.GetAccessControl();
                return security.GetSecurityDescriptorBinaryForm();
            }
        }

        public static void RestoreSecurityDescriptor(string serviceName, byte[] descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException("descriptor");
            }

            using (var controller = new ServiceController(serviceName))
            {
                var security = controller.GetAccessControl();
                var commonDescriptor = new CommonSecurityDescriptor(false, false, descriptor, 0);
                var sddl = commonDescriptor.GetSddlForm(AccessControlSections.Access);
                security.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
                controller.SetAccessControl(security);
            }
        }

        public static void ApplyLockdown(string serviceName)
        {
            using (var controller = new ServiceController(serviceName))
            {
                var security = controller.GetAccessControl();
                security.SetAccessRuleProtection(true, false);

                foreach (AuthorizationRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                {
                    var accessRule = rule as ServiceAccessRule;
                    if (accessRule != null)
                    {
                        security.RemoveAccessRuleSpecific(accessRule);
                    }
                }

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

                security.AddAccessRule(new ServiceAccessRule(adminSid, ServiceControllerRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new ServiceAccessRule(systemSid, ServiceControllerRights.QueryStatus | ServiceControllerRights.Interrogate, AccessControlType.Allow));
                security.AddAccessRule(new ServiceAccessRule(systemSid, ServiceControllerRights.Start | ServiceControllerRights.ChangeConfig | ServiceControllerRights.Stop, AccessControlType.Deny));
                if (trustedInstallerSid != null)
                {
                    security.AddAccessRule(new ServiceAccessRule(trustedInstallerSid, ServiceControllerRights.Start | ServiceControllerRights.ChangeConfig | ServiceControllerRights.Stop, AccessControlType.Deny));
                }

                controller.SetAccessControl(security);
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

        private static class NativeMethods
        {
            public const uint ServiceNoChange = 0xFFFFFFFF;
            public const int ERROR_INSUFFICIENT_BUFFER = 122;

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool ChangeServiceConfig(IntPtr hService, uint nServiceType, uint nStartType, uint nErrorControl, string lpBinaryPathName, string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword, string lpDisplayName);

            [DllImport("advapi32.dll", SetLastError = true)]
            public static extern bool QueryServiceConfig(IntPtr hService, IntPtr lpServiceConfig, uint cbBufSize, out uint pcbBytesNeeded);
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
