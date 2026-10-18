# Puck & Play

A little care. More play.

A small Windows companion for the Steam Controller puck. It checks the receiver after startup and sleep, watches for live controller reports, and recovers USB faults that Windows can safely restart.

**[Download the latest release](https://github.com/lrnolivia/puck-and-play/releases/latest)** · [Report a problem](https://github.com/lrnolivia/puck-and-play/issues)

## Get started

You need **Windows 11, x64**, Steam, and the Steam Controller receiver identified by `VID_28DE&PID_1304`. The original 2015 receiver and Bluetooth connections are outside this app's scope.

### Install

1. Connect one working puck. Disconnect additional pucks during setup.
2. Download and open **PuckAndPlay-1.2.0-Setup.exe** from Releases.
3. Choose whether to open at sign-in and enable USB recovery.
4. Approve Windows' administrator prompt if you enable recovery. Routine checks then run without prompts.

The app lives in your local Programs folder. Recovery installs a protected helper and two on-demand scheduled tasks. It requires approval from the signed-in administrator account. You can enable it later under **Troubleshooting → Enable recovery**.

Releases are unsigned, so Windows may show an unfamiliar-publisher warning. Download from this repository's Releases page and compare the supplied SHA-256 checksums to verify files.

### Portable

Download the portable ZIP, extract the whole folder, and open **PuckAndPlay.exe**. Keep its `Interface` folder beside it. The application runtimes are included; you don't need the .NET SDK or Blender.

Choose **Troubleshooting → Set up receiver** with one working puck connected. The portable app does not add itself to startup. Enabling elevated recovery makes a persistent installation of the recovery helper, even when the main app is portable.

## Read the connection view

Click the tray icon for three separate observations:

| Row | What it tells you |
| --- | --- |
| **Puck USB** | Whether Windows sees the enrolled receiver and reports a device fault. |
| **Controller** | Whether the receiver delivers live controller reports. This works with its keyboard/mouse interfaces and does not rely on an Xbox-style gamepad appearing. |
| **Steam** | Whether the Steam process is running. |

**Connected** means a current controller report arrived. **Disconnected** means the receiver explicitly reported a disconnect. **Not detected** or **Unknown** means a connection could not be confirmed: the controller may be asleep, no report arrived, or another app holds the interface. It does not prove a disconnection.

Choose **Test inputs** to open Steam's controller settings. Select the controller, then **Test Device Inputs → Begin Test**. Steam runs the input test.

The crystal reflects receiver and care status: green is ready, yellow needs attention, red means the puck is absent, and grey means there is no current observation. Read the Controller row separately. A ready receiver cannot guarantee a game's Steam Input configuration is correct.

The native interface follows Windows themes, high contrast, transparency, and animation settings. Its transparent 3D crystal targets 120 updates per second while visible; actual presentation depends on hardware and display. Rendering sleeps when motion is paused or the state is static. Closing the window stops rendering and leaves care running in the tray. A single graphics device is shared while the interface process is open.

## What automatic care can recover

Setup records your receiver's identity and USB port. That enrollment limits recovery to your puck.

| Situation | Action |
| --- | --- |
| Startup, wake, or session resume | Allow USB and Steam to settle, then check again. |
| Steam is closed | Start Steam with bounded retries, unless care is paused or another supported mapper is active. |
| The enrolled receiver reports Windows error **10, 31, or 43** | Request a device restart and check the result. If necessary, cycle its recorded USB port. |
| An unidentified USB descriptor failure appears, error **10 or 43** | Cycle a port only when hub, port number, and location match the enrolled receiver. |
| A recoverable fault persists | Stop after two attempts for the incident, with a cooldown. Changing error codes does not reset that budget. |
| Firmware update mode is detected | Leave hardware alone and let Steam finish. |
| A fault requires a driver, reboot, or manual action | Explain the next step instead of repeatedly resetting it. |

**Reconnect puck** is a manual troubleshooting action. Close games first; it briefly interrupts input.

The helper does not reset an entire USB hub or host controller, flash firmware, change Steam Input mappings, unpair controllers, or automatically restart Steam. It leaves intentionally disabled devices, blocked drivers, reboot-required states, and unsupported hardware alone.

No helper can guarantee every connection. Bad cables, insufficient power, radio interference, damaged hardware, pairing problems, and Steam or game configuration may require attention. If recovery stops, reconnect the USB cable, try another USB port, and test in Steam. After changing ports, run receiver setup again.

## Everyday controls

Right-click the tray icon for checks, Steam settings, troubleshooting, and **Quit**. **Pause for 30 minutes** keeps observations visible while pausing automatic Steam startup and recovery. Resume early whenever you're ready.

To remove an installed copy, use **Uninstall Puck & Play** in Start. Removing elevated recovery needs administrator approval. Settings and logs are retained. For a portable copy, quit and delete the extracted folder; remove elevated recovery separately if enabled.

## Privacy and diagnostics

There is no telemetry or network service. The app reads local device information, passive controller reports, Steam's local status, and its own settings. It does not save button presses, change controller modes, or collect account credentials. Opening Steam can cause Steam itself to use the network.

**Troubleshooting → Technical details** provides a copyable snapshot. Connection and recovery logs are available there too. Logs rotate to bound disk use. Settings live in `%LOCALAPPDATA%\SteamControllerGuard`; protected enrollment and recovery state live in `%PROGRAMDATA%\SteamControllerGuard`.

Before posting diagnostics, review them for serial numbers, account names, and personal paths. Include the Windows and app versions, whether the failure followed boot or wake, and what the controller light did.

## Build from source

Use Windows 11 x64 with the **.NET 10 SDK**. The build uses Windows' .NET Framework compiler for the monitor and the .NET SDK for WinUI. Dependencies restore from NuGet.

```powershell
.\Build.ps1
.\bin\Guard.Diagnostics.exe --test
.\Build-Release.ps1 -SkipBuild
```

`Build.ps1` runs safety and protocol checks without changing hardware. `Build-Release.ps1` creates a self-contained portable ZIP, a setup executable embedding that payload, and checksums in `dist`. It also extracts the installer separately and verifies every packaged file.

The renderer check exercises repeated creation/disposal, failed initialization, static rendering, and frame timing:

```powershell
.\bin\WinUI\PuckAndPlay.UI.exe --test-renderer artifacts\renderer-checks
```

Enrollment and repair are separate from these tests. Do not run recovery commands against hardware you haven't enrolled.

## License

MIT. Copyright © 2026 Lauren Olivia. See [LICENSE](LICENSE).

An independent utility. Steam and Steam Controller belong to Valve; Windows and Fluent belong to Microsoft. Third-party software and studio-lighting notices are included in the packages; see [NOTICE](NOTICE).
