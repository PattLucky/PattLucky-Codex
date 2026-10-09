using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("PattGPT Codex")]
[assembly: AssemblyDescription("Local AI, photo, and video creation workspace")]
[assembly: AssemblyCompany("PattLucky")]
[assembly: AssemblyProduct("PattGPT Codex")]
[assembly: AssemblyVersion("2.0.4.0")]
[assembly: AssemblyFileVersion("2.0.4.0")]

internal static class PattLuckyCodex
{
    private const string AppUrl = "http://127.0.0.1:43771/";
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/PattLucky/PattLucky-Codex/releases/latest";
    private const string GitHubReleasesPage = "https://github.com/PattLucky/PattLucky-Codex/releases/latest";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly AgentInfo[] Agents = {
        new AgentInfo("writer", "Essays & documents", "gemma4:12b", "About 7.7–8 GB", "Draft and polish essays, reports, and documents."),
        new AgentInfo("coding", "Coding", "qwen3-coder:30b", "About 19 GB", "Design, understand, debug, and write software."),
        new AgentInfo("creative", "Creative writing", "qwen3.5:9b", "About 6.6–7.6 GB", "Create stories, scenes, characters, and fresh ideas."),
        new AgentInfo("media", "Photo & video creator (BETA in img gen)", "qwen3.5:9b", "About 6.6–7.6 GB", "Develop image and video prompts, storyboards, and visual direction."),
        new AgentInfo("lite", "Lite chat", "qwen3.5:0.8b", "About 1.2 GB", "Quick everyday chat with a small, fast local model.")
    };
    private static readonly Color Lime = Color.FromArgb(163, 230, 53);
    private static Color bg, panel, sidebar, fg, muted, line, userBubble;
    private static Form form;
    private static FlowLayoutPanel history, messages;
    private static TableLayoutPanel sidebarStack;
    private static Panel statusDetails;
    private static Button statusToggle;
    private static bool statusExpanded;
    private static TextBox prompt;
    private static ComboBox agentPicker;
    private static Label ollamaLabel, runtimeLabel, countsLabel, speedLabel, modelLabel, hintLabel, welcomeStatus;
    private static Label metricsCompactLabel, metricsDetailLabel, versionLabel;
    private static Panel welcomePanel, conversationTailSpacer;
    private static TypingIndicatorPanel typingIndicator;
    private static PictureBox welcomeLogo;
    private static Label welcomeHeading;
    private static readonly List<Control> welcomeCards = new List<Control>();
    private static Label headerTitle;
    private static Button getModelButton, sendButton;
    private static Panel headerPanel;
    private static Label activeModelLabel, activeModelIcon;
    private static Form modelManagerDialog;
    private static Label modelManagerStatus;
    private static Button ollamaStartButton, ollamaInstallButton;
    private static WebClient ollamaInstallerClient;
    private static readonly List<Button> modelManagerButtons = new List<Button>();
    private static System.Windows.Forms.Timer statusTimer, runtimeTimer, spinTimer, resizeTimer, metricsTimer, watermarkTimer, modelManagerTimer;
    private static Image logo;
    private static Bitmap watermarkCache;
    private static int spinAngle;
    private static float watermarkAngle;
    private static long lastWatermarkTick;
    private static ToolTip versionTip;
    private static bool connected, ollamaInstalled, ollamaInstallPending;
    private static bool busy;
    private static string thinkingModel;
    private static readonly Stopwatch responseTimer = new Stopwatch();
    private static DateTime ollamaInstallPendingSince;
    private static int ollamaStatusRequestActive;
    private static int mediaConnectionCheckActive;
    private static readonly HashSet<string> busyChatIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Stopwatch> chatResponseTimers = new Dictionary<string, Stopwatch>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> chatResponseModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static bool storageWriteBlocked;
    private static string startupStorageNotice;
    private static long sessionStart = Environment.TickCount;
    private static string selected = "writer";
    private static readonly HashSet<string> availableModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> downloadingModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> modelDownloadProgress = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, long>> modelLayerBytes = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ChatRecord> chats = new List<ChatRecord>();
    private static ChatRecord current;
    private static string Root
    {
        get
        {
            string appFolder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(appFolder, "launch.ps1"))) return appFolder;
            DirectoryInfo parent = Directory.GetParent(appFolder);
            if (parent != null && File.Exists(Path.Combine(parent.FullName, "launch.ps1"))) return parent.FullName;
            return appFolder;
        }
    }
    private static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PattGPT Codex"); } }
    private static string ChatPath { get { return Path.Combine(DataDirectory, "chats.json"); } }
    private static string CurrentVersion { get { Version version = Assembly.GetExecutingAssembly().GetName().Version; return version == null ? "2.0.4" : version.ToString(3); } }

    [STAThread]
    private static int Main()
    {
        string serverScript = Path.Combine(Root, "launch.ps1");
        if (!File.Exists(serverScript) || !File.Exists(FindResource("app.html")) || !File.Exists(FindResource("PattLuckyLogo.png")))
        {
            MessageBox.Show("PattGPT Codex files are missing. Reinstall it using the setup wizard.", "PattGPT Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        DetectTheme();
        bool ownsServer = false;
        Process server = null;
        try
        {
            if (!WaitForServer(500))
            {
                ProcessStartInfo info = new ProcessStartInfo("powershell.exe");
                info.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + Quote(serverScript) + " -AppMode";
                info.WorkingDirectory = Root; info.UseShellExecute = false; info.CreateNoWindow = true;
                server = Process.Start(info); ownsServer = true;
                if (!WaitForServer(20000))
                {
                    MessageBox.Show("The local app service could not start. Close any other PattGPT Codex window and try again.", "PattGPT Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            Application.Run(CreateWindow());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("PattGPT Codex could not open. " + ex.Message, "PattGPT Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (ownsServer) StopLocalServer();
            try { if (ownsServer && server != null && !server.HasExited && !server.WaitForExit(5000)) server.Kill(); } catch { }
            if (server != null) server.Dispose();
        }
    }

    private static void DetectTheme()
    {
        bool dark = IsDarkTheme();
        bg = dark ? Color.FromArgb(23, 26, 22) : Color.FromArgb(247, 248, 245);
        panel = dark ? Color.FromArgb(30, 34, 29) : Color.White;
        sidebar = dark ? Color.FromArgb(26, 30, 25) : Color.FromArgb(240, 242, 237);
        fg = dark ? Color.FromArgb(235, 238, 231) : Color.FromArgb(34, 37, 31);
        muted = dark ? Color.FromArgb(155, 162, 147) : Color.FromArgb(119, 125, 112);
        line = dark ? Color.FromArgb(52, 58, 49) : Color.FromArgb(225, 229, 220);
        userBubble = dark ? Color.FromArgb(41, 53, 30) : Color.FromArgb(237, 245, 223);
    }

    private static bool IsDarkTheme()
    {
        try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")) return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; }
        catch { return false; }
    }

    private static Image TintLogo(Image source, Color tint)
    {
        Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(result))
        using (ImageAttributes attributes = new ImageAttributes())
        {
            graphics.Clear(Color.Transparent);
            ColorMatrix matrix = new ColorMatrix(new float[][] {
                new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0}, new float[] {0,0,0,0,0},
                new float[] {0,0,0,1,0}, new float[] {tint.R/255f,tint.G/255f,tint.B/255f,0,1}
            });
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }
        return result;
    }

    private static Form CreateWindow()
    {
        using (Image original = Image.FromFile(FindResource("PattLuckyLogo.png")))
            logo = IsDarkTheme() ? TintLogo(original, Lime) : new Bitmap(original);
        form = new BufferedForm(); form.Text = "PattGPT Codex Ver " + CurrentVersion + "."; form.Icon = new Icon(FindResource("PattLuckyCodex.ico"));
        form.AutoScaleDimensions = new SizeF(96f, 96f); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.MinimumSize = new Size(760, 560); form.Size = new Size(1200, 800); form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = bg; form.ForeColor = fg; form.Font = new Font("Segoe UI", 9.5f); form.KeyPreview = true;
        TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill; layout.ColumnCount = 2; layout.RowCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.Controls.Add(layout);
        layout.Controls.Add(BuildSidebar(), 0, 0); layout.Controls.Add(BuildMain(), 1, 0);
        LoadChats(); if (chats.Count > 0) current = chats[0]; else NewChat();
        RefreshHistory(); RefreshMessages(); UpdateAgent();
        if (!String.IsNullOrEmpty(startupStorageNotice)) SetHint(startupStorageNotice);
        resizeTimer = new System.Windows.Forms.Timer(); resizeTimer.Interval = 90; resizeTimer.Tick += delegate { resizeTimer.Stop(); LayoutWindow(); RebuildWatermarkCache(); };
        form.Resize += delegate { resizeTimer.Stop(); resizeTimer.Start(); };
        form.Shown += delegate { LayoutWindow(); RebuildWatermarkCache(); UpdateVersionStatus("Checking GitHub…", false); CheckForUpdates(); };
        form.FormClosing += delegate { SaveChats(); };
        statusTimer = new System.Windows.Forms.Timer(); statusTimer.Interval = 4000; statusTimer.Tick += delegate { RefreshStatus(); }; statusTimer.Start(); RefreshStatus();
        runtimeTimer = new System.Windows.Forms.Timer(); runtimeTimer.Interval = 1000; runtimeTimer.Tick += delegate { UpdateMonitor(); }; runtimeTimer.Start(); UpdateMonitor();
        metricsTimer = new System.Windows.Forms.Timer(); metricsTimer.Interval = 2000; metricsTimer.Tick += delegate { SamplePerformance(); }; metricsTimer.Start(); SamplePerformance();
        watermarkTimer = new System.Windows.Forms.Timer(); watermarkTimer.Interval = 120; watermarkTimer.Tick += delegate {
            if (form == null || form.WindowState == FormWindowState.Minimized || messages == null || watermarkCache == null) { lastWatermarkTick = Stopwatch.GetTimestamp(); return; }
            long now = Stopwatch.GetTimestamp();
            if (lastWatermarkTick != 0) watermarkAngle = (watermarkAngle + (float)((now - lastWatermarkTick) * (8.0 / 3.0) / Stopwatch.Frequency)) % 360f;
            lastWatermarkTick = now; messages.Invalidate(false);
        }; watermarkTimer.Start();
        spinTimer = new System.Windows.Forms.Timer(); spinTimer.Interval = 45; spinTimer.Tick += delegate { spinAngle = (spinAngle + 8) % 360; if (typingIndicator != null && !typingIndicator.IsDisposed) typingIndicator.Invalidate(); };
        form.FormClosed += delegate { statusTimer.Stop(); runtimeTimer.Stop(); if (metricsTimer != null) metricsTimer.Stop(); if (watermarkTimer != null) watermarkTimer.Stop(); if (modelManagerTimer != null) modelManagerTimer.Stop(); if (spinTimer != null) spinTimer.Stop(); if (resizeTimer != null) resizeTimer.Stop(); if (watermarkCache != null) watermarkCache.Dispose(); if (logo != null) logo.Dispose(); };
        return form;
    }

    private static Control BuildSidebar()
    {
        Panel side = new BufferedPanel(); side.Dock = DockStyle.Fill; side.BackColor = sidebar; side.Padding = new Padding(14, 17, 14, 14);
        TableLayoutPanel stack = new TableLayoutPanel(); sidebarStack = stack; stack.Dock = DockStyle.Fill; stack.BackColor = sidebar; stack.ColumnCount = 1; stack.RowCount = 7;
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); side.Controls.Add(stack);
        Panel brand = new Panel(); brand.Dock = DockStyle.Fill; PictureBox mark = new PictureBox(); mark.Image = logo; mark.SizeMode = PictureBoxSizeMode.Zoom; mark.SetBounds(3, 4, 42, 42); brand.Controls.Add(mark);
        Label title = new Label(); title.Text = "PattGPT Codex"; title.Font = new Font("Segoe UI", 11, FontStyle.Bold); title.ForeColor = fg; title.SetBounds(54, 5, 185, 23); brand.Controls.Add(title);
        Label ver = new Label(); ver.Text = "Local AI workspace · Ver " + CurrentVersion + "."; ver.Font = new Font("Segoe UI", 8.5f); ver.ForeColor = muted; ver.SetBounds(54, 28, 200, 18); brand.Controls.Add(ver);
        activeModelIcon = new AgentMarkLabel(); activeModelIcon.TextAlign = ContentAlignment.MiddleCenter; activeModelIcon.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold); activeModelIcon.SetBounds(4, 52, 19, 19); brand.Controls.Add(activeModelIcon);
        activeModelLabel = new Label(); activeModelLabel.TextAlign = ContentAlignment.MiddleLeft; activeModelLabel.AutoEllipsis = true; activeModelLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold); activeModelLabel.Padding = new Padding(25, 0, 2, 0); activeModelLabel.SetBounds(3, 51, 230, 21); brand.Controls.Add(activeModelLabel); activeModelIcon.BringToFront(); stack.Controls.Add(brand, 0, 0);
        Button newChat = MakeButton("＋   New chat", true); newChat.Dock = DockStyle.Fill; newChat.Click += delegate { NewChat(); RefreshHistory(); RefreshMessages(); UpdateMonitor(); }; stack.Controls.Add(newChat, 0, 1);
        Button mediaStudio = MakeButton("▣   Media Studio · BETA", false); mediaStudio.Dock = DockStyle.Fill; mediaStudio.Click += delegate { OpenMediaStudio(); }; stack.Controls.Add(mediaStudio, 0, 2);
        Label historyTitle = new Label(); historyTitle.Text = "RECENT CHATS"; historyTitle.ForeColor = muted; historyTitle.Font = new Font("Segoe UI", 8, FontStyle.Bold); historyTitle.TextAlign = ContentAlignment.MiddleLeft; stack.Controls.Add(historyTitle, 0, 3);
        history = new BufferedFlowLayoutPanel(); history.Dock = DockStyle.Fill; history.FlowDirection = FlowDirection.TopDown; history.WrapContents = false; history.AutoScroll = true; history.BackColor = sidebar; stack.Controls.Add(history, 0, 4);
        Panel bottom = new BufferedPanel(); bottom.Dock = DockStyle.Fill; bottom.BackColor = sidebar;
        TableLayoutPanel bottomLayout = new TableLayoutPanel(); bottomLayout.Dock = DockStyle.Fill; bottomLayout.RowCount = 2; bottomLayout.ColumnCount = 1; bottomLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); bottomLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); bottom.Controls.Add(bottomLayout);
        TableLayoutPanel statusArea = new TableLayoutPanel(); statusArea.Dock = DockStyle.Fill; statusArea.BackColor = sidebar; statusArea.ColumnCount = 1; statusArea.RowCount = 2; statusArea.Margin = Padding.Empty; statusArea.Padding = Padding.Empty; statusArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); statusArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusToggle = MakeButton("● OLLAMA CHECKING  ·  STATUS  ›", false); statusToggle.Dock = DockStyle.Fill; statusToggle.FlatAppearance.BorderSize = 0; statusToggle.BackColor = sidebar; statusToggle.Padding = new Padding(4, 0, 4, 0); statusToggle.Font = new Font("Segoe UI", 8, FontStyle.Regular);
        statusDetails = new BufferedPanel(); statusDetails.Dock = DockStyle.Fill; statusDetails.Visible = false; statusDetails.Margin = Padding.Empty;
        Panel monitor = new Panel(); monitor.BackColor = panel; monitor.BorderStyle = BorderStyle.FixedSingle; monitor.SetBounds(0, 0, 233, 139); statusDetails.Controls.Add(monitor);
        ollamaLabel = new Label(); ollamaLabel.Text = "● OLLAMA CHECKING"; ollamaLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); ollamaLabel.ForeColor = muted; ollamaLabel.SetBounds(10, 5, 210, 18); monitor.Controls.Add(ollamaLabel);
        runtimeLabel = new Label(); runtimeLabel.ForeColor = muted; runtimeLabel.Font = new Font("Segoe UI", 8); runtimeLabel.SetBounds(10, 27, 95, 16); monitor.Controls.Add(runtimeLabel);
        countsLabel = new Label(); countsLabel.ForeColor = muted; countsLabel.Font = new Font("Segoe UI", 8); countsLabel.SetBounds(106, 27, 118, 16); monitor.Controls.Add(countsLabel);
        speedLabel = new Label(); speedLabel.ForeColor = muted; speedLabel.Font = new Font("Segoe UI", 8); speedLabel.SetBounds(10, 41, 205, 15); monitor.Controls.Add(speedLabel);
        metricsDetailLabel = new Label(); metricsDetailLabel.Text = "CPU: waiting  ·  GPU: waiting\r\nRAM: waiting  ·  Temps: unavailable"; metricsDetailLabel.ForeColor = muted; metricsDetailLabel.Font = new Font("Segoe UI", 7.5f); metricsDetailLabel.SetBounds(10, 61, 214, 40); monitor.Controls.Add(metricsDetailLabel);
        Button export = MakeButton("Export chat history", false); export.SetBounds(0, 144, 233, 27); export.Click += delegate { ExportChats(); }; statusDetails.Controls.Add(export);
        statusToggle.Click += delegate { statusExpanded = !statusExpanded; statusDetails.Visible = statusExpanded; sidebarStack.RowStyles[5].Height = statusExpanded ? 230 : 58; statusToggle.Text = StatusToggleText(); };
        statusArea.Controls.Add(statusToggle, 0, 0); statusArea.Controls.Add(statusDetails, 0, 1); bottomLayout.Controls.Add(statusArea, 0, 0);
        metricsCompactLabel = new Label(); metricsCompactLabel.Text = "CPU --%  ·  GPU --%  ·  RAM --"; metricsCompactLabel.ForeColor = muted; metricsCompactLabel.TextAlign = ContentAlignment.MiddleLeft; metricsCompactLabel.Dock = DockStyle.Fill; metricsCompactLabel.Font = new Font("Segoe UI", 7.5f); bottomLayout.Controls.Add(metricsCompactLabel, 0, 1); stack.Controls.Add(bottom, 0, 5);
        Label note = new Label(); note.Text = "Chats save on this device"; note.ForeColor = muted; note.TextAlign = ContentAlignment.MiddleLeft; note.Dock = DockStyle.Fill; note.Font = new Font("Segoe UI", 8.5f); stack.Controls.Add(note, 0, 6);
        return side;
    }

    private static Control BuildMain()
    {
        Panel main = new BufferedPanel(); main.Dock = DockStyle.Fill; main.BackColor = bg;
        TableLayoutPanel stack = new TableLayoutPanel(); stack.Dock = DockStyle.Fill; stack.ColumnCount = 1; stack.RowCount = 3;
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 125)); main.Controls.Add(stack);
        Panel top = new BufferedPanel(); top.Dock = DockStyle.Fill; top.BackColor = panel; top.Padding = new Padding(20, 10, 20, 10); top.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(line)) e.Graphics.DrawLine(p, 0, top.Height - 1, top.Width, top.Height - 1); };
        headerPanel = top;
        headerTitle = new Label(); headerTitle.Text = "Chat"; headerTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold); headerTitle.ForeColor = fg; headerTitle.Location = new Point(16, 20); headerTitle.AutoSize = true; top.Controls.Add(headerTitle);
        versionLabel = new Label(); versionLabel.Text = "Checking updates…"; versionLabel.Font = new Font("Segoe UI", 8.5f); versionLabel.ForeColor = muted; versionLabel.TextAlign = ContentAlignment.MiddleRight; versionLabel.AutoEllipsis = true; versionLabel.Cursor = Cursors.Hand; versionLabel.Click += delegate { UpdateVersionStatus("Checking GitHub…", false); CheckForUpdates(); }; top.Controls.Add(versionLabel);
        versionTip = new ToolTip(); versionTip.SetToolTip(versionLabel, "Click to check GitHub releases");
        agentPicker = new ComboBox(); agentPicker.DropDownStyle = ComboBoxStyle.DropDownList; agentPicker.Width = 205; agentPicker.Location = new Point(105, 17); agentPicker.BackColor = panel; agentPicker.ForeColor = fg;
        foreach (AgentInfo agent in Agents) agentPicker.Items.Add(agent.Name); agentPicker.SelectedIndexChanged += delegate { if (agentPicker.SelectedIndex >= 0) { selected = Agents[agentPicker.SelectedIndex].Key; if (current != null) current.Agent = selected; SaveChats(); UpdateAgent(); } }; top.Controls.Add(agentPicker);
        getModelButton = MakeButton("Models", false); getModelButton.Size = new Size(105, 34); getModelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; getModelButton.Click += delegate { OpenModelManager(); }; top.Controls.Add(getModelButton);
        modelLabel = new Label(); modelLabel.AutoSize = true; modelLabel.ForeColor = muted; modelLabel.Font = new Font("Segoe UI", 8.5f); top.Controls.Add(modelLabel); stack.Controls.Add(top, 0, 0);
        messages = new BufferedFlowLayoutPanel(); messages.Dock = DockStyle.Fill; messages.FlowDirection = FlowDirection.TopDown; messages.WrapContents = false; messages.AutoScroll = true; messages.Padding = new Padding(18, 18, 18, 18); messages.BackColor = bg;
        // Render the watermark in the messages background pass. The active
        // response indicator is a separate foreground control in the message flow.
        messages.Paint += delegate(object sender, PaintEventArgs e) {
            if (watermarkCache != null) { System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save(); e.Graphics.TranslateTransform(messages.ClientSize.Width / 2f, messages.ClientSize.Height / 2f); e.Graphics.RotateTransform(watermarkAngle); e.Graphics.InterpolationMode = InterpolationMode.Bilinear; e.Graphics.DrawImageUnscaled(watermarkCache, -watermarkCache.Width / 2, -watermarkCache.Height / 2); e.Graphics.Restore(state); }
        };
        stack.Controls.Add(messages, 0, 1);
        Panel composer = new BufferedPanel(); composer.Dock = DockStyle.Fill; composer.BackColor = panel; composer.Padding = new Padding(20, 10, 20, 12); composer.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(line)) e.Graphics.DrawLine(p, 0, 0, composer.Width, 0); };
        Panel inputBox = new Panel(); inputBox.Dock = DockStyle.Fill; inputBox.BackColor = bg; inputBox.Padding = new Padding(12, 8, 12, 8); inputBox.BorderStyle = BorderStyle.FixedSingle; composer.Controls.Add(inputBox);
        prompt = new TextBox(); prompt.Multiline = true; prompt.AcceptsReturn = true; prompt.ScrollBars = ScrollBars.Vertical; prompt.BorderStyle = BorderStyle.None; prompt.BackColor = bg; prompt.ForeColor = fg; prompt.Font = new Font("Segoe UI", 10); prompt.Dock = DockStyle.Fill; prompt.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendMessage(); } }; inputBox.Controls.Add(prompt);
        sendButton = MakeButton("Send", true); sendButton.Size = new Size(85, 34); sendButton.Dock = DockStyle.Right; sendButton.Margin = new Padding(6, 0, 0, 0); sendButton.Click += delegate { SendMessage(); };
        Panel inputRow = new Panel(); inputRow.Dock = DockStyle.Bottom; inputRow.Height = 38; inputRow.Padding = new Padding(0, 4, 0, 0); inputRow.Controls.Add(sendButton); inputBox.Controls.Add(inputRow); inputRow.BringToFront();
        hintLabel = new Label(); hintLabel.Text = "Runs on your computer · First model download needs internet"; hintLabel.Dock = DockStyle.Bottom; hintLabel.Height = 22; hintLabel.ForeColor = muted; hintLabel.Font = new Font("Segoe UI", 8); hintLabel.TextAlign = ContentAlignment.MiddleCenter; composer.Controls.Add(hintLabel); stack.Controls.Add(composer, 0, 2);
        return main;
    }

    private static Button MakeButton(string text, bool primary)
    {
        ModernButton b = new ModernButton(); b.Primary = primary; b.Text = text.Replace("&", "&&"); b.BackColor = primary ? Lime : panel; b.ForeColor = primary ? Color.FromArgb(29, 40, 15) : fg; b.Font = new Font("Segoe UI", 9, primary ? FontStyle.Bold : FontStyle.Regular); b.TextAlign = ContentAlignment.MiddleCenter; b.Padding = new Padding(12, 0, 12, 0); b.Cursor = Cursors.Hand; return b;
    }

    private static void NewChat()
    {
        current = new ChatRecord(); current.Id = Guid.NewGuid().ToString("N"); current.Title = "New chat"; current.Agent = selected; current.Messages = new List<MessageRecord>(); chats.Insert(0, current); SaveChats();
    }

    private static void RefreshHistory()
    {
        if (history == null) return; history.SuspendLayout(); history.Controls.Clear();
        foreach (ChatRecord chat in chats)
        {
            ChatRecord item = chat; int rowWidth = Math.Max(150, history.ClientSize.Width - 28);
            Panel row = new Panel(); row.Width = rowWidth; row.Height = 37; row.Margin = new Padding(0, 0, 0, 5); row.BackColor = sidebar;
            Button open = MakeButton(item.Title, false); open.SetBounds(0, 0, rowWidth - 72, 37); open.TextAlign = ContentAlignment.MiddleLeft; open.Padding = new Padding(11, 0, 8, 0); open.BackColor = item == current ? panel : sidebar;
            open.Click += delegate { current = item; selected = item.Agent; agentPicker.SelectedIndex = AgentIndex(selected); UpdateAgent(); RefreshHistory(); RefreshMessages(); };
            Button rename = MakeButton("✎", false); rename.SetBounds(rowWidth - 68, 0, 32, 37); rename.Padding = Padding.Empty; rename.AccessibleName = "Rename chat"; rename.AccessibleDescription = "Rename “" + item.Title + "”";
            rename.Click += delegate { RenameChat(item); };
            Button delete = MakeButton("×", false); delete.SetBounds(rowWidth - 33, 0, 33, 37); delete.Padding = Padding.Empty; delete.AccessibleName = "Delete chat"; delete.AccessibleDescription = "Delete “" + item.Title + "”";
            delete.Click += delegate { DeleteChat(item); };
            row.Controls.Add(open); row.Controls.Add(rename); row.Controls.Add(delete); history.Controls.Add(row);
        }
        history.ResumeLayout(); UpdateMonitor();
    }

    private static void ResizeHistoryItems()
    {
        if (history == null) return;
        int width = Math.Max(150, history.ClientSize.Width - 28);
        foreach (Control item in history.Controls)
        {
            item.Width = width; Panel row = item as Panel; if (row == null) continue;
            foreach (Control child in row.Controls)
            {
                if (child.AccessibleName == "Delete chat") child.SetBounds(width - 33, 0, 33, row.Height);
                else if (child.AccessibleName == "Rename chat") child.SetBounds(width - 68, 0, 32, row.Height);
                else child.SetBounds(0, 0, Math.Max(70, width - 72), row.Height);
            }
        }
    }

    private static void RenameChat(ChatRecord chat)
    {
        if (chat == null || !chats.Contains(chat)) return;
        Form dialog = new Form(); dialog.Text = "Rename chat"; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = false; dialog.MinimizeBox = false; dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ClientSize = new Size(430, 165); dialog.BackColor = panel; dialog.ForeColor = fg; dialog.Font = new Font("Segoe UI", 9.5f);
        Label label = new Label(); label.Text = "Chat name"; label.ForeColor = fg; label.SetBounds(22, 18, 385, 22); dialog.Controls.Add(label);
        TextBox name = new TextBox(); name.Text = chat.Title; name.BackColor = bg; name.ForeColor = fg; name.BorderStyle = BorderStyle.FixedSingle; name.SetBounds(22, 45, 385, 30); dialog.Controls.Add(name);
        Button cancel = MakeButton("Cancel", false); cancel.SetBounds(220, 105, 90, 38); cancel.DialogResult = DialogResult.Cancel; dialog.Controls.Add(cancel);
        Button save = MakeButton("Save name", true); save.SetBounds(317, 105, 90, 38); save.Click += delegate { if (String.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; } dialog.DialogResult = DialogResult.OK; dialog.Close(); }; dialog.Controls.Add(save);
        dialog.AcceptButton = save; dialog.CancelButton = cancel; name.SelectAll(); name.Focus();
        if (dialog.ShowDialog(form) == DialogResult.OK) { chat.Title = name.Text.Trim(); SaveChats(); RefreshHistory(); }
        dialog.Dispose();
    }

    private static void DeleteChat(ChatRecord chat)
    {
        if (chat == null || !chats.Contains(chat)) return;
        DialogResult confirm = MessageBox.Show(form, "Delete this chat and its saved messages?\r\n\r\n“" + chat.Title + "”", "Delete chat", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;
        chats.Remove(chat);
        if (current == chat) { current = chats.Count > 0 ? chats[0] : null; if (current != null) selected = current.Agent; }
        SaveChats(); UpdateAgent(); RefreshHistory(); RefreshMessages(); UpdateMonitor();
    }

    private static void LayoutHeader()
    {
        if (headerPanel == null || agentPicker == null || getModelButton == null) return;
        int width = headerPanel.ClientSize.Width;
        getModelButton.Left = Math.Max(8, width - getModelButton.Width - 12);
        int pickerLeft = 105;
        int statusWidth = width < 700 ? 94 : 178;
        versionLabel.SetBounds(Math.Max(pickerLeft + 130, getModelButton.Left - statusWidth - 10), 19, statusWidth, 26);
        int rightEdge = versionLabel.Left - 12;
        modelLabel.Visible = width >= 940;
        int pickerWidth = modelLabel.Visible ? 205 : Math.Max(150, Math.Min(245, rightEdge - pickerLeft - 8));
        agentPicker.SetBounds(pickerLeft, 17, pickerWidth, agentPicker.Height);
        modelLabel.Left = pickerLeft + pickerWidth + 12;
        modelLabel.Top = 23;
        modelLabel.MaximumSize = new Size(Math.Max(0, rightEdge - modelLabel.Left), 22);
    }

    private static void LayoutWindow()
    {
        LayoutHeader(); ResizeHistoryItems();
        LayoutMessages(); if (sidebarStack != null) sidebarStack.PerformLayout();
    }

    private static void RebuildWatermarkCache()
    {
        if (messages == null || logo == null || messages.ClientSize.Width < 1 || messages.ClientSize.Height < 1) return;
        // Keep enough margin for the watermark's corners during a full rotation,
        // so the mark never gets clipped by the messages viewport.
        int size = Math.Max(1, Math.Min(900, (int)(Math.Min(messages.ClientSize.Width, messages.ClientSize.Height) * 0.68f)));
        Bitmap next = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(next))
        using (ImageAttributes attributes = new ImageAttributes())
        {
            g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle bounds = new Rectangle(0, 0, size, size);
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.015f });
            g.DrawImage(logo, bounds, 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
        }
        Bitmap previous = watermarkCache; watermarkCache = next; if (previous != null) previous.Dispose(); messages.Invalidate(false);
    }

    private static void LayoutMessages()
    {
        if (messages == null) return;
        if (welcomePanel != null && !welcomePanel.IsDisposed)
        {
            if (messages.Padding.Top != 18 || messages.Padding.Bottom != 18)
                messages.Padding = new Padding(messages.Padding.Left, 18, messages.Padding.Right, 18);
            int available = Math.Max(200, messages.ClientSize.Width - messages.Padding.Horizontal - 34);
            welcomePanel.Width = available; welcomePanel.Margin = new Padding(Math.Max(0, (messages.ClientSize.Width - messages.Padding.Horizontal - available) / 2), 0, 0, 0);
            if (welcomeLogo != null) welcomeLogo.Left = (available - welcomeLogo.Width) / 2;
            if (welcomeHeading != null) welcomeHeading.SetBounds(8, 104, Math.Max(184, available - 16), 44);
            if (welcomeStatus != null) { welcomeStatus.Width = Math.Max(170, Math.Min(520, available - 30)); welcomeStatus.Left = (available - welcomeStatus.Width) / 2; }
            int cardWidth = Math.Max(170, Math.Min(520, available - 32)); foreach (Control card in welcomeCards) { card.Width = cardWidth; card.Left = (available - cardWidth) / 2; }
        }
        else
        {
            int width = Math.Max(200, Math.Min(760, messages.ClientSize.Width - messages.Padding.Horizontal - 44));
            if (typingIndicator != null && !typingIndicator.IsDisposed)
                typingIndicator.Margin = new Padding(Math.Max(8, messages.ClientSize.Width - messages.Padding.Horizontal - typingIndicator.Width - 8), 2, 8, 4);
            foreach (Control control in messages.Controls)
            {
                if (control == conversationTailSpacer || control == typingIndicator) continue;
                Panel card = control as Panel; if (card == null || card == welcomePanel) continue;
                bool fromAssistant = String.Equals(Convert.ToString(card.Tag), "assistant", StringComparison.Ordinal);
                int leftMargin = fromAssistant ? Math.Max(8, messages.ClientSize.Width - messages.Padding.Horizontal - width - 8) : 8;
                card.Width = width; card.Margin = new Padding(leftMargin, card.Margin.Top, 8, card.Margin.Bottom);
                int bodyHeight = 0, whoHeight = 23, footerHeight = 0;
                foreach (Control child in card.Controls)
                {
                    Label plainBody = child as Label;
                    if (plainBody != null && plainBody.AutoSize) { plainBody.MaximumSize = new Size(Math.Max(140, width - 38), 0); bodyHeight = plainBody.PreferredHeight; }
                    else if (plainBody != null && plainBody.Dock == DockStyle.Bottom) footerHeight = plainBody.Height;
                    else if (plainBody != null && plainBody.Dock == DockStyle.Top) whoHeight = plainBody.Height;
                    RichTextBox formattedBody = child as RichTextBox;
                    if (formattedBody != null) { formattedBody.Width = Math.Max(140, width - 38); formattedBody.Height = MeasureFormattedHeight(Convert.ToString(formattedBody.Tag), formattedBody.Width, formattedBody.Font); bodyHeight = formattedBody.Height; }
                }
                card.Height = Math.Max(65, bodyHeight + whoHeight + footerHeight + 24);
            }
            // Add a real scrollable tail after the last message. At the bottom of a
            // long chat this leaves clear space below the newest message; for short
            // chats the spacer grows only enough to place the last message lower down.
            messages.PerformLayout();
            int contentHeight = 0;
            foreach (Control control in messages.Controls) if (control != conversationTailSpacer) contentHeight += control.Height + control.Margin.Vertical;
            if (conversationTailSpacer != null && !conversationTailSpacer.IsDisposed)
            {
                int targetBottom = Math.Max(120, (int)(messages.ClientSize.Height * 0.56f));
                conversationTailSpacer.Width = Math.Max(100, messages.ClientSize.Width - messages.Padding.Horizontal);
                conversationTailSpacer.Height = contentHeight <= targetBottom ? Math.Max(1, targetBottom - contentHeight) : Math.Max(120, messages.ClientSize.Height - targetBottom);
            }
        }
    }

    private static void RefreshMessages()
    {
        if (messages == null) return;
        bool showWelcome = current == null || current.Messages.Count == 0;
        messages.SuspendLayout();
        while (messages.Controls.Count > 0) { Control old = messages.Controls[0]; messages.Controls.RemoveAt(0); old.Dispose(); }
        messages.AutoScrollPosition = Point.Empty; conversationTailSpacer = null; typingIndicator = null;
        if (showWelcome)
        {
            messages.Padding = new Padding(messages.Padding.Left, 18, messages.Padding.Right, 18);
            welcomeStatus = null;
            int availableWidth = Math.Max(200, messages.ClientSize.Width - messages.Padding.Horizontal - 34);
            Panel welcome = new Panel(); welcomePanel = welcome; welcome.Width = availableWidth; welcome.Height = 199 + Agents.Length * 42; welcome.BackColor = bg;
            PictureBox image = new PictureBox(); welcomeLogo = image; image.Image = logo; image.SizeMode = PictureBoxSizeMode.Zoom; image.SetBounds((welcome.Width - 88) / 2, 8, 88, 88); welcome.Controls.Add(image);
            Label heading = new Label(); welcomeHeading = heading; heading.Text = "PattGPT Codex, Local AI, Your Workspace."; heading.Font = new Font("Segoe UI", 21, FontStyle.Bold); heading.ForeColor = fg; heading.BackColor = bg; heading.TextAlign = ContentAlignment.MiddleCenter; heading.SetBounds(10, 104, welcome.Width - 20, 44); welcome.Controls.Add(heading);
            welcomeStatus = new Label(); welcomeStatus.Text = connected ? "Ollama is connected. Choose an agent and install its model to begin." : "Start Ollama, choose an agent, and install its model to begin."; welcomeStatus.Font = new Font("Segoe UI", 10); welcomeStatus.ForeColor = muted; welcomeStatus.BackColor = bg; welcomeStatus.Size = new Size(Math.Max(170, Math.Min(520, welcome.Width - 30)), 44); welcomeStatus.TextAlign = ContentAlignment.MiddleCenter; welcomeStatus.Location = new Point((welcome.Width - welcomeStatus.Width) / 2, 150); welcome.Controls.Add(welcomeStatus);
            welcomeCards.Clear(); int y = 205; int cardWidth = Math.Max(170, Math.Min(520, welcome.Width - 32)); foreach (AgentInfo a in Agents) { Button b = MakeButton("", false); b.Tag = a.Key; b.Text = WelcomeModelText(a); b.Size = new Size(cardWidth, 36); b.Location = new Point((welcome.Width - cardWidth) / 2, y); b.TextAlign = ContentAlignment.MiddleLeft; b.Click += delegate { selected = a.Key; if (current != null) current.Agent = selected; agentPicker.SelectedIndex = AgentIndex(selected); UpdateAgent(); }; welcome.Controls.Add(b); welcomeCards.Add(b); y += 42; }
            welcome.Location = new Point(Math.Max(messages.Padding.Left, (messages.ClientSize.Width - welcome.Width) / 2), Math.Max(messages.Padding.Top, (messages.ClientSize.Height - welcome.Height) / 2));
            messages.Controls.Add(welcome);
        }
        else { welcomePanel = null; welcomeLogo = null; welcomeHeading = null; welcomeCards.Clear(); welcomeStatus = null; foreach (MessageRecord m in current.Messages) AddMessage(m); AddTypingIndicator(); AddConversationTail(); }
        messages.ResumeLayout(); LayoutMessages();
        if (showWelcome) messages.AutoScrollPosition = Point.Empty; else ScrollMessages();
    }

    private static void AddMessage(MessageRecord message)
    {
        int width = Math.Max(200, Math.Min(760, messages.ClientSize.Width - messages.Padding.Horizontal - 44));
        Panel card = new Panel(); card.Tag = message.Role; card.Width = width; card.BackColor = message.Role == "user" ? userBubble : panel; card.Padding = new Padding(15, 10, 15, 10); card.Margin = new Padding(message.Role == "assistant" ? Math.Max(8, messages.ClientSize.Width - messages.Padding.Horizontal - width - 8) : 8, 8, 8, 8);
        string messageTime = message.Time > 0 ? new DateTime(message.Time, DateTimeKind.Utc).ToLocalTime().ToString("h:mm tt") : "";
        string labelText = message.Role == "assistant" ? AgentByKey(message.Agent).Name + "  ·  " + messageTime : "You" + (messageTime.Length > 0 ? "  ·  " + messageTime : "");
        Label who = new Label(); who.Text = labelText; who.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); who.ForeColor = muted; who.BackColor = card.BackColor; who.Dock = DockStyle.Top; who.Height = 23; card.Controls.Add(who);
        Control body;
        int bodyHeight;
        if (message.Role == "assistant")
        {
            RichTextBox formatted = new RichTextBox(); formatted.Tag = message.Content; formatted.Rtf = ToAssistantRtf(message.Content); formatted.Font = new Font("Segoe UI", 10); formatted.ForeColor = fg; formatted.BackColor = card.BackColor; formatted.BorderStyle = BorderStyle.None; formatted.ReadOnly = true; formatted.DetectUrls = true; formatted.ScrollBars = RichTextBoxScrollBars.None; formatted.HideSelection = true; formatted.TabStop = false; formatted.Dock = DockStyle.Top; formatted.Width = width - 38; formatted.Height = MeasureFormattedHeight(message.Content, formatted.Width, formatted.Font); body = formatted; bodyHeight = formatted.Height;
        }
        else
        {
            Label plain = new Label(); plain.Text = message.Content; plain.Font = new Font("Segoe UI", 10); plain.ForeColor = fg; plain.BackColor = card.BackColor; plain.AutoSize = true; plain.MaximumSize = new Size(width - 38, 0); plain.Dock = DockStyle.Top; plain.Padding = new Padding(0, 3, 0, 4); body = plain; bodyHeight = plain.PreferredHeight;
        }
        card.Controls.Add(body); body.BringToFront(); who.BringToFront();
        int durationHeight = 0;
        if (message.Role == "assistant" && message.DurationMilliseconds > 0)
        {
            Label duration = new Label(); duration.Text = "Thought for " + FormatDuration(message.DurationMilliseconds); duration.Font = new Font("Segoe UI", 8); duration.ForeColor = muted; duration.BackColor = card.BackColor; duration.Dock = DockStyle.Bottom; duration.Height = 18; card.Controls.Add(duration); durationHeight = duration.Height;
        }
        card.Height = Math.Max(65, bodyHeight + who.Height + durationHeight + 24); messages.Controls.Add(card);
    }

    private static string WelcomeModelText(AgentInfo agent)
    {
        bool installed = availableModels.Contains(agent.Model);
        return agent.Name + "   ·   " + agent.Model + "   ·   " + agent.Size + "   ·   " + (installed ? "Installed" : "Not installed");
    }

    private static void UpdateWelcomeModelStates()
    {
        foreach (Control control in welcomeCards)
        {
            Button card = control as Button; if (card == null) continue;
            AgentInfo agent = AgentByKey(Convert.ToString(card.Tag)); card.Text = WelcomeModelText(agent);
        }
    }

    private static void AddTypingIndicator()
    {
        typingIndicator = null;
        if (!busy || messages == null) return;
        typingIndicator = new TypingIndicatorPanel();
        typingIndicator.Margin = new Padding(Math.Max(8, messages.ClientSize.Width - messages.Padding.Horizontal - typingIndicator.Width - 8), 2, 8, 4);
        messages.Controls.Add(typingIndicator);
    }

    private static void RemoveTypingIndicator()
    {
        if (typingIndicator == null) return;
        if (!typingIndicator.IsDisposed) { if (typingIndicator.Parent == messages) messages.Controls.Remove(typingIndicator); typingIndicator.Dispose(); }
        typingIndicator = null;
    }

    private static void AddConversationTail()
    {
        if (messages == null) return;
        conversationTailSpacer = new Panel(); conversationTailSpacer.BackColor = bg; conversationTailSpacer.Margin = Padding.Empty; conversationTailSpacer.TabStop = false; messages.Controls.Add(conversationTailSpacer);
    }

    private static void RemoveConversationTail()
    {
        if (conversationTailSpacer == null) return;
        if (!conversationTailSpacer.IsDisposed) { if (conversationTailSpacer.Parent == messages) messages.Controls.Remove(conversationTailSpacer); conversationTailSpacer.Dispose(); }
        conversationTailSpacer = null;
    }

    private static int MeasureFormattedHeight(string source, int width, Font font)
    {
        string plain = Regex.Replace(source ?? "", @"```[^\r\n]*|`{1,2}|\*{1,2}|_{1,2}|^\s{0,3}#{1,6}\s|^\s*[-*+]\s+|^\s*\d+[.)]\s+", "", RegexOptions.Multiline);
        Size measured = TextRenderer.MeasureText(plain.Length == 0 ? " " : plain, font, new Size(Math.Max(120, width - 2), 100000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return Math.Max(24, measured.Height + 8);
    }

    private static string ToAssistantRtf(string source)
    {
        Color code = IsDarkTheme() ? Color.FromArgb(193, 202, 178) : Color.FromArgb(70, 80, 65);
        StringBuilder rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}}{\colortbl;\red").Append(fg.R).Append(@"\green").Append(fg.G).Append(@"\blue").Append(fg.B).Append(@";\red").Append(code.R).Append(@"\green").Append(code.G).Append(@"\blue").Append(code.B).Append(@";}\viewkind4\uc1\f0\fs20\cf1 ");
        bool inCode = false;
        string normalized = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string original in normalized.Split('\n'))
        {
            string line = original;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; rtf.Append(@"\par "); continue; }
            if (inCode) { rtf.Append(@"\f1\cf2 ").Append(EscapeRtf(line)).Append(@"\f0\cf1\par "); continue; }
            if (String.IsNullOrWhiteSpace(line)) { rtf.Append(@"\par "); continue; }
            string trimmed = line.TrimStart(); int indent = line.Length - trimmed.Length;
            int heading = 0; while (heading < trimmed.Length && heading < 6 && trimmed[heading] == '#') heading++;
            if (heading > 0 && heading < trimmed.Length && trimmed[heading] == ' ')
            {
                string title = trimmed.Substring(heading).Trim(); int size = heading == 1 ? 26 : (heading == 2 ? 23 : 21);
                rtf.Append(@"\b\fs").Append(size).Append(' ').Append(FormatInlineRtf(title)).Append(@"\b0\fs20\par "); continue;
            }
            if (Regex.IsMatch(trimmed, @"^[-*+]\s+")) { line = new string(' ', Math.Min(8, indent)) + "•  " + Regex.Replace(trimmed, @"^[-*+]\s+", ""); }
            else line = original;
            rtf.Append(FormatInlineRtf(line)).Append(@"\par ");
        }
        rtf.Append('}'); return rtf.ToString();
    }

    private static string FormatInlineRtf(string text)
    {
        string escaped = EscapeRtf(text);
        escaped = Regex.Replace(escaped, @"`([^`]+)`", @"\f1\cf2 $1\f0\cf1");
        escaped = Regex.Replace(escaped, @"\*\*(.+?)\*\*", @"\b $1\b0 ");
        escaped = Regex.Replace(escaped, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", @"\i $1\i0 ");
        escaped = Regex.Replace(escaped, @"__(.+?)__", @"\b $1\b0 ");
        escaped = Regex.Replace(escaped, @"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", @"\i $1\i0 ");
        return escaped;
    }

    private static string EscapeRtf(string text)
    {
        StringBuilder escaped = new StringBuilder();
        foreach (char c in text ?? "")
        {
            if (c == '\\' || c == '{' || c == '}') escaped.Append('\\').Append(c);
            else if (c == '\t') escaped.Append(@"\tab ");
            else if (c < 32) { }
            else if (c > 126) escaped.Append(@"\u").Append((short)c).Append('?');
            else escaped.Append(c);
        }
        return escaped.ToString();
    }

    private static void SendMessage()
    {
        if (busy) return; string text = prompt.Text.Trim(); if (text.Length == 0) return;
        if (current == null) NewChat(); AgentInfo agent = AgentByKey(selected);
        if (!availableModels.Contains(agent.Model)) { SetHint("Install " + agent.Model + " first using the Get model button."); return; }
        if (current.Messages.Count == 0) current.Title = text.Length > 42 ? text.Substring(0, 42) : text;
        current.Agent = selected; MessageRecord user = new MessageRecord(); user.Role = "user"; user.Content = text; user.Agent = selected; user.Time = DateTime.UtcNow.Ticks; current.Messages.Add(user); prompt.Clear(); SaveChats(); RefreshHistory();
        ChatRecord targetChat = current; string targetAgent = selected;
        busy = true; thinkingModel = agent.Model; responseTimer.Restart(); sendButton.Enabled = false; hintLabel.Text = "Thinking locally with " + agent.Model + " · " + FormatDuration(0);
        if (welcomePanel != null && !welcomePanel.IsDisposed) RefreshMessages();
        else { RemoveConversationTail(); AddMessage(user); AddTypingIndicator(); AddConversationTail(); LayoutMessages(); ScrollMessages(); }
        spinTimer.Start();
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                List<object> transcript = new List<object>(); foreach (MessageRecord m in targetChat.Messages) transcript.Add(new { role = m.Role, content = m.Content });
                Dictionary<string, object> payload = new Dictionary<string, object>(); payload["agent"] = targetAgent; payload["messages"] = transcript;
                Dictionary<string, object> result = PostJson("api/chat", payload, 1800000);
                string content = result.ContainsKey("content") ? Convert.ToString(result["content"]) : "";
                BeginUpdate(delegate { FinishResponse(targetChat, targetAgent, content, null, result); });
            }
            catch (Exception ex) { BeginUpdate(delegate { FinishResponse(targetChat, targetAgent, null, ex.Message, null); }); }
        });
    }

    private static void FinishResponse(ChatRecord target, string targetAgent, string content, string error, Dictionary<string, object> result)
    {
        if (spinTimer != null) spinTimer.Stop();
        responseTimer.Stop();
        MessageRecord reply = new MessageRecord(); reply.Role = "assistant"; reply.Agent = targetAgent; reply.Time = DateTime.UtcNow.Ticks; reply.DurationMilliseconds = responseTimer.ElapsedMilliseconds; reply.Content = error == null ? (String.IsNullOrEmpty(content) ? "(The model returned an empty response.)" : content) : "Could not get a local response.\r\n\r\n" + error; target.Messages.Add(reply);
        if (result != null && result.ContainsKey("eval_count") && result.ContainsKey("eval_duration")) { double count = Convert.ToDouble(result["eval_count"]); double duration = Convert.ToDouble(result["eval_duration"]); if (duration > 0) speedLabel.Text = "Model speed: " + (count / (duration / 1000000000.0)).ToString("0.0") + " tokens/sec"; }
        busy = false; sendButton.Enabled = true; hintLabel.Text = "Runs on your computer · First model download needs internet"; SaveChats(); RefreshHistory();
        if (current == target) { RemoveTypingIndicator(); RemoveConversationTail(); AddMessage(reply); AddConversationTail(); LayoutMessages(); ScrollMessages(); }
        else if (current != null) RefreshMessages();
        if (messages != null) messages.Invalidate(false); prompt.Focus();
    }

    private static void PullModel()
    {
        PullAgentModel(AgentByKey(selected));
    }

    private static void OpenMediaStudio()
    {
        using (MediaStudioDialog dialog = new MediaStudioDialog()) dialog.ShowDialog(form);
    }

    private static void PullAgentModel(AgentInfo agent)
    {
        if (!connected) { SetHint("Start Ollama from Models, then download the model."); UpdateModelManager(); return; }
        if (availableModels.Contains(agent.Model)) { SetHint(agent.Model + " is ready to use."); UpdateModelManager(); return; }
        if (downloadingModels.Contains(agent.Model)) { SetHint(agent.Model + " is already downloading."); return; }
        downloadingModels.Add(agent.Model); modelDownloadProgress[agent.Model] = "Preparing…"; modelLayerBytes[agent.Model] = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); UpdateModelManager(); SetHint("Starting download for " + agent.Model + "…");
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                byte[] payload = Encoding.UTF8.GetBytes(Json.Serialize(new Dictionary<string, object> { { "model", agent.Model }, { "stream", true } }));
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/pull"); request.Method = "POST"; request.ContentType = "application/json"; request.Timeout = Timeout.Infinite; request.ReadWriteTimeout = Timeout.Infinite; request.ContentLength = payload.Length;
                using (Stream requestStream = request.GetRequestStream()) requestStream.Write(payload, 0, payload.Length);
                bool completedSuccessfully = false;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (String.IsNullOrWhiteSpace(line)) continue;
                        Dictionary<string, object> update = Json.Deserialize<Dictionary<string, object>>(line);
                        if (update == null) continue;
                        object error; if (update.TryGetValue("error", out error)) throw new InvalidOperationException(Convert.ToString(error));
                        object statusValue; string status = update.TryGetValue("status", out statusValue) ? Convert.ToString(statusValue) : "Downloading";
                        object completedValue, totalValue; long completed = update.TryGetValue("completed", out completedValue) ? Convert.ToInt64(completedValue) : 0; long total = update.TryGetValue("total", out totalValue) ? Convert.ToInt64(totalValue) : 0;
                        object digestValue; string digest = update.TryGetValue("digest", out digestValue) ? Convert.ToString(digestValue) : null;
                        BeginUpdate(delegate {
                            if (!downloadingModels.Contains(agent.Model)) return;
                            if (!String.IsNullOrEmpty(digest) && total > 0) {
                                Dictionary<string, long> layers; if (!modelLayerBytes.TryGetValue(agent.Model, out layers)) { layers = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); modelLayerBytes[agent.Model] = layers; }
                                layers[digest] = Math.Max(0, Math.Min(completed, total)); long downloaded = 0; foreach (long bytes in layers.Values) downloaded += bytes;
                                int percent = Math.Min(99, (int)(downloaded * 100.0 / ExpectedModelBytes(agent)));
                                modelDownloadProgress[agent.Model] = "~" + percent + "% total";
                            } else if (status == "verifying sha256 digest") modelDownloadProgress[agent.Model] = "Checking…";
                            else if (status == "writing manifest" || status == "removing any unused layers") modelDownloadProgress[agent.Model] = "Finishing…";
                            else if (String.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) modelDownloadProgress[agent.Model] = "Finishing…";
                            else if (modelLayerBytes[agent.Model].Count == 0) modelDownloadProgress[agent.Model] = "Preparing…";
                            UpdateModelManager();
                        });
                        if (String.Equals(status, "success", StringComparison.OrdinalIgnoreCase)) { completedSuccessfully = true; break; }
                    }
                }
                if (!completedSuccessfully) throw new InvalidOperationException("Ollama ended the download before reporting success.");
                BeginUpdate(delegate { downloadingModels.Remove(agent.Model); modelDownloadProgress.Remove(agent.Model); modelLayerBytes.Remove(agent.Model); availableModels.Add(agent.Model); UpdateWelcomeModelStates(); UpdateModelManager(); SetHint(agent.Model + " is installed and ready to use."); RefreshStatus(); });
            } catch (Exception ex) {
                BeginUpdate(delegate { downloadingModels.Remove(agent.Model); modelDownloadProgress.Remove(agent.Model); modelLayerBytes.Remove(agent.Model); UpdateModelManager(); SetHint("Model download failed: " + ex.Message); });
            }
        });
    }

    private static void OpenModelManager()
    {
        if (modelManagerDialog != null && !modelManagerDialog.IsDisposed) { modelManagerDialog.Activate(); UpdateModelManager(); return; }
        Form dialog = new Form(); modelManagerDialog = dialog; dialog.Text = "Local models · PattGPT Codex"; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = false; dialog.MinimizeBox = false; dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ClientSize = new Size(650, 400); dialog.BackColor = bg; dialog.ForeColor = fg; dialog.Font = new Font("Segoe UI", 9.5f);
        Label title = new Label(); title.Text = "Local AI models"; title.Font = new Font("Segoe UI", 17, FontStyle.Bold); title.ForeColor = fg; title.SetBounds(24, 18, 300, 32); dialog.Controls.Add(title);
        ollamaInstallButton = MakeButton("Install Ollama", false); ollamaInstallButton.SetBounds(342, 18, 132, 38); ollamaInstallButton.Click += delegate { DownloadOllamaInstaller(); }; dialog.Controls.Add(ollamaInstallButton);
        ollamaStartButton = MakeButton("Start Ollama", true); ollamaStartButton.SetBounds(483, 18, 143, 38); ollamaStartButton.Click += delegate { StartOllamaFromGui(); }; dialog.Controls.Add(ollamaStartButton);
        modelManagerStatus = new Label(); modelManagerStatus.Text = "Checking Ollama…"; modelManagerStatus.ForeColor = muted; modelManagerStatus.SetBounds(26, 54, 420, 24); dialog.Controls.Add(modelManagerStatus);
        modelManagerButtons.Clear();
        int y = 94;
        HashSet<string> listedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AgentInfo agent in Agents)
        {
            if (!listedModels.Add(agent.Model)) continue;
            AgentInfo item = agent;
            Label name = new Label(); name.Text = item.Name; name.Font = new Font("Segoe UI", 10, FontStyle.Bold); name.ForeColor = fg; name.SetBounds(26, y, 230, 23); dialog.Controls.Add(name);
            Label details = new Label(); details.Text = item.Model + "  ·  " + item.Size; details.ForeColor = muted; details.SetBounds(26, y + 23, 420, 21); dialog.Controls.Add(details);
            Button install = MakeButton("Download", false); install.Tag = item; install.SetBounds(483, y + 5, 143, 36); install.Click += delegate { PullAgentModel(item); }; dialog.Controls.Add(install); modelManagerButtons.Add(install);
            y += 67;
        }
        Label note = new Label(); note.Text = "Models are stored locally by Ollama. Downloads need internet and may require several GB of free space."; note.ForeColor = muted; note.SetBounds(26, 365, 600, 22); dialog.Controls.Add(note);
        dialog.FormClosed += delegate { if (modelManagerTimer != null) modelManagerTimer.Stop(); modelManagerDialog = null; modelManagerStatus = null; ollamaStartButton = null; ollamaInstallButton = null; modelManagerButtons.Clear(); };
        modelManagerTimer = new System.Windows.Forms.Timer(); modelManagerTimer.Interval = 2000; modelManagerTimer.Tick += delegate { if (ollamaInstallPending && DateTime.UtcNow - ollamaInstallPendingSince > TimeSpan.FromMinutes(3)) ollamaInstallPending = false; UpdateModelManager(); if (!ollamaInstalled) RefreshOllamaInstallStatus(); }; modelManagerTimer.Start();
        UpdateModelManager(); RefreshOllamaInstallStatus(); dialog.Show(form);
    }

    private static void UpdateModelManager()
    {
        if (modelManagerDialog == null || modelManagerDialog.IsDisposed) return;
        if (modelManagerStatus != null) modelManagerStatus.Text = connected ? "Ollama is installed and running. Choose a model to download." : (ollamaInstalled ? "Ollama is installed but stopped. Start it here to use models." : "Ollama is not installed. Install it once, then start it here.");
        if (ollamaInstallButton != null && ollamaInstallerClient == null)
        {
            ollamaInstallButton.Text = ollamaInstalled ? "Ollama installed" : (ollamaInstallPending ? "Finish setup" : (ollamaStatusRequestActive != 0 ? "Checking…" : "Install Ollama"));
            ollamaInstallButton.Enabled = !ollamaInstalled && !ollamaInstallPending && ollamaStatusRequestActive == 0;
        }
        if (ollamaStartButton != null) { ollamaStartButton.Enabled = !connected && ollamaInstallerClient == null; ollamaStartButton.Text = connected ? "Ollama running" : "Start Ollama"; }
        foreach (Button button in modelManagerButtons)
        {
            AgentInfo agent = button.Tag as AgentInfo; if (agent == null) continue;
            if (availableModels.Contains(agent.Model)) { button.Text = "Installed"; button.Enabled = false; }
            else if (downloadingModels.Contains(agent.Model)) { string progress; button.Text = modelDownloadProgress.TryGetValue(agent.Model, out progress) ? progress : "Downloading…"; button.Enabled = false; }
            else { button.Text = "Download"; button.Enabled = connected; }
        }
    }

    private static void RefreshOllamaInstallStatus()
    {
        if (Interlocked.Exchange(ref ollamaStatusRequestActive, 1) != 0) return;
        if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = false; ollamaInstallButton.Text = "Checking…"; }
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                Dictionary<string, object> state = GetJson("api/ollama/check", 4000);
                bool installed = state.ContainsKey("installed") && Convert.ToBoolean(state["installed"]);
                bool running = state.ContainsKey("running") && Convert.ToBoolean(state["running"]);
                BeginUpdate(delegate { ollamaInstalled = installed || running; if (ollamaInstalled) ollamaInstallPending = false; if (running) connected = true; UpdateModelManager(); });
            }
            catch { BeginUpdate(delegate { UpdateModelManager(); }); }
            finally { Interlocked.Exchange(ref ollamaStatusRequestActive, 0); BeginUpdate(delegate { UpdateModelManager(); }); }
        });
    }

    private static long ExpectedModelBytes(AgentInfo agent)
    {
        // Ollama reports progress per layer, not per model. Use the displayed library
        // size estimate and accumulate each digest so the percentage does not reset
        // when Ollama moves from one layer to the next.
        long gigabyte = 1000000000L;
        if (agent.Key == "writer") return 790 * gigabyte / 100;
        if (agent.Key == "coding") return 19 * gigabyte;
        if (agent.Key == "creative" || agent.Key == "media") return 710 * gigabyte / 100;
        return 125 * gigabyte / 100;
    }

    private static void StartOllamaFromGui()
    {
        if (ollamaStartButton != null) { ollamaStartButton.Enabled = false; ollamaStartButton.Text = "Starting…"; }
        if (modelManagerStatus != null) modelManagerStatus.Text = "Starting the local Ollama service…";
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                PostJson("api/ollama/start", new Dictionary<string, object>(), 15000);
                BeginUpdate(delegate { SetHint("Ollama is starting. Model controls will enable when it is ready."); RefreshStatus(); UpdateModelManager(); });
            } catch (Exception ex) {
                BeginUpdate(delegate { SetHint(ex.Message); if (modelManagerStatus != null) modelManagerStatus.Text = ex.Message; UpdateModelManager(); });
            }
        });
    }

    private static void DownloadOllamaInstaller()
    {
        if (ollamaInstalled || ollamaInstallPending) { SetHint(ollamaInstalled ? "Ollama is already installed. Start it from the Models window." : "Finish the Ollama setup window before trying another download."); UpdateModelManager(); return; }
        if (ollamaInstallerClient != null) return;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string folder = Path.Combine(Path.GetTempPath(), "PattLuckyCodex"); Directory.CreateDirectory(folder);
            string installer = Path.Combine(folder, "OllamaSetup.exe");
            ollamaInstallerClient = new WebClient(); ollamaInstallerClient.Headers[HttpRequestHeader.UserAgent] = "PattLuckyCodex/" + CurrentVersion;
            ollamaInstallerClient.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e) {
                BeginUpdate(delegate { if (modelManagerStatus != null) modelManagerStatus.Text = "Downloading Ollama installer · " + e.ProgressPercentage + "%"; if (ollamaInstallButton != null) ollamaInstallButton.Text = e.ProgressPercentage + "%"; });
            };
            ollamaInstallerClient.DownloadFileCompleted += delegate(object sender, System.ComponentModel.AsyncCompletedEventArgs e) {
                WebClient client = ollamaInstallerClient; ollamaInstallerClient = null; if (client != null) client.Dispose();
                if (e.Error != null) {
                    if (File.Exists(installer)) try { File.Delete(installer); } catch { }
                    if (modelManagerStatus != null) modelManagerStatus.Text = "Ollama download failed. Check your internet and retry.";
                    if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = true; ollamaInstallButton.Text = "Retry install"; }
                    SetHint("Could not download Ollama: " + e.Error.Message); UpdateModelManager(); return;
                }
                try {
                    Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
                    ollamaInstallPending = true; ollamaInstallPendingSince = DateTime.UtcNow;
                    if (modelManagerStatus != null) modelManagerStatus.Text = "Ollama setup opened. Finish installation, then start Ollama here.";
                    SetHint("Ollama installer downloaded. Complete its setup window, then start Ollama from Models.");
                    if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = true; ollamaInstallButton.Text = "Install again"; }
                } catch (Exception ex) {
                    if (modelManagerStatus != null) modelManagerStatus.Text = "Installer downloaded, but could not be opened.";
                    SetHint(ex.Message); if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = true; ollamaInstallButton.Text = "Open installer"; }
                }
                UpdateModelManager();
            };
            if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = false; ollamaInstallButton.Text = "0%"; }
            if (ollamaStartButton != null) ollamaStartButton.Enabled = false;
            if (modelManagerStatus != null) modelManagerStatus.Text = "Downloading the official Ollama installer…";
            ollamaInstallerClient.DownloadFileAsync(new Uri("https://ollama.com/download/OllamaSetup.exe"), installer);
        }
        catch (Exception ex) {
            if (ollamaInstallerClient != null) { ollamaInstallerClient.Dispose(); ollamaInstallerClient = null; }
            if (ollamaInstallButton != null) { ollamaInstallButton.Enabled = true; ollamaInstallButton.Text = "Retry install"; }
            if (modelManagerStatus != null) modelManagerStatus.Text = ex.Message; SetHint("Could not start the Ollama download: " + ex.Message); UpdateModelManager();
        }
    }

    private static void UpdateAgent()
    {
        if (agentPicker != null && agentPicker.SelectedIndex != AgentIndex(selected)) agentPicker.SelectedIndex = AgentIndex(selected);
        AgentInfo a = AgentByKey(selected); if (modelLabel != null) modelLabel.Text = a.Model + "  ·  " + a.Size;
        if (activeModelLabel != null && activeModelIcon != null)
        {
            string shortName = a.Key == "writer" ? "WRITER" : (a.Key == "coding" ? "CODER" : (a.Key == "creative" ? "CREATIVE" : (a.Key == "media" ? "MEDIA" : "LITE")));
            Color accent = AgentAccent(a.Key); activeModelLabel.Text = shortName + "  ·  " + a.Model; activeModelLabel.ForeColor = accent; activeModelLabel.BackColor = MixColor(sidebar, accent, 0.10f);
            activeModelIcon.Text = a.Key == "writer" ? "W" : (a.Key == "coding" ? "C" : (a.Key == "creative" ? "R" : (a.Key == "media" ? "M" : "L"))); activeModelIcon.BackColor = sidebar; activeModelIcon.ForeColor = sidebar; activeModelIcon.Invalidate();
        }
        if (getModelButton != null) { getModelButton.Enabled = true; getModelButton.Text = "Models"; }
    }

    private static Color AgentAccent(string key)
    {
        if (key == "writer") return Color.FromArgb(196, 145, 255);
        if (key == "coding") return Color.FromArgb(74, 198, 235);
        if (key == "creative") return Color.FromArgb(255, 132, 159);
        if (key == "media") return Color.FromArgb(242, 156, 72);
        return Lime;
    }

    private static Color MixColor(Color baseColor, Color accent, float amount)
    {
        return Color.FromArgb((int)(baseColor.R * (1 - amount) + accent.R * amount), (int)(baseColor.G * (1 - amount) + accent.G * amount), (int)(baseColor.B * (1 - amount) + accent.B * amount));
    }

    private static void RefreshStatus()
    {
        if (Interlocked.Exchange(ref statusRequestActive, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                Dictionary<string, object> result = GetJson("api/models", 5000); bool isConnected = Convert.ToBoolean(result["connected"]); HashSet<string> models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                object raw; if (result.TryGetValue("models", out raw)) { IEnumerable arr = raw as IEnumerable; if (arr != null) foreach (object item in arr) models.Add(Convert.ToString(item)); }
                BeginUpdate(delegate { connected = isConnected; availableModels.Clear(); foreach (string model in models) availableModels.Add(model); UpdateWelcomeModelStates(); ollamaLabel.Text = connected ? "● OLLAMA ON" : "● OLLAMA OFF"; ollamaLabel.ForeColor = connected ? Color.FromArgb(91, 150, 25) : Color.FromArgb(210, 100, 72); if (statusToggle != null) statusToggle.Text = StatusToggleText(); if (welcomeStatus != null) welcomeStatus.Text = connected ? "Ollama is connected. Choose an agent and install its model to begin." : "Start Ollama, choose an agent, and install its model to begin."; UpdateAgent(); UpdateModelManager(); });
            } catch { BeginUpdate(delegate { connected = false; ollamaLabel.Text = "● OLLAMA OFF"; ollamaLabel.ForeColor = Color.FromArgb(210, 100, 72); UpdateAgent(); UpdateModelManager(); }); }
            finally { Interlocked.Exchange(ref statusRequestActive, 0); }
        });
    }

    private static void CheckForUpdates()
    {
        if (Interlocked.Exchange(ref updateRequestActive, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GitHubLatestReleaseApi);
                request.Method = "GET"; request.Accept = "application/vnd.github+json"; request.UserAgent = "PattLuckyCodex/" + CurrentVersion; request.Timeout = 8000; request.ReadWriteTimeout = 8000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    Dictionary<string, object> release = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                    string tag = release.ContainsKey("tag_name") ? Convert.ToString(release["tag_name"]) : "";
                    Version latest, currentVersion = new Version(CurrentVersion);
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out latest)) { BeginUpdate(delegate { UpdateVersionStatus("Release tag unreadable", false); SetHint("GitHub returned a release tag the updater could not read."); }); return; }
                    if (latest > currentVersion) BeginUpdate(delegate { UpdateVersionStatus("Update v" + latest.ToString(3) + " available", true); ShowUpdateDialog(latest.ToString(3)); });
                    else BeginUpdate(delegate { UpdateVersionStatus("Up to date", false); SetHint("PattGPT Codex is up to date · v" + CurrentVersion); });
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                bool noRelease = response != null && response.StatusCode == HttpStatusCode.NotFound;
                if (response != null) response.Close();
                BeginUpdate(delegate { UpdateVersionStatus(noRelease ? "No release yet" : "Check unavailable", false); SetHint(noRelease ? "No public GitHub release is available yet." : "Could not check GitHub for updates. You can check again later."); });
            }
            catch { BeginUpdate(delegate { UpdateVersionStatus("Check unavailable", false); SetHint("Could not check GitHub for updates. You can check again later."); }); }
            finally { Interlocked.Exchange(ref updateRequestActive, 0); }
        });
    }

    private static void UpdateVersionStatus(string status, bool updateAvailable)
    {
        if (versionLabel == null) return;
        if (status.StartsWith("Update v", StringComparison.OrdinalIgnoreCase)) versionLabel.Text = status;
        else if (status.StartsWith("Checking", StringComparison.OrdinalIgnoreCase)) versionLabel.Text = "Checking…";
        else if (status == "Up to date") versionLabel.Text = "Up to date";
        else versionLabel.Text = status;
        versionLabel.ForeColor = updateAvailable ? Color.FromArgb(163, 230, 53) : muted;
        versionLabel.Font = new Font("Segoe UI", 8, updateAvailable ? FontStyle.Bold : FontStyle.Regular);
        versionLabel.AccessibleDescription = "Installed version " + CurrentVersion + ". " + status;
        if (versionTip != null) versionTip.SetToolTip(versionLabel, versionLabel.AccessibleDescription + ". Click to check again or open the release page.");
        LayoutHeader();
    }

    private static void ShowUpdateDialog(string latestVersion)
    {
        Form dialog = new Form(); dialog.Text = "PattGPT Codex update"; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = false; dialog.MinimizeBox = false; dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ClientSize = new Size(460, 210); dialog.BackColor = panel; dialog.ForeColor = fg; dialog.Font = new Font("Segoe UI", 9.5f);
        Label heading = new Label(); heading.Text = "A new version is ready"; heading.Font = new Font("Segoe UI", 16, FontStyle.Bold); heading.SetBounds(24, 22, 410, 34); heading.ForeColor = fg; dialog.Controls.Add(heading);
        Label details = new Label(); details.Text = "You have v" + CurrentVersion + ". GitHub has v" + latestVersion + ". Open the release page to download the latest installer."; details.SetBounds(26, 66, 405, 55); details.ForeColor = muted; dialog.Controls.Add(details);
        ModernButton later = new ModernButton(); later.Text = "Later"; later.BackColor = panel; later.ForeColor = fg; later.Size = new Size(98, 38); later.Location = new Point(213, 145); later.TextAlign = ContentAlignment.MiddleCenter; later.Padding = new Padding(10, 0, 10, 0); later.DialogResult = DialogResult.Cancel; dialog.Controls.Add(later);
        ModernButton download = new ModernButton(); download.Primary = true; download.Text = "View update"; download.BackColor = Lime; download.ForeColor = Color.FromArgb(29, 40, 15); download.Font = new Font("Segoe UI", 9, FontStyle.Bold); download.Size = new Size(128, 38); download.Location = new Point(320, 145); download.TextAlign = ContentAlignment.MiddleCenter; download.Padding = new Padding(10, 0, 10, 0); download.Click += delegate { try { Process.Start(new ProcessStartInfo(GitHubReleasesPage) { UseShellExecute = true }); } catch { } dialog.Close(); }; dialog.Controls.Add(download);
        dialog.AcceptButton = download; dialog.CancelButton = later; dialog.ShowDialog(form); dialog.Dispose();
    }

    private static void UpdateMonitor()
    {
        if (runtimeLabel == null) return; long seconds = (Environment.TickCount - sessionStart) / 1000; runtimeLabel.Text = "Open " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); int total = 0; foreach (ChatRecord chat in chats) total += chat.Messages.Count; countsLabel.Text = chats.Count + " chats · " + total + " msgs"; if (speedLabel.Text.Length == 0) speedLabel.Text = "Model speed: waiting for a reply";
        if (busy && hintLabel != null) hintLabel.Text = "Thinking locally with " + thinkingModel + " · " + FormatDuration(responseTimer.ElapsedMilliseconds);
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds < 10000) return (milliseconds / 1000.0).ToString("0.0") + "s";
        TimeSpan elapsed = TimeSpan.FromMilliseconds(milliseconds);
        if (elapsed.TotalHours >= 1) return ((int)elapsed.TotalHours).ToString() + ":" + elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
        return ((int)elapsed.TotalMinutes).ToString("00") + ":" + elapsed.Seconds.ToString("00");
    }

    private static int metricsSampling;
    private static int statusRequestActive, updateRequestActive;
    private static ulong previousIdle, previousKernel, previousUser;
    private static bool haveCpuSample;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MemoryStatus { public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)); public uint MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
    private static void SamplePerformance()
    {
        if (Interlocked.Exchange(ref metricsSampling, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate {
            string cpu = "--", ram = "--", gpu = "--", gpuTemp = "N/A";
            try { long idle, kernel, user; if (GetSystemTimes(out idle, out kernel, out user)) { ulong i = unchecked((ulong)idle), k = unchecked((ulong)kernel), u = unchecked((ulong)user); if (haveCpuSample) { ulong total = (k - previousKernel) + (u - previousUser), idleDelta = i - previousIdle; if (total > 0) cpu = Math.Max(0, Math.Min(100, (int)(100.0 * (total - idleDelta) / total))).ToString() + "%"; } previousIdle = i; previousKernel = k; previousUser = u; haveCpuSample = true; } } catch { }
            try { MemoryStatus m = new MemoryStatus(); if (GlobalMemoryStatusEx(m) && m.TotalPhysical > 0) { double used = (m.TotalPhysical - m.AvailablePhysical) / 1073741824.0; ram = used.ToString("0.0") + "/" + (m.TotalPhysical / 1073741824.0).ToString("0.0") + " GB (" + m.MemoryLoad + "%)"; } } catch { }
            try { ProcessStartInfo psi = new ProcessStartInfo("nvidia-smi.exe", "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits"); psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; using (Process p = Process.Start(psi)) { if (p != null) { if (p.WaitForExit(1200)) { string[] vals = p.StandardOutput.ReadToEnd().Trim().Split(','); if (vals.Length >= 2) { gpu = vals[0].Trim() + "%"; gpuTemp = vals[1].Trim() + "°C"; } } else try { p.Kill(); } catch { } } } } catch { }
            string cpuResult = cpu, ramResult = ram, gpuResult = gpu, tempResult = gpuTemp;
            BeginUpdate(delegate { if (metricsCompactLabel != null) metricsCompactLabel.Text = "CPU " + cpuResult + "  ·  GPU " + gpuResult + "  ·  RAM " + ramResult; if (metricsDetailLabel != null) metricsDetailLabel.Text = "CPU: " + cpuResult + "  ·  CPU temp: unavailable\r\nGPU: " + gpuResult + "  ·  GPU temp: " + tempResult + "\r\nRAM used: " + ramResult; });
            Interlocked.Exchange(ref metricsSampling, 0);
        });
    }

    private static void SetHint(string text) { if (hintLabel != null) hintLabel.Text = text; }
    private static string StatusToggleText() { return (connected ? "● OLLAMA ON" : "● OLLAMA OFF") + "  ·  STATUS  " + (statusExpanded ? "⌄" : "›"); }
    private static void ScrollMessages()
    {
        if (messages == null || messages.Controls.Count == 0) return;
        messages.PerformLayout();
        messages.ScrollControlIntoView(messages.Controls[messages.Controls.Count - 1]);
    }
    private static void BeginUpdate(MethodInvoker action) { try { if (form != null && !form.IsDisposed) form.BeginInvoke(action); } catch { } }

    private static Dictionary<string, object> GetJson(string path, int timeout) { HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl + path); req.Timeout = timeout; using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) using (StreamReader reader = new StreamReader(resp.GetResponseStream())) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); }
    private static Dictionary<string, object> PostJson(string path, object payload, int timeout) { byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(payload)); HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl + path); req.Method = "POST"; req.ContentType = "application/json; charset=utf-8"; req.Timeout = timeout; req.ReadWriteTimeout = timeout; req.ContentLength = data.Length; using (Stream stream = req.GetRequestStream()) stream.Write(data, 0, data.Length); try { using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) using (StreamReader reader = new StreamReader(resp.GetResponseStream())) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); } catch (WebException ex) { HttpWebResponse resp = ex.Response as HttpWebResponse; if (resp != null) { using (resp) using (StreamReader reader = new StreamReader(resp.GetResponseStream())) { try { Dictionary<string, object> error = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); object detail; if (error != null && error.TryGetValue("error", out detail)) throw new InvalidOperationException(Convert.ToString(detail)); } catch (InvalidOperationException) { throw; } catch { } } } throw new InvalidOperationException(ex.Message, ex); } }
    private static bool WaitForServer(int timeoutMs) { Stopwatch timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < timeoutMs) { try { HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl); req.Timeout = 500; using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) return resp.StatusCode == HttpStatusCode.OK; } catch { Thread.Sleep(250); } } return false; }
    private static void StopLocalServer() { try { HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl + "api/shutdown"); req.Method = "POST"; req.ContentLength = 0; req.Timeout = 2000; using (req.GetResponse()) { } } catch { } }
    private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    private static string FindResource(string name)
    {
        string[] candidates = { Path.Combine(Root, name), Path.Combine(Root, "assets", name), Path.Combine(Root, "ui", name), Path.Combine(Root, "dist", name) };
        foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
        return candidates[0];
    }
    private static AgentInfo AgentByKey(string key) { foreach (AgentInfo a in Agents) if (a.Key == key) return a; return Agents[0]; }
    private static int AgentIndex(string key) { for (int i = 0; i < Agents.Length; i++) if (Agents[i].Key == key) return i; return 0; }

    private static void LoadChats()
    {
        string oldDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PattLucky Codex");
        string oldChatPath = Path.Combine(oldDataDirectory, "chats.json");
        if (!File.Exists(ChatPath) && File.Exists(oldChatPath))
        {
            Directory.CreateDirectory(DataDirectory); File.Copy(oldChatPath, ChatPath, false);
            string oldBackup = oldChatPath + ".bak"; if (File.Exists(oldBackup) && !File.Exists(ChatPath + ".bak")) File.Copy(oldBackup, ChatPath + ".bak", false);
        }
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(ChatPath)) return;
        try
        {
            List<ChatRecord> data = Json.Deserialize<List<ChatRecord>>(File.ReadAllText(ChatPath));
            if (data != null)
            {
                foreach (ChatRecord chat in data)
                {
                    if (chat == null) continue;
                    if (String.IsNullOrWhiteSpace(chat.Id)) chat.Id = Guid.NewGuid().ToString("N");
                    if (String.IsNullOrWhiteSpace(chat.Title)) chat.Title = "New chat";
                    if (chat.Title.Length > 120) chat.Title = chat.Title.Substring(0, 120);
                    if (chat.Messages == null) chat.Messages = new List<MessageRecord>();
                    chat.Messages.RemoveAll(delegate(MessageRecord message) { return message == null; });
                    bool validAgent = false; foreach (AgentInfo agent in Agents) if (String.Equals(agent.Key, chat.Agent, StringComparison.OrdinalIgnoreCase)) validAgent = true;
                    if (!validAgent) chat.Agent = "writer";
                    foreach (MessageRecord message in chat.Messages)
                    {
                        bool validMessageAgent = false; foreach (AgentInfo agent in Agents) if (String.Equals(agent.Key, message.Agent, StringComparison.OrdinalIgnoreCase)) validMessageAgent = true;
                        if (!validMessageAgent) message.Agent = chat.Agent;
                    }
                    chats.Add(chat);
                }
            }
        }
        catch
        {
            chats.Clear();
            string backup = ChatPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(ChatPath, backup); startupStorageNotice = "Unreadable chat history was preserved as " + Path.GetFileName(backup) + "."; }
            catch { storageWriteBlocked = true; startupStorageNotice = "Chat history could not be read or backed up. PattGPT Codex will not overwrite that file."; }
        }
    }

    private static void SaveChats()
    {
        if (storageWriteBlocked) { if (hintLabel != null) SetHint(startupStorageNotice); return; }
        string temp = ChatPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(temp, Json.Serialize(chats), new UTF8Encoding(false));
            if (File.Exists(ChatPath)) File.Replace(temp, ChatPath, ChatPath + ".bak", true);
            else File.Move(temp, ChatPath);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            string message = "Could not save local chat history: " + ex.Message;
            if (hintLabel != null) SetHint(message);
            else startupStorageNotice = message;
        }
    }
    private static void ExportChats()
    {
        using (SaveFileDialog dialog = new SaveFileDialog())
        {
            dialog.FileName = "pattlucky-codex-chats.json"; dialog.Filter = "JSON files (*.json)|*.json";
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, Json.Serialize(chats), new UTF8Encoding(false)); SetHint("Chat history exported."); }
            catch (Exception ex) { MessageBox.Show(form, "Could not export chat history. " + ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private sealed class MediaStudioDialog : Form
    {
        private readonly TextBox endpoint = new TextBox();
        private readonly TextBox promptText = new TextBox();
        private readonly TextBox workflowPath = new TextBox();
        private readonly ComboBox promptBinding = new ComboBox();
        private readonly ComboBox mediaKind = new ComboBox();
        private readonly Button generateButton = new Button();
        private readonly Label status = new Label();
        private readonly ListBox results = new ListBox();
        private readonly PictureBox preview = new PictureBox();
        private string loadedWorkflow;
        private string outputDirectory = Path.Combine(DataDirectory, "Media");

        public MediaStudioDialog()
        {
            Text = "PattLucky Media Studio"; StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(720, 610); Size = new Size(940, 760); BackColor = bg; ForeColor = fg; Font = new Font("Segoe UI", 9); ShowInTaskbar = false;
            TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill; layout.Padding = new Padding(18); layout.ColumnCount = 1; layout.RowCount = 9;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); Controls.Add(layout);

            Panel heading = new Panel(); heading.Dock = DockStyle.Fill;
            PictureBox brandMark = new PictureBox(); brandMark.Image = logo; brandMark.SizeMode = PictureBoxSizeMode.Zoom; brandMark.SetBounds(0, 7, 42, 42); heading.Controls.Add(brandMark);
            Label title = new Label(); title.Text = "MEDIA STUDIO · BETA"; title.Font = new Font("Segoe UI", 12, FontStyle.Bold); title.ForeColor = Color.FromArgb(242, 156, 72); title.SetBounds(54, 5, 280, 24); heading.Controls.Add(title);
            Label subtitle = new Label(); subtitle.Text = "Image generation is in beta · Open-source ComfyUI"; subtitle.ForeColor = muted; subtitle.SetBounds(54, 30, 520, 20); heading.Controls.Add(subtitle); layout.Controls.Add(heading, 0, 0);

            FlowLayoutPanel connection = new FlowLayoutPanel(); connection.Dock = DockStyle.Fill; connection.WrapContents = false; connection.Padding = new Padding(0, 4, 0, 0);
            mediaKind.DropDownStyle = ComboBoxStyle.DropDownList; mediaKind.Width = 105; mediaKind.Items.AddRange(new object[] { "Image", "Video" }); mediaKind.SelectedIndex = 0; mediaKind.Margin = new Padding(0, 0, 8, 0); connection.Controls.Add(mediaKind);
            endpoint.Text = "http://127.0.0.1:8188"; endpoint.Width = 245; endpoint.Margin = new Padding(0, 0, 8, 0); connection.Controls.Add(endpoint);
            Button check = MakeButton("Connect", false); check.Size = new Size(82, 30); check.Margin = new Padding(0, 0, 8, 0); check.Click += delegate { CheckConnection(); }; connection.Controls.Add(check);
            Button openUi = MakeButton("Open ComfyUI", false); openUi.Size = new Size(105, 30); openUi.Margin = Padding.Empty; openUi.Click += delegate { OpenComfyUi(); }; connection.Controls.Add(openUi); layout.Controls.Add(connection, 0, 1);

            Label promptLabel = new Label(); promptLabel.Text = "PROMPT"; promptLabel.ForeColor = muted; promptLabel.Font = new Font("Segoe UI", 8, FontStyle.Bold); promptLabel.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(promptLabel, 0, 2);
            promptText.Multiline = true; promptText.AcceptsReturn = true; promptText.ScrollBars = ScrollBars.Vertical; promptText.Dock = DockStyle.Fill; promptText.BackColor = panel; promptText.ForeColor = fg; promptText.BorderStyle = BorderStyle.FixedSingle; promptText.Font = new Font("Segoe UI", 10); layout.Controls.Add(promptText, 0, 3);

            FlowLayoutPanel workflowRow = new FlowLayoutPanel(); workflowRow.Dock = DockStyle.Fill; workflowRow.WrapContents = false; workflowRow.Padding = new Padding(0, 3, 0, 0);
            workflowPath.ReadOnly = true; workflowPath.Width = 420; workflowPath.BackColor = panel; workflowPath.ForeColor = muted; workflowPath.Margin = new Padding(0, 0, 8, 0); workflowRow.Controls.Add(workflowPath);
            Button load = MakeButton("Load API workflow", false); load.Size = new Size(145, 30); load.Margin = Padding.Empty; load.Click += delegate { LoadWorkflow(); }; workflowRow.Controls.Add(load); layout.Controls.Add(workflowRow, 0, 4);

            FlowLayoutPanel bindingRow = new FlowLayoutPanel(); bindingRow.Dock = DockStyle.Fill; bindingRow.WrapContents = false; bindingRow.Padding = new Padding(0, 3, 0, 0);
            Label bindingLabel = new Label(); bindingLabel.Text = "Prompt input"; bindingLabel.Width = 88; bindingLabel.Height = 27; bindingLabel.TextAlign = ContentAlignment.MiddleLeft; bindingLabel.ForeColor = muted; bindingRow.Controls.Add(bindingLabel);
            promptBinding.DropDownStyle = ComboBoxStyle.DropDownList; promptBinding.Width = 320; promptBinding.Enabled = false; bindingRow.Controls.Add(promptBinding);
            Label bindingHelp = new Label(); bindingHelp.Text = "Choose the positive text input"; bindingHelp.Width = 225; bindingHelp.Height = 27; bindingHelp.TextAlign = ContentAlignment.MiddleLeft; bindingHelp.ForeColor = muted; bindingRow.Controls.Add(bindingHelp); layout.Controls.Add(bindingRow, 0, 5);

            FlowLayoutPanel actions = new FlowLayoutPanel(); actions.Dock = DockStyle.Fill; actions.WrapContents = false; actions.Padding = new Padding(0, 5, 0, 0);
            generateButton = MakeButton("Generate locally", true); generateButton.Size = new Size(160, 34); generateButton.Click += delegate { Generate(); }; actions.Controls.Add(generateButton);
            Button openOutput = MakeButton("Open output folder", false); openOutput.Size = new Size(140, 34); openOutput.Margin = new Padding(10, 0, 0, 0); openOutput.Click += delegate { OpenOutputFolder(); }; actions.Controls.Add(openOutput); layout.Controls.Add(actions, 0, 6);

            SplitContainer split = new SplitContainer(); split.Dock = DockStyle.Fill; split.BackColor = line; split.SplitterDistance = 300; split.Panel1.Padding = new Padding(0, 0, 6, 0); split.Panel2.Padding = new Padding(6, 0, 0, 0);
            results.Dock = DockStyle.Fill; results.BackColor = panel; results.ForeColor = fg; results.BorderStyle = BorderStyle.FixedSingle; results.SelectedIndexChanged += delegate { ShowSelectedResult(); }; results.DoubleClick += delegate { OpenSelectedResult(); }; split.Panel1.Controls.Add(results);
            preview.Dock = DockStyle.Fill; preview.BackColor = Color.FromArgb(18, 20, 17); preview.SizeMode = PictureBoxSizeMode.Zoom; preview.DoubleClick += delegate { OpenSelectedResult(); }; split.Panel2.Controls.Add(preview); layout.Controls.Add(split, 0, 7);

            status.Text = "Free and local: install ComfyUI and your chosen models, then load a ComfyUI API workflow."; status.Dock = DockStyle.Fill; status.ForeColor = muted; status.TextAlign = ContentAlignment.MiddleLeft; status.AutoEllipsis = true; layout.Controls.Add(status, 0, 8);
            FormClosed += delegate { if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; } };
        }

        private void LoadWorkflow()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select a ComfyUI API workflow"; dialog.Filter = "ComfyUI API workflow (*.json)|*.json|JSON files (*.json)|*.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    object parsed = Json.DeserializeObject(File.ReadAllText(dialog.FileName));
                    Dictionary<string, object> graph = parsed as Dictionary<string, object>;
                    if (graph == null) throw new InvalidOperationException("This file is not a ComfyUI API workflow object.");
                    List<PromptBinding> candidates = FindPromptBindings(graph);
                    if (candidates.Count == 0) throw new InvalidOperationException("No text or prompt inputs were found. Export the workflow in ComfyUI API format and include a text-encoder prompt node.");
                    loadedWorkflow = dialog.FileName; workflowPath.Text = loadedWorkflow; promptBinding.Items.Clear();
                    foreach (PromptBinding candidate in candidates) promptBinding.Items.Add(candidate);
                    promptBinding.SelectedIndex = 0; promptBinding.Enabled = true; status.Text = "Workflow loaded. Choose its positive prompt input, then generate.";
                }
                catch (Exception ex) { loadedWorkflow = null; promptBinding.Items.Clear(); promptBinding.Enabled = false; status.Text = "Could not load workflow: " + ex.Message; }
            }
        }

        private static List<PromptBinding> FindPromptBindings(Dictionary<string, object> graph)
        {
            List<PromptBinding> found = new List<PromptBinding>();
            foreach (KeyValuePair<string, object> nodePair in graph)
            {
                Dictionary<string, object> node = nodePair.Value as Dictionary<string, object>; if (node == null) continue;
                string classType = node.ContainsKey("class_type") ? Convert.ToString(node["class_type"]) : "Node";
                Dictionary<string, object> inputs = node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null; if (inputs == null) continue;
                foreach (KeyValuePair<string, object> input in inputs)
                {
                    string name = input.Key.ToLowerInvariant(); string value = input.Value as string;
                    if (value == null) continue;
                    if (value.Contains("{{prompt}}") || name == "text" || name == "prompt" || name == "positive" || name == "caption")
                        found.Add(new PromptBinding(nodePair.Key, input.Key, "Node " + nodePair.Key + " · " + classType + " · " + input.Key));
                }
            }
            return found;
        }

        private void CheckConnection()
        {
            string baseUrl, error;
            if (!TryGetLocalBaseUrl(out baseUrl, out error)) { status.Text = error; return; }
            if (Interlocked.CompareExchange(ref mediaConnectionCheckActive, 1, 0) != 0) return;
            status.Text = "Checking local ComfyUI…";
            ThreadPool.QueueUserWorkItem(delegate {
                try { RequestComfyJsonAt(baseUrl, "GET", "/system_stats", null, 8000); PostUi(delegate { status.Text = "ComfyUI is connected at " + baseUrl + " · Generation runs locally."; }); }
                catch (Exception ex) { PostUi(delegate { status.Text = "ComfyUI is not reachable: " + ex.Message; }); }
                finally { Interlocked.Exchange(ref mediaConnectionCheckActive, 0); }
            });
        }

        private void OpenComfyUi()
        {
            string baseUrl, error; if (!TryGetLocalBaseUrl(out baseUrl, out error)) { status.Text = error; return; }
            try { Process.Start(new ProcessStartInfo(baseUrl + "/") { UseShellExecute = true }); }
            catch (Exception ex) { status.Text = "Could not open ComfyUI: " + ex.Message; }
        }

        private bool TryGetLocalBaseUrl(out string baseUrl, out string error)
        {
            return TryNormalizeLocalBaseUrl(endpoint.Text, out baseUrl, out error);
        }

        private static bool TryNormalizeLocalBaseUrl(string value, out string baseUrl, out string error)
        {
            baseUrl = null; error = null; Uri address;
            if (!Uri.TryCreate((value ?? "").Trim(), UriKind.Absolute, out address) || address.Scheme != Uri.UriSchemeHttp || !address.IsLoopback || !String.IsNullOrEmpty(address.UserInfo) || !String.IsNullOrEmpty(address.Query) || !String.IsNullOrEmpty(address.Fragment) || address.AbsolutePath != "/")
            {
                error = "Use a local ComfyUI address such as http://127.0.0.1:8188. Remote endpoints are disabled."; return false;
            }
            baseUrl = address.GetLeftPart(UriPartial.Authority).TrimEnd('/'); return true;
        }

        private void Generate()
        {
            if (String.IsNullOrWhiteSpace(promptText.Text)) { status.Text = "Write a prompt first."; return; }
            if (String.IsNullOrEmpty(loadedWorkflow) || promptBinding.SelectedItem == null) { status.Text = "Load a ComfyUI API workflow first."; return; }
            string baseUrl, error; if (!TryGetLocalBaseUrl(out baseUrl, out error)) { status.Text = error; return; }
            string file = loadedWorkflow; string prompt = promptText.Text.Trim(); PromptBinding binding = (PromptBinding)promptBinding.SelectedItem; string kind = Convert.ToString(mediaKind.SelectedItem);
            generateButton.Enabled = false; status.Text = "Sending workflow to local ComfyUI…";
            ThreadPool.QueueUserWorkItem(delegate {
                try
                {
                    Dictionary<string, object> graph = Json.DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                    Dictionary<string, object> node = graph != null && graph.ContainsKey(binding.NodeId) ? graph[binding.NodeId] as Dictionary<string, object> : null;
                    Dictionary<string, object> inputs = node != null && node.ContainsKey("inputs") ? node["inputs"] as Dictionary<string, object> : null;
                    if (inputs == null || !inputs.ContainsKey(binding.InputName)) throw new InvalidOperationException("The selected prompt input is no longer present in this workflow.");
                    string original = Convert.ToString(inputs[binding.InputName]);
                    inputs[binding.InputName] = original.Contains("{{prompt}}") ? original.Replace("{{prompt}}", prompt) : prompt;
                    Dictionary<string, object> queued = RequestComfyJsonAt(baseUrl, "POST", "/prompt", new Dictionary<string, object> { { "prompt", graph }, { "client_id", Guid.NewGuid().ToString("N") } }, 15000);
                    object promptIdValue; if (!queued.TryGetValue("prompt_id", out promptIdValue)) throw new InvalidOperationException("ComfyUI did not return a prompt ID.");
                    string promptId = Convert.ToString(promptIdValue); SetStatus("Rendering " + kind.ToLowerInvariant() + " locally… ComfyUI prompt " + promptId.Substring(0, Math.Min(8, promptId.Length)));
                    List<MediaOutput> outputs = WaitForOutputs(baseUrl, promptId, kind);
                    if (outputs.Count == 0) throw new InvalidOperationException("The workflow finished without a " + kind.ToLowerInvariant() + " output. Check the workflow's save node and selected render type.");
                    string kindDirectory = Path.Combine(outputDirectory, kind == "Video" ? "Videos" : "Images"); Directory.CreateDirectory(kindDirectory); List<string> saved = new List<string>();
                    foreach (MediaOutput output in outputs)
                    {
                        string query = "?filename=" + Uri.EscapeDataString(output.FileName) + "&subfolder=" + Uri.EscapeDataString(output.Subfolder) + "&type=" + Uri.EscapeDataString(output.Type);
                        string safeName = Path.GetFileName(output.FileName); string destination = Path.Combine(kindDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "_" + safeName);
                        DownloadComfyOutput(baseUrl + "/view" + query, destination, 600000); saved.Add(destination);
                    }
                    PostUi(delegate { foreach (string path in saved) results.Items.Add(path); if (saved.Count > 0) results.SelectedIndex = results.Items.Count - saved.Count; status.Text = saved.Count + " output(s) saved locally to " + outputDirectory; generateButton.Enabled = true; });
                }
                catch (Exception ex) { PostUi(delegate { status.Text = "Generation failed: " + ex.Message; generateButton.Enabled = true; }); }
            });
        }

        private List<MediaOutput> WaitForOutputs(string baseUrl, string promptId, string kind)
        {
            DateTime deadline = DateTime.UtcNow.AddHours(3);
            while (DateTime.UtcNow < deadline)
            {
                Dictionary<string, object> history = RequestComfyJsonAt(baseUrl, "GET", "/history/" + Uri.EscapeDataString(promptId), null, 15000);
                object entryValue;
                if (history.TryGetValue(promptId, out entryValue))
                {
                    Dictionary<string, object> entry = entryValue as Dictionary<string, object>;
                    Dictionary<string, object> nodes = entry != null && entry.ContainsKey("outputs") ? entry["outputs"] as Dictionary<string, object> : null;
                    if (nodes != null) return ExtractOutputs(nodes, kind);
                }
                SetStatus("Rendering locally… ComfyUI is processing the workflow."); Thread.Sleep(1200);
            }
            throw new TimeoutException("ComfyUI did not finish within three hours.");
        }

        private static List<MediaOutput> ExtractOutputs(Dictionary<string, object> nodes, string kind)
        {
            List<MediaOutput> found = new List<MediaOutput>(); HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object nodeValue in nodes.Values)
            {
                Dictionary<string, object> node = nodeValue as Dictionary<string, object>; if (node == null) continue;
                foreach (string outputKey in new string[] { "images", "videos", "gifs" })
                {
                    object values; if (!node.TryGetValue(outputKey, out values)) continue;
                    IEnumerable items = values as IEnumerable; if (items == null || values is string) continue;
                    foreach (object itemValue in items)
                    {
                        Dictionary<string, object> item = itemValue as Dictionary<string, object>; if (item == null) continue;
                        object filename; if (!item.TryGetValue("filename", out filename)) continue;
                        string name = Convert.ToString(filename); string extension = Path.GetExtension(name).ToLowerInvariant();
                        bool isVideo = outputKey == "videos" || outputKey == "gifs" || extension == ".mp4" || extension == ".webm" || extension == ".mov" || extension == ".mkv" || extension == ".gif";
                        if (String.Equals(kind, "Video", StringComparison.OrdinalIgnoreCase) != isVideo) continue;
                        string subfolder = item.ContainsKey("subfolder") ? Convert.ToString(item["subfolder"]) : ""; string type = item.ContainsKey("type") ? Convert.ToString(item["type"]) : "output"; string key = type + "|" + subfolder + "|" + Convert.ToString(filename);
                        if (seen.Add(key)) found.Add(new MediaOutput(name, subfolder, type));
                    }
                }
            }
            return found;
        }

        private void ShowSelectedResult()
        {
            string path = results.SelectedItem as string; if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp" || extension == ".bmp")
            {
                try { using (Image image = Image.FromFile(path)) { Image prior = preview.Image; preview.Image = new Bitmap(image); if (prior != null) prior.Dispose(); } }
                catch (Exception ex) { status.Text = "Could not preview image: " + ex.Message; }
            }
            else { Image prior = preview.Image; preview.Image = null; if (prior != null) prior.Dispose(); status.Text = "Video selected · double-click to open in your default video player."; }
        }

        private void OpenSelectedResult()
        {
            string path = results.SelectedItem as string;
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { status.Text = "Could not open output: " + ex.Message; }
        }

        private void OpenOutputFolder()
        {
            try { Directory.CreateDirectory(outputDirectory); Process.Start(new ProcessStartInfo(outputDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { status.Text = "Could not open output folder: " + ex.Message; }
        }

        private void SetStatus(string value)
        {
            PostUi(delegate { status.Text = value; });
        }

        private void PostUi(MethodInvoker action)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); } catch { }
        }

        private static Dictionary<string, object> RequestComfyJsonAt(string baseUrl, string method, string path, object payload, int timeout)
        {
            string url = baseUrl.TrimEnd('/') + path; HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url); request.Method = method; request.Timeout = timeout; request.ReadWriteTimeout = timeout;
            if (payload != null)
            {
                byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(payload)); request.ContentType = "application/json"; request.ContentLength = data.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
            }
            try { using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) using (StreamReader reader = new StreamReader(response.GetResponseStream())) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response == null) throw new InvalidOperationException("Could not reach local ComfyUI at " + url + ". Start ComfyUI and check its port.", ex);
                using (response) using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                {
                    string detail = reader.ReadToEnd();
                    throw new InvalidOperationException("ComfyUI returned HTTP " + (int)response.StatusCode + (String.IsNullOrWhiteSpace(detail) ? "." : ": " + detail));
                }
            }
        }

        private static void DownloadComfyOutput(string url, string destination, int timeout)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url); request.Timeout = timeout; request.ReadWriteTimeout = timeout;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            }
            catch
            {
                try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                throw;
            }
        }

        private sealed class PromptBinding
        {
            public string NodeId, InputName, Label;
            public PromptBinding(string nodeId, string inputName, string label) { NodeId = nodeId; InputName = inputName; Label = label; }
            public override string ToString() { return Label; }
        }

        private sealed class MediaOutput
        {
            public string FileName, Subfolder, Type;
            public MediaOutput(string fileName, string subfolder, string type) { FileName = fileName; Subfolder = subfolder; Type = type; }
        }
    }

    private sealed class AgentInfo { public string Key, Name, Model, Size, Description; public AgentInfo(string key, string name, string model, string size, string description) { Key = key; Name = name; Model = model; Size = size; Description = description; } }
    private sealed class BufferedForm : Form { public BufferedForm() { DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class AgentMarkLabel : Label
    {
        public AgentMarkLabel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush mark = new SolidBrush(AgentAccent(selected))) e.Graphics.FillEllipse(mark, 1, 1, Width - 3, Height - 3);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
    private sealed class TypingIndicatorPanel : Panel
    {
        public TypingIndicatorPanel()
        {
            Size = new Size(104, 62); BackColor = bg;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            string elapsed = "Thinking · " + FormatDuration(responseTimer.ElapsedMilliseconds);
            using (Font timerFont = new Font("Segoe UI", 8)) TextRenderer.DrawText(e.Graphics, elapsed, timerFont, new Rectangle(1, 0, Width - 2, 17), muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            Rectangle box = new Rectangle(0, 19, 90, 40); int radius = 12;
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush fill = new SolidBrush(panel))
            using (Pen edge = new Pen(line))
            {
                path.AddArc(box.Left, box.Top, radius * 2, radius * 2, 180, 90); path.AddArc(box.Right - radius * 2, box.Top, radius * 2, radius * 2, 270, 90); path.AddArc(box.Right - radius * 2, box.Bottom - radius * 2, radius * 2, radius * 2, 0, 90); path.AddArc(box.Left, box.Bottom - radius * 2, radius * 2, radius * 2, 90, 90); path.CloseFigure();
                e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(edge, path);
            }
            if (logo != null) { GraphicsState state = e.Graphics.Save(); e.Graphics.TranslateTransform(24, box.Top + box.Height / 2f); e.Graphics.RotateTransform(spinAngle); e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; e.Graphics.DrawImage(logo, -10, -10, 20, 20); e.Graphics.Restore(state); }
            int active = (spinAngle / 45) % 3;
            for (int i = 0; i < 3; i++) { int size = i == active ? 8 : 6; int x = 49 + i * 11; using (SolidBrush dot = new SolidBrush(i == active ? Lime : muted)) e.Graphics.FillEllipse(dot, x, box.Top + box.Height / 2 - size / 2, size, size); }
        }
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel { public BufferedFlowLayoutPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class ModernButton : Button
    {
        private bool hovered, pressed;
        public bool Primary { get; set; }
        public ModernButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint covers the full client area; leaving the native button background
            // untouched creates dark square patches around custom button edges.
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.None;
            Color fill = BackColor;
            if (!Enabled) fill = Primary ? Color.FromArgb(101, 125, 68) : Color.FromArgb(125, 130, 120);
            else if (pressed) fill = Primary ? Color.FromArgb(139, 199, 35) : Blend(BackColor, Lime, 0.16f);
            else if (hovered) fill = Primary ? Color.FromArgb(178, 237, 76) : Blend(BackColor, Lime, 0.09f);
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            // Keep a clear edge on every button, including lime primary actions.
            // The former 8% tint was too subtle on dark panels and looked borderless.
            Color borderColor = Focused ? Lime : (Primary ? Color.FromArgb(112, 157, 29) : Blend(line, fg, hovered ? 0.42f : 0.30f));
            using (Pen border = new Pen(borderColor, 1f))
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            Color textColor = Enabled ? ForeColor : Color.FromArgb(190, 190, 190);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.TopLeft || TextAlign == ContentAlignment.BottomLeft) flags |= TextFormatFlags.Left;
            else if (TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.HorizontalCenter;
            Rectangle textRect = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical));
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor, flags);
        }
        private static Color Blend(Color original, Color accent, float amount) { return Color.FromArgb((int)(original.R * (1 - amount) + accent.R * amount), (int)(original.G * (1 - amount) + accent.G * amount), (int)(original.B * (1 - amount) + accent.B * amount)); }
    }
    public sealed class ChatRecord { public string Id { get; set; } public string Title { get; set; } public string Agent { get; set; } public List<MessageRecord> Messages { get; set; } public ChatRecord() { Messages = new List<MessageRecord>(); } }
    public sealed class MessageRecord { public string Role { get; set; } public string Content { get; set; } public string Agent { get; set; } public long Time { get; set; } public long DurationMilliseconds { get; set; } }
}
