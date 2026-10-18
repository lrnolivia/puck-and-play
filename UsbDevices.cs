using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace SteamControllerGuard
{
    public sealed class UsbDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public string[] Locations
        {
            get; set;
        }
        public string LocationInfo { get; set; }
        public uint Port { get; set; }
        public uint Problem { get; set; }
        public bool Present { get; set; }
    }

    public static class Scope
    {
        public static string PuckId
        {
            get
            {
                var profile = ReceiverConfiguration.Current;
                return profile == null ? "" : profile.DeviceId;
            }
        }
        public static bool IsPuck(UsbDevice d)
        {
            return d != null && PuckId.Length > 0 && Same(d.Id, PuckId);
        }
        public static bool Same(string a, string b)
        {
            return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsFirmware(UsbDevice d)
        {
            return d != null && d.Present && d.Id != null && (d.Id.StartsWith(@"USB\VID_28DE&PID_1007\", StringComparison.OrdinalIgnoreCase)
                || d.Id.StartsWith(@"USB\VID_28DE&PID_1005\", StringComparison.OrdinalIgnoreCase));
        }
        public static bool IsFailedPuckPort(UsbDevice d, UsbDevice puck)
        {
            if (d == null || d.Id == null || !d.Present || (d.Problem != 43 && d.Problem != 10))
                return false;
            if (!(d.Id.StartsWith(@"USB\VID_0000&PID_0002\", StringComparison.OrdinalIgnoreCase)
                || d.Id.StartsWith(@"USB\VID_0000&PID_0004\", StringComparison.OrdinalIgnoreCase)))
                return false;
            // An unidentified device is eligible only on a port independently recorded for this serial.
            var enrolled = ReceiverConfiguration.Current;
            if (enrolled == null || !enrolled.IsValid())
                return false;
            if (puck != null && !IsPuck(puck))
                return false;
            string parent = puck == null ? enrolled.Parent : puck.Parent;
            string[] locations = puck == null ? enrolled.Locations : puck.Locations;
            uint port = puck == null ? enrolled.Port : puck.Port;
            return port > 0 && d.Port == port && Same(d.Parent, parent)
                && d.Locations != null && locations != null
                && d.Locations.Any(x => locations.Any(y => Same(x, y)));
        }
        public static UsbDevice SelectFailure(List<UsbDevice> present, UsbDevice history)
        {
            if (present.Any(IsFirmware))
                return null;
            UsbDevice puck = present.FirstOrDefault(IsPuck);
            if (puck != null)
                return DeviceFault.CanRestart(puck.Problem) ? puck : null;
            return present.FirstOrDefault(d => IsFailedPuckPort(d, history));
        }
    }

    public static class UsbDevices
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Info
        {
            public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Interface
        {
            public uint Size; public Guid ClassGuid; public uint Flags; public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct CycleParams
        {
            public uint Port; public uint Status;
        }
        static readonly Guid HubGuid = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8");
        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr g, string e, IntPtr w, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr w, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr s, uint i, ref Info d);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceRegistryProperty(IntPtr s, ref Info d, uint p, out uint t, byte[] b, uint n, out uint required);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref Interface data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref Interface d, IntPtr b, uint n, out uint required, ref Info info);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern uint CM_Get_Device_ID(uint d, StringBuilder b, uint n, uint f);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint p, uint d, uint f);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint s, out uint p, uint d, uint f);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string p, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint c, ref CycleParams input, uint insize, ref CycleParams output, uint outsize, out uint read, IntPtr overlapped);
        static string Id(uint node)
        {
            var b = new StringBuilder(1024);
            return CM_Get_Device_ID(node, b, (uint)b.Capacity, 0) == 0 ? b.ToString() : "";
        }
        static byte[] Property(IntPtr set, ref Info info, uint property)
        {
            uint type, size;
            SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, null, 0, out size);
            if (size == 0 || size > 65536)
                return new byte[0];
            var buffer = new byte[size];
            return SetupDiGetDeviceRegistryProperty(set, ref info, property, out type, buffer, size, out size) ? buffer : new byte[0];
        }
        static string TextProperty(IntPtr set, ref Info info, uint property)
        {
            return Encoding.Unicode.GetString(Property(set, ref info, property)).TrimEnd('\0');
        }
        public static List<UsbDevice> Enumerate(bool presentOnly)
        {
            var result = new List<UsbDevice>();
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, 4u | (presentOnly ? 2u : 0u));
            if (set == new IntPtr(-1))
                throw new Win32Exception();
            try
            {
                for (uint i = 0; ; i++)
                {
                    var info = new Info { Size = (uint)Marshal.SizeOf(typeof(Info)) };
                    if (!SetupDiEnumDeviceInfo(set, i, ref info))
                    {
                        if (Marshal.GetLastWin32Error() != 259)
                            throw new Win32Exception();
                        break;
                    }
                    string id = Id(info.DevInst);
                    if (!id.StartsWith(@"USB\VID_28DE&", StringComparison.OrdinalIgnoreCase)
                        && !id.StartsWith(@"USB\VID_0000&", StringComparison.OrdinalIgnoreCase))
                        continue;
                    uint status, problem, parent;
                    bool available = CM_Get_DevNode_Status(out status, out problem, info.DevInst, 0) == 0;
                    string parentId = CM_Get_Parent(out parent, info.DevInst, 0) == 0 ? Id(parent) : "";
                    string location = TextProperty(set, ref info, 13);
                    var portMatch = Regex.Match(location, @"Port_#(\d+)");
                    byte[] address = Property(set, ref info, 28);
                    uint port = address.Length >= 4 ? BitConverter.ToUInt32(address, 0) : 0;
                    if (portMatch.Success)
                        UInt32.TryParse(portMatch.Groups[1].Value, out port);
                    result.Add(new UsbDevice
                    {
                        Id = id,
                        Name = TextProperty(set, ref info, 0),
                        Parent = parentId,
                        Locations = TextProperty(set, ref info, 35).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries),
                        LocationInfo = location,
                        Port = port,
                        Problem = available ? problem : UInt32.MaxValue,
                        Present = presentOnly
                    });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
        static string HubPath(string parentId)
        {
            Guid g = HubGuid;
            IntPtr set = SetupDiGetClassDevs(ref g, null, IntPtr.Zero, 18);
            if (set == new IntPtr(-1))
                throw new Win32Exception();
            try
            {
                for (uint i = 0; ; i++)
                {
                    var data = new Interface { Size = (uint)Marshal.SizeOf(typeof(Interface)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref data))
                    {
                        if (Marshal.GetLastWin32Error() != 259)
                            throw new Win32Exception();
                        break;
                    }
                    var info = new Info { Size = (uint)Marshal.SizeOf(typeof(Info)) };
                    uint size;
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, ref info);
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out size, ref info))
                            throw new Win32Exception();
                        if (Scope.Same(Id(info.DevInst), parentId))
                            return Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            throw new InvalidOperationException("The puck's parent hub interface is unavailable.");
        }
        public static string[] ControllerPaths()
        {
            if (ReceiverConfiguration.Current == null)
                return new string[0];
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 18);
            if (set == new IntPtr(-1))
                throw new Win32Exception();
            var paths = new List<string>();
            try
            {
                for (uint index = 0; ; index++)
                {
                    var item = new Interface { Size = (uint)Marshal.SizeOf(typeof(Interface)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref item))
                    {
                        if (Marshal.GetLastWin32Error() != 259)
                            throw new Win32Exception();
                        break;
                    }
                    var info = new Info { Size = (uint)Marshal.SizeOf(typeof(Info)) };
                    uint size;
                    SetupDiGetDeviceInterfaceDetail(set, ref item, IntPtr.Zero, 0, out size, ref info);
                    if (size < 8 || size > 65536)
                        continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref item, detail, size, out size, ref info))
                            continue;
                        string id = Id(info.DevInst);
                        if (!Regex.IsMatch(id, @"^HID\\VID_28DE&PID_1304&MI_0[2-5]", RegexOptions.IgnoreCase))
                            continue;
                        uint node = info.DevInst;
                        for (int depth = 0; depth < 6; depth++)
                        {
                            if (Scope.Same(Id(node), Scope.PuckId))
                            {
                                paths.Add(Marshal.PtrToStringUni(IntPtr.Add(detail, 4)));
                                break;
                            }
                            uint parent;
                            if (CM_Get_Parent(out parent, node, 0) != 0)
                                break;
                            node = parent;
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        }
        public static void CycleFailedPort(UsbDevice expected, UsbDevice history)
        {
            // Re-read immediately before mutation. Never restart the hub or another port.
            var now = Enumerate(true);
            if (now.Any(Scope.IsFirmware))
                throw new InvalidOperationException("Firmware mode: recovery deferred.");
            var current = Scope.SelectFailure(now, history);
            if (current == null || !Scope.Same(current.Id, expected.Id) || current.Port == 0 || current.Port != expected.Port
                || !Scope.Same(current.Parent, expected.Parent))
                throw new InvalidOperationException("Device state changed: port cycle cancelled.");
            string path = HubPath(current.Parent);
            using (var handle = CreateFile(path, 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new Win32Exception();
                var input = new CycleParams { Port = current.Port, Status = 0 };
                var output = new CycleParams();
                uint read;
                // usbioctl.h: CTL_CODE(FILE_DEVICE_USB=0x22, USB_HUB_CYCLE_PORT=273, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0).
                if (!DeviceIoControl(handle, 0x220444, ref input, 8, ref output, 8, out read, IntPtr.Zero))
                    throw new Win32Exception();
                if (output.Status != 0)
                    throw new InvalidOperationException("USB port cycle status: 0x" + output.Status.ToString("X8"));
            }
        }
    }
}
