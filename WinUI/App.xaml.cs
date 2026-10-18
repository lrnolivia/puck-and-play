using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PuckPlay;

public partial class App : Application
{
    internal DispatcherQueue Queue { get; } = DispatcherQueue.GetForCurrentThread();
    readonly Dictionary<string, FluentWindow> windows = new Dictionary<string, FluentWindow>();
    MenuWindow? menu;
    InterfaceActivation? activation;
    CancellationTokenSource? parentWait;
    bool warm;
    int pending;
    internal bool IsPreview;
    internal bool TestMenuActivation;
    public App()
    {
        InitializeComponent();
        UnhandledException += delegate (object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
        {
            Bridge.Log(args.Exception);
        };
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] launch = Environment.GetCommandLineArgs();
        int menuTest = Array.IndexOf(launch, "--test-menu");
        if (menuTest >= 0 && menuTest + 1 < launch.Length)
        {
            IsPreview = true;
            Bridge.Preview = true;
            Directory.CreateDirectory(launch[menuTest + 1]);
            try { await Previews.VerifyMenu(this, launch[menuTest + 1]); }
            catch (Exception error) { File.WriteAllText(Path.Combine(launch[menuTest + 1], "error.txt"), error.ToString()); Environment.ExitCode = 1; }
            Exit();
            return;
        }
        int rendererTest = Array.IndexOf(launch, "--test-renderer");
        if (rendererTest >= 0 && rendererTest + 1 < launch.Length)
        {
            IsPreview = true;
            Bridge.Preview = true;
            try
            {
                await Previews.VerifyRenderer(launch[rendererTest + 1]);
            }
            catch (Exception error) { Directory.CreateDirectory(launch[rendererTest + 1]); File.WriteAllText(Path.Combine(launch[rendererTest + 1], "error.txt"), error.ToString()); Environment.ExitCode = 1; }
            Exit();
            return;
        }
        if (Array.IndexOf(launch, "--preview-crystal") >= 0)
        {
            IsPreview = true;
            Bridge.Preview = true;
            try
            {
                await Previews.ShowCrystal();
            }
            catch (Exception e) { Bridge.Log(e); Exit(); }
            return;
        }
        int export = Array.IndexOf(launch, "--export-artwork");
        int preview = export >= 0 ? export : Array.IndexOf(launch, "--preview");
        if (preview >= 0 && preview + 1 < launch.Length)
        {
            IsPreview = true;
            Bridge.Preview = true;
            try
            {
                if (export >= 0)
                    await Previews.ExportArtwork(launch[preview + 1]);
                else
                    await Previews.Render(this, launch[preview + 1]);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(launch[preview + 1], "error.txt"), e.ToString()); Environment.ExitCode = 1; }
            Exit();
            return;
        }
        activation = new InterfaceActivation(this);
        Handle(Environment.CommandLine);
    }
    internal static bool ValidActivation(string command) => command == "--status" || command == "--about" || command == "--details" || command == "--reconnect" || command == "--exit"
        || Regex.IsMatch(command, @"^--menu -?\d{1,6} -?\d{1,6}$") || Regex.IsMatch(command, @"^--warm \d{1,10}$");

    async void KeepWarm(int processId)
    {
        parentWait?.Cancel();
        var cancellation = new CancellationTokenSource();
        parentWait = cancellation;
        try
        {
            using var parent = Process.GetProcessById(processId);
            // Preload XAML without activating a window or running the status animation.
            warm = true;
            EnsureMenu().Prepare();
            await parent.WaitForExitAsync(cancellation.Token);
            if (!cancellation.IsCancellationRequested)
                Exit();
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { warm = false; MaybeExit(); }
        catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
        {
            Bridge.Log(error);
            warm = false;
            MaybeExit();
        }
        finally
        {
            if (parentWait == cancellation) parentWait = null;
            cancellation.Dispose();
        }
    }
    internal MenuWindow EnsureMenu()
    {
        if (menu == null)
        {
            menu = new MenuWindow(this, 0, 0, true);
            menu.Dismissed += MaybeExit;
        }
        return menu;
    }
    internal void DisposeActivation()
    {
        parentWait?.Cancel();
        activation?.Dispose();
    }
    internal void Handle(string command)
    {
        if (command.Contains("--exit", StringComparison.Ordinal))
        {
            Exit();
            return;
        }
        var parent = Regex.Match(command, @"--warm\s+(\d+)");
        if (parent.Success && int.TryParse(parent.Groups[1].Value, out int processId))
        {
            KeepWarm(processId);
            return;
        }
        var match = Regex.Match(command, @"--menu\s+(-?\d+)\s+(-?\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int x) && int.TryParse(match.Groups[2].Value, out int y))
        {
            EnsureMenu().Open(x, y);
            return;
        }
        Open(command.Contains("--about", StringComparison.Ordinal) ? "about" : command.Contains("--details", StringComparison.Ordinal) ? "details" : "status",
            command.Contains("--reconnect", StringComparison.Ordinal));
    }
    internal FluentWindow Open(string kind, bool troubleshoot = false)
    {
        if (!windows.TryGetValue(kind, out FluentWindow? window))
        {
            window = kind == "about" ? new AboutWindow() : kind == "details" ? new DetailsWindow(this) : new StatusWindow(this);
            windows.Add(kind, window);
            window.Closed += delegate
            {
                windows.Remove(kind);
                MaybeExit();
            };
        }
        window.AppWindow.Show();
        window.Activate();
        if (troubleshoot && window is StatusWindow status)
            status.ExpandTroubleshooting();
        return window;
    }
    internal async Task<bool> Command(string command)
    {
        pending++;
        try
        {
            await Bridge.Send(command);
            if (command == "quit")
                Exit();
            return true;
        }
        catch (Exception e) when (e is IOException || e is TimeoutException || e is OperationCanceledException || e is UnauthorizedAccessException)
        {
            ((StatusWindow)Open("status")).ShowError("Couldn't reach automatic care. Try again in a moment.");
            return false;
        }
        finally { pending--; MaybeExit(); }
    }
    void MaybeExit()
    {
        if (!IsPreview && !warm && windows.Count == 0 && menu?.IsOpen != true && pending == 0)
            Queue.TryEnqueue(() => { if (!warm && windows.Count == 0 && menu?.IsOpen != true && pending == 0) Exit(); });
    }
}
