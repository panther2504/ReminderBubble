using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ReminderBubble.Windows;

/// <summary>Notification-area icon: left click opens the panel, right click shows a dark menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _toggle;

    public TrayIcon(Action open, Action newReminder, Action toggleBubble, Action settings, Action quit)
    {
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f),
            BackColor = Drawing.Color.FromArgb(21, 21, 33),
            ForeColor = Drawing.Color.FromArgb(241, 241, 248),
            Padding = new Forms.Padding(2),
        };
        Forms.ToolStripMenuItem Item(string text, Action a) { var i = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(6, 5, 6, 5) }; i.Click += (_, _) => a(); return i; }
        _toggle = Item("Hide bubble", toggleBubble);
        menu.Items.Add(Item("Open reminders", open));
        menu.Items.Add(Item("New reminder", newReminder));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_toggle);
        menu.Items.Add(Item("Settings", settings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Item("Quit", quit));

        Drawing.Icon icon;
        try { icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? "") ?? Drawing.SystemIcons.Application; }
        catch { icon = Drawing.SystemIcons.Application; }

        _icon = new Forms.NotifyIcon { Icon = icon, Text = "Reminder Bubble", ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    public void SetBubbleVisible(bool visible) => _toggle.Text = visible ? "Hide bubble" : "Show bubble";

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    sealed class DarkColors : Forms.ProfessionalColorTable
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(21, 21, 33);
        static readonly Drawing.Color Hover = Drawing.Color.FromArgb(48, 48, 70);
        static readonly Drawing.Color Line = Drawing.Color.FromArgb(52, 52, 72);
        public override Drawing.Color ToolStripDropDownBackground => Bg;
        public override Drawing.Color MenuBorder => Line;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color ImageMarginGradientBegin => Bg;
        public override Drawing.Color ImageMarginGradientMiddle => Bg;
        public override Drawing.Color ImageMarginGradientEnd => Bg;
        public override Drawing.Color SeparatorDark => Line;
        public override Drawing.Color SeparatorLight => Line;
    }
}
