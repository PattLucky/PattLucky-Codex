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
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;
using System.Reflection;

[assembly: AssemblyTitle("PattLucky Codex")]
[assembly: AssemblyDescription("Local AI desktop chat workspace")]
[assembly: AssemblyCompany("PattLucky")]
[assembly: AssemblyProduct("PattLucky Codex")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class PattLuckyCodex
{
    private const string AppUrl = "http://127.0.0.1:43771/";
    private const string GitHubLatestReleaseApi = "https://api.github.com/repos/PattLucky/PattLucky-Codex/releases/latest";
    private const string GitHubReleasesPage = "https://github.com/PattLucky/PattLucky-Codex/releases/latest";
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly AgentInfo[] Agents = {
        new AgentInfo("writer", "Essays & documents", "gemma4:12b", "About 7.7–8 GB", "Draft and polish essays, reports, and documents."),
        new AgentInfo("coding", "Coding", "qwen3-coder:30b", "About 19 GB", "Design, understand, debug, and write software."),
        new AgentInfo("creative", "Creative writing", "qwen3.5:9b", "About 6.6–7.6 GB", "Create stories, scenes, characters, and fresh ideas.")
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
    private static Label headerTitle;
    private static Button getModelButton, sendButton;
    private static Panel headerPanel;
    private static PictureBox spinner;
    private static System.Windows.Forms.Timer statusTimer, runtimeTimer, spinTimer, resizeTimer;
    private static Image logo;
    private static int spinAngle;
    private static bool busy, connected;
    private static bool storageWriteBlocked;
    private static string startupStorageNotice;
    private static long sessionStart = Environment.TickCount;
    private static string selected = "writer";
    private static readonly HashSet<string> availableModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
    private static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PattLucky Codex"); } }
    private static string ChatPath { get { return Path.Combine(DataDirectory, "chats.json"); } }
    private static string CurrentVersion { get { Version version = Assembly.GetExecutingAssembly().GetName().Version; return version == null ? "1.0.0" : version.ToString(3); } }

    [STAThread]
    private static int Main()
    {
        string serverScript = Path.Combine(Root, "launch.ps1");
        if (!File.Exists(serverScript) || !File.Exists(FindResource("app.html")) || !File.Exists(FindResource("PattLuckyLogo.png")))
        {
            MessageBox.Show("PattLucky Codex files are missing. Reinstall it using the setup wizard.", "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    MessageBox.Show("The local app service could not start. Close any other PattLucky Codex window and try again.", "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            Application.Run(CreateWindow());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("PattLucky Codex could not open. " + ex.Message, "PattLucky Codex", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        form = new BufferedForm(); form.Text = "PattLucky Codex Ver " + CurrentVersion + "."; form.Icon = new Icon(FindResource("PattLuckyCodex.ico"));
        form.AutoScaleDimensions = new SizeF(96f, 96f); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.MinimumSize = new Size(760, 560); form.Size = new Size(1200, 800); form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = bg; form.ForeColor = fg; form.Font = new Font("Segoe UI", 9.5f); form.KeyPreview = true;
        TableLayoutPanel layout = new TableLayoutPanel(); layout.Dock = DockStyle.Fill; layout.ColumnCount = 2; layout.RowCount = 1;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 265)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); form.Controls.Add(layout);
        layout.Controls.Add(BuildSidebar(), 0, 0); layout.Controls.Add(BuildMain(), 1, 0);
        LoadChats(); if (chats.Count > 0) current = chats[0]; else NewChat();
        RefreshHistory(); RefreshMessages(); UpdateAgent();
        if (!String.IsNullOrEmpty(startupStorageNotice)) SetHint(startupStorageNotice);
        resizeTimer = new System.Windows.Forms.Timer(); resizeTimer.Interval = 180; resizeTimer.Tick += delegate { resizeTimer.Stop(); LayoutHeader(); ResizeHistoryItems(); if (!busy && current != null) RefreshMessages(); };
        form.Resize += delegate { if (resizeTimer != null) { resizeTimer.Stop(); resizeTimer.Start(); } };
        form.Shown += delegate { CheckForUpdates(); };
        form.FormClosing += delegate { SaveChats(); };
        statusTimer = new System.Windows.Forms.Timer(); statusTimer.Interval = 4000; statusTimer.Tick += delegate { RefreshStatus(); }; statusTimer.Start(); RefreshStatus();
        runtimeTimer = new System.Windows.Forms.Timer(); runtimeTimer.Interval = 1000; runtimeTimer.Tick += delegate { UpdateMonitor(); }; runtimeTimer.Start(); UpdateMonitor();
        spinTimer = new System.Windows.Forms.Timer(); spinTimer.Interval = 45; spinTimer.Tick += delegate { spinAngle = (spinAngle + 8) % 360; spinner.Invalidate(); };
        form.FormClosed += delegate { statusTimer.Stop(); runtimeTimer.Stop(); if (spinTimer != null) spinTimer.Stop(); if (resizeTimer != null) resizeTimer.Stop(); if (logo != null) logo.Dispose(); };
        return form;
    }

    private static Control BuildSidebar()
    {
        Panel side = new BufferedPanel(); side.Dock = DockStyle.Fill; side.BackColor = sidebar; side.Padding = new Padding(14, 17, 14, 14);
        TableLayoutPanel stack = new TableLayoutPanel(); sidebarStack = stack; stack.Dock = DockStyle.Fill; stack.ColumnCount = 1; stack.RowCount = 6;
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); side.Controls.Add(stack);
        Panel brand = new Panel(); brand.Dock = DockStyle.Fill; PictureBox mark = new PictureBox(); mark.Image = logo; mark.SizeMode = PictureBoxSizeMode.Zoom; mark.SetBounds(3, 4, 42, 42); brand.Controls.Add(mark);
        Label title = new Label(); title.Text = "PattLucky Codex"; title.Font = new Font("Segoe UI", 11, FontStyle.Bold); title.ForeColor = fg; title.SetBounds(54, 5, 185, 23); brand.Controls.Add(title);
        Label ver = new Label(); ver.Text = "Local AI workspace · Ver " + CurrentVersion + "."; ver.Font = new Font("Segoe UI", 8.5f); ver.ForeColor = muted; ver.SetBounds(54, 29, 200, 20); brand.Controls.Add(ver); stack.Controls.Add(brand, 0, 0);
        Button newChat = MakeButton("＋   New chat", true); newChat.Dock = DockStyle.Fill; newChat.Click += delegate { NewChat(); RefreshHistory(); RefreshMessages(); UpdateMonitor(); }; stack.Controls.Add(newChat, 0, 1);
        Label historyTitle = new Label(); historyTitle.Text = "RECENT CHATS"; historyTitle.ForeColor = muted; historyTitle.Font = new Font("Segoe UI", 8, FontStyle.Bold); historyTitle.TextAlign = ContentAlignment.MiddleLeft; stack.Controls.Add(historyTitle, 0, 2);
        history = new BufferedFlowLayoutPanel(); history.Dock = DockStyle.Fill; history.FlowDirection = FlowDirection.TopDown; history.WrapContents = false; history.AutoScroll = true; history.BackColor = sidebar; stack.Controls.Add(history, 0, 3);
        Panel bottom = new BufferedPanel(); bottom.Dock = DockStyle.Fill; bottom.BackColor = sidebar;
        statusToggle = MakeButton("● OLLAMA CHECKING  ·  STATUS & TOOLS  ›", false); statusToggle.Dock = DockStyle.Top; statusToggle.Height = 32; statusToggle.FlatAppearance.BorderSize = 0; statusToggle.BackColor = sidebar;
        statusDetails = new BufferedPanel(); statusDetails.Dock = DockStyle.Top; statusDetails.Height = 87; statusDetails.Visible = false;
        Panel monitor = new Panel(); monitor.BackColor = panel; monitor.BorderStyle = BorderStyle.FixedSingle; monitor.SetBounds(0, 0, 233, 56); statusDetails.Controls.Add(monitor);
        ollamaLabel = new Label(); ollamaLabel.Text = "● OLLAMA CHECKING"; ollamaLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); ollamaLabel.ForeColor = muted; ollamaLabel.SetBounds(10, 5, 210, 18); monitor.Controls.Add(ollamaLabel);
        runtimeLabel = new Label(); runtimeLabel.ForeColor = muted; runtimeLabel.Font = new Font("Segoe UI", 8); runtimeLabel.SetBounds(10, 27, 95, 16); monitor.Controls.Add(runtimeLabel);
        countsLabel = new Label(); countsLabel.ForeColor = muted; countsLabel.Font = new Font("Segoe UI", 8); countsLabel.SetBounds(106, 27, 118, 16); monitor.Controls.Add(countsLabel);
        speedLabel = new Label(); speedLabel.ForeColor = muted; speedLabel.Font = new Font("Segoe UI", 8); speedLabel.SetBounds(10, 41, 205, 15); monitor.Controls.Add(speedLabel);
        Button export = MakeButton("Export chat history", false); export.SetBounds(0, 61, 233, 27); export.Click += delegate { ExportChats(); }; statusDetails.Controls.Add(export);
        statusToggle.Click += delegate { statusExpanded = !statusExpanded; statusDetails.Visible = statusExpanded; sidebarStack.RowStyles[4].Height = statusExpanded ? 126 : 38; statusToggle.Text = StatusToggleText(); };
        bottom.Controls.Add(statusDetails); bottom.Controls.Add(statusToggle); statusToggle.BringToFront(); stack.Controls.Add(bottom, 0, 4);
        Label note = new Label(); note.Text = "Chats save on this device"; note.ForeColor = muted; note.TextAlign = ContentAlignment.MiddleLeft; note.Dock = DockStyle.Fill; note.Font = new Font("Segoe UI", 8.5f); stack.Controls.Add(note, 0, 5);
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
        agentPicker = new ComboBox(); agentPicker.DropDownStyle = ComboBoxStyle.DropDownList; agentPicker.Width = 205; agentPicker.Location = new Point(105, 17); agentPicker.BackColor = panel; agentPicker.ForeColor = fg;
        foreach (AgentInfo agent in Agents) agentPicker.Items.Add(agent.Name); agentPicker.SelectedIndexChanged += delegate { if (agentPicker.SelectedIndex >= 0) { selected = Agents[agentPicker.SelectedIndex].Key; if (current != null) current.Agent = selected; SaveChats(); UpdateAgent(); RefreshMessages(); } }; top.Controls.Add(agentPicker);
        getModelButton = MakeButton("Get model", false); getModelButton.Size = new Size(105, 34); getModelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; getModelButton.Click += delegate { PullModel(); }; top.Controls.Add(getModelButton);
        modelLabel = new Label(); modelLabel.AutoSize = true; modelLabel.ForeColor = muted; modelLabel.Font = new Font("Segoe UI", 8.5f); top.Controls.Add(modelLabel); stack.Controls.Add(top, 0, 0);
        top.Resize += delegate { LayoutHeader(); };
        messages = new BufferedFlowLayoutPanel(); messages.Dock = DockStyle.Fill; messages.FlowDirection = FlowDirection.TopDown; messages.WrapContents = false; messages.AutoScroll = true; messages.Padding = new Padding(18, 18, 18, 18); messages.BackColor = bg;
        messages.Paint += delegate(object sender, PaintEventArgs e) { if (logo == null) return; Rectangle r = new Rectangle((messages.ClientSize.Width - 300) / 2, (messages.ClientSize.Height - 300) / 2, 300, 300); using (ImageAttributes a = new ImageAttributes()) { a.SetColorMatrix(new ColorMatrix { Matrix33 = 0.035f }); e.Graphics.DrawImage(logo, r, 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, a); } };
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
            ChatRecord item = chat; Button b = MakeButton(item.Title, false); b.Width = Math.Max(205, history.ClientSize.Width - 28); b.Height = 37; b.TextAlign = ContentAlignment.MiddleLeft; b.FlatAppearance.BorderSize = 0; b.BackColor = item == current ? panel : sidebar; b.Click += delegate { current = item; selected = item.Agent; agentPicker.SelectedIndex = AgentIndex(selected); UpdateAgent(); RefreshHistory(); RefreshMessages(); }; history.Controls.Add(b);
        }
        history.ResumeLayout(); UpdateMonitor();
    }

    private static void ResizeHistoryItems()
    {
        if (history == null) return;
        int width = Math.Max(150, history.ClientSize.Width - 28);
        foreach (Control item in history.Controls) item.Width = width;
    }

    private static void LayoutHeader()
    {
        if (headerPanel == null || agentPicker == null || getModelButton == null) return;
        int width = headerPanel.ClientSize.Width;
        getModelButton.Left = Math.Max(8, width - getModelButton.Width - 12);
        int pickerLeft = 105;
        int rightEdge = getModelButton.Left - 14;
        modelLabel.Visible = width >= 650;
        int pickerWidth = modelLabel.Visible ? 205 : Math.Max(150, Math.Min(245, rightEdge - pickerLeft - 8));
        agentPicker.SetBounds(pickerLeft, 17, pickerWidth, agentPicker.Height);
        modelLabel.Left = pickerLeft + pickerWidth + 12;
        modelLabel.Top = 23;
        modelLabel.MaximumSize = new Size(Math.Max(0, rightEdge - modelLabel.Left), 22);
    }

    private static void RefreshMessages()
    {
        if (messages == null) return; messages.SuspendLayout(); messages.Controls.Clear();
        if (current == null || current.Messages.Count == 0)
        {
            welcomeStatus = null;
            Panel welcome = new Panel(); welcome.Width = Math.Max(320, messages.ClientSize.Width - messages.Padding.Horizontal - 24); welcome.Height = 350; welcome.BackColor = Color.Transparent;
            PictureBox image = new PictureBox(); image.Image = logo; image.SizeMode = PictureBoxSizeMode.Zoom; image.SetBounds((welcome.Width - 88) / 2, 8, 88, 88); welcome.Controls.Add(image);
            Label heading = new Label(); heading.Text = "Local AI. Your workspace."; heading.Font = new Font("Segoe UI", 21, FontStyle.Bold); heading.ForeColor = fg; heading.TextAlign = ContentAlignment.MiddleCenter; heading.SetBounds(10, 104, welcome.Width - 20, 44); welcome.Controls.Add(heading);
            welcomeStatus = new Label(); welcomeStatus.Text = connected ? "Ollama is connected. Choose an agent and install its model to begin." : "Start Ollama, choose an agent, and install its model to begin."; welcomeStatus.Font = new Font("Segoe UI", 10); welcomeStatus.ForeColor = muted; welcomeStatus.Size = new Size(Math.Min(520, welcome.Width - 30), 44); welcomeStatus.TextAlign = ContentAlignment.MiddleCenter; welcomeStatus.Location = new Point((welcome.Width - welcomeStatus.Width) / 2, 150); welcome.Controls.Add(welcomeStatus);
            int y = 205; int cardWidth = Math.Min(520, welcome.Width - 32); foreach (AgentInfo a in Agents) { Button b = MakeButton(a.Name + "   ·   " + a.Model + "   ·   " + a.Size, false); b.Size = new Size(cardWidth, 36); b.Location = new Point((welcome.Width - cardWidth) / 2, y); b.TextAlign = ContentAlignment.MiddleLeft; b.Click += delegate { selected = a.Key; if (current != null) current.Agent = selected; agentPicker.SelectedIndex = AgentIndex(selected); UpdateAgent(); }; welcome.Controls.Add(b); y += 42; }
            messages.Controls.Add(welcome);
        }
        else { welcomeStatus = null; foreach (MessageRecord m in current.Messages) AddMessage(m); }
        messages.ResumeLayout(); ScrollMessages();
    }

    private static void AddMessage(MessageRecord message)
    {
        int width = Math.Max(320, Math.Min(760, messages.ClientSize.Width - 75));
        Panel card = new Panel(); card.Width = width; card.BackColor = message.Role == "user" ? userBubble : panel; card.Padding = new Padding(15, 10, 15, 10); card.Margin = new Padding(message.Role == "user" ? Math.Max(10, messages.ClientSize.Width - width - 75) : 8, 8, 8, 8);
        string labelText = message.Role == "assistant" ? AgentByKey(message.Agent).Name + "  ·  " + (message.Time > 0 ? new DateTime(message.Time, DateTimeKind.Utc).ToLocalTime().ToString("h:mm tt") : "") : "You";
        Label who = new Label(); who.Text = labelText; who.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); who.ForeColor = muted; who.Dock = DockStyle.Top; who.Height = 23; card.Controls.Add(who);
        Label body = new Label(); body.Text = message.Content; body.Font = new Font("Segoe UI", 10); body.ForeColor = fg; body.AutoSize = true; body.MaximumSize = new Size(width - 38, 0); body.Dock = DockStyle.Top; body.Padding = new Padding(0, 3, 0, 4); card.Controls.Add(body); body.BringToFront(); who.BringToFront();
        card.Height = Math.Max(65, body.PreferredHeight + who.Height + 24); messages.Controls.Add(card);
    }

    private static void SendMessage()
    {
        if (busy) return; string text = prompt.Text.Trim(); if (text.Length == 0) return;
        if (current == null) NewChat(); AgentInfo agent = AgentByKey(selected);
        if (!availableModels.Contains(agent.Model)) { SetHint("Install " + agent.Model + " first using the Get model button."); return; }
        if (current.Messages.Count == 0) current.Title = text.Length > 42 ? text.Substring(0, 42) : text;
        current.Agent = selected; MessageRecord user = new MessageRecord(); user.Role = "user"; user.Content = text; user.Agent = selected; user.Time = DateTime.UtcNow.Ticks; current.Messages.Add(user); prompt.Clear(); SaveChats(); RefreshHistory(); RefreshMessages();
        ChatRecord targetChat = current; string targetAgent = selected;
        busy = true; sendButton.Enabled = false; hintLabel.Text = "Thinking locally with " + agent.Model + "…"; spinner = new PictureBox(); spinner.Size = new Size(30, 30); spinner.Image = logo; spinner.SizeMode = PictureBoxSizeMode.Zoom; spinner.Paint += PaintSpinner; messages.Controls.Add(spinner); spinTimer.Start(); ScrollMessages();
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
        if (spinTimer != null) spinTimer.Stop(); if (spinner != null) { messages.Controls.Remove(spinner); spinner.Dispose(); spinner = null; }
        MessageRecord reply = new MessageRecord(); reply.Role = "assistant"; reply.Agent = targetAgent; reply.Time = DateTime.UtcNow.Ticks; reply.Content = error == null ? (String.IsNullOrEmpty(content) ? "(The model returned an empty response.)" : content) : "Could not get a local response.\r\n\r\n" + error; target.Messages.Add(reply);
        if (result != null && result.ContainsKey("eval_count") && result.ContainsKey("eval_duration")) { double count = Convert.ToDouble(result["eval_count"]); double duration = Convert.ToDouble(result["eval_duration"]); if (duration > 0) speedLabel.Text = "Model speed: " + (count / (duration / 1000000000.0)).ToString("0.0") + " tokens/sec"; }
        busy = false; sendButton.Enabled = true; hintLabel.Text = "Runs on your computer · First model download needs internet"; SaveChats(); RefreshHistory(); if (current == target) RefreshMessages(); prompt.Focus();
    }

    private static void PullModel()
    {
        if (!connected) { SetHint("Start Ollama, then try downloading the model again."); return; }
        AgentInfo a = AgentByKey(selected); if (availableModels.Contains(a.Model)) { SetHint(a.Model + " is ready to use."); return; }
        getModelButton.Enabled = false; getModelButton.Text = "Starting…";
        ThreadPool.QueueUserWorkItem(delegate { try { PostJson("api/pull", new Dictionary<string, object> { { "agent", selected } }, 15000); BeginUpdate(delegate { getModelButton.Enabled = true; getModelButton.Text = "Downloading…"; SetHint("Downloading " + a.Model + ". You can keep using the app."); }); } catch (Exception ex) { BeginUpdate(delegate { getModelButton.Enabled = true; UpdateAgent(); SetHint(ex.Message); }); } });
    }

    private static void UpdateAgent()
    {
        if (agentPicker != null && agentPicker.SelectedIndex != AgentIndex(selected)) agentPicker.SelectedIndex = AgentIndex(selected);
        AgentInfo a = AgentByKey(selected); if (modelLabel != null) modelLabel.Text = a.Model + "  ·  " + a.Size;
        if (getModelButton != null) { getModelButton.Enabled = true; getModelButton.Text = availableModels.Contains(a.Model) ? "Model ready" : "Get model"; }
    }

    private static void RefreshStatus()
    {
        ThreadPool.QueueUserWorkItem(delegate {
            try {
                Dictionary<string, object> result = GetJson("api/models", 5000); bool isConnected = Convert.ToBoolean(result["connected"]); HashSet<string> models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                object raw; if (result.TryGetValue("models", out raw)) { IEnumerable arr = raw as IEnumerable; if (arr != null) foreach (object item in arr) models.Add(Convert.ToString(item)); }
                BeginUpdate(delegate { connected = isConnected; availableModels.Clear(); foreach (string model in models) availableModels.Add(model); ollamaLabel.Text = connected ? "● OLLAMA ON" : "● OLLAMA OFF"; ollamaLabel.ForeColor = connected ? Color.FromArgb(91, 150, 25) : Color.FromArgb(210, 100, 72); if (statusToggle != null) statusToggle.Text = StatusToggleText(); if (welcomeStatus != null) welcomeStatus.Text = connected ? "Ollama is connected. Choose an agent and install its model to begin." : "Start Ollama, choose an agent, and install its model to begin."; UpdateAgent(); });
            } catch { BeginUpdate(delegate { connected = false; ollamaLabel.Text = "● OLLAMA OFF"; ollamaLabel.ForeColor = Color.FromArgb(210, 100, 72); UpdateAgent(); }); }
        });
    }

    private static void CheckForUpdates()
    {
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
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out latest)) { BeginUpdate(delegate { SetHint("GitHub returned a release tag the updater could not read."); }); return; }
                    if (latest > currentVersion) BeginUpdate(delegate { ShowUpdateDialog(latest.ToString()); });
                    else BeginUpdate(delegate { SetHint("PattLucky Codex is up to date · v" + CurrentVersion); });
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                bool noRelease = response != null && response.StatusCode == HttpStatusCode.NotFound;
                if (response != null) response.Close();
                BeginUpdate(delegate { SetHint(noRelease ? "No public GitHub release is available yet." : "Could not check GitHub for updates. You can check again later."); });
            }
            catch { BeginUpdate(delegate { SetHint("Could not check GitHub for updates. You can check again later."); }); }
        });
    }

    private static void ShowUpdateDialog(string latestVersion)
    {
        Form dialog = new Form(); dialog.Text = "PattLucky Codex update"; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.MaximizeBox = false; dialog.MinimizeBox = false; dialog.ShowInTaskbar = false; dialog.StartPosition = FormStartPosition.CenterParent; dialog.ClientSize = new Size(460, 210); dialog.BackColor = panel; dialog.ForeColor = fg; dialog.Font = new Font("Segoe UI", 9.5f);
        Label heading = new Label(); heading.Text = "A new version is ready"; heading.Font = new Font("Segoe UI", 16, FontStyle.Bold); heading.SetBounds(24, 22, 410, 34); heading.ForeColor = fg; dialog.Controls.Add(heading);
        Label details = new Label(); details.Text = "You have v" + CurrentVersion + ". GitHub has v" + latestVersion + ". Open the release page to download the latest installer."; details.SetBounds(26, 66, 405, 55); details.ForeColor = muted; dialog.Controls.Add(details);
        ModernButton later = new ModernButton(); later.Text = "Later"; later.BackColor = panel; later.ForeColor = fg; later.Size = new Size(98, 38); later.Location = new Point(213, 145); later.TextAlign = ContentAlignment.MiddleCenter; later.Padding = new Padding(10, 0, 10, 0); later.DialogResult = DialogResult.Cancel; dialog.Controls.Add(later);
        ModernButton download = new ModernButton(); download.Primary = true; download.Text = "View update"; download.BackColor = Lime; download.ForeColor = Color.FromArgb(29, 40, 15); download.Font = new Font("Segoe UI", 9, FontStyle.Bold); download.Size = new Size(128, 38); download.Location = new Point(320, 145); download.TextAlign = ContentAlignment.MiddleCenter; download.Padding = new Padding(10, 0, 10, 0); download.Click += delegate { try { Process.Start(new ProcessStartInfo(GitHubReleasesPage) { UseShellExecute = true }); } catch { } dialog.Close(); }; dialog.Controls.Add(download);
        dialog.AcceptButton = download; dialog.CancelButton = later; dialog.ShowDialog(form); dialog.Dispose();
    }

    private static void UpdateMonitor()
    {
        if (runtimeLabel == null) return; long seconds = (Environment.TickCount - sessionStart) / 1000; runtimeLabel.Text = "Open " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00"); int total = 0; foreach (ChatRecord chat in chats) total += chat.Messages.Count; countsLabel.Text = chats.Count + " chats · " + total + " msgs"; if (speedLabel.Text.Length == 0) speedLabel.Text = "Model speed: waiting for a reply";
    }

    private static void SetHint(string text) { if (hintLabel != null) hintLabel.Text = text; }
    private static string StatusToggleText() { return (connected ? "● OLLAMA ON" : "● OLLAMA OFF") + "  ·  STATUS & TOOLS  " + (statusExpanded ? "⌄" : "›"); }
    private static void ScrollMessages() { if (messages != null && messages.Controls.Count > 0) messages.ScrollControlIntoView(messages.Controls[messages.Controls.Count - 1]); }
    private static void PaintSpinner(object sender, PaintEventArgs e) { if (logo == null) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.TranslateTransform(spinner.Width / 2f, spinner.Height / 2f); e.Graphics.RotateTransform(spinAngle); e.Graphics.DrawImage(logo, -12, -12, 24, 24); e.Graphics.ResetTransform(); }
    private static void BeginUpdate(MethodInvoker action) { try { if (form != null && !form.IsDisposed) form.BeginInvoke(action); } catch { } }

    private static Dictionary<string, object> GetJson(string path, int timeout) { HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl + path); req.Timeout = timeout; using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) using (StreamReader reader = new StreamReader(resp.GetResponseStream())) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); }
    private static Dictionary<string, object> PostJson(string path, object payload, int timeout) { byte[] data = Encoding.UTF8.GetBytes(Json.Serialize(payload)); HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AppUrl + path); req.Method = "POST"; req.ContentType = "application/json; charset=utf-8"; req.Timeout = timeout; req.ReadWriteTimeout = timeout; req.ContentLength = data.Length; using (Stream stream = req.GetRequestStream()) stream.Write(data, 0, data.Length); using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) using (StreamReader reader = new StreamReader(resp.GetResponseStream())) return Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd()); }
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
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(ChatPath)) return;
        try
        {
            List<ChatRecord> data = Json.Deserialize<List<ChatRecord>>(File.ReadAllText(ChatPath));
            if (data != null) chats.AddRange(data);
        }
        catch
        {
            chats.Clear();
            string backup = ChatPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(ChatPath, backup); startupStorageNotice = "Unreadable chat history was preserved as " + Path.GetFileName(backup) + "."; }
            catch { storageWriteBlocked = true; startupStorageNotice = "Chat history could not be read or backed up. PattLucky Codex will not overwrite that file."; }
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
    private static void ExportChats() { using (SaveFileDialog dialog = new SaveFileDialog()) { dialog.FileName = "pattlucky-codex-chats.json"; dialog.Filter = "JSON files (*.json)|*.json"; if (dialog.ShowDialog(form) == DialogResult.OK) File.WriteAllText(dialog.FileName, Json.Serialize(chats), Encoding.UTF8); } }

    private sealed class AgentInfo { public string Key, Name, Model, Size, Description; public AgentInfo(string key, string name, string model, string size, string description) { Key = key; Name = name; Model = model; Size = size; Description = description; } }
    private sealed class BufferedForm : Form { public BufferedForm() { DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class BufferedPanel : Panel { public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel { public BufferedFlowLayoutPanel() { DoubleBuffered = true; ResizeRedraw = true; } }
    private sealed class ModernButton : Button
    {
        private bool hovered, pressed;
        public bool Primary { get; set; }
        public ModernButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = BackColor;
            if (!Enabled) fill = Primary ? Color.FromArgb(101, 125, 68) : Color.FromArgb(125, 130, 120);
            else if (pressed) fill = Primary ? Color.FromArgb(112, 176, 15) : Blend(BackColor, Lime, 0.20f);
            else if (hovered) fill = Primary ? Color.FromArgb(145, 216, 37) : Blend(BackColor, Lime, 0.11f);
            Rectangle rect = new Rectangle(1, 1, Math.Max(0, Width - 3), Math.Max(0, Height - 3));
            using (GraphicsPath path = RoundedPath(rect, 9))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(Primary ? fill : (hovered ? Lime : line), Focused ? 1.5f : 1f))
            {
                e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path);
            }
            Color textColor = Enabled ? ForeColor : Color.FromArgb(190, 190, 190);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            if (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.TopLeft || TextAlign == ContentAlignment.BottomLeft) flags |= TextFormatFlags.Left;
            else if (TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.HorizontalCenter;
            Rectangle textRect = Rectangle.Inflate(ClientRectangle, -Padding.Left, -Padding.Top);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, textColor, flags);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -5, -5), textColor, fill);
        }
        private static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        private static Color Blend(Color original, Color accent, float amount) { return Color.FromArgb((int)(original.R * (1 - amount) + accent.R * amount), (int)(original.G * (1 - amount) + accent.G * amount), (int)(original.B * (1 - amount) + accent.B * amount)); }
    }
    public sealed class ChatRecord { public string Id { get; set; } public string Title { get; set; } public string Agent { get; set; } public List<MessageRecord> Messages { get; set; } public ChatRecord() { Messages = new List<MessageRecord>(); } }
    public sealed class MessageRecord { public string Role { get; set; } public string Content { get; set; } public string Agent { get; set; } public long Time { get; set; } }
}
