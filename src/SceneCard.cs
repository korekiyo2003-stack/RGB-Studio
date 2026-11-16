using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// One scene in the list on the left: name, idea, the three fan zones, and the memory sticks playing the
// show that would be sent for this scene (same frame function as the hardware thread).
internal sealed class SceneCard : Control
{
    private static readonly Color Surface = Color.FromArgb(23, 30, 40);
    private static readonly Color SurfaceHover = Color.FromArgb(28, 37, 49);
    private static readonly Color SurfaceShown = Color.FromArgb(31, 42, 56);
    private static readonly Color Well = Color.FromArgb(13, 17, 23);
    private static readonly Color Ink = Color.FromArgb(236, 239, 248);
    private static readonly Color Muted = Color.FromArgb(160, 172, 196);
    private static readonly Color Accent = Color.FromArgb(189, 232, 221);
    private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
    private static readonly Font SmallFont = new Font("Microsoft YaHei UI", 9f);
    private static readonly Font ChipFont = new Font("Microsoft YaHei UI", 8.5f);
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string[] ZoneNames = { "底排", "侧排", "后置" };

    public readonly Scene Scene;
    public readonly CheckBox Include;
    public readonly Button ApplyButton;
    private bool hover, shown, current;
    private double flashTime = 0;

    public void Flash()
    {
        flashTime = Clock.Elapsed.TotalSeconds;
        Invalidate();
    }

    public SceneCard(Scene scene, bool included)
    {
        Scene = scene;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(490, 104);
        Margin = new Padding(0, 0, 0, 8);
        BackColor = Surface;
        Cursor = Cursors.Hand;
        AccessibleName = scene.name;
        Include = new CheckBox { Text = "轮换", Checked = included, ForeColor = Muted, BackColor = Surface, AutoSize = true,
            Font = SmallFont, AccessibleName = scene.name + " 参与轮换", Cursor = Cursors.Default };
        ApplyButton = new Button { Text = "应用", Size = new Size(72, 32), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            Font = SmallFont, AccessibleName = "应用 " + scene.name };
        ApplyButton.FlatAppearance.BorderSize = 0;
        Controls.Add(Include);
        Controls.Add(ApplyButton);
        Place();
    }

    public bool Shown { get { return shown; } set { if (shown != value) { shown = value; Refit(); } } }
    public bool Current
    {
        get { return current; }
        set
        {
            if (current != value)
            {
                current = value;
                if (current) Flash();
                Invalidate();
            }
        }
    }

    // Where the animated memory chip is, so the list can repaint only that.
    public Rectangle MemoryArea { get { return new Rectangle(18 + 3 * 92, 66, Math.Max(60, Width - 18 - 3 * 92 - 12), 28); } }

    private Color Fill { get { return shown ? SurfaceShown : hover ? SurfaceHover : Surface; } }

    private void Refit()
    {
        Include.BackColor = Fill;   // A CheckBox cannot be see-through over custom painting, so it wears the card colour.
        Invalidate();
    }

    private void Place()
    {
        if (ApplyButton == null) return;   // OnResize runs once from the constructor, before the children exist.
        ApplyButton.Location = new Point(Width - ApplyButton.Width - 12, 12);
        Include.Location = new Point(ApplyButton.Left - 70, 18);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Place(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Refit(); }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = ClientRectangle.Contains(PointToClient(MousePosition));
        Refit();
    }

    private static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Hex(string value) { return ColorTranslator.FromHtml("#" + value); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Parent == null ? Well : Parent.BackColor);
        var body = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        double now = Clock.Elapsed.TotalSeconds;
        float flash = 0f;
        if (flashTime > 0 && now - flashTime < 0.8)
        {
            flash = (float)(1.0 - (now - flashTime) / 0.8);
        }
        double pulse = current ? 0.80 + 0.20 * Math.Sin(now * 3.8) : 1.0;

        using (var shape = Round(body, 10))
        {
            using (var fill = new SolidBrush(Fill)) g.FillPath(fill, shape);
            if (shown || current || flash > 0)
            {
                int er = current ? Math.Min(255, (int)(Accent.R * pulse + 55 * flash)) : 86;
                int eg = current ? Math.Min(255, (int)(Accent.G * pulse + 55 * flash)) : 104;
                int eb = current ? Math.Min(255, (int)(Accent.B * pulse + 55 * flash)) : 128;
                using (var edge = new Pen(Color.FromArgb(er, eg, eb), current ? (1.6f + 1.2f * flash) : 1.4f))
                    g.DrawPath(edge, shape);
            }
            // Colour spine: the scene's memory colours top to bottom with subtle breathing animation
            Color[] rawSpine = (Scene.memory ?? new[] { Scene.cooler ?? "6F8F9D" }).Select(Hex).ToArray();
            Color[] spine = rawSpine;
            if (current)
            {
                spine = new Color[rawSpine.Length];
                for (int si = 0; si < rawSpine.Length; si++)
                {
                    Color sc = rawSpine[si];
                    int sr = Math.Min(255, (int)(sc.R * pulse + 40 * flash));
                    int sg = Math.Min(255, (int)(sc.G * pulse + 40 * flash));
                    int sb = Math.Min(255, (int)(sc.B * pulse + 40 * flash));
                    spine[si] = Color.FromArgb(sc.A, sr, sg, sb);
                }
            }
            var clip = g.Clip;
            g.SetClip(shape);
            var bar = new Rectangle(0, 0, current ? 7 : 6, Height);
            if (spine.Length == 1) using (var flat = new SolidBrush(spine[0])) g.FillRectangle(flat, bar);
            else
                using (var blend = new LinearGradientBrush(new Rectangle(0, -1, current ? 7 : 6, Height + 2), spine[0], spine[spine.Length - 1], 90f))
                {
                    blend.InterpolationColors = new ColorBlend { Colors = spine,
                        Positions = spine.Select((c, k) => k / (float)(spine.Length - 1)).ToArray() };
                    g.FillRectangle(blend, bar);
                }
            g.Clip = clip;
        }

        int right = Include.Left - 10;
        TextRenderer.DrawText(g, Scene.name, TitleFont, new Rectangle(16, 8, right - 16, 26), Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        int used = TextRenderer.MeasureText(g, Scene.name, TitleFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        string tag = (Scene.group ?? "") + (String.IsNullOrEmpty(Scene.pattern) ? "" : "  ·  " + Scene.pattern);
        if (16 + used + 12 < right)
            TextRenderer.DrawText(g, tag, SmallFont, new Rectangle(16 + used + 12, 10, right - 16 - used - 12, 24), Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        string idea = String.IsNullOrEmpty(Scene.idea) ? "CPU 风扇 " + LightingPanel.FanCaption(Scene) : Scene.idea;
        TextRenderer.DrawText(g, idea, SmallFont, new Rectangle(17, 38, Width - 17 - 14, 20), Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        for (int i = 0; i < Scene.zones.Length && i < 3; i++)
        {
            Zone zone = Scene.zones[i];
            var chip = new RectangleF(18 + i * 92, 68, 86, 24);
            Color[] shades = zone.colors.Select(Hex).ToArray();
            using (var shape = Round(chip, 7))
            {
                if (shades.Length == 1) using (var flat = new SolidBrush(shades[0])) g.FillPath(flat, shape);
                else
                    using (var blend = new LinearGradientBrush(chip, shades[0], shades[shades.Length - 1], 0f))
                    {
                        blend.InterpolationColors = new ColorBlend { Colors = shades,
                            Positions = shades.Select((c, k) => k / (float)(shades.Length - 1)).ToArray() };
                        g.FillPath(blend, shape);
                    }
            }
            TextRenderer.DrawText(g, ZoneNames[i] + " " + Studio.ModeName(zone.effect), ChipFont, Rectangle.Round(chip), Color.FromArgb(20, 24, 36),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // Memory: two sticks, eight LEDs each, playing live.
        Rectangle area = MemoryArea;
        var well = new RectangleF(area.X, 68, Math.Min(area.Width, 190), 24);
        using (var shape = Round(well, 7))
        using (var fill = new SolidBrush(Well)) g.FillPath(fill, shape);
        Color[][] sticks = LightingPanel.MemoryFrame(Scene, Clock.Elapsed.TotalSeconds);
        for (int s = 0; s < 2; s++)
            for (int k = 0; k < 8; k++)
                using (var led = new SolidBrush(sticks[s][k]))
                    g.FillRectangle(led, well.X + 8 + k * 7, well.Y + 5 + s * 8, 6, 6);
        TextRenderer.DrawText(g, "内存 " + LightingPanel.MemoryCaption(Scene), ChipFont,
            new Rectangle((int)well.X + 70, (int)well.Y, (int)well.Width - 74, (int)well.Height), Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}
