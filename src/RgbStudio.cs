using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed class Scene
{
    public string slug { get; set; }
    public string name { get; set; }
    public string profileId { get; set; }
    public Zone[] zones { get; set; }
    public string group { get; set; }
    public string[] memory { get; set; }
    public string cooler { get; set; }
    public bool coolerBreathing { get; set; }
    public string board { get; set; }
    public string idea { get; set; }
    public string pattern { get; set; }
    public int coolerSpeed { get; set; }
}

public sealed class Zone
{
    public string effect { get; set; }
    public double brightness { get; set; }
    public string[] colors { get; set; }
    public string speed { get; set; }
    public string direction { get; set; }
    public int length { get; set; }
}

public sealed class Preferences
{
    public bool running { get; set; }
    public string[] included { get; set; }
    public int minutes { get; set; }
    public string last { get; set; }
    public int vivid { get; set; }   // 1-based index of the memory colour depth; 0 = not chosen yet
}

internal sealed class Studio : Form
{
    private static readonly Color Background = Color.FromArgb(13, 17, 23);
    private static readonly Color Card = Color.FromArgb(23, 30, 40);
    private static readonly Color Foreground = Color.FromArgb(236, 239, 248);
    private static readonly Color Muted = Color.FromArgb(170, 181, 204);
    private static readonly Color Accent = Color.FromArgb(189, 232, 221);
    private static readonly string DirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
    private static readonly string CamLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NZXT CAM", "logs", "renderer.log");
    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "preferences.json");
    private TimeSpan Period = TimeSpan.FromMinutes(30);
    private readonly List<string> recent = new List<string>();
    private Label ideaLabel;
    private static readonly Regex Applied = new Regex(@"Applying master profile \(id: ([^,]+),",
        RegexOptions.Compiled);
    private readonly Scene[] scenes;
    private readonly Random random = new Random();
    private readonly Dictionary<string, CheckBox> pool = new Dictionary<string, CheckBox>();
    private readonly Dictionary<string, Button> previews = new Dictionary<string, Button>();
    private readonly Label status;
    private readonly Label countdown;
    private readonly Button toggle;
    private readonly NotifyIcon tray;
    private readonly RigPreview rig;
    private readonly Label heroTitle;
    private readonly Label boardStatus;
    private readonly LightingPanel lighting;
    private readonly Dictionary<string, Control> sceneCards = new Dictionary<string, Control>();
    private readonly Timer timer = new Timer { Interval = 1000 };
    private bool running;
    private DateTime next;
    private Scene active;
    private Scene pending;
    private Process activeTrigger;
    private Process pendingTrigger;
    private System.Threading.EventWaitHandle activeStop;
    private System.Threading.EventWaitHandle pendingStop;
    private DateTime sent;
    private long logOffset;
    private string error;
    private Scene shown;
    private string boardResult = MotherboardDetector.Current.Summary;
    private Scene wanted;
    private int vividIndex = 1;
    private static readonly double[] Gammas = { 1.0, 2.2, 3.0 };

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string app, string id);

    // Dark title bar and dark scroll bars, so the window is one piece (Windows 10 1903 and later).
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    private static void Darken(Control control, string theme)
    {
        EventHandler apply = delegate {
            try { SetWindowTheme(control.Handle, theme, null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        };
        if (control.IsHandleCreated) apply(control, EventArgs.Empty);
        control.HandleCreated += apply;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        bool first;
        using (var mutex = new System.Threading.Mutex(true, @"Local\Codex-RGB-Studio", out first))
        {
            if (!first)
            {
                MessageBox.Show("RGB Studio 已在运行。请从任务栏或系统托盘打开。", "RGB Studio");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new Studio(args.Contains("--tray"))); }
            catch (Exception e) { MessageBox.Show(e.Message, "RGB Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private Studio(bool startInTray)
    {
        var json = new JavaScriptSerializer();
        scenes = json.Deserialize<Scene[]>(File.ReadAllText(Path.Combine(DirectoryPath, "scenes.json")));
        var saved = LoadPreferences(json);
        running = saved == null || saved.running;
        if (saved != null && saved.minutes > 0) Period = TimeSpan.FromMinutes(saved.minutes);
        var included = new HashSet<string>(saved == null || saved.included == null
            ? scenes.Select(s => s.slug) : saved.included);

        Text = "RGB Studio";
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(1050, 685);
        MinimumSize = new Size(875, 610);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = MakeIcon();

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16),
            ColumnCount = 1, RowCount = 4 };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(shell);
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        heading.Controls.Add(TextLabel("RGB Studio", 26, Foreground), 0, 0);
        var count = TextLabel(scenes.Length + " 套灯效  ·  " + scenes.Select(x => x.pattern ?? "经典").Distinct().Count() + " 种模式", 10, Muted);
        count.TextAlign = ContentAlignment.MiddleRight;
        heading.Controls.Add(count, 1, 0);
        shell.Controls.Add(heading, 0, 0);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        toggle = ActionButton("", true);
        toggle.Width = 120;
        toggle.Click += delegate {
            running = !running;
            if (running) { next = DateTime.UtcNow.Add(Period); if (active == null) Shuffle(); }
            SavePreferences();
            UpdateLabels();
        };
        var shuffle = ActionButton("随机切换", false);
        shuffle.Click += delegate { Shuffle(); };
        var hide = ActionButton("隐藏到托盘", false);
        hide.Click += delegate { Hide(); };
        countdown = TextLabel("", 10, Muted);
        countdown.Size = new Size(180, 40);
        countdown.Dock = DockStyle.None;
        var every = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 118, BackColor = Card, ForeColor = Foreground,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 10, 10, 0) };
        int[] choices = { 5, 10, 15, 30, 60 };
        foreach (int m in choices) every.Items.Add("每 " + m + " 分钟");
        every.SelectedIndex = Math.Max(0, Array.IndexOf(choices, (int)Period.TotalMinutes));
        every.SelectedIndexChanged += delegate {
            Period = TimeSpan.FromMinutes(choices[every.SelectedIndex]);
            next = DateTime.UtcNow.Add(Period);
            SavePreferences();
            UpdateLabels();
        };
        var vivid = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 128, BackColor = Card, ForeColor = Foreground,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 10, 10, 0), AccessibleName = "内存色彩浓度" };
        vivid.Items.AddRange(new object[] { "内存色 · 原色", "内存色 · 鲜艳", "内存色 · 浓郁" });
        if (saved != null && saved.vivid >= 1 && saved.vivid <= 3) vividIndex = saved.vivid - 1;
        vivid.SelectedIndex = vividIndex;
        MemoryLighting.Gamma = Gammas[vividIndex];
        vivid.SelectedIndexChanged += delegate {
            vividIndex = vivid.SelectedIndex;
            MemoryLighting.Gamma = Gammas[vividIndex];   // The show thread reads this on its next frame.
            SavePreferences();
            if (active != null && pending == null) ApplyBoard(active);
        };
        Darken(every, "DarkMode_CFD");
        Darken(vivid, "DarkMode_CFD");
        controls.Controls.AddRange(new Control[] { toggle, shuffle, every, vivid, hide, countdown });
        shell.Controls.Add(controls, 0, 1);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(main, 0, 2);
        var filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155,
            BackColor = Card, ForeColor = Foreground, FlatStyle = FlatStyle.Flat };
        filter.Items.AddRange(new object[] { "全部色系", "冷色", "暖色", "粉紫", "青绿", "霓虹", "中性", "联动", "律动" });
        filter.SelectedIndex = 0;
        var patternFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155,
            BackColor = Card, ForeColor = Foreground, FlatStyle = FlatStyle.Flat };
        patternFilter.Items.Add("全部模式");
        foreach (string name in scenes.Select(x => x.pattern ?? "经典").Distinct()) patternFilter.Items.Add(name);
        patternFilter.SelectedIndex = 0;
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        filters.Controls.Add(filter);
        filters.Controls.Add(patternFilter);
        main.Controls.Add(filters, 0, 0);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true };
        main.Controls.Add(cards, 0, 1);
        Darken(cards, "DarkMode_Explorer");
        Darken(filter, "DarkMode_CFD");
        Darken(patternFilter, "DarkMode_CFD");
        // The memory chips on the cards play live; only cards inside the viewport are repainted.
        var cardFrames = new Timer { Interval = 100 };
        cardFrames.Tick += delegate {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            foreach (Control item in cards.Controls)
                if (item.Visible && item.Bottom > 0 && item.Top < cards.ClientSize.Height) item.Invalidate(((SceneCard)item).MemoryArea);
        };
        cardFrames.Start();
        FormClosed += delegate { cardFrames.Dispose(); };
        foreach (var scene in scenes)
        {
            var item = CreateCard(scene, included.Contains(scene.slug));
            sceneCards.Add(scene.slug, item);
            cards.Controls.Add(item);
        }
        cards.SizeChanged += delegate {
            foreach (Control item in cards.Controls) item.Width = Math.Max(440, cards.ClientSize.Width - 24);
        };
        EventHandler refilter = delegate {
            cards.SuspendLayout();
            foreach (var scene in scenes)
                sceneCards[scene.slug].Visible = (filter.SelectedIndex == 0 || scene.group == (string)filter.SelectedItem)
                    && (patternFilter.SelectedIndex == 0 || (scene.pattern ?? "经典") == (string)patternFilter.SelectedItem);
            cards.ResumeLayout();
        };
        filter.SelectedIndexChanged += refilter;
        patternFilter.SelectedIndexChanged += refilter;

        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Card,
            ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 6, 14, 10), Margin = new Padding(10, 0, 0, 0) };
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        main.Controls.Add(detail, 1, 0);
        main.SetRowSpan(detail, 2);
        heroTitle = TextLabel("灯光预览", 16, Foreground);
        detail.Controls.Add(heroTitle, 0, 0);
        ideaLabel = TextLabel("", 9, Muted);
        ideaLabel.AutoEllipsis = false;
        ideaLabel.TextAlign = ContentAlignment.TopLeft;
        detail.Controls.Add(ideaLabel, 0, 1);
        rig = new RigPreview { Dock = DockStyle.Fill };
        detail.Controls.Add(rig, 0, 2);
        boardStatus = TextLabel("", 9, Muted);
        detail.Controls.Add(boardStatus, 0, 3);
        lighting = LightingPanel.Attach(rig, boardStatus, ApplySelectedLighting);
        status = TextLabel("", 9, Muted);
        shell.Controls.Add(status, 0, 3);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 RGB Studio", null, delegate { Restore(); });
        menu.Items.Add("随机切换", null, delegate { Shuffle(); });
        menu.Items.Add("退出", null, delegate { Close(); });
        tray = new NotifyIcon { Text = "RGB Studio", Icon = Icon, Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += delegate { Restore(); };
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
        FormClosed += delegate {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
            timer.Stop();
            Stop(ref pendingTrigger, ref pendingStop);
            Stop(ref activeTrigger, ref activeStop);
            tray.Dispose();
        };
        timer.Tick += delegate { Tick(); };
        timer.Start();
        Shown += delegate {
            if (startInTray) Hide();
            next = DateTime.UtcNow.Add(Period);
            ShowScene(scenes[0]);
            // With rotation paused the last scene comes back after a restart, so the case is not left
            // on whatever CAM and the sticks fall back to.
            if (running) Shuffle();
            else if (saved != null) wanted = scenes.FirstOrDefault(x => x.slug == saved.last);
            if (wanted != null) { next = DateTime.UtcNow; ShowScene(wanted); }
            UpdateLabels();
        };
        UpdateLabels();
    }

    private Control CreateCard(Scene scene, bool included)
    {
        var card = new SceneCard(scene, included);
        card.Include.CheckedChanged += delegate { SavePreferences(); };
        pool.Add(scene.slug, card.Include);
        card.ApplyButton.Click += delegate { Apply(scene); };
        previews.Add(scene.slug, card.ApplyButton);
        card.Click += delegate { ShowScene(scene); };
        return card;
    }

    private static Control Pill(string text, string[] colors)
    {
        var pill = new Panel { Width = 104, Height = 24, Margin = new Padding(0, 2, 8, 0), Cursor = Cursors.Hand };
        var shades = colors.Select(c => ColorTranslator.FromHtml("#" + c)).ToArray();
        pill.Paint += delegate(object sender, PaintEventArgs e) {
            var area = new Rectangle(0, 0, pill.Width, pill.Height);
            if (shades.Length == 1)
                using (var flat = new SolidBrush(shades[0])) e.Graphics.FillRectangle(flat, area);
            else
                using (var blend = new LinearGradientBrush(area, shades[0], shades[shades.Length - 1], 0f))
                {
                    blend.InterpolationColors = new ColorBlend {
                        Colors = shades, Positions = shades.Select((c, k) => k / (float)(shades.Length - 1)).ToArray() };
                    e.Graphics.FillRectangle(blend, area);
                }
            TextRenderer.DrawText(e.Graphics, text, pill.Font, area, Color.FromArgb(22, 25, 38),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        return pill;
    }

    private static Icon MakeIcon()
    {
        using (var canvas = new Bitmap(32, 32))
        {
            using (var g = Graphics.FromImage(canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var back = new SolidBrush(Color.FromArgb(19, 25, 34))) g.FillEllipse(back, 1, 1, 30, 30);
                Color[] ring = { Color.FromArgb(24, 200, 192), Color.FromArgb(59, 140, 255), Color.FromArgb(242, 64, 154), Color.FromArgb(255, 180, 84) };
                for (int k = 0; k < ring.Length; k++)
                    using (var arc = new Pen(ring[k], 4.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(arc, 6, 6, 20, 20, k * 90 - 80, 62);
                using (var hub = new SolidBrush(Color.FromArgb(236, 239, 248))) g.FillEllipse(hub, 13, 13, 6, 6);
            }
            return Icon.FromHandle(canvas.GetHicon());
        }
    }

    private void ShowScene(Scene scene)
    {
        if (shown == scene) return;
        shown = scene;
        heroTitle.Text = scene.name;
        ideaLabel.Text = scene.idea ?? "";
        rig.Scene = scene;
        rig.Invalidate();
        lighting.ShowScene(scene);
        foreach (var item in sceneCards) ((SceneCard)item.Value).Shown = item.Key == scene.slug;
    }

    internal static string ModeName(string effect)
    {
        switch (effect)
        {
            case "Fading": return "渐变";
            case "Pulse": return "脉冲";
            case "Fixed": return "常亮";
            case "Marquee": return "跑马";
            case "CoveringMarquee": return "铺色";
            case "Alternating": return "交替";
            case "StarryNight": return "星点";
            case "Candle": return "烛火";
            case "Wave": return "波浪";
            case "TaiChi": return "太极";
            case "Swish": return "彗星";
        }
        return "呼吸";
    }

    private static Label TextLabel(string text, int size, Color color)
    {
        return new Label { Text = text, Font = new Font("Microsoft YaHei UI", size), ForeColor = color,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    }

    private static Button ActionButton(string text, bool accent)
    {
        var button = new Button { Text = text, Width = 110, Height = 39, FlatStyle = FlatStyle.Flat,
            Margin = new Padding(0, 4, 8, 0), BackColor = accent ? Accent : Card,
            ForeColor = accent ? Background : Foreground, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Preferences LoadPreferences(JavaScriptSerializer json)
    {
        try { return json.Deserialize<Preferences>(File.ReadAllText(SettingsPath)); }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private void SavePreferences()
    {
        try {
            var settings = new Preferences { running = running, minutes = (int)Period.TotalMinutes,
                included = pool.Where(p => p.Value.Checked).Select(p => p.Key).ToArray(), vivid = vividIndex + 1,
                last = active != null ? active.slug : wanted != null ? wanted.slug : null };
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings));
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
        }
        catch (IOException) { error = "轮换偏好无法保存，本次运行仍有效。"; }
    }

    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    private void Shuffle()
    {
        var choices = scenes.Where(s => pool[s.slug].Checked && s != active && s != pending).ToArray();
        // Everything recently shown waits its turn, so a long rotation walks through the whole pool.
        while (recent.Count > Math.Max(0, choices.Length * 2 / 3)) recent.RemoveAt(0);
        var fresh = choices.Where(s => !recent.Contains(s.slug)).ToArray();
        if (fresh.Length > 0) choices = fresh;
        if (choices.Length == 0)
        {
            next = DateTime.UtcNow.Add(Period);
            error = "请勾选至少两套参与轮换的方案。";
            UpdateLabels();
            return;
        }
        Apply(choices[random.Next(choices.Length)]);
    }

    private void Apply(Scene scene)
    {
        if (pending != null || scene == active) return;
        ShowScene(scene);
        if (Process.GetProcessesByName("NZXT CAM").Length == 0)
        {
            error = "等待 NZXT CAM 启动。";
            next = DateTime.UtcNow.AddSeconds(20);
            UpdateLabels();
            return;
        }
        try
        {
            logOffset = new FileInfo(CamLog).Length;
            string executable = Path.Combine(DirectoryPath, "Triggers", "RGB-" + scene.slug + ".exe");
            string signal = @"Local\RGB-Studio-" + Guid.NewGuid().ToString("N");
            pendingStop = new System.Threading.EventWaitHandle(false,
                System.Threading.EventResetMode.ManualReset, signal);
            pendingTrigger = Process.Start(new ProcessStartInfo(executable,
                Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " " + signal) {
                WorkingDirectory = DirectoryPath, UseShellExecute = false, CreateNoWindow = true
            });
            pending = scene;
            recent.Remove(scene.slug);
            recent.Add(scene.slug);
            sent = DateTime.UtcNow;
            error = null;
        }
        catch (IOException)
        {
            Stop(ref pendingTrigger, ref pendingStop);
            error = "无法读取 CAM 的切换状态。请确认 CAM 已打开。";
            next = DateTime.UtcNow.AddSeconds(30);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Stop(ref pendingTrigger, ref pendingStop);
            error = "无法启动切换。请确认 CAM 已打开，并保留 Triggers 文件夹。";
            next = DateTime.UtcNow.AddSeconds(30);
        }
        UpdateLabels();
    }

    private void Tick()
    {
        if (pending != null)
        {
            if (CamApplied(pending.profileId))
            {
                Stop(ref activeTrigger, ref activeStop);
                active = pending;
                activeTrigger = pendingTrigger;
                activeStop = pendingStop;
                pendingTrigger = null;
                pendingStop = null;
                pending = null;
                ApplyBoard(active);
                next = DateTime.UtcNow.Add(Period);
            }
            else if (DateTime.UtcNow - sent > TimeSpan.FromSeconds(25))
            {
                Stop(ref pendingTrigger, ref pendingStop);
                error = "CAM 未确认「" + pending.name + "」。可在 CAM → Profile Sync 检查绑定。";
                pending = null;
                next = DateTime.UtcNow.AddSeconds(45);
            }
        }
        else if (running && DateTime.UtcNow >= next) Shuffle();
        else if (!running && wanted != null && active == null && DateTime.UtcNow >= next) Apply(wanted);
        if (active != null) wanted = null;
        UpdateLabels();
    }

    private bool CamApplied(string id)
    {
        try
        {
            using (var log = new FileStream(CamLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                log.Seek(Math.Max(log.Length < logOffset ? 0 : logOffset, log.Length - 256 * 1024), SeekOrigin.Begin);
                using (var reader = new StreamReader(log))
                {
                    return Applied.Matches(reader.ReadToEnd()).Cast<Match>()
                        .Any(match => match.Groups[1].Value == id);
                }
            }
        }
        catch (IOException) { return false; }
    }

    private void UpdateLabels()
    {
        toggle.Text = running ? "暂停轮换" : "开启轮换";
        TimeSpan remaining = next > DateTime.UtcNow ? next - DateTime.UtcNow : TimeSpan.Zero;
        countdown.Text = running ? "下次切换  " + remaining.ToString(@"mm\:ss") : "随机轮换已暂停";
        status.Text = pending != null ? "正在切换  " + pending.name :
            error ?? (active != null ? "CAM 已应用  " + active.name : "等待应用灯光方案");
        boardStatus.Text = boardResult ?? "风扇与内存 · 等待应用";
        foreach (var item in previews)
        {
            bool selected = active != null && active.slug == item.Key;
            item.Value.Text = selected ? "当前" : "应用";
            item.Value.BackColor = selected ? Accent : Background;
            item.Value.ForeColor = selected ? Background : Foreground;
            item.Value.Enabled = pending == null && !selected;
            ((SceneCard)sceneCards[item.Key]).Current = selected;
        }
    }

    private void ApplyBoard(Scene scene)
    {
        boardResult = "风扇与内存 · 正在应用";
        LightingPanel.RequestScene(scene.slug);
        System.Threading.Tasks.Task.Factory.StartNew(() => LightingPanel.ApplyScene(scene)).ContinueWith(task => {
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke((Action)delegate {
                    if (active != scene) return;
                    boardResult = task.IsFaulted ? "风扇与内存 · 控制器没有回应" : task.Result;
                    SavePreferences();
                    WriteStatus();
                }); } catch (InvalidOperationException) { }
            }
        });
    }

    // What was last sent and what the controllers answered, for qlight status and the console.
    private void WriteStatus()
    {
        try
        {
            File.WriteAllText(Path.Combine(DirectoryPath, "status.json"), new JavaScriptSerializer().Serialize(
                new Dictionary<string, object> { { "scene", active.slug }, { "name", active.name },
                    { "board", boardResult }, { "time", DateTime.Now.ToString("s") }, { "rotation", running } }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // "保存并应用" in the lighting panel: the current scene only needs its cooler and memory resent,
    // any other scene goes through CAM first.
    private void ApplySelectedLighting()
    {
        if (shown == null || pending != null) return;
        if (active == shown) ApplyBoard(shown);
        else Apply(shown);
    }

    // The sticks fall back to their stored effect after sleep (the ENE "direct" flag and the effect
    // registers are volatile), so the active scene is sent again a few seconds after resume.
    private void OnPower(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        var wait = new Timer { Interval = 12000 };
        wait.Tick += delegate {
            wait.Dispose();
            if (!IsDisposed && active != null && pending == null) ApplyBoard(active);
        };
        try { BeginInvoke((Action)delegate { wait.Start(); }); } catch (InvalidOperationException) { }
    }

    private static void Stop(ref Process process, ref System.Threading.EventWaitHandle signal)
    {
        try
        {
            if (signal != null) signal.Set();
            if (process != null && !process.WaitForExit(1200)) process.Kill();
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        finally
        {
            if (process != null) process.Dispose();
            if (signal != null) signal.Dispose();
            process = null;
            signal = null;
        }
    }
}
