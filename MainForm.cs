using System.Drawing.Drawing2D;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PhoneCastShell;

sealed class MainForm : Form
{
    const int HotkeyId = 0xB055;
    const int WM_HOTKEY = 0x0312;

    readonly Settings settings;
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0xFA, 0xF9, 0xF5) };
    readonly MirrorGlue glue;
    readonly System.Windows.Forms.Timer ticker = new() { Interval = 100 };
    readonly System.Windows.Forms.Timer pickTimer = new() { Interval = 3000 };
    MirrorState state = new(false, 0, 0, "");
    bool pageReady;

    public MainForm(Settings settings)
    {
        this.settings = settings;
        Text = settings.WindowTitle;
        Icon = MakeIcon(settings.Logo);
        BackColor = Color.FromArgb(0xFA, 0xF9, 0xF5);
        StartPosition = FormStartPosition.Manual;
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 1000);
        var size = new Size(Math.Min(1320, wa.Width - 80), Math.Min(980, wa.Height - 40));
        Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Top + (wa.Height - size.Height) / 2,
            size.Width, size.Height);
        MinimumSize = new Size(760, 560);
        Controls.Add(web);

        glue = new MirrorGlue(this, settings, () => web.PointToScreen(Point.Empty));
        glue.Changed += st => { state = st; Post(StateMessage()); };

        LocationChanged += (_, _) => glue.Tick();
        SizeChanged += (_, _) => glue.Tick();
        ticker.Tick += (_, _) => glue.Tick();
        pickTimer.Tick += (_, _) =>
        {
            pickTimer.Stop();
            bool ok = glue.PickUnderCursor();
            Post(new { type = "picked", ok });
            Post(SettingsMessage());
        };
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RegisterBossKey();

        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Settings.Dir, "WebView2"));
        await web.EnsureCoreWebView2Async(env);
        var cw = web.CoreWebView2;
        cw.Settings.IsZoomControlEnabled = false;   // 縮放會讓 CSS 像素跟實體像素對不上
        cw.Settings.IsStatusBarEnabled = false;
        cw.Settings.AreDefaultContextMenusEnabled = false;
#if !DEBUG
        cw.Settings.AreDevToolsEnabled = false;
#endif
        cw.SetVirtualHostNameToFolderMapping("shell.local",
            Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Allow);
        cw.WebMessageReceived += OnWebMessage;
        var cfg = JsonSerializer.Serialize(new
        {
            name = settings.AssistantName, model = settings.ModelName, user = settings.UserName, logo = settings.Logo,
        });
        await cw.AddScriptToExecuteOnDocumentCreatedAsync($"window.SHELL_CFG = {cfg};");
        cw.Navigate("https://shell.local/index.html");
        ticker.Start();
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var m = doc.RootElement;
        float F(string k) => m.TryGetProperty(k, out var v) ? v.GetSingle() : 0;

        switch (m.GetProperty("type").GetString())
        {
            case "ready":
                pageReady = true;
                Post(StateMessage());
                Post(SettingsMessage());
                Post(new { type = "boss", on = glue.Boss });
                break;
            case "slot":
                glue.SetSlot(new SlotInfo(F("x"), F("y"), F("w"), F("h"), F("cx"), F("cy"), F("cw"), F("ch"),
                    Math.Max(0.5f, F("dpr"))));
                break;
            case "boss":
                ToggleBoss();
                break;
            case "inset":
                var side = m.GetProperty("side").GetString();
                int d = (int)F("delta");
                switch (side)
                {
                    case "left": settings.InsetLeft = Math.Max(0, settings.InsetLeft + d); break;
                    case "top": settings.InsetTop = Math.Max(0, settings.InsetTop + d); break;
                    case "right": settings.InsetRight = Math.Max(0, settings.InsetRight + d); break;
                    case "bottom": settings.InsetBottom = Math.Max(0, settings.InsetBottom + d); break;
                }
                settings.Save();
                glue.Tick();
                Post(SettingsMessage());
                break;
            case "zoom":
                glue.Zoom(F("factor"));
                break;
            case "rescan":
                glue.Rescan();
                Post(SettingsMessage());
                break;
            case "pick":
                pickTimer.Stop();
                pickTimer.Start();
                break;
        }
    }

    void ToggleBoss()
    {
        glue.Boss = !glue.Boss;
        Post(new { type = "boss", on = glue.Boss });
        if (glue.Boss)
        {
            // 焦點如果在手機上，鍵盤還會打到手機裡；拉回殼
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            web.Focus();
        }
    }

    object StateMessage() => new { type = "mirror", found = state.Found, w = state.W, h = state.H, title = state.Title };

    object SettingsMessage() => new
    {
        type = "settings",
        insets = new { left = settings.InsetLeft, top = settings.InsetTop, right = settings.InsetRight, bottom = settings.InsetBottom },
        process = settings.TargetProcess,
        hotkey = settings.BossHotkey,
        found = state.Found,
        title = state.Title,
    };

    void Post(object msg)
    {
        if (!pageReady || IsDisposed || InvokeRequired) return; // 結束時可能從別的執行緒呼叫，網頁已經不用通知了
        try { web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(msg)); }
        catch (InvalidOperationException) { }
    }

    void RegisterBossKey()
    {
        uint mods = 0x4000; // MOD_NOREPEAT
        Keys key = Keys.None;
        foreach (var part in settings.BossHotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods |= 0x1; break;
                case "ctrl": case "control": mods |= 0x2; break;
                case "shift": mods |= 0x4; break;
                case "win": mods |= 0x8; break;
                default:
                    if (part == "`") key = Keys.Oemtilde;
                    else if (Enum.TryParse<Keys>(part, true, out var k)) key = k;
                    else if (part.Length == 1 && char.IsDigit(part[0])) key = Keys.D0 + (part[0] - '0');
                    break;
            }
        }
        if (key != Keys.None && !Native.RegisterHotKey(Handle, HotkeyId, mods, (uint)key))
            settings.BossHotkey += "（被別的程式佔用了）";
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId) ToggleBoss();
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        ticker.Stop();
        pickTimer.Stop();
        Native.UnregisterHotKey(Handle, HotkeyId);
        glue.Release(); // 一定要在殼的視窗銷毀前把投屏視窗還回去
        base.OnFormClosing(e);
    }

    public void ReleaseMirror() => glue.Release();

    static Icon MakeIcon(string logo)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var color = Color.FromArgb(0xD9, 0x77, 0x57);
            using var pen = new Pen(color, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (logo != "spark")
            {
                g.DrawEllipse(pen, 10, 10, 44, 44);
                using var fill = new SolidBrush(color);
                g.FillEllipse(fill, 24, 24, 16, 16);
                return Icon.FromHandle(bmp.GetHicon());
            }
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI * i / 4 + 0.2;
                float len = i % 2 == 0 ? 27 : 21;
                g.DrawLine(pen, 32, 32, 32 + (float)Math.Cos(a) * len, 32 + (float)Math.Sin(a) * len);
            }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
