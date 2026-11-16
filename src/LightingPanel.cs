using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

public sealed class LightingPanel : TableLayoutPanel
{
    private enum FanEffect { Recommended, Static, Breath, PulseGlow, BeatFlash, Alternate, Off, SlowFlash = PulseGlow }
    private enum Palette { Scene, Unified, Accent, Soft, Custom }
    private enum MemEffect { Linked, Static, Breath, Flow, Chase, Flash, Off, Wave, Twinkle, Bounce, Duet, Heartbeat, Fill, DoubleBeat, Shimmer, RippleFlash }
    private static readonly string[] MemNames = { "场景联动", "常亮", "呼吸", "流光", "追逐", "柔光闪", "关灯", "波浪", "星点", "回弹", "对答", "心跳", "铺色", "双击闪", "微烁", "掠影" };
    // Effect keys of MemoryLighting.Frame, in the order of MemEffect.
    private static readonly string[] MemModes = { null, "Static", "Breath", "Flow", "Chase", "Flash", "Off", "Wave", "Twinkle", "Bounce", "Duet", "Heartbeat", "Fill", "DoubleBeat", "Shimmer", "RippleFlash" };
    private static readonly string[] FanNames = {
        "场景推荐", "常亮", "柔和呼吸", "柔光脉冲", "双击律动", "双色交替", "关灯"
    };
    private static readonly string[] PaletteNames = { "场景原色", "主色统一", "撞色点缀", "柔雾浅色", "自定义" };
    private const int AlternateInterval = 1800;
    private static readonly object DeviceLock = new object();
    private static readonly object SettingsLock = new object();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RGB Studio", "scene-lighting.tsv");
    private static readonly Dictionary<string, Choice> Saved = LoadChoices();
    private static readonly ConditionalWeakTable<Control, LightingPanel> Previews =
        new ConditionalWeakTable<Control, LightingPanel>();
    private static System.Threading.Timer alternation;
    private static string requestedScene;

    private sealed class Choice
    {
        public FanEffect Effect;
        public Palette Palette;
        public string[] Colors;
        public MemEffect Memory;
    }

    private sealed class Plan
    {
        public FanEffect Effect;
        public string[] Colors;
        public byte Mode;
        public int Speed;
        public MemEffect Memory;
        public int MemSpeed;
        public bool MemReverse;
    }

    private readonly ComboBox effects = new ComboBox();
    private readonly ComboBox palettes = new ComboBox();
    private readonly Button[] swatches = new Button[6];
    private readonly TableLayoutPanel fanStrip;
    private readonly Panel noteRow = new Panel();
    private readonly Label note = new Label();
    private readonly TextBox hex = new TextBox();
    private readonly Label editLabel = new Label();
    private readonly Button confirmColor = new Button();
    private readonly Button apply = new Button();
    private readonly ToolTip tips = new ToolTip();
    private readonly Control rig;
    private readonly Action applySelected;
    private readonly Color surface;
    private readonly Color ink;
    private readonly Color muted;
    private readonly ComboBox memEffects = new ComboBox();
    private Scene scene;
    private string[] custom;
    private bool changing;
    private bool unsaved;
    private int editing = -1;
    private Plan preview;

    public static LightingPanel Attach(Control rig, Label status, Action applySelected)
    {
        var detail = (TableLayoutPanel)status.Parent;
        var panel = new LightingPanel(rig, detail, status.ForeColor, applySelected);
        detail.SuspendLayout();
        foreach (Control control in detail.Controls.Cast<Control>().Where(c => detail.GetRow(c) >= 4).ToArray())
            detail.Controls.Remove(control);
        detail.RowCount = 5;
        while (detail.RowStyles.Count > 5) detail.RowStyles.RemoveAt(5);
        detail.RowStyles[4].Height = 154;
        detail.Controls.Add(panel, 0, 4);
        detail.ResumeLayout();
        Previews.Add(rig, panel);
        return panel;
    }

    private LightingPanel(Control rig, Control parent, Color statusColor, Action applySelected)
    {
        this.rig = rig;
        this.applySelected = applySelected;
        BackColor = parent.BackColor;
        surface = Color.FromArgb(15, 21, 29);
        ink = Color.FromArgb(230, 236, 244);
        muted = statusColor;
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        ColumnCount = 3;
        RowCount = 5;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        foreach (int height in new[] { 22, 31, 26, 30, 44 })
            RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        AddCaption("CPU 风扇", 0);
        AddCaption("内存", 1);
        AddCaption("色组", 2);
        AddPicker(effects, FanNames, "CPU 风扇灯效", 0);
        AddPicker(memEffects, MemNames, "内存灯效", 1);
        AddPicker(palettes, PaletteNames, "风扇与内存色组", 2);
        effects.SelectionChangeCommitted += delegate { MarkPreviewChanged(); };
        memEffects.SelectionChangeCommitted += delegate { MarkPreviewChanged(); };
        palettes.SelectionChangeCommitted += delegate { MarkPreviewChanged(); };
        fanStrip = AddSwatches(0, 0, 2);
        SetColumnSpan(AddSwatches(1, 2, 4), 2);
        BuildNoteRow();

        StyleButton(apply, "保存并应用", Color.FromArgb(168, 218, 213), surface);
        apply.Margin = new Padding(0, 4, 0, 4);
        apply.Enabled = false;
        apply.Click += delegate {
            if (scene == null || !FinishEdit()) return;
            if (!SaveChoice(scene.slug, CurrentChoice()))
            {
                note.Text = "保存失败，请重试";
                return;
            }
            unsaved = false;
            RefreshPreview();
            applySelected();
        };
        Controls.Add(apply, 0, 4);
        SetColumnSpan(apply, 3);
        Disposed += delegate { tips.Dispose(); StopEffects(); };
    }

    public void ShowScene(Scene scene)
    {
        this.scene = scene;
        Choice choice = GetChoice(scene.slug);
        changing = true;
        unsaved = false;
        editing = -1;
        custom = choice.Colors;
        effects.SelectedIndex = (int)choice.Effect;
        memEffects.SelectedIndex = (int)choice.Memory;
        palettes.SelectedIndex = (int)choice.Palette;
        changing = false;
        apply.Enabled = true;
        RefreshPreview();
    }

    private void AddCaption(string title, int column)
    {
        Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = ink }, column, 0);
    }

    private void AddPicker(ComboBox picker, string[] options, string title, int column)
    {
        picker.Dock = DockStyle.Fill;
        picker.AccessibleName = title;
        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.FlatStyle = FlatStyle.Flat;
        picker.BackColor = surface;
        picker.ForeColor = ink;
        picker.Margin = new Padding(0, 2, column == 2 ? 0 : 8, 1);
        picker.Items.AddRange(options);
        Controls.Add(picker, column, 1);
    }

    private TableLayoutPanel AddSwatches(int column, int first, int count)
    {
        var strip = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = count,
            RowCount = 1, Margin = new Padding(0, 2, column == 0 ? 8 : 0, 0) };
        for (int i = 0; i < count; i++)
        {
            int index = first + i;
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / count));
            var button = new Button();
            StyleButton(button, "", surface, ink);
            button.Margin = new Padding(0, 1, i == count - 1 ? 0 : 4, 2);
            button.FlatAppearance.BorderColor = Color.FromArgb(66, 78, 92);
            button.FlatAppearance.BorderSize = 1;
            button.Click += delegate { EditColor(index); };
            swatches[index] = button;
            strip.Controls.Add(button, i, 0);
        }
        Controls.Add(strip, column, 2);
        return strip;
    }

    private void BuildNoteRow()
    {
        noteRow.Dock = DockStyle.Fill;
        noteRow.Margin = Padding.Empty;
        note.TextAlign = ContentAlignment.MiddleLeft;
        note.Dock = DockStyle.Fill;
        note.ForeColor = muted;
        note.AutoEllipsis = true;
        noteRow.Controls.Add(note);
        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Visible = false };
        editLabel.ForeColor = ink;
        editLabel.AutoSize = true;
        editLabel.Margin = new Padding(0, 6, 8, 0);
        hex.Width = 72;
        hex.MaxLength = 7;
        hex.AccessibleName = "颜色十六进制值";
        hex.BackColor = surface;
        hex.ForeColor = ink;
        hex.BorderStyle = BorderStyle.FixedSingle;
        hex.KeyDown += delegate(object sender, KeyEventArgs args) {
            if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; FinishEdit(); }
            if (args.KeyCode == Keys.Escape) { args.SuppressKeyPress = true; editing = -1; RefreshPreview(); }
        };
        StyleButton(confirmColor, "确定", surface, ink);
        confirmColor.Dock = DockStyle.None;
        confirmColor.Size = new Size(52, 23);
        confirmColor.Click += delegate { FinishEdit(); };
        editor.Controls.AddRange(new Control[] { editLabel, hex, confirmColor });
        noteRow.Controls.Add(editor);
        editor.BringToFront();
        Controls.Add(noteRow, 0, 3);
        SetColumnSpan(noteRow, 3);
    }

    private static void StyleButton(Button button, string text, Color background, Color foreground)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = background;
        button.ForeColor = foreground;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
    }

    private void EditColor(int index)
    {
        if (preview == null || !FinishEdit()) return;
        editing = index;
        editLabel.Text = ColorName(index);
        hex.Text = preview.Colors[index];
        ShowEditor(true);
        hex.Focus();
        hex.SelectAll();
    }

    private bool FinishEdit()
    {
        if (editing < 0) return true;
        string value = hex.Text.Trim().TrimStart('#').ToUpperInvariant();
        if (!MemoryLighting.IsHex(value))
        {
            hex.ForeColor = Color.Salmon;
            hex.Focus();
            hex.SelectAll();
            return false;
        }
        custom = (string[])preview.Colors.Clone();
        custom[editing] = value;
        editing = -1;
        palettes.SelectedIndex = (int)Palette.Custom;
        MarkPreviewChanged();
        return true;
    }

    private void MarkPreviewChanged()
    {
        unsaved = true;
        editing = -1;
        RefreshPreview();
    }

    private void ShowEditor(bool visible)
    {
        hex.ForeColor = ink;
        note.Visible = !visible;
        hex.Parent.Visible = visible;
    }

    private static string ColorName(int index)
    {
        return index == 0 ? "风扇主色" : index == 1 ? "风扇副色" : "内存颜色 " + (index - 1);
    }

    private Choice CurrentChoice()
    {
        return new Choice { Effect = (FanEffect)effects.SelectedIndex, Memory = (MemEffect)memEffects.SelectedIndex,
            Palette = (Palette)palettes.SelectedIndex, Colors = custom };
    }

    private void RefreshPreview()
    {
        if (changing || scene == null) return;
        preview = BuildPlan(scene, CurrentChoice());
        bool twoColors = preview.Effect == FanEffect.Alternate;
        fanStrip.SetColumnSpan(swatches[0], twoColors ? 1 : 2);
        swatches[1].Visible = twoColors;
        for (int i = 0; i < swatches.Length; i++)
        {
            string caption = ColorName(i) + " #" + preview.Colors[i];
            swatches[i].BackColor = i == 0 && preview.Effect == FanEffect.Off
                ? surface : ColorTranslator.FromHtml("#" + preview.Colors[i]);
            swatches[i].Enabled = i != 0 || preview.Effect != FanEffect.Off;
            swatches[i].AccessibleName = caption;
            tips.SetToolTip(swatches[i], caption + " · 点击编辑");
        }
        ShowEditor(editing >= 0);
        note.Text = (unsaved ? "待应用 · " : "") + "风扇 " + FanNames[(int)preview.Effect] + " · 内存 "
            + MemNames[(int)preview.Memory] + (preview.Memory == MemEffect.Static || preview.Memory == MemEffect.Off
                ? "" : " " + SpeedName(preview.MemSpeed)) + " · 点色块改色";
        rig.Invalidate();
    }

    public static Color PreviewFan(Control rig, Color fallback, double seconds)
    {
        LightingPanel panel;
        if (!Previews.TryGetValue(rig, out panel) || panel.preview == null) return fallback;
        Plan plan = panel.preview;
        int index = plan.Effect == FanEffect.Alternate && ((int)(seconds * 1000 / AlternateInterval) & 1) != 0 ? 1 : 0;
        string color = plan.Effect == FanEffect.Off ? "000000" : plan.Colors[index];
        if (plan.Effect == FanEffect.Breath)
            color = Mix("202A39", color, 0.18 + 0.82 * (1 - Math.Cos(seconds * 1.35)) / 2);
        else if (plan.Effect == FanEffect.PulseGlow)
        {
            double cycle = 2.0;
            double p = (seconds % cycle) / cycle;
            double attack = 0.14;
            double intensity = p < attack ? Math.Pow(p / attack, 2) : Math.Exp(-4.8 * (p - attack));
            color = Mix("202A39", color, 0.10 + 0.90 * intensity);
        }
        else if (plan.Effect == FanEffect.BeatFlash)
        {
            double cycle = 1.6;
            double p = (seconds % cycle) / cycle;
            double b1 = p < 0.18 ? Math.Exp(-40 * Math.Pow(p - 0.08, 2)) : 0;
            double b2 = (p >= 0.18 && p < 0.45) ? 0.85 * Math.Exp(-45 * Math.Pow(p - 0.28, 2)) : 0;
            double intensity = Math.Max(b1, b2);
            color = Mix("202A39", color, 0.08 + 0.92 * intensity);
        }
        return ColorTranslator.FromHtml("#" + color);
    }

    private static string SpeedName(int speed)
    {
        return speed >= 4 ? "很慢" : speed == 3 ? "慢" : speed == 2 ? "中" : "快";
    }

    private static MemoryLighting.Show ShowOf(Plan plan)
    {
        return new MemoryLighting.Show { Effect = MemModes[(int)plan.Memory], Speed = plan.MemSpeed, Reverse = plan.MemReverse,
            Points = plan.Colors.Skip(2).ToArray() };
    }

    private static Color[][] PaintSticks(Plan plan, double seconds)
    {
        Color dark = Color.FromArgb(32, 42, 57);
        return MemoryLighting.Frame(ShowOf(plan), seconds).Select(stick => stick.Select(px => Color.FromArgb(
            (int)(dark.R + (px.R - dark.R) * px.Level), (int)(dark.G + (px.G - dark.G) * px.Level),
            (int)(dark.B + (px.B - dark.B) * px.Level))).ToArray()).ToArray();
    }

    // Both sticks, eight LEDs each, as the show thread sends them (same frame function, before LED gamma).
    public static Color[][] PreviewMemory(Control rig, Color fallback, double seconds)
    {
        LightingPanel panel;
        if (!Previews.TryGetValue(rig, out panel) || panel.preview == null)
            return new[] { Enumerable.Repeat(fallback, 8).ToArray(), Enumerable.Repeat(fallback, 8).ToArray() };
        return PaintSticks(panel.preview, seconds);
    }

    // For the scene cards: the saved memory show of a scene, and its name.
    public static Color[][] MemoryFrame(Scene scene, double seconds)
    {
        return PaintSticks(BuildPlan(scene, GetChoice(scene.slug)), seconds);
    }

    public static string MemoryCaption(Scene scene)
    {
        Plan plan = BuildPlan(scene, GetChoice(scene.slug));
        bool still = plan.Memory == MemEffect.Static || plan.Memory == MemEffect.Off;
        return MemNames[(int)plan.Memory] + (still ? "" : " · " + SpeedName(plan.MemSpeed))
            + (GetChoice(scene.slug).Memory == MemEffect.Linked ? "" : " · 自选");
    }

    public static string FanCaption(Scene scene)
    {
        return FanNames[(int)BuildPlan(scene, GetChoice(scene.slug)).Effect];
    }

    private static Plan BuildPlan(Scene scene, Choice choice)
    {
        string slug = scene.slug, group = scene.group, pattern = scene.pattern, cooler = scene.cooler;
        bool breathing = scene.coolerBreathing;
        int speed = scene.coolerSpeed;
        string[] memory = scene.memory;
        if (!MemoryLighting.IsHex(cooler)) cooler = "FFFFFF";
        if (memory == null || memory.Length != 4 || !memory.All(MemoryLighting.IsHex))
            memory = new[] { cooler, cooler, cooler, cooler };
        string[] colors = { cooler, memory[1], memory[0], memory[1], memory[2], memory[3] };
        switch (choice.Palette)
        {
            case Palette.Unified:
                colors = new[] { memory[1], cooler, memory[1], Mix(memory[1], cooler, 0.3),
                    Mix(memory[1], cooler, 0.55), memory[1] };
                break;
            case Palette.Accent:
                colors = new[] { memory[0], memory[1], memory[0], memory[1], cooler, memory[0] };
                break;
            case Palette.Soft:
                colors = colors.Select(color => Mix(color, "FFF1E8", 0.32)).ToArray();
                break;
            case Palette.Custom:
                if (choice.Colors != null && choice.Colors.Length == 6 && choice.Colors.All(MemoryLighting.IsHex))
                    colors = (string[])choice.Colors.Clone();
                break;
        }
        FanEffect effect = choice.Effect == FanEffect.Recommended
            ? Recommended(slug, group, pattern, breathing) : choice.Effect;
        if (effect == FanEffect.Alternate && choice.Palette != Palette.Custom && colors[0] == colors[1])
            colors[1] = Mix(colors[0], "202A39", 0.45);
        byte mode = effect == FanEffect.Breath ? (byte)2 :
            effect == FanEffect.PulseGlow || effect == FanEffect.BeatFlash ? (byte)3 : (byte)1;
        int rate = effect == FanEffect.PulseGlow ? 235 : effect == FanEffect.BeatFlash ? 205 :
            effect == FanEffect.Breath ? (speed > 0 ? speed : 222) : 128;
        var plan = new Plan { Effect = effect, Colors = colors, Mode = mode, Speed = rate };
        LinkMemory(scene, choice.Memory, plan);
        return plan;
    }

    // "场景联动" follows the bottom fan row: travelling fan effects become a travelling effect on the
    // sticks in the same direction, breathing-like ones breathe at the same pace, a fixed row stays lit.
    private static void LinkMemory(Scene scene, MemEffect chosen, Plan plan)
    {
        Zone zone = scene.zones != null && scene.zones.Length > 0 ? scene.zones[0] : null;
        string effect = zone == null ? "Fixed" : zone.effect;
        string pace = zone == null ? null : zone.speed;
        plan.MemSpeed = pace == "Slowest" ? 4 : pace == "Slow" ? 3 : pace == "Fast" ? 1 : 2;
        plan.MemReverse = zone != null && zone.direction == "Backwards";
        if (effect == "Pulse") plan.MemSpeed = Math.Max(1, plan.MemSpeed - 1);
        if (chosen != MemEffect.Linked) { plan.Memory = chosen; return; }
        switch (effect)
        {
            case "Marquee": plan.Memory = MemEffect.Chase; break;
            case "CoveringMarquee": plan.Memory = MemEffect.Fill; break;
            // Not Duet: there the sticks take turns on purpose, and the user asked for sticks that move together.
            case "Alternating": plan.Memory = MemEffect.Chase; break;
            case "TaiChi": plan.Memory = MemEffect.Bounce; break;
            case "Swish": plan.Memory = MemEffect.Flow; break;
            case "Wave": case "Fading": plan.Memory = MemEffect.Wave; break;
            case "StarryNight": plan.Memory = MemEffect.Twinkle; break;
            case "Pulse":
                plan.Memory = scene.pattern == "雷鸣" ? MemEffect.DoubleBeat :
                              scene.pattern == "微烁" ? MemEffect.Shimmer :
                              scene.pattern == "幻影" ? MemEffect.RippleFlash :
                              scene.pattern == "脉涌" ? MemEffect.Flash : MemEffect.Heartbeat;
                break;
            case "Fixed": plan.Memory = MemEffect.Static; break;
            default: plan.Memory = MemEffect.Breath; break;
        }
    }

    private static FanEffect Recommended(string slug, string group, string pattern, bool breathing)
    {
        switch (slug)
        {
            case "NeonRails": case "Relay": case "SunMoon": case "Duet": return FanEffect.Alternate;
        }
        switch (pattern)
        {
            case "心跳": case "雷鸣": case "微烁": case "幻影": return FanEffect.PulseGlow;
            case "追逐": case "双彗": case "脉涌": return FanEffect.BeatFlash;
            case "细雨": case "扫光": case "流星": case "闪耀": return FanEffect.PulseGlow;
            case "双轨": case "交织": case "轮转": case "对答": case "对流": return FanEffect.Alternate;
            case "余烬": case "潮涌": case "绽放": case "星河": return FanEffect.Breath;
        }
        if (group == "律动") return FanEffect.PulseGlow;
        if (group == "霓虹") return FanEffect.Alternate;
        return breathing || group == "粉紫" || group == "青绿" ? FanEffect.Breath : FanEffect.Static;
    }

    private static string Mix(string first, string second, double proportion)
    {
        Color a = ColorTranslator.FromHtml("#" + first);
        Color b = ColorTranslator.FromHtml("#" + second);
        return String.Format("{0:X2}{1:X2}{2:X2}", (int)Math.Round(a.R + (b.R - a.R) * proportion),
            (int)Math.Round(a.G + (b.G - a.G) * proportion), (int)Math.Round(a.B + (b.B - a.B) * proportion));
    }

    public static void RequestScene(string slug) { Volatile.Write(ref requestedScene, slug); }

    public static string ApplyScene(Scene scene)
    {
        lock (DeviceLock)
        {
            if (Volatile.Read(ref requestedScene) != scene.slug) return "灯光已切换到更新的场景";
            StopEffects();
            Plan plan = BuildPlan(scene, GetChoice(scene.slug));
            string color = plan.Effect == FanEffect.Off ? "000000" : plan.Colors[0];
            string error = MemoryLighting.SetFan(color, plan.Mode, plan.Speed);
            string memoryError = MemoryLighting.Start(ShowOf(plan));
            if (error == null && plan.Effect == FanEffect.Alternate)
                StartAlternating(plan);
            return "风扇 · " + (error ?? FanNames[(int)plan.Effect]) + "   内存 · "
                + (memoryError ?? "两条同步 " + MemNames[(int)plan.Memory]);
        }
    }

    private static void StartAlternating(Plan plan)
    {
        int phase = 0;
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(delegate {
            if (!Monitor.TryEnter(DeviceLock)) return;
            try
            {
                if (alternation != timer) return;
                phase ^= 1;
                if (MemoryLighting.SetFan(plan.Colors[phase], plan.Mode, plan.Speed) != null) StopEffects();
            }
            finally { Monitor.Exit(DeviceLock); }
        }, null, AlternateInterval, AlternateInterval);
        alternation = timer;
    }

    private static void StopEffects()
    {
        lock (DeviceLock)
        {
            if (alternation == null) return;
            alternation.Dispose();
            alternation = null;
        }
    }

    private static Choice GetChoice(string slug)
    {
        lock (SettingsLock)
        {
            Choice choice;
            return Saved.TryGetValue(slug, out choice) ? choice : new Choice();
        }
    }

    private static Dictionary<string, Choice> LoadChoices()
    {
        var result = new Dictionary<string, Choice>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(SettingsPath)) return result;
            foreach (string row in File.ReadLines(SettingsPath))
            {
                string[] values = row.Split('\t');
                FanEffect effect;
                Palette palette;
                MemEffect mem = MemEffect.Linked;
                if (values.Length == 5 && (!Enum.TryParse(values[4], out mem) || !Enum.IsDefined(typeof(MemEffect), mem))) continue;
                if (values.Length < 4 || values.Length > 5 || !Enum.TryParse(values[1], out effect) || !Enum.IsDefined(typeof(FanEffect), effect) ||
                    !Enum.TryParse(values[2], out palette) || !Enum.IsDefined(typeof(Palette), palette)) continue;
                string[] colors = values[3].Split(',');
                result[values[0]] = new Choice { Effect = effect, Palette = palette, Memory = mem,
                    Colors = colors.Length == 6 && colors.All(MemoryLighting.IsHex) ? colors : null };
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return result;
    }

    private static bool SaveChoice(string slug, Choice choice)
    {
        lock (SettingsLock)
        {
            var updated = new Dictionary<string, Choice>(Saved, StringComparer.Ordinal);
            updated[slug] = choice;
            string staging = SettingsPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(staging, updated.Select(entry => String.Join("\t", entry.Key,
                    entry.Value.Effect.ToString(), entry.Value.Palette.ToString(),
                    entry.Value.Colors == null ? "" : String.Join(",", entry.Value.Colors), entry.Value.Memory.ToString())));
                if (File.Exists(SettingsPath)) File.Replace(staging, SettingsPath, null);
                else File.Move(staging, SettingsPath);
                Saved[slug] = choice;
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
