using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SteamControllerGuard
{
    sealed class WindowTheme
    {
        public Color Background, Text, Muted, Rule, Button, Accent, AccentText, Warning;
        public bool HighContrast;
        public static WindowTheme Current(bool? darkOverride)
        {
            if (SystemInformation.HighContrast && !darkOverride.HasValue)
                return new WindowTheme
                {
                    HighContrast = true,
                    Background = SystemColors.Window,
                    Text = SystemColors.WindowText,
                    Muted = SystemColors.WindowText,
                    Rule = SystemColors.WindowText,
                    Button = SystemColors.Control,
                    Accent = SystemColors.Highlight,
                    AccentText = SystemColors.HighlightText,
                    Warning = SystemColors.WindowText
                };
            bool dark = false;
            try
            {
                dark = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1), CultureInfo.InvariantCulture) == 0;
            }
            catch (System.Security.SecurityException) { }
            if (darkOverride.HasValue)
                dark = darkOverride.Value;
            return dark
                ? new WindowTheme
                {
                    Background = Color.FromArgb(31, 34, 40),
                    Text = Color.FromArgb(242, 243, 245),
                    Muted = Color.FromArgb(190, 197, 207),
                    Rule = Color.FromArgb(78, 85, 96),
                    Button = Color.FromArgb(43, 48, 57),
                    Accent = Color.FromArgb(222, 192, 133),
                    AccentText = Color.FromArgb(34, 32, 27),
                    Warning = Color.FromArgb(244, 202, 137)
                }
                : new WindowTheme
                {
                    Background = Color.FromArgb(247, 246, 242),
                    Text = Color.FromArgb(38, 41, 47),
                    Muted = Color.FromArgb(87, 94, 106),
                    Rule = Color.FromArgb(176, 179, 182),
                    Button = Color.FromArgb(239, 238, 233),
                    Accent = Color.FromArgb(68, 61, 49),
                    AccentText = Color.White,
                    Warning = Color.FromArgb(125, 72, 15)
                };
        }
    }

    class PuckWindow : Form
    {
        protected readonly WindowTheme Theme;
        readonly List<IDisposable> owned = new List<IDisposable>();
        protected PuckWindow(string title, Size size, bool? dark)
        {
            SuspendLayout();
            Theme = WindowTheme.Current(dark);
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Type(10, FontStyle.Regular);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = size;
            Icon = Brand.LoadIcon();
            owned.Add(Icon);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            KeyPreview = true;
            KeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };
        }
        protected Font Type(float size, FontStyle style)
        {
            var font = new Font("Segoe UI", size, style);
            owned.Add(font);
            return font;
        }
        protected Label TextLabel(string text, Rectangle bounds, float size, Color color, FontStyle style)
        {
            return new Label
            {
                Text = text,
                Bounds = bounds,
                Font = Type(size, style),
                ForeColor = color,
                UseMnemonic = false,
                UseCompatibleTextRendering = true,
                AutoSize = false
            };
        }
        protected PictureBox Artwork(Rectangle bounds)
        {
            var artwork = Brand.LoadArtwork();
            owned.Add(artwork);
            return new PictureBox
            {
                Image = artwork,
                Bounds = bounds,
                SizeMode = PictureBoxSizeMode.Zoom,
                AccessibleName = "Steam Controller puck with an etched play triangle",
                AccessibleRole = AccessibleRole.Graphic,
                TabStop = false
            };
        }
        protected Button Button(string text, Rectangle bounds, Action action, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Bounds = bounds,
                AccessibleName = text.Replace("&", ""),
                BackColor = primary ? Theme.Accent : Theme.Button,
                ForeColor = primary ? Theme.AccentText : Theme.Text,
                FlatStyle = Theme.HighContrast ? FlatStyle.System : FlatStyle.Flat,
                UseVisualStyleBackColor = Theme.HighContrast,
                UseCompatibleTextRendering = true,
                TabStop = true
            };
            button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Rule;
            button.Click += delegate
            {
                if (action != null)
                    action();
            };
            return button;
        }
        protected LinkLabel Link(string text, Rectangle bounds, Action action)
        {
            var link = new LinkLabel
            {
                Text = text,
                Bounds = bounds,
                LinkColor = Theme.Text,
                ActiveLinkColor = Theme.Muted,
                VisitedLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline,
                UseMnemonic = false,
                UseCompatibleTextRendering = true,
                TabStop = true
            };
            link.LinkClicked += delegate
            {
                if (action != null)
                    action();
            };
            return link;
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                foreach (var value in owned)
                    value.Dispose();
        }
    }

    sealed class AboutWindow : PuckWindow
    {
        public AboutWindow(bool? dark) : base("About " + Brand.Name, new Size(440, 444), dark)
        {
            MinimizeBox = false;
            Controls.Add(Artwork(new Rectangle(78, 26, 284, 134)));
            var name = TextLabel(Brand.Name, new Rectangle(24, 182, 392, 43), 24, Theme.Text, FontStyle.Bold);
            name.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(name);
            var version = TextLabel("Version " + Brand.Version, new Rectangle(24, 231, 392, 26), 10, Theme.Muted, FontStyle.Regular);
            version.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(version);
            var hello = TextLabel("A little care. More play.", new Rectangle(30, 278, 380, 28), 13, Theme.Text, FontStyle.Regular);
            hello.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(hello);
            var description = TextLabel("A little help for your Steam Controller puck.\r\nKeeps Steam ready and helps recover USB faults.", new Rectangle(30, 318, 380, 48), 10, Theme.Muted, FontStyle.Regular);
            description.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(description);
            var close = Button("&Close", new Rectangle(168, 388, 104, 34), Close, false);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            ResumeLayout(true);
        }
    }

    sealed class StatusWindow : PuckWindow
    {
        readonly Label heading, summary, puckValue, steamValue, careValue, checkedLabel, reconnectNote;
        readonly Button checkButton, reconnectButton;
        readonly Panel troubleshooting;
        readonly LinkLabel troubleshootLink, aboutLink;
        readonly ToolTip tips = new ToolTip();
        bool expanded;
        public StatusWindow(MenuActions actions, bool? dark) : base(Brand.Name, new Size(464, 568), dark)
        {
            Controls.Add(Artwork(new Rectangle(108, 23, 248, 117)));
            heading = TextLabel("Checking your puck...", new Rectangle(28, 166, 408, 36), 19, Theme.Text, FontStyle.Bold);
            Controls.Add(heading);
            summary = TextLabel("Reading USB and Steam status.", new Rectangle(28, 213, 408, 50), 10.5f, Theme.Muted, FontStyle.Regular);
            Controls.Add(summary);
            var rule = new Panel { Bounds = new Rectangle(28, 279, 408, 1), BackColor = Theme.Rule };
            Controls.Add(rule);
            puckValue = Row("Puck USB", 294);
            steamValue = Row("Steam", 330);
            careValue = Row("Automatic care", 366);
            var note = TextLabel("Receiver status only. Test controller input in Steam.", new Rectangle(28, 411, 408, 25), 9, Theme.Muted, FontStyle.Regular);
            Controls.Add(note);
            checkedLabel = TextLabel("Waiting for the first check", new Rectangle(28, 439, 408, 23), 9, Theme.Muted, FontStyle.Regular);
            Controls.Add(checkedLabel);
            checkButton = Button("&Check now", new Rectangle(28, 479, 198, 38), actions.Check, true);
            Controls.Add(checkButton);
            tips.SetToolTip(checkButton, "Checks USB and Steam. Automatic care can recover a detected USB fault.");
            var steam = Button("&Steam settings", new Rectangle(238, 479, 198, 38), actions.Steam, false);
            Controls.Add(steam);
            troubleshootLink = Link("Troubleshooting", new Rectangle(28, 537, 180, 24), delegate
            {
                ExpandTroubleshooting(!expanded);
            });
            Controls.Add(troubleshootLink);
            aboutLink = Link("About", new Rectangle(389, 537, 47, 24), actions.About);
            Controls.Add(aboutLink);
            troubleshooting = new Panel { Bounds = new Rectangle(28, 579, 408, 155), Visible = false };
            reconnectNote = TextLabel("Reconnect briefly disconnects controllers on this puck. Close games first.", new Rectangle(0, 0, 408, 46), 10, Theme.Muted, FontStyle.Regular);
            troubleshooting.Controls.Add(reconnectNote);
            reconnectButton = Button("&Reconnect puck", new Rectangle(0, 58, 198, 36), actions.Reconnect, false);
            troubleshooting.Controls.Add(reconnectButton);
            troubleshooting.Controls.Add(Link("Technical details", new Rectangle(0, 110, 198, 24), actions.Technical));
            Controls.Add(troubleshooting);
            UpdateState(null, DateTime.MinValue, false);
            ResumeLayout(true);
        }
        Label Row(string label, int y)
        {
            Controls.Add(TextLabel(label, new Rectangle(28, y, 174, 27), 10.5f, Theme.Muted, FontStyle.Regular));
            var value = TextLabel("Checking...", new Rectangle(202, y, 234, 27), 10.5f, Theme.Text, FontStyle.Regular);
            value.TextAlign = ContentAlignment.TopRight;
            Controls.Add(value);
            return value;
        }
        public void ExpandTroubleshooting(bool show)
        {
            if (expanded == show)
                return;
            expanded = show;
            troubleshooting.Visible = show;
            ClientSize = new Size(ClientSize.Width, show ? troubleshooting.Bottom + 8 : aboutLink.Bottom + 7);
            troubleshootLink.Text = show ? "Hide troubleshooting" : "Troubleshooting";
            if (show)
                reconnectButton.Focus();
        }
        public void UpdateState(Snapshot snapshot, DateTime pausedUntil, bool busy)
        {
            var view = ConnectionView.Create(snapshot, pausedUntil, DateTime.Now);
            heading.Text = view.Title;
            summary.Text = view.Description;
            summary.ForeColor = view.NeedsAttention ? Theme.Warning : Theme.Muted;
            puckValue.Text = view.Puck;
            steamValue.Text = view.Steam;
            careValue.Text = view.Care;
            checkedLabel.Text = view.Checked;
            SetChecking(busy);
            bool paused = pausedUntil > DateTime.UtcNow;
            bool firmware = snapshot != null && (snapshot.Devices ?? new List<UsbDevice>()).Any(Scope.IsFirmware);
            reconnectButton.Enabled = !paused && snapshot != null && !firmware;
            reconnectNote.Text = paused ? "Automatic care is paused. Resume it from the tray menu before reconnecting."
                : firmware ? "Steam is updating firmware. Wait for it to finish before reconnecting."
                : "Reconnect briefly disconnects controllers on this puck. Close games first.";
            tips.SetToolTip(reconnectButton, paused ? "Resume automatic care from the tray menu before reconnecting." : "Briefly disconnects controllers on this puck. Close games first.");
        }
        public void SetChecking(bool busy)
        {
            checkButton.Enabled = !busy;
            checkButton.Text = busy ? "Checking..." : "&Check now";
        }
        public void CheckFailed()
        {
            SetChecking(false);
            heading.Text = "Couldn't check just now";
            summary.Text = "The last status may be out of date. Choose Check now to try again.";
            summary.ForeColor = Theme.Warning;
        }
        internal void InvokeCheckForPreview()
        {
            checkButton.PerformClick();
        }
        internal void VerifyAccessibleStatus()
        {
            foreach (var label in Controls.OfType<Label>())
                if (label.AccessibilityObject.Name != label.Text)
                    throw new InvalidOperationException("Accessible status differs from visible text: " + label.Text);
            if (troubleshootLink.AccessibilityObject.Name != troubleshootLink.Text)
                throw new InvalidOperationException("Troubleshooting accessibility name is stale.");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                tips.Dispose();
            base.Dispose(disposing);
        }
    }

    sealed class DetailsWindow : PuckWindow
    {
        readonly TextBox details;
        public DetailsWindow(string text) : base(Brand.Name + " - Technical details", new Size(720, 580), null)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = new Size(480, 340);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(12) };
            var copy = Button("&Copy details", new Rectangle(12, 12, 130, 34), delegate
            {
                try
                {
                    Clipboard.SetText(details.Text);
                }
                catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "The clipboard is busy. Try again.", Brand.Name); }
            }, false);
            footer.Controls.Add(copy);
            Controls.Add(footer);
            details = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Background,
                ForeColor = Theme.Text,
                Text = text,
                AccessibleName = "USB and Steam technical details"
            };
            var padding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            padding.Controls.Add(details);
            Controls.Add(padding);
            padding.BringToFront();
            ResumeLayout(true);
        }
        public void UpdateDetails(string text)
        {
            // Keep an active selection stable while the user reads or copies diagnostics.
            if (!details.Focused && details.Text != text)
                details.Text = text;
        }
    }

    static class UiPreview
    {
        public static Snapshot Example(bool present)
        {
            return new Snapshot
            {
                At = DateTime.Now.ToString("o"),
                Version = Brand.Version,
                Status = present ? "Puck ready; Steam running" : "Puck absent: waiting for USB connection",
                SteamRunning = true,
                RepairService = "Installed",
                Devices = present ? new List<UsbDevice> { new UsbDevice { Id = Scope.PuckId, Present = true } } : new List<UsbDevice>()
            };
        }
        static void Save(Form window, string path)
        {
            using (window)
            {
                window.ShowInTaskbar = false;
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-12000, -12000);
                window.Show();
                window.PerformLayout();
                Application.DoEvents();
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                window.Close();
            }
        }
        public static void Render(string directory)
        {
            Directory.CreateDirectory(directory);
            Save(new AboutWindow(true), Path.Combine(directory, "about-dark.png"));
            Save(new AboutWindow(false), Path.Combine(directory, "about-light.png"));
            var healthy = new StatusWindow(new MenuActions(), true);
            healthy.UpdateState(Example(true), DateTime.MinValue, false);
            Save(healthy, Path.Combine(directory, "status-ready.png"));
            var absent = new StatusWindow(new MenuActions(), false);
            absent.UpdateState(Example(false), DateTime.MinValue, false);
            Save(absent, Path.Combine(directory, "status-absent.png"));
            var paused = new StatusWindow(new MenuActions(), true);
            paused.UpdateState(Example(true), DateTime.UtcNow.AddMinutes(30), false);
            paused.ExpandTroubleshooting(true);
            Save(paused, Path.Combine(directory, "status-paused.png"));
            var failed = new StatusWindow(new MenuActions(), true);
            failed.UpdateState(Example(true), DateTime.MinValue, false);
            failed.CheckFailed();
            Save(failed, Path.Combine(directory, "status-check-failed.png"));
            var fault = Example(true);
            fault.Devices[0].Problem = 43;
            fault.Status = "Puck USB error: unplug/replug needed";
            var error = new StatusWindow(new MenuActions(), true);
            error.UpdateState(fault, DateTime.MinValue, false);
            error.ExpandTroubleshooting(true);
            Save(error, Path.Combine(directory, "status-usb-error.png"));
            int clicked = 0;
            using (var interaction = new StatusWindow(new MenuActions { Check = delegate { clicked++; } }, true))
            {
                interaction.ShowInTaskbar = false;
                interaction.StartPosition = FormStartPosition.Manual;
                interaction.Location = new Point(-12000, -12000);
                interaction.Show();
                interaction.InvokeCheckForPreview();
                if (clicked != 1)
                    throw new InvalidOperationException("Check action is not connected.");
                interaction.UpdateState(Example(true), DateTime.MinValue, false);
                interaction.VerifyAccessibleStatus();
                interaction.CheckFailed();
                interaction.VerifyAccessibleStatus();
                interaction.ExpandTroubleshooting(true);
                interaction.VerifyAccessibleStatus();
                interaction.SetChecking(true);
                interaction.InvokeCheckForPreview();
                if (clicked != 1)
                    throw new InvalidOperationException("Busy check action must be disabled.");
                interaction.Close();
            }
            Console.WriteLine("Rendered native About/status states at the current Windows DPI; check action, busy guard and current accessible status passed. No hardware was changed.");
        }
    }
}
