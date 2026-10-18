using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace SteamControllerGuard
{
    static class UiHost
    {
        public static string Executable
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Interface", typeof(UiHost).Assembly.GetName().Version.ToString(), "PuckAndPlay.UI.exe");
            }
        }
        public static bool Available
        {
            get
            {
                return File.Exists(Executable);
            }
        }
        public static bool TryOpen(string view)
        {
            if (!Available)
                return false;
            try
            {
                using (var launched = Process.Start(new ProcessStartInfo(Executable, "--" + view) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Executable) }))
                { }
                return true;
            }
            catch (Exception e) { Files.Log("Modern interface launch failed: " + e.Message, false); return false; }
        }
    }

    sealed class UiBridge : IDisposable
    {
        readonly Action<string> action;
        readonly Thread thread;
        readonly string name;
        readonly SecurityIdentifier user;
        readonly object gate = new object();
        readonly ManualResetEventSlim stop = new ManualResetEventSlim();
        volatile bool stopped;
        NamedPipeServerStream active;
        public UiBridge(Action<string> action)
        {
            this.action = action;
            using (var identity = WindowsIdentity.GetCurrent())
                user = identity.User;
            name = "loew.fi.PuckPlay." + user.Value;
            thread = new Thread(Serve) { IsBackground = true, Name = "Puck & Play interface commands" };
            thread.Start();
        }
        static bool Allowed(string command)
        {
            return command == "check" || command == "pause" || command == "resume" || command == "reconnect" || command == "steam" || command == "open-steam"
                || command == "logs" || command == "recovery-logs" || command == "quit" || command == "test-inputs" || command == "enroll" || command == "setup-recovery";
        }
        void Serve()
        {
            while (!stopped)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 256, 256, security))
                    {
                        lock (gate)
                        {
                            if (stopped)
                                return;
                            active = server;
                        }
                        server.WaitForConnection();
                        byte[] buffer = new byte[128];
                        var read = server.ReadAsync(buffer, 0, buffer.Length);
                        if (!read.Wait(3000))
                        {
                            server.Dispose();
                            read.ContinueWith(task => { var ignored = task.Exception; }, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                            continue;
                        }
                        if (read.Result == 0 || !server.IsMessageComplete)
                            continue;
                        string command = Encoding.UTF8.GetString(buffer, 0, read.Result).Trim();
                        if (!Allowed(command))
                            continue;
                        action(command);
                        byte[] reply = Encoding.UTF8.GetBytes("queued\n");
                        server.Write(reply, 0, reply.Length);
                    }
                }
                catch (Exception e) { if (!stopped) { Files.Log("Interface command: " + e.Message, false); stop.Wait(250); } }
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (stopped)
                    return;
                stopped = true;
                stop.Set();
                if (active != null)
                    active.Dispose();
            }
            if (thread.Join(3500))
                stop.Dispose();
            else
                ThreadPool.QueueUserWorkItem(delegate
                {
                    thread.Join();
                    stop.Dispose();
                });
        }
    }
}
