# Disable Windows Updates

This repository contains a .NET Framework 4.8.1 Windows application that can disable or re-enable Windows Update with a single execution. The executable requests administrative privileges (through the application manifest) and then toggles the update infrastructure:

* Stops the relevant services (`wuauserv`, `WaaSMedicSvc`, and `UsoSvc`).
* Sets their startup type to `Disabled` (or restores it on re-enable).
* Applies a restrictive ACL to the services so the operating system cannot restart them while updates are disabled.
* Persists the original configuration so it can be restored in a production-safe way.
* Notifies the operator through a tray balloon notification.

## Project layout

```
DisableWindowsUpdates.sln                # Visual Studio solution
src/DisableWindowsUpdates/               # WinForms-based runner (no UI, just tray notifications)
```

## Building

Open the solution in Visual Studio 2022 (or newer) on Windows with .NET Framework 4.8.1 targeting pack installed, then build the `DisableWindowsUpdates` project in `Release` configuration.

## Usage

Run the compiled executable on a Windows host. It will request elevation if required and then toggle the Windows Update state:

* If updates are currently enabled, you will be prompted to optionally create a system restore point before the services are stopped, disabled, locked down, and a notification will confirm success.
* If updates were previously disabled with this tool, running it again restores the saved configuration, re-enables the services, and shows a notification.

State is stored under `%ProgramData%\DisableWindowsUpdates\state.json` so that the original configuration can be restored safely.
