using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SteamControllerGuard
{
    enum ConnectionState
    {
        Unknown, Ready, Attention, Disconnected
    }

    sealed class ConnectionView
    {
        public string Title, Description, Puck, Steam, Care, Checked;
        public bool NeedsAttention;
        public ConnectionState State = ConnectionState.Unknown;
        public static ConnectionView Create(Snapshot snapshot, DateTime pausedUntil, DateTime now)
        {
            bool paused = pausedUntil > now.ToUniversalTime();
            var view = new ConnectionView
            {
                Title = "Checking your puck...",
                Description = "Reading USB and Steam status.",
                Puck = "Checking...",
                Steam = "Checking...",
                Care = paused ? "Paused until " + pausedUntil.ToLocalTime().ToShortTimeString() : "On",
                Checked = "Waiting for the first check"
            };
            if (snapshot == null)
                return view;
            var devices = snapshot.Devices ?? new List<UsbDevice>();
            bool healthy = devices.Any(d => Scope.IsPuck(d) && d.Present && d.Problem == 0);
            bool firmware = devices.Any(Scope.IsFirmware);
            bool failed = devices.Any(d => Scope.IsPuck(d) && d.Problem != 0) || Scope.SelectFailure(devices, null) != null || (snapshot.Status ?? "").StartsWith("Puck USB error", StringComparison.Ordinal);
            view.State = !healthy && !failed && !firmware ? ConnectionState.Disconnected
                : healthy && snapshot.SteamRunning && !paused && snapshot.RepairService == "Installed" && !firmware && !failed ? ConnectionState.Ready
                : ConnectionState.Attention;
            view.Puck = firmware ? "Updating firmware" : healthy ? "Ready" : failed ? "USB fault" : "Not found";
            view.Steam = snapshot.SteamRunning ? "Running" : snapshot.OtherMapperRunning ? "Other mapper active" : "Closed";
            view.Care = paused ? view.Care : snapshot.RepairService == "Installed" ? "On" : "Recovery setup needed";
            view.Title = firmware ? "Firmware update in progress" : failed ? "Puck needs attention" : !healthy ? "Puck isn't connected" : "Puck ready";
            view.Description = firmware ? "Let Steam finish the firmware update."
                : failed ? ((snapshot.Status ?? "").Contains("unplug/replug") ? "Reconnect the puck's USB cable." : "Windows reported a puck USB fault.")
                : !healthy ? "Connect the puck's USB cable."
                : snapshot.SteamRunning ? "USB receiver ready. Steam is running."
                : snapshot.OtherMapperRunning ? "Another controller mapper is running."
                : "USB receiver ready. Steam is closed.";
            if (paused)
            {
                view.Title = "Automatic care paused";
                view.Description = failed ? "Resume automatic care to allow USB recovery." : "Checks continue. Startup and recovery are paused.";
            }
            DateTime checkedAt;
            if (DateTime.TryParse(snapshot.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out checkedAt))
            {
                view.Checked = "Last checked at " + checkedAt.ToLocalTime().ToLongTimeString();
                if ((now.ToUniversalTime() - checkedAt.ToUniversalTime()).TotalSeconds > 30)
                {
                    view.Title = "Status needs a refresh";
                    view.Description = "The last check is out of date. Choose Check now to try again.";
                    view.Checked += " (out of date)";
                    view.NeedsAttention = true;
                    view.State = ConnectionState.Unknown;
                }
            }
            else
            {
                view.Title = "Status needs a refresh";
                view.Description = "The last check time isn't available. Choose Check now to try again.";
                view.NeedsAttention = true;
                view.State = ConnectionState.Unknown;
            }
            view.NeedsAttention = view.NeedsAttention || failed || !healthy;
            if (ReceiverConfiguration.Current == null)
            {
                view.Title = "Set up your receiver";
                view.Description = "Connect one puck, then choose Set up receiver.";
                view.Puck = "Not set up";
                view.State = ConnectionState.Unknown;
            }
            return view;
        }
    }

}
