using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace PuckPlay;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        bool preview = Array.IndexOf(args, "--preview") >= 0 || Array.IndexOf(args, "--export-artwork") >= 0 || Array.IndexOf(args, "--preview-crystal") >= 0 || Array.IndexOf(args, "--test-renderer") >= 0;
        var instance = AppInstance.FindOrRegisterForKey(preview ? "loew.fi.PuckPlay.Preview." + Environment.ProcessId : "loew.fi.PuckPlay.Interface");
        if (!instance.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(async () => { await instance.RedirectActivationToAsync(activation); }).GetAwaiter().GetResult();
            return;
        }
        App? app = null;
        instance.Activated += delegate (object? sender, AppActivationArguments activation)
        {
            string command = (activation.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "--status";
            app?.Queue.TryEnqueue(() => app.Handle(command));
        };
        try
        {
            Application.Start(delegate
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            app = new App();
        });
        }
        finally { CrystalDevice.ReleaseAll(); }
    }
}
