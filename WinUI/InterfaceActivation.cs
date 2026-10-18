using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PuckPlay;

// A warm interface accepts small, same-user activation messages without
// starting another Windows App SDK process for each tray click.
internal sealed class InterfaceActivation : IDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly Task listener;
    internal static string Name
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return "loew.fi.PuckPlay.Interface." + identity.User!.Value + "." + typeof(App).Assembly.GetName().Version
                + (Bridge.Preview ? ".Preview." + Environment.ProcessId : "");
        }
    }

    internal InterfaceActivation(App app) => listener = Listen(app);

    async Task Listen(App app)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 256, 256);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(1000);
                byte[] buffer = new byte[128];
                int count = await pipe.ReadAsync(buffer, timeout.Token);
                if (count == 0 || !pipe.IsMessageComplete)
                    continue;
                string command = Encoding.UTF8.GetString(buffer, 0, count).Trim();
                if (!App.ValidActivation(command) || !app.Queue.TryEnqueue(() => app.Handle(command)))
                    continue;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is OperationCanceledException)
            {
                if (!stop.IsCancellationRequested)
                {
                    Bridge.Log(error);
                    try { await Task.Delay(250, stop.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        _ = listener.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
