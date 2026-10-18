using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using System.Xml;

namespace SteamControllerGuard
{
    public sealed class ReceiverProfile
    {
        public string DeviceId { get; set; }
        public string Parent { get; set; }
        public string[] Locations
        {
            get; set;
        }
        public uint Port { get; set; }

        public bool IsValid()
        {
            return IsSupportedId(DeviceId) && Port > 0 && Port <= 255
                && !String.IsNullOrWhiteSpace(Parent) && Parent.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
                && Locations != null && Locations.Length > 0 && Locations.All(x => !String.IsNullOrWhiteSpace(x));
        }

        public static bool IsSupportedId(string id)
        {
            return id != null && Regex.IsMatch(id, @"^USB\\VID_28DE&PID_1304\\[A-Za-z0-9_-]+$", RegexOptions.IgnoreCase);
        }

        public static ReceiverProfile FromDevice(UsbDevice device)
        {
            var profile = new ReceiverProfile { DeviceId = device.Id, Parent = device.Parent, Port = device.Port, Locations = device.Locations };
            if (!profile.IsValid())
                throw new InvalidDataException("The receiver has no usable USB port identity.");
            return profile;
        }

        public UsbDevice AsDevice()
        {
            return new UsbDevice { Id = DeviceId, Parent = Parent, Port = Port, Locations = Locations };
        }
    }

    public static class ReceiverConfiguration
    {
        static readonly object Gate = new object();
        static ReceiverProfile current;
        static bool loaded;
        public static readonly string UserPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamControllerGuard", "receiver.xml");
        public static readonly string MachinePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteamControllerGuard", "receiver.xml");

        public static ReceiverProfile Current
        {
            get
            {
                lock (Gate)
                {
                    if (!loaded)
                        Reload();
                    return current;
                }
            }
        }

        public static void Reload()
        {
            lock (Gate)
            {
                // A corrupt protected profile must not fall back to a user-editable
                // identity while an elevated recovery task is running.
                current = File.Exists(MachinePath) ? Read(MachinePath) : Read(UserPath);
                loaded = true;
            }
        }

        static ReceiverProfile Read(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > 16384)
                        return null;
                    using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 }))
                    {
                        var value = new XmlSerializer(typeof(ReceiverProfile)).Deserialize(reader) as ReceiverProfile;
                        return value != null && value.IsValid() ? value : null;
                    }
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        public static void Save(string path, ReceiverProfile profile)
        {
            if (profile == null || !profile.IsValid())
                throw new InvalidDataException("Invalid receiver identity.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                    new XmlSerializer(typeof(ReceiverProfile)).Serialize(stream, profile);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Reload();
        }

        internal static void UseTestProfile(ReceiverProfile profile)
        {
            lock (Gate)
            {
                current = profile;
                loaded = true;
            }
        }
    }
}
