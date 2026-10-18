using System;
using System.Collections.Generic;

namespace SteamControllerGuard
{
    static class Tests
    {
        static int checks;
        internal static int ControllerLifecycle()
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                int baselineHandles = 0;
                long baselineBytes = 0;
                ControllerObservation observation = null;
                var devices = UsbDevices.Enumerate(true);
                for (int cycle = 0; cycle < 32; cycle++)
                {
                    observation = ControllerConnection.Read(devices);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    System.Threading.Thread.Sleep(25);
                    process.Refresh();
                    if (cycle == 7)
                    { baselineHandles = process.HandleCount; baselineBytes = process.PrivateMemorySize64; }
                }
                int handles = process.HandleCount - baselineHandles;
                long bytes = process.PrivateMemorySize64 - baselineBytes;
                Console.WriteLine(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { Cycles = 32, HandleGrowth = handles, PrivateBytesGrowth = bytes, Controller = observation.Status, HardwareActions = 0 }));
                if (handles > 12 || bytes > 8 * 1024 * 1024)
                    throw new InvalidOperationException("Controller observation resources did not settle.");
                return 0;
            }
        }
        const string TestParent = @"USB\ROOT_HUB30\TEST";
        const string TestLocation = "TEST_LOCATION_PORT_2";
        static void Check(bool pass, string name)
        {
            if (!pass)
                throw new Exception("FAIL: " + name);
            checks++;
            Console.WriteLine("PASS: " + name);
        }
        static UsbDevice Bad()
        {
            return new UsbDevice
            {
                Id = @"USB\VID_0000&PID_0002\6&20BBB31A&0&2",
                Name = "Descriptor failure",
                Present = true,
                Parent = TestParent,
                Port = 2,
                Problem = 43,
                Locations = new[] { TestLocation }
            };
        }
        static UsbDevice Puck()
        {
            return new UsbDevice { Id = Scope.PuckId, Name = "Puck", Present = true, Parent = TestParent, Port = 2, Problem = 0, Locations = new[] { TestLocation } };
        }
        public static int Run()
        {
            ReceiverConfiguration.UseTestProfile(new ReceiverProfile { DeviceId = @"USB\VID_28DE&PID_1304\TEST_SERIAL", Parent = TestParent, Port = 2, Locations = new[] { TestLocation } });
            var bad = Bad();
            var puck = Puck();
            Check(Scope.SelectFailure(new List<UsbDevice> { bad }, puck) == bad, "Recorded Code 43 port is eligible");
            Check(Scope.SelectFailure(new List<UsbDevice> { bad }, null) == bad, "Verified bootstrap location works without a devnode history");
            Check(Scope.SelectFailure(new List<UsbDevice> { puck, bad }, puck) == null, "A healthy puck prevents resetting an unidentified device");
            Check(Scope.SelectFailure(new List<UsbDevice>(), puck) == null, "An unplugged puck does not trigger a reset");
            bad.Port = 3;
            Check(!Scope.IsFailedPuckPort(bad, puck), "Different port cannot be reset");
            bad = Bad();
            bad.Parent = @"USB\ROOT_HUB30\OTHER";
            Check(!Scope.IsFailedPuckPort(bad, puck), "Different hub cannot be reset");
            bad = Bad();
            bad.Locations = new[] { "OTHER" };
            Check(!Scope.IsFailedPuckPort(bad, puck), "Location must match recorded puck history");
            bad = Bad();
            bad.Id = @"USB\VID_1234&PID_5678\Disk";
            Check(!Scope.IsFailedPuckPort(bad, puck), "Identified third-party device cannot be reset");
            bad = Bad();
            bad.Problem = 0;
            Check(!Scope.IsFailedPuckPort(bad, puck), "No error means no automatic reset");
            bad = Bad();
            bad.Present = false;
            Check(!Scope.IsFailedPuckPort(bad, puck), "Ghost devices cannot be reset");
            bad = Bad();
            bad.Problem = 28;
            Check(!Scope.IsFailedPuckPort(bad, puck), "Unknown-driver failures are not treated as a descriptor failure");
            bad = Bad();
            var firmware = new UsbDevice { Id = @"USB\VID_28DE&PID_1007\TEST_FIRMWARE_SERIAL", Present = true };
            Check(Scope.SelectFailure(new List<UsbDevice> { bad, firmware }, puck) == null, "Firmware bootloader suppresses recovery");
            puck.Problem = 43;
            Check(Scope.SelectFailure(new List<UsbDevice> { puck }, puck) == puck, "Identified puck with a real error is eligible");
            var other = Puck();
            other.Id = @"USB\VID_28DE&PID_1304\DIFFERENT_SERIAL";
            Check(Scope.SelectFailure(new List<UsbDevice> { other }, puck) == null, "Another person's puck is out of scope");
            Check(Runtime.Fingerprint(Bad()) == Runtime.Fingerprint(Bad()), "Identical faults share a retry budget");
            var now = DateTime.Now;
            var snapshot = new Snapshot { At = now.ToString("o"), SteamRunning = true, RepairService = "Installed", Devices = new List<UsbDevice> { Puck() } };
            var view = ConnectionView.Create(snapshot, DateTime.MinValue, now);
            Check(view.Title == "Puck ready" && view.Puck == "Ready" && view.Steam == "Running" && view.State == ConnectionState.Ready, "Fresh healthy receiver with Steam running is ready without claiming controller input");
            snapshot.SteamRunning = false;
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).State == ConnectionState.Attention, "Steam closed needs attention despite a healthy USB receiver");
            snapshot.SteamRunning = true;
            snapshot.RepairService = "Missing";
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).State == ConnectionState.Attention, "Missing recovery setup cannot show green readiness");
            snapshot.RepairService = "Installed";
            Check(ConnectionView.Create(snapshot, now.ToUniversalTime().AddMinutes(30), now).State == ConnectionState.Attention, "Paused automatic care needs attention even when Steam and USB are ready");
            snapshot.Devices = new List<UsbDevice>();
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).State == ConnectionState.Disconnected, "A freshly observed absent puck is disconnected");
            Check(ConnectionView.Create(null, DateTime.MinValue, now).State == ConnectionState.Unknown, "Initial status stays neutral rather than claiming connection or disconnection");
            snapshot.Devices = new List<UsbDevice> { other };
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).Puck == "Not found", "Another receiver cannot make the monitored puck appear ready");
            snapshot.Devices = new List<UsbDevice> { Bad() };
            view = ConnectionView.Create(snapshot, now.ToUniversalTime().AddMinutes(30), now);
            Check(view.Puck == "USB fault" && view.Description.Contains("Resume automatic care") && view.State == ConnectionState.Attention, "USB fault needs attention and paused overview gives the right next action");
            snapshot.Devices = new List<UsbDevice> { Puck() };
            snapshot.At = now.AddMinutes(-2).ToString("o");
            view = ConnectionView.Create(snapshot, DateTime.MinValue, now);
            Check(view.Title == "Status needs a refresh" && view.NeedsAttention && view.State == ConnectionState.Unknown, "Old observations become unknown rather than presenting green readiness");
            snapshot.Devices = new List<UsbDevice>();
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).State == ConnectionState.Unknown, "Stale absence cannot claim a current disconnection");
            snapshot.At = null;
            Check(ConnectionView.Create(snapshot, DateTime.MinValue, now).NeedsAttention, "Missing check time is treated as unverified status");
            snapshot.At = now.ToString("o");
            snapshot.Devices = new List<UsbDevice> { firmware };
            view = ConnectionView.Create(snapshot, DateTime.MinValue, now);
            Check(view.Description.Contains("Let Steam finish") && view.State == ConnectionState.Attention, "Firmware update needs attention and directs the user to wait for Steam");
            var firstFault = Bad();
            var changedFault = Bad();
            changedFault.Problem = 10;
            Check(Runtime.Fingerprint(firstFault) == Runtime.Fingerprint(changedFault), "An error-code change cannot reset the recovery budget");
            var identifiedFault = Puck();
            identifiedFault.Problem = 43;
            Check(Runtime.Fingerprint(identifiedFault) == Runtime.Fingerprint(firstFault), "An unidentified descriptor fault shares the enrolled receiver's recovery budget");
            foreach (uint code in new uint[] { 10, 31, 43 })
            {
                var device = Puck();
                device.Problem = code;
                Check(Scope.SelectFailure(new List<UsbDevice> { device }, puck) == device, "Recoverable identified device error " + code);
            }
            foreach (uint code in new uint[] { 14, 18, 19, 22, 28, 32, 37, 38, 39, 45, 47, 48, 51, 52, 54, UInt32.MaxValue })
            {
                var device = Puck();
                device.Problem = code;
                Check(Scope.SelectFailure(new List<UsbDevice> { device }, puck) == null, "Device error " + code + " is not reset automatically");
            }
            var profile = ReceiverConfiguration.Current;
            ReceiverConfiguration.UseTestProfile(null);
            Check(Scope.SelectFailure(new List<UsbDevice> { Bad() }, null) == null, "No enrollment means no unidentified port reset");
            Check(!Scope.IsPuck(Puck()), "No enrollment cannot claim a recognized receiver");
            ReceiverConfiguration.UseTestProfile(profile);
            Check(!ReceiverProfile.IsSupportedId(@"USB\VID_1234&PID_1304\TEST"), "Only the supported Valve receiver can be enrolled");
            Check(!ReceiverProfile.IsSupportedId(@"USB\VID_28DE&PID_1304&MI_02\TEST"), "A child interface cannot be enrolled as the receiver");
            var report = new byte[64];
            report[0] = 0x42;
            Check(ControllerConnection.Decode(report, report.Length) == true, "A complete Triton input report proves a live controller");
            Check(ControllerConnection.Decode(report, 54) == true, "The puck's 54-byte HID report proves a live controller");
            Check(ControllerConnection.Decode(report, 45) == null, "An incomplete state payload stays unknown");
            report[0] = 0;
            report[1] = 0x42;
            Check(ControllerConnection.Decode(report, 54) == true, "A leading zero report byte is supported");
            Check(ControllerConnection.Decode(report, 12) == null, "A truncated input report cannot claim connection");
            Check(ControllerConnection.Decode(new byte[] { 0x79, 1 }, 2) == false, "An explicit wireless disconnect is recognized");
            Check(ControllerConnection.Decode(new byte[] { 0x79, 2 }, 2) == true, "An explicit wireless connect is recognized");
            Check(ControllerConnection.Decode(new byte[] { 0x79, 9 }, 2) == null, "An unknown wireless event remains unknown");
            Check(ControllerConnection.Decode(null, 64) == null, "Missing reports remain unknown");
            var observation = new ControllerObservation { At = now.AddMinutes(-2).ToString("o"), Connected = true, Status = "Connected" };
            Check(ControllerConnection.Display(observation, now).Connected == null, "A stale controller observation cannot claim a live connection");
            observation.At = now.ToString("o");
            Check(ControllerConnection.Display(observation, now).Connected == true, "A current controller observation is preserved");
            observation.At = now.AddMinutes(1).ToString("o");
            Check(ControllerConnection.Display(observation, now).Connected == null, "A future controller observation cannot claim a live connection");
            Check(!new RepairState { Attempts = -1 }.IsValid(), "A corrupt negative recovery budget is rejected");
            Check(!new RepairState { Attempts = 3 }.IsValid(), "An out-of-range recovery budget is rejected");
            Console.WriteLine(checks + " checks passed. No hardware was changed.");
            return 0;
        }
    }
}
