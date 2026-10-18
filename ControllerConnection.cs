using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SteamControllerGuard
{
    public sealed class ControllerObservation
    {
        public string Status { get; set; }
        public string Detail { get; set; }
        public string At { get; set; }
        public bool? Connected { get; set; }
    }

    public static class ControllerConnection
    {
        public static ControllerObservation Display(ControllerObservation observation, DateTime now)
        {
            DateTime at;
            if (observation != null && DateTime.TryParse(observation.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at)
                && (now.ToUniversalTime() - at.ToUniversalTime()).TotalSeconds >= -5
                && (now.ToUniversalTime() - at.ToUniversalTime()).TotalSeconds <= 15)
                return observation;
            return new ControllerObservation { Status = "Unknown", Detail = "Waiting for a current controller observation." };
        }
        [StructLayout(LayoutKind.Sequential)]
        struct HidCaps
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkCollectionNodes, InputButtonCaps, InputValueCaps, InputDataIndices;
            public ushort OutputButtonCaps, OutputValueCaps, OutputDataIndices;
            public ushort FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);

        // Report IDs and state values are defined by Valve's Triton protocol,
        // also used by SDL's Steam Controller driver. Only input is read here.
        public static bool? Decode(byte[] report, int count)
        {
            if (report == null || count < 2 || count > report.Length)
                return null;
            int start = report[0] == 0 && count > 2 ? 1 : 0;
            int id = report[start];
            if ((id == 0x42 || id == 0x45 || id == 0x47) && count - start >= 46)
                return true;
            if ((id == 0x46 || id == 0x79) && count - start >= 2)
                return report[start + 1] == 2 ? true : report[start + 1] == 1 ? (bool?)false : null;
            return null;
        }

        static bool? ReadEndpoint(string path)
        {
            string ignored;
            return ReadEndpoint(path, out ignored);
        }
        static bool? ReadEndpoint(string path, out string diagnostic)
        {
            diagnostic = "No report";
            using (var handle = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    diagnostic = "Open failed: " + Marshal.GetLastWin32Error();
                    return null;
                }
                IntPtr data;
                if (!HidD_GetPreparsedData(handle, out data))
                    return null;
                HidCaps caps;
                try
                {
                    if (HidP_GetCaps(data, out caps) < 0)
                        return null;
                }
                finally { HidD_FreePreparsedData(data); }
                diagnostic = "Usage " + caps.UsagePage.ToString("X4") + ":" + caps.Usage.ToString("X4") + ", report bytes " + caps.InputReportByteLength;
                if (caps.InputReportByteLength < 2 || caps.InputReportByteLength > 1024)
                    return null;
                using (var stream = new FileStream(handle, FileAccess.Read, caps.InputReportByteLength, true))
                {
                    var buffer = new byte[caps.InputReportByteLength];
                    var read = stream.ReadAsync(buffer, 0, buffer.Length);
                    if (!read.Wait(350))
                    {
                        // Closing the stream cancels the outstanding read. Observe
                        // its completion so cancellation cannot become an unobserved fault.
                        stream.Dispose();
                        diagnostic += "; no input within 350 ms";
                        read.ContinueWith(task => { var ignored = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                        return null;
                    }
                    diagnostic += "; received " + read.Result + " bytes; report " + buffer[0].ToString("X2") + "/" + buffer[1].ToString("X2");
                    return Decode(buffer, read.Result);
                }
            }
        }
        public static string[] Probe()
        {
            return UsbDevices.ControllerPaths().Select(path =>
            {
                try
                {
                    string diagnostic;
                    ReadEndpoint(path, out diagnostic);
                    return diagnostic;
                }
                catch (Exception error) { return error.GetType().Name + ": " + error.Message; }
            }).ToArray();
        }

        public static ControllerObservation Read(List<UsbDevice> devices)
        {
            var result = new ControllerObservation { At = DateTime.Now.ToString("o"), Status = "Unavailable", Detail = "Connect and set up the puck to check the controller." };
            if (!devices.Any(d => Scope.IsPuck(d) && d.Present && d.Problem == 0))
                return result;
            try
            {
                string[] paths = UsbDevices.ControllerPaths();
                if (paths.Length == 0)
                {
                    result.Detail = "Windows has not exposed the controller input interfaces.";
                    return result;
                }
                var reports = new bool?[paths.Length];
                Parallel.For(0, paths.Length, i =>
                {
                    try
                    {
                        reports[i] = ReadEndpoint(paths[i]);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (AggregateException) { }
                });
                if (reports.Any(value => value == true))
                {
                    result.Connected = true;
                    result.Status = "Connected";
                    result.Detail = "Live controller reports received through the puck.";
                }
                else if (reports.Any(value => value == false))
                {
                    result.Connected = false;
                    result.Status = "Disconnected";
                    result.Detail = "The receiver reported a wireless disconnect.";
                }
                else
                {
                    result.Status = "Not detected";
                    result.Detail = "No live report received. Turn the controller on or check it in Steam; another app may hold its input interface.";
                }
            }
            catch (System.ComponentModel.Win32Exception) { result.Detail = "Windows could not read the controller interface. Check it in Steam."; }
            return result;
        }
    }
}
