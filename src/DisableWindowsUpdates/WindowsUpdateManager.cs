using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DisableWindowsUpdates
{
    internal sealed class WindowsUpdateManager
    {
        private static readonly IReadOnlyList<ServiceTarget> ServiceTargets = new List<ServiceTarget>
        {
            new ServiceTarget("wuauserv", true, true, true),
            new ServiceTarget("WaaSMedicSvc", true, true, true),
            new ServiceTarget("UsoSvc", false, true, true)
        };

        private readonly StateRepository _stateRepository;
        private readonly TrayNotifier _notifier;
        private readonly HashSet<string> _reportedMissingServices;

        public WindowsUpdateManager(StateRepository stateRepository, TrayNotifier notifier)
        {
            _stateRepository = stateRepository;
            _notifier = notifier;
            _reportedMissingServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public WindowsUpdateState GetCurrentState()
        {
            PersistentState state;
            if (_stateRepository.TryLoad(out state))
            {
                Dictionary<string, ServiceSnapshot> services = state.Services ?? new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
                bool hasPersistedServices = services.Count > 0;

                if (state.UpdatesDisabled)
                {
                    Logger.Info("Persisted state indicates Windows Update services were previously disabled.");

                    if (hasPersistedServices)
                    {
                        return WindowsUpdateState.Disabled;
                    }

                    foreach (ServiceTarget target in ServiceTargets)
                    {
                        uint startType;
                        if (!TryGetStartType(target.Name, out startType))
                        {
                            continue;
                        }

                        if (startType != (uint)ServiceStartType.Disabled)
                        {
                            Logger.Info("Service " + target.Name + " is not disabled; considering updates enabled.");
                            return WindowsUpdateState.Enabled;
                        }
                    }

                    Logger.Info("All monitored services report a disabled state.");
                    return WindowsUpdateState.Disabled;
                }

                if (hasPersistedServices)
                {
                    Logger.Warning("Persisted service snapshots exist while updates are marked enabled; treating state as Disabled for safety.");
                    return WindowsUpdateState.Disabled;
                }
            }

            uint wuauservStartType;
            if (TryGetStartType("wuauserv", out wuauservStartType))
            {
                return wuauservStartType == (uint)ServiceStartType.Disabled
                    ? WindowsUpdateState.Disabled
                    : WindowsUpdateState.Enabled;
            }

            Logger.Warning("Unable to read wuauserv configuration; defaulting state to Enabled.");
            return WindowsUpdateState.Enabled;
        }

        public void DisableUpdates()
        {
            _notifier.ShowInfo("Disabling Windows Update services...");
            Logger.Info("Beginning disable operation for Windows Update services.");

            PersistentState state = new PersistentState();
            Dictionary<string, ServiceSnapshot> services = state.Services;
            HashSet<string> capturedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ServiceTarget target in ServiceTargets)
            {
                try
                {
                    uint startType = ServiceManager.GetStartType(target.Name);
                    string descriptorString = null;

                    if (target.LockDown)
                    {
                        byte[] descriptor = ServiceManager.GetSecurityDescriptor(target.Name);
                        descriptorString = Convert.ToBase64String(descriptor);
                    }

                    services[target.Name] = new ServiceSnapshot
                    {
                        StartType = startType,
                        SecurityDescriptor = descriptorString
                    };

                    capturedServices.Add(target.Name);
                    Logger.Info("Captured configuration for service " + target.Name + ".");
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to capture state for service " + target.Name + ".", ex);
                    _notifier.ShowWarning("Failed to capture state for service " + target.Name + ": " + ex.Message);
                }
            }

            bool anyServiceModified = false;

            foreach (ServiceTarget target in ServiceTargets)
            {
                if (!capturedServices.Contains(target.Name))
                {
                    Logger.Warning("Skipping service " + target.Name + " because state capture was unsuccessful.");
                    _notifier.ShowWarning("Skipping service " + target.Name + " because its configuration could not be captured safely. No changes will be made to this service.");
                    continue;
                }

                try
                {
                    if (target.StopWhenDisabling)
                    {
                        ServiceManager.StopService(target.Name, TimeSpan.FromSeconds(30));
                        Logger.Info("Stopped service " + target.Name + " prior to disabling.");
                    }

                    ServiceManager.SetStartType(target.Name, ServiceStartType.Disabled);
                    anyServiceModified = true;
                    Logger.Info("Set service " + target.Name + " start type to Disabled.");

                    if (target.LockDown)
                    {
                        ServiceManager.ApplyLockdown(target.Name);
                        Logger.Info("Applied access control lockdown to service " + target.Name + ".");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to harden service " + target.Name + ".", ex);
                    _notifier.ShowWarning("Failed to harden service " + target.Name + ": " + ex.Message);
                }
            }

            if (!anyServiceModified)
            {
                Logger.Warning("Disable operation aborted because no services could be modified.");
                _notifier.ShowWarning("No Windows Update services were modified. The disable operation was canceled to avoid leaving services in an unknown state.");
                return;
            }

            state.UpdatesDisabled = true;
            _stateRepository.Save(state);
            Logger.Info("Disable operation completed successfully.");
            _notifier.ShowInfo("Windows Updates have been disabled and locked.");
        }

        public void EnableUpdates()
        {
            _notifier.ShowInfo("Restoring Windows Update services...");
            Logger.Info("Beginning enable operation for Windows Update services.");

            PersistentState state;
            bool stateLoaded = _stateRepository.TryLoad(out state);
            bool restoreFailed = !stateLoaded;

            if (!stateLoaded)
            {
                Logger.Warning("No persisted state was available when attempting to enable services.");
                state = new PersistentState();
                _notifier.ShowWarning("No persisted Windows Update state was found. Service permissions may remain locked until the disable operation is run again.");
            }

            if (state.Services == null)
            {
                state.Services = new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase);
            }

            List<string> restoredServices = new List<string>();

            foreach (ServiceTarget target in ServiceTargets)
            {
                bool serviceFailed = false;
                ServiceSnapshot snapshot;
                bool hadSnapshot = state.Services.TryGetValue(target.Name, out snapshot);

                try
                {
                    if (hadSnapshot)
                    {
                        if (!string.IsNullOrEmpty(snapshot.SecurityDescriptor))
                        {
                            byte[] descriptor = Convert.FromBase64String(snapshot.SecurityDescriptor);
                            ServiceManager.RestoreSecurityDescriptor(target.Name, descriptor);
                            Logger.Info("Restored security descriptor for service " + target.Name + ".");
                        }

                        ServiceManager.SetStartType(target.Name, (ServiceStartType)snapshot.StartType);
                        Logger.Info("Restored start type for service " + target.Name + ".");
                    }
                    else
                    {
                        restoreFailed = true;
                        serviceFailed = true;
                        Logger.Warning("No persisted state for service " + target.Name + "; defaulting to Manual.");
                        _notifier.ShowWarning("No persisted state was available for service " + target.Name + ". Its start type has been reset to Manual, but access control lists may still block Windows Update.");
                        ServiceManager.SetStartType(target.Name, ServiceStartType.Manual);
                    }

                    if (target.StartWhenEnabling)
                    {
                        try
                        {
                            ServiceManager.StartService(target.Name, TimeSpan.FromSeconds(30));
                            Logger.Info("Started service " + target.Name + " after restoration.");
                        }
                        catch (Exception startEx)
                        {
                            restoreFailed = true;
                            serviceFailed = true;
                            Logger.Error("Failed to start service " + target.Name + " after enabling.", startEx);
                            _notifier.ShowWarning("Failed to start service " + target.Name + ": " + startEx.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    restoreFailed = true;
                    serviceFailed = true;
                    Logger.Error("Failed to restore service " + target.Name + ".", ex);
                    _notifier.ShowWarning("Failed to restore service " + target.Name + ": " + ex.Message);
                }

                if (!serviceFailed && hadSnapshot)
                {
                    restoredServices.Add(target.Name);
                }
            }

            foreach (string service in restoredServices)
            {
                state.Services.Remove(service);
            }

            if (restoreFailed)
            {
                state.UpdatesDisabled = !stateLoaded || state.Services.Count > 0;
                _stateRepository.Save(state);
                Logger.Warning("Enable operation completed with warnings; some services remain unrestored.");
                _notifier.ShowWarning("Some Windows Update services could not be fully restored. Retry enabling once underlying issues are resolved.");
            }
            else
            {
                state.UpdatesDisabled = false;
                state.Services.Clear();
                _stateRepository.Clear();
                Logger.Info("Enable operation completed successfully.");
                _notifier.ShowInfo("Windows Updates have been re-enabled.");
            }
        }

        private bool TryGetStartType(string serviceName, out uint startType)
        {
            try
            {
                startType = ServiceManager.GetStartType(serviceName);
                return true;
            }
            catch (InvalidOperationException ex)
            {
                ReportMissingService(serviceName, ex);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1060)
                {
                    ReportMissingService(serviceName, ex);
                }
                else
                {
                    Logger.Error("Failed to query start type for service " + serviceName + ".", ex);
                }
            }

            startType = 0;
            return false;
        }

        private void ReportMissingService(string serviceName, Exception ex)
        {
            if (_reportedMissingServices.Add(serviceName))
            {
                Logger.Warning("Service " + serviceName + " was not found: " + ex.Message);
                _notifier.ShowWarning("Service " + serviceName + " was not found on this system and will be skipped when evaluating Windows Update state: " + ex.Message);
            }
        }
    }

    internal sealed class ServiceTarget
    {
        public ServiceTarget(string name, bool lockDown, bool stopWhenDisabling, bool startWhenEnabling)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }

            Name = name;
            LockDown = lockDown;
            StopWhenDisabling = stopWhenDisabling;
            StartWhenEnabling = startWhenEnabling;
        }

        public string Name { get; private set; }

        public bool LockDown { get; private set; }

        public bool StopWhenDisabling { get; private set; }

        public bool StartWhenEnabling { get; private set; }
    }
}
