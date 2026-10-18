using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Puck & Play setup")]
[assembly: AssemblyProduct("Puck & Play")]
[assembly: AssemblyCompany("loew.fi")]
[assembly: AssemblyVersion("1.2.3.0")]

namespace PuckPlay.Setup
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--verify-package")
                {
                    Extract(Path.GetFullPath(args[1]));
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerWindow());
                return 0;
            }
            catch (Exception error)
            {
                if (args.Length == 0)
                    MessageBox.Show(error.Message, "Puck & Play setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        internal static void Extract(string destination)
        {
            if (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The extraction folder must not be redirected.");
            if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length != 0)
                throw new IOException("The extraction folder must be empty.");
            Directory.CreateDirectory(destination);
            string prefix = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.Payload"))
            {
                using (var hash = SHA256.Create())
                {
                    string digest = BitConverter.ToString(hash.ComputeHash(payload)).Replace("-", "");
                    if (!String.Equals(digest, PackageInfo.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The installer payload is damaged. Download a fresh copy from GitHub Releases.");
                }
                payload.Position = 0;
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read, false))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid package path.");
                        if (String.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var source = entry.Open())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                            source.CopyTo(output);
                    }
                }
            }
            foreach (string line in File.ReadAllLines(Path.Combine(destination, "SHA256SUMS.txt")))
            {
                int separator = line.IndexOf("  ", StringComparison.Ordinal);
                if (separator != 64)
                    throw new InvalidDataException("Invalid package checksum manifest.");
                string path = Path.GetFullPath(Path.Combine(destination, line.Substring(separator + 2)));
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid checksum path.");
                using (var input = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
                    if (!String.Equals(actual, line.Substring(0, separator), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A package file did not match its checksum.");
                }
            }
        }

        internal static void Install(bool startup, bool recovery)
        {
            string staging = Path.Combine(Path.GetTempPath(), "PuckAndPlay-Setup-" + Guid.NewGuid().ToString("N"));
            try
            {
                Extract(staging);
                string script = Path.Combine(staging, "Install-App.ps1");
                string arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
                if (startup)
                    arguments += " -Startup";
                if (recovery)
                    arguments += " -EnableRecovery";
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"), arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(output, errors);
                    if (process.ExitCode != 0)
                        throw new IOException(errors.Result.Length == 0 ? "Setup could not finish. Your existing settings were kept." : errors.Result);
                }
            }
            finally
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(staging));
                string expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
                if (String.Equals(parent, expected, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(staging).StartsWith("PuckAndPlay-Setup-", StringComparison.Ordinal))
                {
                    try
                    {
                        Directory.Delete(staging, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
    }

    sealed class InstallerWindow : Form
    {
        readonly List<IDisposable> owned = new List<IDisposable>();
        readonly CheckBox startup = new CheckBox { Text = "Open automatically when I sign in", Checked = true, AutoSize = true };
        readonly CheckBox recovery = new CheckBox { Text = "Enable automatic USB recovery", Checked = true, AutoSize = true };
        readonly Button install = new Button { Text = "Install", Width = 104, Height = 36 };
        readonly Label status = new Label { AutoSize = false, Height = 52, Dock = DockStyle.Fill };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false, Height = 8 };
        bool installing;

        internal InstallerWindow()
        {
            SuspendLayout();
            Text = "Install Puck & Play";
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(500, 500);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.Icon"))
            using (var source = new Icon(stream))
                Icon = (Icon)source.Clone();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var header = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 24) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Image artwork;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.SetupArtwork"))
            using (var source = Image.FromStream(stream))
                artwork = new Bitmap(source);
            owned.Add(artwork);
            header.Controls.Add(new PictureBox { Image = artwork, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(72, 72), Margin = Padding.Empty, TabStop = false, AccessibleName = "Puck and green play crystal" }, 0, 0);
            var wordmark = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            var headingFont = new Font("Segoe UI Semibold", 24);
            owned.Add(headingFont);
            wordmark.Controls.Add(new AlignedLabel { Text = "Puck & Play", Font = headingFont, AutoSize = true, Margin = Padding.Empty });
            wordmark.Controls.Add(new AlignedLabel { Text = "A little care. More play.", AutoSize = true, Margin = new Padding(0, 4, 0, 0) });
            header.Controls.Add(wordmark, 1, 0);
            layout.Controls.Add(header);
            layout.Controls.Add(new Label { Text = "Keep your Steam Controller puck ready, see when your controller connects, and test your inputs in Steam.", AutoSize = true, MaximumSize = new Size(438, 0), Margin = new Padding(0, 0, 0, 18) });
            layout.Controls.Add(startup);
            layout.Controls.Add(recovery);
            layout.Controls.Add(new Label { Text = "Recovery asks for administrator approval once. Connect your puck during setup. You can enable it later.", AutoSize = true, MaximumSize = new Size(438, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 14) });
            layout.Controls.Add(status);
            layout.Controls.Add(progress);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(20, 0, 20, 12) };
            var cancel = new Button { Text = "Cancel", Width = 88, Height = 36 };
            footer.Controls.Add(install);
            footer.Controls.Add(cancel);
            Controls.Add(layout);
            Controls.Add(footer);
            AcceptButton = install;
            CancelButton = cancel;
            cancel.Click += delegate
            {
                Close();
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (installing)
                    e.Cancel = true;
            };
            install.Click += async delegate
            {
                if (install.Text == "Done")
                {
                    Close();
                    return;
                }
                installing = true;
                install.Enabled = false;
                cancel.Enabled = false;
                progress.Visible = true;
                status.Text = "Installing Puck & Play…";
                bool startAtLogin = startup.Checked, enableRecovery = recovery.Checked;
                try
                {
                    await Task.Run(() => Program.Install(startAtLogin, enableRecovery));
                    status.Text = "Puck & Play is installed. If recovery wasn't enabled, you can finish that step in Troubleshooting.";
                    install.Text = "Done";
                }
                catch (Exception error) { status.Text = "Setup couldn't finish."; MessageBox.Show(this, error.Message, "Puck & Play setup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { installing = false; install.Enabled = true; cancel.Enabled = true; progress.Visible = false; }
            };
            ResumeLayout(true);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            { foreach (var resource in owned) resource.Dispose(); owned.Clear(); }
        }
    }
    sealed class AlignedLabel:Label
    {
        const TextFormatFlags Flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix;
        public override Size GetPreferredSize(Size proposedSize)
        {
            return TextRenderer.MeasureText(Text,Font,Size.Empty,Flags);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,Flags);
        }
    }
}
