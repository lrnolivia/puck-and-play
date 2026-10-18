using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SteamControllerGuard
{
    static class Brand
    {
        public const string Name = "Puck & Play";
        public const string MenuName = "Puck && Play";
        public const string Version = "2.6.0";
        public static Icon LoadIcon(ConnectionState state)
        {
            string key = state == ConnectionState.Ready ? "green" : state == ConnectionState.Attention ? "yellow" : state == ConnectionState.Disconnected ? "red" : "grey";
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.Icon." + key))
            {
                if (source == null)
                    throw new FileNotFoundException("The " + key + " status icon is missing from this build.");
                using (var original = new Icon(source))
                    return (Icon)original.Clone();
            }
        }
        public static Image LoadArtwork()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.Artwork"))
            {
                if (source == null)
                    throw new FileNotFoundException("The puck artwork is missing from this build.");
                using (var original = Image.FromStream(source))
                    return new Bitmap(original);
            }
        }
        public static Icon LoadIcon()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("PuckPlay.Icon"))
            {
                if (source == null)
                    throw new FileNotFoundException("The Puck & Play icon is missing from this build.");
                using (var original = new Icon(source))
                    return (Icon)original.Clone();
            }
        }
        public static Image MenuGlyph(string name, Color color)
        {
            var bitmap = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(color, 1.65f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (name == "status")
                {
                    g.DrawEllipse(pen, 3, 3, 14, 14);
                    g.DrawLine(pen, 10, 8, 10, 14);
                    g.DrawEllipse(pen, 9.5f, 5.4f, 1, 1);
                }
                else if (name == "check")
                {
                    g.DrawArc(pen, 4, 4, 12, 12, 40, 290);
                    g.DrawLines(pen, new[] { new PointF(12, 3), new PointF(16, 4), new PointF(15, 8) });
                }
                else if (name == "link")
                {
                    g.DrawArc(pen, 2, 6, 10, 8, 80, 280);
                    g.DrawArc(pen, 8, 6, 10, 8, 260, 280);
                    g.DrawLine(pen, 7, 10, 13, 10);
                }
                else if (name == "settings")
                {
                    g.DrawLine(pen, 3, 6, 17, 6);
                    g.DrawLine(pen, 3, 14, 17, 14);
                    using (var brush = new SolidBrush(color))
                    {
                        g.FillEllipse(brush, 6, 3.5f, 5, 5);
                        g.FillEllipse(brush, 11, 11.5f, 5, 5);
                    }
                }
                else if (name == "pause")
                {
                    using (var thick = new Pen(color, 3))
                    {
                        g.DrawLine(thick, 7, 5, 7, 15);
                        g.DrawLine(thick, 13, 5, 13, 15);
                    }
                }
                else if (name == "play")
                {
                    using (var brush = new SolidBrush(color))
                        g.FillPolygon(brush, new[] { new Point(6, 4), new Point(16, 10), new Point(6, 16) });
                }
                else if (name == "folder")
                {
                    g.DrawLines(pen, new[] { new Point(3, 6), new Point(3, 4), new Point(8, 4), new Point(10, 6), new Point(17, 6), new Point(17, 16), new Point(3, 16), new Point(3, 6), new Point(17, 6) });
                }
                else if (name == "help")
                {
                    g.DrawEllipse(pen, 3, 3, 14, 14);
                    g.DrawArc(pen, 7, 5, 6, 6, 175, 230);
                    g.DrawLine(pen, 10, 10, 10, 12);
                    g.DrawEllipse(pen, 9.6f, 14, 0.8f, 0.8f);
                }
                else if (name == "quit")
                {
                    g.DrawArc(pen, 3, 3, 14, 14, 20, 320);
                    g.DrawLine(pen, 10, 5, 10, 10);
                }
            }
            return bitmap;
        }
    }

    sealed class MenuActions
    {
        public Action Details, Technical, Check, Reconnect, Steam, Pause, Logs, RecoveryLogs, About, Quit;
    }
    static class MenuBuilder
    {
        public static ContextMenuStrip Create(bool paused, MenuActions actions, out ToolStripMenuItem statusItem, out ToolStripMenuItem pauseItem)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = true, ImageScalingSize = new Size(20, 20), Padding = new Padding(5, 7, 5, 7), Font = new Font("Segoe UI", 10) };
            var title = new ToolStripLabel(Brand.MenuName) { Font = new Font("Segoe UI Semibold", 11), Padding = new Padding(3, 3, 3, 0) };
            menu.Items.Add(title);
            statusItem = new ToolStripMenuItem("Checking your puck...") { Enabled = false, Padding = new Padding(3, 0, 3, 6) };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            var details = Item("Open Puck && Play", "status", actions.Details);
            details.Font = new Font("Segoe UI Semibold", 10);
            menu.Items.Add(details);
            var check = Item("Check now", "check", actions.Check);
            check.ToolTipText = "Checks USB and Steam; automatic care can recover a detected USB fault.";
            menu.Items.Add(check);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Steam controller settings", "settings", actions.Steam));
            pauseItem = Item(paused ? "Resume automatic care" : "Pause for 30 minutes", paused ? "play" : "pause", actions.Pause);
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            var diagnostics = Item("Troubleshooting", "folder", null);
            var reconnect = Item("Reconnect puck...", "link", actions.Reconnect);
            reconnect.ToolTipText = "Briefly disconnects controllers using this puck. Close games first.";
            diagnostics.DropDownItems.Add(reconnect);
            diagnostics.DropDownItems.Add(Item("Technical details", "status", actions.Technical));
            diagnostics.DropDownItems.Add(new ToolStripSeparator());
            diagnostics.DropDownItems.Add("Connection logs", null, delegate
            {
                if (actions.Logs != null)
                    actions.Logs();
            });
            diagnostics.DropDownItems.Add("USB recovery logs", null, delegate
            {
                if (actions.RecoveryLogs != null)
                    actions.RecoveryLogs();
            });
            menu.Items.Add(diagnostics);
            menu.Items.Add(Item("About Puck && Play", "help", actions.About));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("Quit Puck && Play", "quit", actions.Quit));
            return menu;
        }
        static ToolStripMenuItem Item(string label, string glyph, Action action)
        {
            return new ToolStripMenuItem(label, Brand.MenuGlyph(glyph, SystemColors.MenuText), delegate { if (action != null) action(); }) { Padding = new Padding(3, 3, 3, 3) };
        }
        public static void Preview(string path)
        {
            ToolStripMenuItem status, pause;
            using (var menu = Create(false, new MenuActions(), out status, out pause))
            {
                status.Text = "Puck ready; Steam running";
                menu.PerformLayout();
                menu.Size = menu.GetPreferredSize(Size.Empty);
                menu.CreateControl();
                using (var bitmap = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                foreach (ToolStripItem item in menu.Items)
                {
                    Console.WriteLine(item is ToolStripSeparator ? "---" : item.Text.Replace("&&", "&"));
                    var entry = item as ToolStripMenuItem;
                    if (entry != null)
                        foreach (ToolStripItem child in entry.DropDownItems)
                            Console.WriteLine("  " + child.Text);
                }
            }
        }
    }
    sealed partial class GuardContext
    {
        ContextMenuStrip CreateMenu()
        {
            return MenuBuilder.Create(pausedUntil > DateTime.UtcNow, new MenuActions
            {
                Details = ShowStatus,
                Technical = ShowTechnical,
                Check = CheckNow,
                About = ShowAbout,
                Reconnect = ShowReconnect,
                Steam = delegate { Open("steam://settings/controller"); },
                Pause = Pause,
                Logs = delegate { Open(Files.UserDir); },
                RecoveryLogs = delegate { Open(Files.MachineDir); },
                Quit = ExitThread
            }, out statusItem, out pauseItem);
        }
    }
}
