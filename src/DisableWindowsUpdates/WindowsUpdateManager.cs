using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace DisableWindowsUpdates;

internal sealed class WindowsUpdateManager
{
    private static readonly IReadOnlyList<ServiceTarget> ServiceTargets = new List<ServiceTarget>
    {
        new("wuauserv", lockDown: true, stopWhenDisabling: true, startWhenEnabling: true),
        new("WaaSMedicSvc", lockDown: true, stopWhenDisabling: true, startWhenEnabling: true),
        new("UsoSvc", lockDown: false, stopWhenDisabling: true, startWhenEnabling: true)
    };

    private readonly StateRepository _stateRepository;
    private readonly TrayNotifier _notifier;
    private readonly HashSet<string> _reportedMissingServices = new(StringComparer.OrdinalIgnoreCase);

    public WindowsUpdateManager(StateRepository stateRepository, TrayNotifier notifier)
    {
        _stateRepository = stateRepository;
        _notifier = notifier;
    }

    public WindowsUpdateState GetCurrentState()
    {
        if (_stateRepository.TryLoad(out var state) && state.UpdatesDisabled)
        {
            foreach (var target in ServiceTargets)
            {
                if (!TryGetStartType(target.Name, out var startType))
                {
                    continue;
                }

                if (startType != (uint)ServiceStartType.Disabled)
                {
                    return WindowsUpdateState.Enabled;
                }
            }

            return WindowsUpdateState.Disabled;
        }

        if (TryGetStartType("wuauserv", out var wuauservStartType))
        {
            return wuauservStartType == (uint)ServiceStartType.Disabled
                ? WindowsUpdateState.Disabled
                : WindowsUpdateState.Enabled;
        }

        return WindowsUpdateState.Enabled;
    }

    public void DisableUpdates()
    {
        _notifier.ShowInfo("Disabling Windows Update services...");

        var state = new PersistentState
        {
            UpdatesDisabled = true,
            Services = new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var target in ServiceTargets)
        {
            try
            {
                var startType = ServiceManager.GetStartType(target.Name);
                var descriptor = ServiceManager.GetSecurityDescriptor(target.Name);
                state.Services[target.Name] = new ServiceSnapshot
                {
                    StartType = startType,
                    SecurityDescriptor = Convert.ToBase64String(descriptor)
                };
            }
            catch (Exception ex)
            {
                _notifier.ShowWarning($"Failed to capture state for service {target.Name}: {ex.Message}");
            }
        }

        foreach (var target in ServiceTargets)
        {
            try
            {
                if (target.StopWhenDisabling)
                {
                    ServiceManager.StopService(target.Name, TimeSpan.FromSeconds(30));
                }

                ServiceManager.SetStartType(target.Name, ServiceStartType.Disabled);

                if (target.LockDown)
                {
                    ServiceManager.ApplyLockdown(target.Name);
                }
            }
            catch (Exception ex)
            {
                _notifier.ShowWarning($"Failed to harden service {target.Name}: {ex.Message}");
            }
        }

        _stateRepository.Save(state);
        _notifier.ShowInfo("Windows Updates have been disabled and locked.");
    }

    public void EnableUpdates()
    {
        _notifier.ShowInfo("Restoring Windows Update services...");

        var stateLoaded = _stateRepository.TryLoad(out var state);
        var restoreFailed = !stateLoaded;

        if (!stateLoaded)
        {
            state = new PersistentState();
            _notifier.ShowWarning("No persisted Windows Update state was found. Service permissions may remain locked until the disable operation is run again.");
        }

        state.Services ??= new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase);

        var restoredServices = new List<string>();

        foreach (var target in ServiceTargets)
        {
            var serviceFailed = false;
            var hadSnapshot = state.Services.TryGetValue(target.Name, out var snapshot);

            try
            {
                if (hadSnapshot)
                {
                    if (!string.IsNullOrEmpty(snapshot.SecurityDescriptor))
                    {
                        var descriptor = Convert.FromBase64String(snapshot.SecurityDescriptor);
                        ServiceManager.RestoreSecurityDescriptor(target.Name, descriptor);
                    }

                    ServiceManager.SetStartType(target.Name, (ServiceStartType)snapshot.StartType);
                }
                else
                {
                    restoreFailed = true;
                    serviceFailed = true;
                    _notifier.ShowWarning($"No persisted state was available for service {target.Name}. Its start type has been reset to Manual, but access control lists may still block Windows Update.");
                    ServiceManager.SetStartType(target.Name, ServiceStartType.Manual);
                }

                if (target.StartWhenEnabling)
                {
                    try
                    {
                        ServiceManager.StartService(target.Name, TimeSpan.FromSeconds(30));
                    }
                    catch (Exception startEx)
                    {
                        restoreFailed = true;
                        serviceFailed = true;
                        _notifier.ShowWarning($"Failed to start service {target.Name}: {startEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                restoreFailed = true;
                serviceFailed = true;
                _notifier.ShowWarning($"Failed to restore service {target.Name}: {ex.Message}");
            }

            if (!serviceFailed && hadSnapshot)
            {
                restoredServices.Add(target.Name);
            }
        }

        foreach (var service in restoredServices)
        {
            state.Services.Remove(service);
        }

        if (restoreFailed)
        {
            state.UpdatesDisabled = !stateLoaded || state.Services.Count > 0;
            _stateRepository.Save(state);
            _notifier.ShowWarning("Some Windows Update services could not be fully restored. Retry enabling once underlying issues are resolved.");
        }
        else
        {
            state.UpdatesDisabled = false;
            state.Services.Clear();
            _stateRepository.Clear();
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
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1060)
        {
            ReportMissingService(serviceName, ex);
        }

        startType = default;
        return false;
    }

    private void ReportMissingService(string serviceName, Exception ex)
    {
        if (_reportedMissingServices.Add(serviceName))
        {
            _notifier.ShowWarning($"Service {serviceName} was not found on this system and will be skipped when evaluating Windows Update state: {ex.Message}");
        }
    }

}

internal sealed class ServiceTarget
{
    public ServiceTarget(string name, bool lockDown, bool stopWhenDisabling, bool startWhenEnabling)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        LockDown = lockDown;
        StopWhenDisabling = stopWhenDisabling;
        StartWhenEnabling = startWhenEnabling;
    }

    public string Name { get; }

    public bool LockDown { get; }

    public bool StopWhenDisabling { get; }

    public bool StartWhenEnabling { get; }
}
