using System;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BareBonesDownloader
{
    internal static class AppTheme
    {
        private static readonly Color DarkForm = ColorTranslator.FromHtml("#070F2B");
        private static readonly Color DarkSurface = ColorTranslator.FromHtml("#1B1A55");
        private static readonly Color DarkText = ColorTranslator.FromHtml("#F7F6FF");
        private static readonly Color DarkMuted = ColorTranslator.FromHtml("#9290C3");
        private static readonly Color DarkBorder = ColorTranslator.FromHtml("#9290C3");
        private static readonly Color DarkAccent = ColorTranslator.FromHtml("#9290C3");
        private static readonly Color DarkHover = ColorTranslator.FromHtml("#535C91");
        private static readonly object PreferenceSync = new object();
        private static readonly Dictionary<Form, PreferenceState> PreferenceStates = new Dictionary<Form, PreferenceState>();

        public static Icon CreateApplicationIcon()
        {
            using (System.IO.Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "BareBonesDownloader.AppIcon.ico"))
            {
                if (stream == null) return null;
                return new Icon(stream);
            }
        }

        public static void Apply(Form form, string theme)
        {
            bool dark = theme == "dark" && !SystemInformation.HighContrast;
            ApplyControl(form, dark);
            AttachPreferenceHandler(form, theme);
        }

        private static void AttachPreferenceHandler(Form form, string theme)
        {
            lock (PreferenceSync)
            {
                PreferenceState existing;
                if (PreferenceStates.TryGetValue(form, out existing))
                {
                    existing.Theme = theme;
                    return;
                }
                PreferenceState state = new PreferenceState();
                state.Theme = theme;
                state.Handler = delegate
                {
                    if (form.IsDisposed)
                    {
                        RemovePreferenceHandler(form);
                        return;
                    }
                    if (form.InvokeRequired)
                    {
                        try { form.BeginInvoke((MethodInvoker)delegate { ApplyLatest(form); }); }
                        catch (InvalidOperationException) { }
                    }
                    else ApplyLatest(form);
                };
                PreferenceStates.Add(form, state);
                SystemEvents.UserPreferenceChanged += state.Handler;
                form.Disposed += delegate { RemovePreferenceHandler(form); };
            }
        }

        private static void ApplyLatest(Form form)
        {
            string theme;
            lock (PreferenceSync)
            {
                PreferenceState state;
                if (!PreferenceStates.TryGetValue(form, out state)) return;
                theme = state.Theme;
            }
            if (!form.IsDisposed) ApplyControl(form, theme == "dark" && !SystemInformation.HighContrast);
        }

        private static void RemovePreferenceHandler(Form form)
        {
            lock (PreferenceSync)
            {
                PreferenceState state;
                if (!PreferenceStates.TryGetValue(form, out state)) return;
                SystemEvents.UserPreferenceChanged -= state.Handler;
                PreferenceStates.Remove(form);
            }
        }

        private static void ApplyControl(Control control, bool dark)
        {
            Color background = dark ? DarkForm : SystemColors.Control;
            Color surface = dark ? DarkSurface : SystemColors.Window;
            Color foreground = dark ? DarkText : SystemColors.ControlText;
            Color muted = dark ? DarkMuted : SystemColors.GrayText;
            Color accent = dark ? DarkAccent : SystemColors.Highlight;

            control.BackColor = background;
            control.ForeColor = foreground;
            TabControl tabs = control as TabControl;
            ListView list = control as ListView;
            ComboBox combo = control as ComboBox;
            Button button = control as Button;
            TextBox text = control as TextBox;

            if (text != null)
            {
                text.BackColor = surface;
                text.ForeColor = foreground;
            }
            if (combo != null)
            {
                combo.BackColor = surface;
                combo.ForeColor = foreground;
                if (dark)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.DrawItem -= DrawComboItem;
                    combo.DrawItem += DrawComboItem;
                }
                else
                {
                    combo.DrawMode = DrawMode.Normal;
                    combo.DrawItem -= DrawComboItem;
                }
            }
            if (button != null && dark)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = DarkBorder;
                button.FlatAppearance.MouseOverBackColor = DarkHover;
                button.FlatAppearance.MouseDownBackColor = DarkSurface;
            }
            else if (button != null)
            {
                button.UseVisualStyleBackColor = true;
                button.FlatStyle = FlatStyle.Standard;
            }
            if (list != null)
            {
                list.BackColor = surface;
                list.ForeColor = foreground;
                list.OwnerDraw = dark;
                list.DrawColumnHeader -= DrawColumnHeader;
                list.DrawItem -= DrawListItem;
                list.DrawSubItem -= DrawListSubItem;
                if (dark)
                {
                    list.DrawColumnHeader += DrawColumnHeader;
                    list.DrawItem += DrawListItem;
                    list.DrawSubItem += DrawListSubItem;
                }
            }
            if (tabs != null)
            {
                tabs.DrawMode = dark ? TabDrawMode.OwnerDrawFixed : TabDrawMode.Normal;
                tabs.DrawItem -= DrawTab;
                if (dark) tabs.DrawItem += DrawTab;
            }
            TabPage page = control as TabPage;
            if (page != null) page.UseVisualStyleBackColor = !dark;

            Label label = control as Label;
            if (label != null && !label.Enabled) label.ForeColor = muted;
            RadioButton radio = control as RadioButton;
            if (radio != null) radio.UseVisualStyleBackColor = !dark;
            CheckBox check = control as CheckBox;
            if (check != null) check.UseVisualStyleBackColor = !dark;

            foreach (Control child in control.Controls) ApplyControl(child, dark);
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color background = selected ? DarkAccent : DarkSurface;
            Color foreground = selected ? DarkForm : DarkText;
            using (SolidBrush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, ((ComboBox)sender).Items[e.Index].ToString(), e.Font,
                Rectangle.Inflate(e.Bounds, -3, 0), foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, DarkForm, background);
        }

        private static void DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(DarkSurface)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, Rectangle.Inflate(e.Bounds, -6, 0), DarkText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (Pen pen = new Pen(DarkBorder))
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        private static void DrawListItem(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = false;
        }

        private static void DrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color background = selected ? DarkAccent : DarkSurface;
            Color foreground = selected ? DarkForm : DarkText;
            using (SolidBrush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.Item.Font, Rectangle.Inflate(e.Bounds, -6, 0), foreground,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (selected && e.Item.Focused) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, DarkForm, background);
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            bool selected = e.Index == tabs.SelectedIndex;
            Color background = selected ? DarkSurface : DarkForm;
            using (SolidBrush brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font,
                Rectangle.Inflate(e.Bounds, -4, 0), selected ? DarkText : DarkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (e.State == DrawItemState.Focus) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, DarkText, background);
        }

        private sealed class PreferenceState
        {
            public string Theme;
            public UserPreferenceChangedEventHandler Handler;
        }
    }
}
