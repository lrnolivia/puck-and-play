using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using SteamControllerGuard;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Text;

namespace PuckPlay;

internal class FluentWindow : Window
{
    internal Grid Root { get; } = new Grid();
    protected ContentControl Body { get; } = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    readonly Image brandImage;
    readonly DispatcherTimer brandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    string brandKey = "";
    string? brandOverride;
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    internal FluentWindow(string title, int width, int height, bool acrylic = false)
    {
        Title = title;
        SystemBackdrop = acrylic ? new DesktopAcrylicBackdrop() : new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titlebar = new Grid { Margin = new Thickness(16, 0, 140, 0), VerticalAlignment = VerticalAlignment.Stretch };
        var titleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        brandImage = Puck(20, 20);
        titleContent.Children.Add(brandImage);
        titleContent.Children.Add(Text(title, 12));
        titlebar.Children.Add(titleContent);
        Root.Children.Add(titlebar);
        Grid.SetRow(Body, 1);
        Root.Children.Add(Body);
        Content = Root;
        SetTitleBar(titlebar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "puck.ico"));
        float scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96f;
        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsMaximizable = false;
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += delegate (KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            Close();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(escape);
        Root.ActualThemeChanged += delegate
        {
            CaptionTheme();
        };
        Root.Loaded += delegate
        {
            CaptionTheme();
            RefreshBrand();
            brandTimer.Start();
        };
        brandTimer.Tick += delegate
        {
            RefreshBrand();
        };
        Closed += delegate
        {
            brandTimer.Stop();
        };
    }
    void RefreshBrand()
    {
        string key = brandOverride ?? StatusArt.CurrentKey();
        if (key == brandKey)
            return;
        brandKey = key;
        brandImage.Source = new BitmapImage(new Uri(StatusArt.File(key)));
        AppWindow.SetIcon(StatusArt.File(key, "ico"));
    }
    protected void SetBrandOverride(string? key)
    {
        brandOverride = key;
        RefreshBrand();
    }
    void CaptionTheme()
    {
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
    }
    internal static TextBlock Text(string text, double size = 14, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        if (secondary)
            block.Opacity = 0.72;
        return block;
    }
    internal static Image Puck(double width, double height)
    {
        var image = new Image { Source = new BitmapImage(new Uri(StatusArt.File(StatusArt.CurrentKey()))), Width = width, Height = height, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(image, "Steam Controller puck with a luminous play triangle hovering above it");
        return image;
    }
    protected static Button ActionButton(string title, Func<Task> action, bool accent = false)
    {
        var button = new Button { Content = title, MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (accent)
            button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += async delegate { await action(); };
        return button;
    }
    protected static HyperlinkButton Link(string title, Action action)
    {
        var button = new HyperlinkButton { Content = title, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += delegate
        {
            action();
        };
        return button;
    }
    protected static ScrollViewer Scroll(UIElement content) => new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    internal void PreviewTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        Root.Background = new SolidColorBrush(theme == ElementTheme.Dark ? ColorHelper.FromArgb(255, 32, 32, 32) : ColorHelper.FromArgb(255, 243, 243, 243));
    }
}

internal sealed class AboutWindow : FluentWindow
{
    internal AboutWindow() : base("About Puck & Play", 440, 492)
    {
        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(28, 20, 28, 24), HorizontalAlignment = HorizontalAlignment.Stretch };
        var artwork = Puck(272, 130);
        artwork.Margin = new Thickness(0, 0, 0, 12);
        layout.Children.Add(artwork);
        var title = Text("Puck & Play", 28);
        title.FontWeight = FontWeights.SemiBold;
        title.TextAlignment = TextAlignment.Center;
        layout.Children.Add(title);
        var version = Text("Version " + typeof(AboutWindow).Assembly.GetName().Version!.ToString(3), 14, true);
        version.TextAlignment = TextAlignment.Center;
        layout.Children.Add(version);
        var hello = Text("A little care. More play.", 20);
        hello.TextAlignment = TextAlignment.Center;
        hello.Margin = new Thickness(0, 16, 0, 0);
        layout.Children.Add(hello);
        var description = Text("A little help for your Steam Controller puck.\nKeeps Steam ready and helps recover USB faults.", 14, true);
        description.TextAlignment = TextAlignment.Center;
        description.Margin = new Thickness(0, 4, 0, 12);
        layout.Children.Add(description);
        var close = ActionButton("Close", () => { Close(); return Task.CompletedTask; });
        close.Width = 104;
        close.HorizontalAlignment = HorizontalAlignment.Center;
        layout.Children.Add(close);
        Body.Content = Scroll(layout);
    }
}

internal sealed class StatusWindow : FluentWindow
{
    readonly App app;
    readonly TextBlock title, summary, puck, steam, controller, checkedAt, reconnectNote, careDetail;
    readonly ToggleSwitch care;
    readonly Button check, reconnect, play;
    readonly CrystalStatusMark playArt = new CrystalStatusMark { Width = 96, Height = 96 };
    readonly Expander troubleshooting;
    readonly ScrollViewer scroller;
    readonly InfoBar notice = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Warning, Visibility = Visibility.Collapsed };
    readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    bool updating, checking;
    bool scrollToTroubleshooting;
    bool? requestedCare;
    DateTime careRequested;
    internal StatusWindow(App app) : base("Puck & Play", 480, 640, true)
    {
        this.app = app;
        var layout = new StackPanel { Spacing = 14, Margin = new Thickness(24, 8, 24, 24) };
        var header = new Grid { ColumnSpacing = 12, MinHeight = 96 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        play = ActionButton("Open Steam", async () => { await app.Command("open-steam"); });
        play.Content = null;
        play.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
        play.Width = 96;
        play.Height = 96;
        play.Padding = new Thickness(0);
        play.CornerRadius = new CornerRadius(24);
        // Artwork is a sibling of the hit target so the button template cannot
        // round-clip its halo. The button retains keyboard and focus behavior.
        play.Background = new SolidColorBrush(Colors.Transparent);
        play.BorderThickness = new Thickness(0);
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            play.Resources["SubtleButtonBackground" + state] = new SolidColorBrush(Colors.Transparent);
            play.Resources["SubtleButtonBorderBrush" + state] = new SolidColorBrush(Colors.Transparent);
        }
        play.HorizontalAlignment = HorizontalAlignment.Left;
        play.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(play, "Open Steam");
        ToolTipService.SetToolTip(play, "Open Steam");
        var playSurface = new Grid { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        playArt.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(playArt, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        playSurface.Children.Add(playArt);
        playSurface.Children.Add(play);
        header.Children.Add(playSurface);
        var status = new StackPanel { Spacing = 4 };
        status.VerticalAlignment = VerticalAlignment.Center;
        title = Text("Checking your puck...", 20);
        title.FontWeight = FontWeights.SemiBold;
        status.Children.Add(title);
        summary = Text("Reading USB and Steam status.", 13, true);
        status.Children.Add(summary);
        Grid.SetColumn(status, 1);
        header.Children.Add(status);
        layout.Children.Add(header);
        layout.Children.Add(notice);
        notice.Closed += delegate
        {
            notice.Visibility = Visibility.Collapsed;
        };
        var rows = new StackPanel();
        puck = Text("Checking...");
        steam = Text("Checking...");
        rows.Children.Add(Row(Symbol.Link, "Puck USB", null, puck));
        controller = Text("Checking...");
        rows.Children.Add(Row(new FontIcon { Glyph = "\uE7FC", FontSize = 20 }, "Controller", null, controller));
        string steamPath = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "steam.path"));
        var steamGlyph = (FrameworkElement)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='20' Height='20' Stretch='Uniform' Fill='{ThemeResource TextFillColorPrimaryBrush}' Data='" + steamPath + "' />");
        rows.Children.Add(Row(steamGlyph, "Steam", null, steam));
        careDetail = Text("", 12, true);
        care = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(care, "Automatic care");
        care.Toggled += async delegate
        {
            if (updating)
                return;
            requestedCare = care.IsOn;
            careRequested = DateTime.UtcNow;
            if (!await app.Command(care.IsOn ? "resume" : "pause"))
                requestedCare = null;
            Refresh();
        };
        rows.Children.Add(Row(Symbol.Refresh, "Automatic care", careDetail, care));
        var group = (Border)XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' CornerRadius='8' BorderThickness='1' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' />");
        group.Child = rows;
        layout.Children.Add(group);
        var hint = new StackPanel { Spacing = 6 };
        hint.Children.Add(Text("Use Steam's input test to check buttons and trackpads.", 12, true));
        checkedAt = Text("Waiting for the first check", 12, true);
        hint.Children.Add(checkedAt);
        layout.Children.Add(hint);
        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        check = ActionButton("Check now", Check, true);
        actions.Children.Add(check);
        ToolTipService.SetToolTip(check, "Checks USB and Steam. Automatic care can recover a detected USB fault.");
        var settings = ActionButton("Test inputs", async () =>
        {
            if (await app.Command("test-inputs"))
            {
                notice.Title = "Test in Steam";
                notice.Message = "Choose your Steam Controller, then Test Device Inputs and Begin Test.";
                notice.Severity = InfoBarSeverity.Informational;
                notice.Visibility = Visibility.Visible;
                notice.IsOpen = true;
            }
        });
        Grid.SetColumn(settings, 1);
        actions.Children.Add(settings);
        layout.Children.Add(actions);
        var trouble = new StackPanel { Spacing = 18 };
        trouble.Children.Add(TroubleRow("Receiver setup", Text("Connect one working puck. Updating protected setup asks for administrator approval.", 12, true), ActionButton("Set up receiver", async () => { await app.Command("enroll"); })));
        trouble.Children.Add(TroubleRow("USB recovery", Text("One-time administrator setup enables targeted USB repair.", 12, true), ActionButton("Enable recovery", async () => { await app.Command("setup-recovery"); })));
        reconnectNote = Text("Briefly disconnects controllers. Close games first.", 12, true);
        reconnect = ActionButton("Reconnect", async () => { if (await app.Command("reconnect")) { notice.Title = "Reconnect requested"; notice.Message = "The puck may disconnect briefly. Checking its USB status."; notice.Severity = InfoBarSeverity.Informational; notice.Visibility = Visibility.Visible; notice.IsOpen = true; } });
        AutomationProperties.SetName(reconnect, "Reconnect puck");
        trouble.Children.Add(TroubleRow("Reconnect puck", reconnectNote, reconnect));
        trouble.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
        var detailsButton = ActionButton("Open", () => { app.Open("details"); return Task.CompletedTask; });
        AutomationProperties.SetName(detailsButton, "Open technical details");
        trouble.Children.Add(TroubleRow("Technical details", Text("USB devices and recovery history.", 12, true), detailsButton));
        var logLinks = new Grid { ColumnSpacing = 12 };
        logLinks.ColumnDefinitions.Add(new ColumnDefinition());
        logLinks.ColumnDefinitions.Add(new ColumnDefinition());
        var logs = ActionButton("Connection logs", async () => { await app.Command("logs"); });
        logLinks.Children.Add(logs);
        var recoveryLogs = ActionButton("Recovery logs", async () => { await app.Command("recovery-logs"); });
        Grid.SetColumn(recoveryLogs, 1);
        logLinks.Children.Add(recoveryLogs);
        trouble.Children.Add(logLinks);
        troubleshooting = new Expander { Header = "Troubleshooting", Content = trouble, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        troubleshooting.Expanding += delegate
        {
            scrollToTroubleshooting = true;
        };
        layout.Children.Add(troubleshooting);
        layout.Children.Add(Link("About Puck & Play", () => app.Open("about")));
        scroller = Scroll(layout);
        scroller.LayoutUpdated += delegate
        {
            RevealTroubleshooting();
            UpdatePlayMotion();
        };
        scroller.ViewChanged += delegate
        {
            UpdatePlayMotion();
        };
        Body.Content = scroller;
        timer.Tick += delegate
        {
            Refresh();
        };
        timer.Start();
        Closed += delegate
        {
            timer.Stop();
            playArt.DisposeGraphics();
        };
        Activated += delegate
        {
            UpdatePlayMotion();
        };
        AppWindow.Changed += delegate
        {
            UpdatePlayMotion();
        };
        Root.ActualThemeChanged += delegate
        {
            Refresh();
        };
        Refresh();
    }
    static Grid Row(Symbol symbol, string name, TextBlock? detail, FrameworkElement value)
    {
        return Row(new SymbolIcon(symbol), name, detail, value);
    }
    static Grid Row(FrameworkElement icon, string name, TextBlock? detail, FrameworkElement value)
    {
        var row = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 14, MinHeight = 58 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        var label = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(Text(name));
        if (detail != null)
            label.Children.Add(detail);
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        return row;
    }
    static Grid TroubleRow(string name, TextBlock description, Button action)
    {
        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var heading = Text(name);
        heading.FontWeight = FontWeights.SemiBold;
        label.Children.Add(heading);
        label.Children.Add(description);
        row.Children.Add(label);
        action.MinWidth = 104;
        action.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(action, 1);
        row.Children.Add(action);
        return row;
    }
    internal void Refresh()
    {
        var snapshot = Bridge.Read();
        DateTime paused = snapshot?.PausedUntil ?? DateTime.MinValue;
        var view = ConnectionView.Create(snapshot, paused, DateTime.Now);
        title.Text = view.Title;
        summary.Text = view.Description;
        puck.Text = view.Puck;
        steam.Text = view.Steam;
        checkedAt.Text = view.Checked;
        var controllerView = ControllerConnection.Display(snapshot?.Controller, DateTime.Now);
        controller.Text = controllerView.Status;
        ToolTipService.SetToolTip(controller, controllerView.Detail);
        UpdatePlay(view);
        bool on = paused <= DateTime.UtcNow;
        if (requestedCare.HasValue && (requestedCare.Value == on || (DateTime.UtcNow - careRequested).TotalSeconds > 10))
            requestedCare = null;
        updating = true;
        care.IsOn = requestedCare ?? on;
        updating = false;
        careDetail.Text = on ? "Boot and wake checks" : "Paused until " + paused.ToLocalTime().ToShortTimeString();
        bool firmware = snapshot?.Devices?.Any(Scope.IsFirmware) == true;
        reconnect.IsEnabled = on && !firmware && snapshot != null;
        reconnectNote.Text = !on ? "Automatic care is paused. Resume it above to reconnect." : firmware ? "Wait for Steam to finish the firmware update." : "Briefly disconnects controllers. Close games first.";
        check.IsEnabled = !checking;
        check.Content = checking ? "Checking..." : "Check now";
    }
    void UpdatePlay(ConnectionView view)
    {
        playArt.SetState(checking ? "white" : StatusArt.Key(view.State));
        SetBrandOverride(checking ? "white" : null);
        string status = checking ? "Checking" : view.State == ConnectionState.Ready ? "Ready" : view.State == ConnectionState.Disconnected ? "Puck disconnected" : view.State == ConnectionState.Unknown ? "Status unknown" : "Needs attention";
        AutomationProperties.SetItemStatus(play, status);
        ToolTipService.SetToolTip(play, status + ". Open Steam");
    }
    void UpdatePlayMotion()
    {
        if (!scroller.IsLoaded || !play.IsLoaded)
        {
            playArt.MotionEnabled = false;
            return;
        }
        var bounds = play.TransformToVisual(scroller).TransformBounds(new Windows.Foundation.Rect(0, 0, play.ActualWidth, play.ActualHeight));
        bool visible = AppWindow.IsVisible && (!(AppWindow.Presenter is OverlappedPresenter presenter) || presenter.State != OverlappedPresenterState.Minimized);
        playArt.MotionEnabled = visible && bounds.Bottom > 0 && bounds.Top < scroller.ViewportHeight;
    }
    async Task Check()
    {
        checking = true;
        notice.IsOpen = false;
        notice.Visibility = Visibility.Collapsed;
        Refresh();
        string? before = Bridge.Read()?.At;
        try
        {
            if (await app.Command("check") && !Bridge.Preview)
            {
                DateTime until = DateTime.UtcNow.AddSeconds(12);
                while (Bridge.Read()?.At == before && DateTime.UtcNow < until)
                    await Task.Delay(250);
                if (Bridge.Read()?.At == before)
                    ShowError("The helper hasn't returned a new check yet. The last status may be out of date.");
            }
        }
        finally { checking = false; Refresh(); }
    }
    internal void ShowError(string message)
    {
        notice.Title = "Couldn't check just now";
        notice.Message = message;
        notice.Severity = InfoBarSeverity.Warning;
        notice.Visibility = Visibility.Visible;
        notice.IsOpen = true;
    }
    internal void PreviewPlayInteraction(string state)
    {
        if (play.IsLoaded)
            VisualStateManager.GoToState(play, state, false);
        else
            play.Loaded += delegate
            {
                VisualStateManager.GoToState(play, state, false);
            };
    }
    internal void ExpandTroubleshooting()
    {
        scrollToTroubleshooting = true;
        troubleshooting.IsExpanded = true;
        Root.DispatcherQueue.TryEnqueue(RevealTroubleshooting);
    }
    void RevealTroubleshooting()
    {
        if (!scrollToTroubleshooting || !scroller.IsLoaded || scroller.ScrollableHeight <= 0)
            return;
        scrollToTroubleshooting = false;
        scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
    }
}

internal sealed class DetailsWindow : FluentWindow
{
    readonly TextBox details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
    internal DetailsWindow(App app) : base("Technical details", 740, 640)
    {
        var layout = new Grid { Margin = new Thickness(24, 8, 24, 24), RowSpacing = 16 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = Text("Technical details", 24);
        title.FontWeight = FontWeights.SemiBold;
        layout.Children.Add(title);
        AutomationProperties.SetName(details, "USB and Steam technical details");
        details.Text = Bridge.Details(Bridge.Read());
        Grid.SetRow(details, 1);
        layout.Children.Add(details);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var feedback = new InfoBar { IsOpen = false, IsClosable = true };
        Grid.SetRow(feedback, 3);
        layout.Children.Add(feedback);
        buttons.Children.Add(ActionButton("Copy details", () =>
        {
            try
            {
                var data = new DataPackage();
                data.SetText(details.Text);
                Clipboard.SetContent(data);
                feedback.Title = "Details copied";
                feedback.Message = "";
                feedback.Severity = InfoBarSeverity.Success;
            }
            catch (COMException)
            {
                feedback.Title = "Clipboard is busy";
                feedback.Message = "Try Copy details again.";
                feedback.Severity = InfoBarSeverity.Warning;
            }
            feedback.IsOpen = true;
            return Task.CompletedTask;
        }));
        buttons.Children.Add(ActionButton("Connection logs", async () => { await app.Command("logs"); }));
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        Body.Content = layout;
        timer.Tick += delegate
        {
            if (details.FocusState == FocusState.Unfocused)
                details.Text = Bridge.Details(Bridge.Read());
        };
        timer.Start();
        Closed += delegate
        {
            timer.Stop();
        };
    }
}

internal sealed class MenuWindow : Window
{
    readonly App app;
    internal Grid Root { get; } = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
    internal MenuFlyout Flyout { get; } = new MenuFlyout { ShouldConstrainToRootBounds = false };
    bool opened;
    bool waiting, dismissed;
    internal MenuWindow(App app, int x, int y)
    {
        this.app = app;
        Content = Root;
        Title = "Puck & Play menu";
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        Build();
        Root.Loaded += delegate
        {
            if (!opened)
            {
                opened = true;
                // The first show can apply Windows' default size and centered
                // position, so place the host after its native window is ready.
                AppWindow.MoveAndResize(new RectInt32(x, y, 2, 2));
                var origin = AppWindow.Position;
                double scale = Root.XamlRoot.RasterizationScale;
                Flyout.ShowAt(Root, new FlyoutShowOptions
                {
                    Position = new Windows.Foundation.Point((x - origin.X) / scale, (y - origin.Y) / scale)
                });
            }
        };
        Flyout.Closed += delegate
        {
            if (waiting)
                dismissed = true;
            else
                Close();
        };
    }
    internal void Open()
    {
        Activate();
    }
    void Build()
    {
        var snapshot = Bridge.Read();
        bool paused = snapshot != null && snapshot.PausedUntil > DateTime.UtcNow;
        Flyout.Items.Add(new MenuFlyoutItem { Text = "Puck & Play", IsEnabled = false, FontWeight = FontWeights.SemiBold, Icon = new BitmapIcon { UriSource = new Uri(StatusArt.File(StatusArt.CurrentKey())), ShowAsMonochrome = false } });
        Flyout.Items.Add(new MenuFlyoutItem { Text = snapshot?.Status ?? "Checking your puck...", IsEnabled = false });
        Flyout.Items.Add(new MenuFlyoutSeparator());
        Flyout.Items.Add(Navigate("Open Puck & Play", Symbol.Home, () => app.Open("status")));
        Flyout.Items.Add(Command("Check now", Symbol.Refresh, "check"));
        Flyout.Items.Add(Command("Steam controller settings", Symbol.Setting, "steam"));
        Flyout.Items.Add(Command(paused ? "Resume automatic care" : "Pause for 30 minutes", paused ? Symbol.Play : Symbol.Pause, paused ? "resume" : "pause"));
        Flyout.Items.Add(new MenuFlyoutSeparator());
        var trouble = new MenuFlyoutSubItem { Text = "Troubleshooting", Icon = new SymbolIcon(Symbol.Help) };
        trouble.Items.Add(Navigate("Reconnect puck...", Symbol.Sync, () => app.Open("status", true)));
        trouble.Items.Add(Navigate("Technical details", Symbol.Document, () => app.Open("details")));
        trouble.Items.Add(new MenuFlyoutSeparator());
        trouble.Items.Add(Command("Connection logs", Symbol.OpenFile, "logs"));
        trouble.Items.Add(Command("USB recovery logs", Symbol.OpenFile, "recovery-logs"));
        Flyout.Items.Add(trouble);
        Flyout.Items.Add(Navigate("About Puck & Play", Symbol.Help, () => app.Open("about")));
        Flyout.Items.Add(new MenuFlyoutSeparator());
        Flyout.Items.Add(Command("Quit Puck & Play", Symbol.Cancel, "quit"));
    }
    static MenuFlyoutItem Navigate(string text, Symbol icon, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
        item.Click += delegate
        {
            action();
        };
        return item;
    }
    MenuFlyoutItem Command(string text, Symbol icon, string command)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new SymbolIcon(icon) };
        item.Click += async delegate
        {
            waiting = true;
            try
            {
                await app.Command(command);
            }
            finally { waiting = false; if (dismissed) Close(); }
        };
        return item;
    }
}
