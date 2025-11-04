using System;
using System.Collections.Generic;

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
                if (ServiceManager.GetStartType(target.Name) != (uint)ServiceStartType.Disabled)
                {
                    return WindowsUpdateState.Enabled;
                }
            }

            return WindowsUpdateState.Disabled;
        }

        return ServiceManager.GetStartType("wuauserv") == (uint)ServiceStartType.Disabled
            ? WindowsUpdateState.Disabled
            : WindowsUpdateState.Enabled;
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

        if (!_stateRepository.TryLoad(out var state))
        {
            state = new PersistentState();
        }

        state.Services ??= new Dictionary<string, ServiceSnapshot>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in ServiceTargets)
        {
            try
            {
                if (state.Services.TryGetValue(target.Name, out var snapshot))
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
                        _notifier.ShowWarning($"Failed to start service {target.Name}: {startEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _notifier.ShowWarning($"Failed to restore service {target.Name}: {ex.Message}");
            }
        }

        state.UpdatesDisabled = false;
        state.Services.Clear();
        _stateRepository.Clear();

        _notifier.ShowInfo("Windows Updates have been re-enabled.");
    }
}

internal sealed record ServiceTarget(string Name, bool LockDown, bool StopWhenDisabling, bool StartWhenEnabling);
