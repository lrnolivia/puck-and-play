using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SteamControllerGuard;

namespace PuckPlay;

internal static class Bridge
{
    public static readonly string UserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamControllerGuard");
    public static readonly string MachineRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SteamControllerGuard");
    public static bool Preview;
    public static Snapshot? Sample;
    static Snapshot? last;
    static readonly JsonSerializerOptions Options = new JsonSerializerOptions { Converters = { new OldDateConverter() } };
    public static Snapshot? Read()
    {
        if (Preview)
            return Sample;
        try
        {
            using var file = new FileStream(Path.Combine(UserRoot, "status.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            last = JsonSerializer.Deserialize<Snapshot>(file, Options);
            ReceiverConfiguration.Reload();
        }
        catch (IOException) { }
        catch (JsonException) { }
        catch (UnauthorizedAccessException) { }
        return last;
    }
    public static void Log(Exception error)
    {
        try
        {
            Directory.CreateDirectory(UserRoot);
            string path = Path.Combine(UserRoot, "interface.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.Move(path, path + ".1", true);
            File.AppendAllText(path, DateTime.Now.ToString("o") + " " + error + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static async Task Send(string command)
    {
        if (Preview)
            return;
        using var timeout = new CancellationTokenSource(2500);
        string name;
        using (var identity = WindowsIdentity.GetCurrent())
            name = "loew.fi.PuckPlay." + identity.User!.Value;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), timeout.Token);
        byte[] reply = new byte[32];
        int count = await pipe.ReadAsync(reply, timeout.Token);
        if (Encoding.UTF8.GetString(reply, 0, count).Trim() != "queued")
            throw new IOException("The helper couldn't accept that action.");
    }
    public static string Details(Snapshot? snapshot)
    {
        if (snapshot == null)
            return "Waiting for the first connection check.";
        var text = new StringBuilder();
        text.AppendLine(snapshot.Status).AppendLine("Checked: " + snapshot.At).AppendLine();
        text.AppendLine("Monitored puck: " + Scope.PuckId).AppendLine("Steam: " + (snapshot.SteamRunning ? "running" : "closed"));
        text.AppendLine("USB recovery: " + snapshot.RepairService).AppendLine("Another mapper running: " + snapshot.OtherMapperRunning).AppendLine();
        if (snapshot.Devices != null)
            foreach (var device in snapshot.Devices)
                text.AppendLine(device.Name + " | Error " + device.Problem).AppendLine(device.Id).AppendLine(device.LocationInfo).AppendLine();
        if (snapshot.Repair != null)
            text.AppendLine("Recovery: " + snapshot.Repair.Outcome).AppendLine(snapshot.Repair.Detail).AppendLine();
        text.AppendLine("Latest Steam controller event (may be historical):").AppendLine(snapshot.LastControllerEvent).AppendLine();
        var controller = ControllerConnection.Display(snapshot.Controller, DateTime.Now);
        text.AppendLine("Controller: " + controller.Status).AppendLine(controller.Detail);
        text.AppendLine("A ready receiver does not verify game input. Test in Steam Settings > Controller > Test Device Inputs.");
        text.AppendLine("Automatic recovery acts only on actual USB faults, stops after two attempts and protects firmware update mode.");
        text.AppendLine("Quit or pause this helper before intentionally keeping Steam closed.").AppendLine();
        return text.AppendLine("Connection logs: " + UserRoot).AppendLine("Recovery logs: " + MachineRoot).ToString();
    }
    sealed class OldDateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            string? text = reader.GetString();
            if (text != null && text.StartsWith("/Date(", StringComparison.Ordinal))
            {
                int end = text.IndexOf(')');
                if (end > 6 && long.TryParse(text.AsSpan(6, end - 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out long milliseconds))
                {
                    try
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
                return DateTime.MinValue;
            }
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime value) ? value : DateTime.MinValue;
        }
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
