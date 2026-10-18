using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Puck & Play")]
[assembly: AssemblyProduct("Puck & Play")]
[assembly: AssemblyCompany("loew.fi")]
[assembly: AssemblyVersion("2.6.0.0")]

namespace SteamControllerGuard
{
    public static class Files
    {
        public static readonly string UserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamControllerGuard");
        public static readonly string MachineDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteamControllerGuard");
        public static string MachineState
        {
            get
            {
                return Path.Combine(MachineDir, "repair-state.json");
            }
        }
        public static T Read<T>(string path) where T : class
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    if (stream.Length > 1024 * 1024)
                        return null;
                    return new JavaScriptSerializer().Deserialize<T>(reader.ReadToEnd());
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
        public static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temp, path, null);
                else
                    File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Log(string message, bool machine)
        {
            try
            {
                string dir = machine ? MachineDir : UserDir;
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, machine ? "recovery.log" : "guard.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                {
                    if (File.Exists(path + ".1"))
                        File.Delete(path + ".1");
                    File.Move(path, path + ".1");
                }
                File.AppendAllText(path, DateTime.Now.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { /* Disk errors must not crash the input watchdog. */ }
        }
    }
    public static class Runtime
    {
        public const string AutoTask = "Steam Controller Guard - Device Recovery";
        public const string ManualTask = "Steam Controller Guard - Reconnect Puck";
        public const string TrayTask = "Steam Controller Guard";
        public const string ShowStatusSignal = @"Local\loew.fi.SteamControllerGuard.ShowStatus";
        public static readonly string BackendRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam Controller Guard");
        public static bool RecoveryInstalled
        {
            get
            {
                string backend = Path.Combine(BackendRoot, "SteamControllerGuard.exe");
                try
                {
                    return File.Exists(Path.Combine(BackendRoot, "installed.json")) && File.Exists(backend)
                        && File.Exists(ReceiverConfiguration.MachinePath) && ReceiverConfiguration.Current != null
                        && FileVersionInfo.GetVersionInfo(backend).FileVersion == typeof(Runtime).Assembly.GetName().Version.ToString();
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }
        public static bool Elevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        public static string SteamRoot
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    return key == null ? "" : Convert.ToString(key.GetValue("SteamPath", "")).Replace('/', Path.DirectorySeparatorChar);
            }
        }
        public static bool Running(string name)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                return processes.Length > 0;
            }
            finally { foreach (var p in processes) p.Dispose(); }
        }
        public static bool OtherMapper
        {
            get
            {
                return Running("SteamlessController") || Running("sisr");
            }
        }
        public static bool GameRunning()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key != null && Convert.ToInt64(key.GetValue("RunningAppID", 0)) != 0)
                    return true;
            }
            return Running("GameOverlayUI");
        }
        public static string Run(string file, string arguments, int seconds)
        {
            var start = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(start))
            {
                var output = p.StandardOutput.ReadToEndAsync();
                var error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(seconds * 1000))
                {
                    // A device operation may be pending in the kernel; do not stack another operation over it.
                    throw new TimeoutException(Path.GetFileName(file) + " timed out; no further recovery attempted.");
                }
                if (!System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, 2000))
                    throw new TimeoutException(Path.GetFileName(file) + " did not close its output streams; recovery stopped.");
                string text = (output.Result + " " + error.Result).Trim();
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(Path.GetFileName(file) + " exit " + p.ExitCode + ": " + text);
                return text;
            }
        }
        public static string SystemExe(string name)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
        }
        public static bool Healthy(List<UsbDevice> devices)
        {
            return devices.Any(d => Scope.IsPuck(d) && d.Problem == 0);
        }
        public static UsbDevice History()
        {
            var profile = ReceiverConfiguration.Current;
            return profile == null ? null : profile.AsDevice();
        }
        public static string Fingerprint(UsbDevice d)
        {
            return Scope.PuckId.ToUpperInvariant() + "|" + (d.Parent ?? "").ToUpperInvariant() + "|" + d.Port;
        }
        public static void Enroll(string deviceId, bool machine)
        {
            if (machine && !Elevated)
                throw new UnauthorizedAccessException("Administrator approval is required to enable USB recovery.");
            var candidates = UsbDevices.Enumerate(true).Where(d => d.Present && d.Problem == 0 && ReceiverProfile.IsSupportedId(d.Id)).ToList();
            if (!String.IsNullOrEmpty(deviceId))
                candidates = candidates.Where(d => Scope.Same(d.Id, deviceId)).ToList();
            if (candidates.Count != 1)
                throw new InvalidOperationException(candidates.Count == 0
                ? "Connect a working Steam Controller puck, then choose Set up receiver."
                : "More than one puck is connected. Disconnect the others while setting up this receiver.");
            ReceiverConfiguration.Save(machine ? ReceiverConfiguration.MachinePath : ReceiverConfiguration.UserPath, ReceiverProfile.FromDevice(candidates[0]));
        }
        public static string LatestControllerEvent(string root)
        {
            string path = Path.Combine(root, "logs", "controller.txt");
            if (!File.Exists(path))
                return "No controller log yet.";
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(Math.Max(0, stream.Length - 131072), SeekOrigin.Begin);
                using (var reader = new StreamReader(stream))
                {
                    string[] lines = reader.ReadToEnd().Split('\n');
                    return lines.LastOrDefault(x => x.Contains("SetLocalControllerConnectionState") || x.Contains("Controller Connected to Dongle"))
                        ?? "No recent controller connection event. A ready puck does not prove the controller is connected.";
                }
            }
        }
    }
    public static class Recovery
    {
        static bool Verify(int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                Thread.Sleep(1000);
                var devices = UsbDevices.Enumerate(true);
                if (devices.Any(Scope.IsFirmware))
                    return false;
                if (Runtime.Healthy(devices))
                    return true;
            }
            return false;
        }
        public static int Run(bool manual)
        {
            if (!Runtime.Elevated)
                return 5;
            if (!File.Exists(ReceiverConfiguration.MachinePath))
            {
                Files.Log("No protected receiver enrollment; recovery was not attempted.", true);
                return 5;
            }
            ReceiverConfiguration.Reload();
            if (ReceiverConfiguration.Current == null)
            {
                Files.Log("Protected receiver enrollment is invalid; recovery was not attempted.", true);
                return 5;
            }
            // Installed tasks never accept a device ID, file path, command or arbitrary arguments from the caller.
            bool acquired;
            using (var mutex = new Mutex(true, @"Global\loew.fi.SteamControllerGuard.Recovery", out acquired))
            {
                if (!acquired)
                    return 0;
                var state = Files.Read<RepairState>(Files.MachineState) ?? new RepairState();
                try
                {
                    if (File.Exists(Files.MachineState) && (Files.Read<RepairState>(Files.MachineState) == null || !state.IsValid()))
                    {
                        Files.Log("Recovery state is unreadable; no recovery attempted. Repair the installation before retrying.", true);
                        return 2;
                    }
                    var devices = UsbDevices.Enumerate(true);
                    if (devices.Any(Scope.IsFirmware))
                    {
                        Files.Log("Firmware bootloader detected; recovery deferred.", true);
                        return 0;
                    }
                    UsbDevice history = Runtime.History();
                    var identified = devices.FirstOrDefault(Scope.IsPuck);
                    if (identified != null && identified.Problem == 0)
                        ReceiverConfiguration.Save(ReceiverConfiguration.MachinePath, ReceiverProfile.FromDevice(identified));
                    UsbDevice failure = Scope.SelectFailure(devices, history);
                    if (failure == null && Runtime.Healthy(devices) && !manual)
                    {
                        state.Fingerprint = null;
                        state.Attempts = 0;
                        state.Outcome = "ReceiverHealthy";
                        state.Detail = "Windows enumerates the puck with problem code 0. Controller input not tested.";
                        Files.Save(Files.MachineState, state);
                        return 0;
                    }
                    if (failure == null && manual)
                    {
                        if (Runtime.GameRunning() || Runtime.OtherMapper)
                            throw new InvalidOperationException("Reconnect deferred: close the game or other controller mapper first.");
                        if ((DateTime.UtcNow - state.LastManualUtc).TotalSeconds < 60)
                            return 0;
                        var puck = devices.FirstOrDefault(Scope.IsPuck);
                        if (puck == null)
                            throw new InvalidOperationException("Puck is absent; no identifiable device can be reconnected.");
                        if (puck.Problem != 0)
                            throw new InvalidOperationException(DeviceFault.Advice(puck.Problem));
                        state.LastManualUtc = DateTime.UtcNow;
                        state.Outcome = "Reconnecting";
                        Files.Save(Files.MachineState, state);
                        // Explicit tray action only. Automatic recovery never resets a healthy idle receiver.
                        devices = UsbDevices.Enumerate(true);
                        if (devices.Any(Scope.IsFirmware) || !Runtime.Healthy(devices))
                            return 0;
                        Files.Log("User requested reconnect of " + puck.Id, true);
                        Runtime.Run(Runtime.SystemExe("pnputil.exe"), "/restart-device \"" + puck.Id + "\"", 25);
                        bool ready = Verify(15);
                        state.Outcome = ready ? "ReceiverHealthy" : "NeedsAttention";
                        state.Detail = ready ? "Puck re-enumerated successfully. Controller input still requires observation." : "Reconnect did not restore the puck.";
                        Files.Save(Files.MachineState, state);
                        return ready ? 0 : 1;
                    }
                    if (failure == null)
                        return 0; // Unplugged or on an unrecognized port: do not reset any hub.
                    string fingerprint = Runtime.Fingerprint(failure);
                    if (!Scope.Same(state.Fingerprint, fingerprint))
                    {
                        state.Fingerprint = fingerprint;
                        state.Attempts = 0;
                    }
                    if (state.Attempts >= 2)
                    {
                        Files.Log("Retry budget exhausted for " + fingerprint, true);
                        return 2;
                    }
                    if ((DateTime.UtcNow - state.LastAttemptUtc).TotalSeconds < 30)
                        return 0;
                    state.Attempts++;
                    state.LastAttemptUtc = DateTime.UtcNow;
                    state.Outcome = "Repairing";
                    state.Detail = "Target " + failure.Id + ", problem " + failure.Problem + ", stage " + state.Attempts;
                    Files.Save(Files.MachineState, state);
                    Files.Log(state.Detail, true);
                    // Check identity and firmware mode again at the last point before mutation.
                    var fresh = UsbDevices.Enumerate(true);
                    var current = Scope.SelectFailure(fresh, Runtime.History());
                    if (current == null || !Scope.Same(current.Id, failure.Id) || !Scope.Same(current.Parent, failure.Parent) || current.Port != failure.Port)
                        throw new InvalidOperationException("Device state changed; recovery cancelled.");
                    if (state.Attempts == 1)
                    {
                        string output = Runtime.Run(Runtime.SystemExe("pnputil.exe"), "/restart-device \"" + current.Id + "\"", 25);
                        Files.Log("Targeted device restart: " + output, true);
                    }
                    else
                    {
                        UsbDevices.CycleFailedPort(current, history);
                        Files.Log("Cycled only port " + current.Port + " on " + current.Parent, true);
                    }
                    bool restored = Verify(15);
                    state.Outcome = restored ? "ReceiverHealthy" : state.Attempts >= 2 ? "NeedsAttention" : "FirstAttemptUnsuccessful";
                    state.Detail = restored ? "Puck enumerated as " + Scope.PuckId + " with problem code 0."
                        : "Puck not restored. " + (state.Attempts >= 2 ? "Automatic recovery stopped; unplug/replug the puck or try another cable/port." : "One bounded port-cycle attempt remains.");
                    if (restored)
                    {
                        state.Fingerprint = null;
                        state.Attempts = 0;
                    }
                    Files.Save(Files.MachineState, state);
                    Files.Log(state.Outcome + ": " + state.Detail, true);
                    return restored ? 0 : 1;
                }
                catch (Exception e)
                {
                    state.Outcome = "NeedsAttention";
                    state.Detail = e.Message;
                    if (e is TimeoutException)
                        state.Attempts = 2;
                    Files.Save(Files.MachineState, state);
                    Files.Log(e.ToString(), true);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    sealed partial class GuardContext : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        ToolStripMenuItem statusItem;
        ToolStripMenuItem pauseItem;
        readonly Control dispatcher;
        readonly Icon appIcon;
        readonly Dictionary<ConnectionState, Icon> statusIcons = new Dictionary<ConnectionState, Icon>();
        readonly EventWaitHandle showStatusSignal;
        readonly RegisteredWaitHandle showStatusWait;
        readonly UiBridge uiBridge;
        DateTime pausedUntil;
        DateTime nextCheck = DateTime.UtcNow.AddSeconds(10);
        DateTime nextRequest = DateTime.MinValue;
        DateTime nextSteamStart = DateTime.MinValue;
        DateTime steamHealthySince = DateTime.MinValue;
        DateTime nextErrorNotice = DateTime.MinValue;
        int steamAttempts;
        int requestFailures;
        int checking;
        int requesting;
        bool exiting;
        string lastStatus = "Starting...";
        string lastFingerprint = "";
        Snapshot latest;
        StatusWindow statusWindow;
        DetailsWindow detailsWindow;
        AboutWindow aboutWindow;
        public GuardContext(bool showStatus)
        {
            Directory.CreateDirectory(Files.UserDir);
            dispatcher = new Control();
            IntPtr dispatcherHandle = dispatcher.Handle;
            DateTime.TryParse(ReadPreference("pause-until.txt"), out pausedUntil);
            appIcon = Brand.LoadIcon();
            foreach (ConnectionState state in Enum.GetValues(typeof(ConnectionState)))
                statusIcons[state] = Brand.LoadIcon(state);
            menu = CreateMenu();
            menu.Opening += delegate
            {
                RefreshPauseItem();
            };
            tray = new NotifyIcon { Icon = appIcon, Text = Brand.Name, Visible = true, ContextMenuStrip = UiHost.Available ? null : menu };
            tray.DoubleClick += delegate
            {
                ShowStatus();
            };
            tray.MouseClick += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    ShowStatus();
                if (e.Button == MouseButtons.Right && UiHost.Available && !UiHost.TryOpen("menu " + Cursor.Position.X + " " + Cursor.Position.Y))
                    menu.Show(Cursor.Position);
            };
            showStatusSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Runtime.ShowStatusSignal);
            showStatusWait = ThreadPool.RegisterWaitForSingleObject(showStatusSignal, delegate
            {
                menuInvoke(delegate
                {
                    ShowStatus();
                });
            }, null, Timeout.Infinite, false);
            uiBridge = new UiBridge(delegate (string command)
            {
                menuInvoke(delegate
                {
                    HandleUiCommand(command);
                });
            });
            timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += Tick;
            timer.Start();
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.SessionSwitch += SessionChanged;
            Files.Log(Brand.Name + " " + Brand.Version + " started; monitoring only " + Scope.PuckId, false);
            if (showStatus)
                ShowStatus();
        }
        string ReadPreference(string name)
        {
            string path = Path.Combine(Files.UserDir, name);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        void Pause()
        {
            pausedUntil = pausedUntil > DateTime.UtcNow ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(30);
            File.WriteAllText(Path.Combine(Files.UserDir, "pause-until.txt"), pausedUntil.ToString("o"));
            RefreshPauseItem();
            if (statusWindow != null && !statusWindow.IsDisposed)
                statusWindow.UpdateState(latest, pausedUntil, Interlocked.CompareExchange(ref checking, 0, 0) != 0);
            nextCheck = DateTime.MinValue;
        }
        void RefreshPauseItem()
        {
            bool paused = pausedUntil > DateTime.UtcNow;
            string label = paused ? "Resume automatic care" : "Pause for 30 minutes";
            if (pauseItem.Text == label)
                return;
            pauseItem.Text = label;
            var previous = pauseItem.Image;
            pauseItem.Image = Brand.MenuGlyph(paused ? "play" : "pause", SystemColors.MenuText);
            if (previous != null)
                previous.Dispose();
        }
        void Open(string path)
        {
            try
            {
                using (var launched = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
                { }
            }
            catch (Exception e) { Notice(e.Message); }
        }
        void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume)
                return;
            Files.Log("Windows resumed; device check scheduled after USB settles.", false);
            nextCheck = DateTime.UtcNow.AddSeconds(8);
        }
        void SessionChanged(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
                nextCheck = DateTime.UtcNow.AddSeconds(5);
        }
        void Notice(string text)
        {
            if (DateTime.UtcNow < nextErrorNotice)
                return;
            nextErrorNotice = DateTime.UtcNow.AddMinutes(10);
            menuInvoke(delegate
            {
                tray.ShowBalloonTip(8000, Brand.Name, text, ToolTipIcon.Warning);
            });
        }
        void Request(bool manual)
        {
            if (Interlocked.CompareExchange(ref requesting, 1, 0) != 0)
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    RequestCore(manual);
                }
                finally { Interlocked.Exchange(ref requesting, 0); }
            });
        }
        void RequestCore(bool manual)
        {
            if (manual)
                requestFailures = 0;
            if (!manual && requestFailures >= 2)
                return;
            if (pausedUntil > DateTime.UtcNow)
            {
                Notice("The helper is paused. Resume it from the tray menu first.");
                return;
            }
            if (manual && (Runtime.GameRunning() || Runtime.OtherMapper))
            {
                Notice("Close the game or other controller mapper before reconnecting a healthy puck.");
                return;
            }
            if (!Runtime.RecoveryInstalled)
            {
                requestFailures++;
                Notice("Enable USB recovery in Troubleshooting. Windows requires administrator approval for this setup step.");
                return;
            }
            try
            {
                Runtime.Run(Runtime.SystemExe("schtasks.exe"), "/Run /TN \"" + (manual ? Runtime.ManualTask : Runtime.AutoTask) + "\"", 5);
                requestFailures = 0;
                Files.Log("Requested " + (manual ? "manual reconnect" : "device-error recovery"), false);
            }
            catch (Exception e) { requestFailures++; Files.Log("Recovery request failed: " + e.Message, false); Notice(e.Message); }
        }
        void Tick(object sender, EventArgs args)
        {
            UpdateTrayIcon();
            if (DateTime.UtcNow < nextCheck || Interlocked.Exchange(ref checking, 1) != 0)
                return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var devices = UsbDevices.Enumerate(true);
                    ReceiverConfiguration.Reload();
                    bool healthy = Runtime.Healthy(devices);
                    var history = Runtime.History();
                    var failure = Scope.SelectFailure(devices, history);
                    bool firmware = devices.Any(Scope.IsFirmware);
                    bool paused = pausedUntil > DateTime.UtcNow;
                    bool other = Runtime.OtherMapper;
                    bool steam = Runtime.Running("steam");
                    var repair = Files.Read<RepairState>(Files.MachineState);
                    string status;
                    if (paused)
                        status = "Paused until " + pausedUntil.ToLocalTime().ToShortTimeString();
                    else if (firmware)
                        status = "Firmware update mode: waiting";
                    else if (ReceiverConfiguration.Current == null)
                        status = "Receiver setup needed";
                    else if (failure != null)
                        status = "Puck USB error " + failure.Problem;
                    else if (devices.Any(d => Scope.IsPuck(d) && d.Problem != 0))
                        status = "Puck USB error: " + DeviceFault.Advice(devices.First(Scope.IsPuck).Problem);
                    else if (!healthy)
                        status = "Puck absent: waiting for USB connection";
                    else if (other && !steam)
                        status = "Puck ready; another mapper is running";
                    else if (!steam)
                        status = "Puck ready; starting Steam";
                    else
                        status = "Puck ready; Steam running";
                    if (failure != null && !paused && !Runtime.RecoveryInstalled)
                        status = "Puck USB error: repair setup needs approval";
                    if (!paused && !firmware && failure != null)
                    {
                        string fp = Runtime.Fingerprint(failure);
                        bool exhausted = repair != null && Scope.Same(repair.Fingerprint, fp) && repair.Attempts >= 2;
                        if (exhausted)
                            status = "Puck USB error: unplug/replug needed";
                        if (!exhausted && DateTime.UtcNow >= nextRequest)
                        {
                            nextRequest = DateTime.UtcNow.AddSeconds(45);
                            Request(false);
                        }
                    }
                    if (!paused && !firmware && healthy && repair != null && repair.Fingerprint != null && DateTime.UtcNow >= nextRequest)
                    {
                        nextRequest = DateTime.UtcNow.AddSeconds(45);
                        Request(false);
                    }
                    if (!paused && !firmware && (healthy || failure != null) && !other && !steam && DateTime.UtcNow >= nextSteamStart)
                    {
                        string exe = Path.Combine(Runtime.SteamRoot, "steam.exe");
                        if (File.Exists(exe) && steamAttempts < 2)
                        {
                            steamAttempts++;
                            nextSteamStart = DateTime.UtcNow.AddMinutes(2);
                            using (var launched = Process.Start(new ProcessStartInfo(exe, "-silent") { UseShellExecute = true, WorkingDirectory = Runtime.SteamRoot }))
                            { }
                            Files.Log("Started Steam silently; attempt " + steamAttempts, false);
                        }
                        else if (steamAttempts >= 2)
                            status = "Steam did not stay open: attention needed";
                    }
                    if (steam)
                    {
                        if (steamHealthySince == DateTime.MinValue)
                            steamHealthySince = DateTime.UtcNow;
                        if ((DateTime.UtcNow - steamHealthySince).TotalSeconds >= 60)
                            steamAttempts = 0;
                    }
                    else
                        steamHealthySince = DateTime.MinValue;
                    string fingerprint = String.Join(";", devices.Select(d => d.Id + ":" + d.Problem));
                    if (lastFingerprint != fingerprint)
                    {
                        Files.Log("Device transition: " + fingerprint, false);
                        lastFingerprint = fingerprint;
                    }
                    latest = new Snapshot
                    {
                        At = DateTime.Now.ToString("o"),
                        Version = Brand.Version,
                        Status = status,
                        SteamRunning = steam,
                        SteamPath = Runtime.SteamRoot,
                        OtherMapperRunning = other,
                        Devices = devices,
                        Repair = repair,
                        LastControllerEvent = Runtime.LatestControllerEvent(Runtime.SteamRoot),
                        PausedUntil = pausedUntil,
                        RepairService = Runtime.RecoveryInstalled ? "Installed" : "Not installed",
                        Controller = ControllerConnection.Read(devices)
                    };
                    Files.Save(Path.Combine(Files.UserDir, "status.json"), latest);
                    if (!exiting)
                        menuInvoke(delegate
                    {
                        if (lastStatus != status)
                        {
                            Files.Log("Status: " + status, false);
                            lastStatus = status;
                            if (status.Contains("unplug/replug"))
                                Notice("Windows could not restore the puck after two attempts. Unplug/replug its USB cable; try another cable or port if this returns.");
                        }
                        statusItem.Text = status;
                        string tooltip = Brand.Name + ": " + status;
                        tray.Text = tooltip.Substring(0, Math.Min(63, tooltip.Length));
                        UpdateTrayIcon();
                        RefreshPauseItem();
                        if (statusWindow != null && !statusWindow.IsDisposed)
                            statusWindow.UpdateState(latest, pausedUntil, false);
                        if (detailsWindow != null && !detailsWindow.IsDisposed)
                            detailsWindow.UpdateDetails(Describe());
                    });
                }
                catch (Exception e)
                {
                    Files.Log("Check failed: " + e.ToString(), false);
                    menuInvoke(delegate
                    {
                        if (statusWindow != null && !statusWindow.IsDisposed)
                            statusWindow.CheckFailed();
                    });
                }
                finally { Interlocked.Exchange(ref checking, 0); }
            });
        }
        void menuInvoke(MethodInvoker action)
        {
            if (exiting || dispatcher.IsDisposed)
                return;
            try
            {
                dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
        }
        void UpdateTrayIcon()
        {
            var view = ConnectionView.Create(latest, pausedUntil, DateTime.Now);
            Icon next = statusIcons[view.State];
            if (!Object.ReferenceEquals(tray.Icon, next))
                tray.Icon = next;
        }
        string Describe()
        {
            if (latest == null)
                return "Checking USB devices...";
            var b = new StringBuilder();
            b.AppendLine(latest.Status).AppendLine("Checked: " + latest.At).AppendLine();
            b.AppendLine("Monitored puck: " + Scope.PuckId);
            b.AppendLine("Steam: " + (latest.SteamRunning ? "running" : "closed") + " | USB recovery: " + latest.RepairService);
            b.AppendLine("Another mapper running: " + latest.OtherMapperRunning).AppendLine();
            foreach (var d in latest.Devices)
                b.AppendLine(d.Name + " | Error " + d.Problem + "\r\n" + d.Id + "\r\n" + d.LocationInfo + "\r\n");
            if (latest.Repair != null)
                b.AppendLine("Recovery: " + latest.Repair.Outcome + "\r\n" + latest.Repair.Detail).AppendLine();
            b.AppendLine("Latest Steam controller event (may be historical):").AppendLine(latest.LastControllerEvent).AppendLine();
            var controller = ControllerConnection.Display(latest.Controller, DateTime.Now);
            b.AppendLine("Controller: " + controller.Status).AppendLine(controller.Detail);
            b.AppendLine("Test inputs in Steam Settings > Controller > Test Device Inputs.");
            b.AppendLine("Automatic recovery acts only on actual USB errors and stops after two attempts. Firmware bootloader mode is protected.");
            b.AppendLine("Quit or pause this helper before intentionally keeping Steam closed.");
            b.AppendLine("Logs: " + Files.UserDir + "\r\nRecovery logs: " + Files.MachineDir);
            return b.ToString();
        }
        void ShowStatus()
        {
            if (UiHost.TryOpen("status"))
                return;
            if (statusWindow != null && !statusWindow.IsDisposed)
            {
                if (statusWindow.WindowState == FormWindowState.Minimized)
                    statusWindow.WindowState = FormWindowState.Normal;
                statusWindow.Show();
                statusWindow.Activate();
                return;
            }
            statusWindow = new StatusWindow(new MenuActions
            {
                Check = CheckNow,
                Steam = delegate { Open("steam://settings/controller"); },
                Reconnect = delegate { Request(true); nextCheck = DateTime.UtcNow.AddSeconds(2); },
                Technical = ShowTechnical,
                About = ShowAbout
            }, null);
            statusWindow.UpdateState(latest, pausedUntil, Interlocked.CompareExchange(ref checking, 0, 0) != 0);
            statusWindow.Show();
            CheckNow();
        }
        void CheckNow()
        {
            if (Interlocked.CompareExchange(ref checking, 0, 0) != 0)
                return;
            if (statusWindow != null && !statusWindow.IsDisposed)
                statusWindow.SetChecking(true);
            nextCheck = DateTime.MinValue;
            Tick(null, EventArgs.Empty);
        }
        void ShowReconnect()
        {
            if (UiHost.TryOpen("reconnect"))
                return;
            ShowStatus();
            statusWindow.ExpandTroubleshooting(true);
        }
        void ShowTechnical()
        {
            if (UiHost.TryOpen("details"))
                return;
            if (detailsWindow != null && !detailsWindow.IsDisposed)
            {
                if (detailsWindow.WindowState == FormWindowState.Minimized)
                    detailsWindow.WindowState = FormWindowState.Normal;
                detailsWindow.Show();
                detailsWindow.Activate();
                return;
            }
            detailsWindow = new DetailsWindow(Describe());
            detailsWindow.Show();
        }
        void ShowAbout()
        {
            if (UiHost.TryOpen("about"))
                return;
            if (aboutWindow != null && !aboutWindow.IsDisposed)
            {
                aboutWindow.Show();
                aboutWindow.Activate();
                return;
            }
            aboutWindow = new AboutWindow(null);
            aboutWindow.Show();
        }
        protected override void ExitThreadCore()
        {
            exiting = true;
            uiBridge.Dispose();
            timer.Stop();
            timer.Dispose();
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.SessionSwitch -= SessionChanged;
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            appIcon.Dispose();
            foreach (var icon in statusIcons.Values)
                icon.Dispose();
            showStatusWait.Unregister(null);
            showStatusSignal.Dispose();
            dispatcher.Dispose();
            if (statusWindow != null)
                statusWindow.Dispose();
            if (detailsWindow != null)
                detailsWindow.Dispose();
            if (aboutWindow != null)
                aboutWindow.Dispose();
            Files.Log("Stopped by user.", false);
            base.ExitThreadCore();
        }
        void HandleUiCommand(string command)
        {
            if (command == "check")
                CheckNow();
            else if (command == "pause" && pausedUntil <= DateTime.UtcNow)
                Pause();
            else if (command == "resume" && pausedUntil > DateTime.UtcNow)
                Pause();
            else if (command == "reconnect")
            {
                Request(true);
                nextCheck = DateTime.UtcNow.AddSeconds(2);
            }
            else if (command == "steam")
                Open("steam://settings/controller");
            else if (command == "open-steam")
            {
                string steam = Path.Combine(Runtime.SteamRoot, "steam.exe");
                if (!Path.IsPathRooted(steam) || !File.Exists(steam))
                    Notice("Steam was not found. Install Steam, then open it once before using this action.");
                else
                    Open(steam);
            }
            else if (command == "test-inputs")
                Open("steam://settings/controller");
            else if (command == "enroll")
            {
                if (File.Exists(ReceiverConfiguration.MachinePath))
                {
                    Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Enable recovery.cmd"));
                    return;
                }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Runtime.Enroll(null, false);
                        nextCheck = DateTime.MinValue;
                    }
                    catch (Exception error) { Notice(error.Message); }
                });
            }
            else if (command == "setup-recovery")
                Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Enable recovery.cmd"));
            else if (command == "logs")
                Open(Files.UserDir);
            else if (command == "recovery-logs")
                Open(Files.MachineDir);
            else if (command == "quit")
                ExitThread();
        }
    }
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args.Contains("--snapshot"))
                {
                    var devices = UsbDevices.Enumerate(true);
                    Console.WriteLine(new JavaScriptSerializer().Serialize(new
                    {
                        Present = devices,
                        History = Runtime.History(),
                        SteamRunning = Runtime.Running("steam"),
                        SteamRoot = Runtime.SteamRoot,
                        Controller = ControllerConnection.Read(devices)
                    }));
                    return 0;
                }
                if (args.Contains("--test"))
                    return Tests.Run();
                if (args.Contains("--validate-state"))
                {
                    var state = Files.Read<RepairState>(Files.MachineState);
                    return !File.Exists(Files.MachineState) || (state != null && state.IsValid()) ? 0 : 2;
                }
                if (args.Contains("--test-controller"))
                    return Tests.ControllerLifecycle();
                if (args.Contains("--probe-controller"))
                {
                    foreach (string result in ControllerConnection.Probe())
                        Console.WriteLine(result);
                    return 0;
                }
                if (args.Contains("--enroll") || args.Contains("--enroll-machine"))
                {
                    bool machine = args.Contains("--enroll-machine");
                    int index = Array.IndexOf(args, machine ? "--enroll-machine" : "--enroll");
                    Runtime.Enroll(index + 1 < args.Length ? args[index + 1] : null, machine);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--preview-menu")
                {
                    Application.EnableVisualStyles();
                    MenuBuilder.Preview(args[1]);
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--preview-ui")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    UiPreview.Render(args[1]);
                    return 0;
                }
                if (args.Contains("--repair"))
                    return Recovery.Run(false);
                if (args.Contains("--reconnect"))
                    return Recovery.Run(true);
                bool first;
                using (var mutex = new Mutex(true, @"Local\loew.fi.SteamControllerGuard.Tray", out first))
                {
                    if (!first)
                    {
                        if (args.Contains("--show-status"))
                        {
                            try
                            {
                                using (var signal = EventWaitHandle.OpenExisting(Runtime.ShowStatusSignal))
                                    signal.Set();
                            }
                            catch (WaitHandleCannotBeOpenedException) { }
                        }
                        return 0;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new GuardContext(!args.Contains("--tray")));
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception e) { Files.Log(e.ToString(), Runtime.Elevated); return 1; }
        }
    }
}
