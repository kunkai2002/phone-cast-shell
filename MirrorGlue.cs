using System.Diagnostics;
using static PhoneCastShell.Native;

namespace PhoneCastShell;

/// <summary>網頁回報的「那張圖」位置：CSS 像素、相對 WebView 左上角；C 開頭是對話捲動區（看得見的範圍）。</summary>
record SlotInfo(float X, float Y, float W, float H, float Cx, float Cy, float Cw, float Ch, float Dpr);

record MirrorState(bool Found, int W, int H, string Title);

/// <summary>
/// 把別的程式的投屏視窗收進殼裡：
/// 1. 殼上放一個容器（host），位置＝「那張圖」在對話裡看得見的那一塊；
/// 2. 投屏視窗改成容器的子視窗（SetParent），往左上錯開，讓標題列、工具列落在容器外面被切掉；
/// 3. 捲動時容器跟著縮放移動，捲出範圍就藏起來；老闆鍵也是藏容器。
/// 不用 SetWindowRgn：O+ 的投屏視窗（phoneCast.exe，Qt）直接把畫面交給合成器，裁切區域對它無效，
/// 但子視窗被父視窗邊界切掉這件事它躲不掉。
/// 點擊不轉發：滑鼠本來就落在投屏視窗上。
/// </summary>
sealed class MirrorGlue
{
    static readonly string[] MainWindowTitles = { "O+ 互聯", "O+ 互联", "O+Connect", "O+ Connect" };

    readonly Form shell;
    readonly Settings s;
    readonly Func<Point> viewportOrigin;
    readonly Panel host = new() { Visible = false, BackColor = Color.FromArgb(0xE9, 0xE6, 0xDC) };

    public IntPtr Hwnd { get; private set; }
    IntPtr origOwner, origStyle;
    RECT origRect;

    SlotInfo? slot;
    bool boss;
    Size lastContent = Size.Empty;
    string lastRgn = "";
    DateTime nextScan = DateTime.MinValue;

    public event Action<MirrorState>? Changed;

    public MirrorGlue(Form shell, Settings s, Func<Point> viewportOrigin)
    {
        this.shell = shell;
        this.s = s;
        this.viewportOrigin = viewportOrigin;
        shell.Controls.Add(host);
        host.BringToFront();
    }

    public bool Boss
    {
        get => boss;
        set { boss = value; Tick(); }
    }

    public void SetSlot(SlotInfo info)
    {
        slot = info;
        Tick();
    }

    public void Rescan()
    {
        Release();
        nextScan = DateTime.MinValue;
        Tick();
    }

    public void Tick()
    {
        if (shell.WindowState == FormWindowState.Minimized) return;

        // 投屏結束時 phoneCast 會把視窗毀掉或藏起來；容器藏起來時子視窗的 IsWindowVisible 也是 false，所以看它自己的 WS_VISIBLE
        if (Hwnd != IntPtr.Zero && !(IsWindow(Hwnd) && (GetWindowLongPtr(Hwnd, GWL_STYLE).ToInt64() & WS_VISIBLE) != 0))
            Release();

        if (Hwnd == IntPtr.Zero)
        {
            if (DateTime.Now < nextScan) return;
            nextScan = DateTime.Now.AddSeconds(1);
            var found = Find();
            if (found == IntPtr.Zero) return;
            Lock(found);
        }

        if (IsIconic(Hwnd)) ShowWindow(Hwnd, SW_RESTORE);

        var g = Measure();
        if (g == null) return;
        if (g.Value.Content != lastContent)
        {
            lastContent = g.Value.Content;
            Changed?.Invoke(new MirrorState(true, lastContent.Width, lastContent.Height, GetTitle(Hwnd)));
        }
        Layout(g.Value);
    }

    /// <summary>投屏視窗等比放大縮小（只改手機畫面那一塊的大小，標題列、工具列不變）。</summary>
    public void Zoom(float factor)
    {
        if (Hwnd == IntPtr.Zero || Measure() is not { } g) return;
        int dw = (int)Math.Round(g.Content.Width * factor) - g.Content.Width;
        int dh = (int)Math.Round(g.Content.Height * factor) - g.Content.Height;
        SetWindowPos(Hwnd, HWND_TOP, 0, 0, g.Win.Width + dw, g.Win.Height + dh,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        Tick();
    }

    /// <summary>手動指定：拿滑鼠底下那個視窗（不限 O+，scrcpy 之類的也行）。</summary>
    public bool PickUnderCursor()
    {
        if (!GetCursorPos(out var p)) return false;
        var root = GetAncestor(WindowFromPoint(p), GA_ROOT);
        if (root == IntPtr.Zero) return false;
        GetWindowThreadProcessId(root, out var pid);
        if (pid == Environment.ProcessId) return Hwnd != IntPtr.Zero; // 指到的是已經收進來的那個

        string? procName = null;
        try { using var proc = Process.GetProcessById((int)pid); procName = proc.ProcessName; } catch { }
        if (procName == null) return false;

        Release();
        s.TargetProcess = procName;
        s.Save();
        Lock(root);
        Tick();
        return true;
    }

    public void Release()
    {
        host.Visible = false;
        if (Hwnd == IntPtr.Zero) return;
        var h = Hwnd;
        Hwnd = IntPtr.Zero;
        lastContent = Size.Empty;
        if (IsWindow(h))
        {
            SetParent(h, IntPtr.Zero);
            SetWindowLongPtr(h, GWL_STYLE, origStyle);
            if (origOwner != IntPtr.Zero) SetWindowLongPtr(h, GWLP_HWNDPARENT, origOwner);
            SetWindowPos(h, HWND_TOP, origRect.Left, origRect.Top, origRect.Width, origRect.Height,
                SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        Changed?.Invoke(new MirrorState(false, 0, 0, ""));
    }

    IntPtr Find()
    {
        var pids = new HashSet<uint>();
        foreach (var name in s.TargetProcess.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            foreach (var p in Process.GetProcessesByName(name))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
        if (pids.Count == 0) return IntPtr.Zero;

        IntPtr best = IntPtr.Zero;
        long bestScore = 0;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true; // 縮到最小的也算 visible，大小用 NormalRect 量
            GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            if (MainWindowTitles.Contains(GetTitle(h))) return true;
            var r = NormalRect(h);
            if (r.Width < 150 || r.Height < 150) return true;

            // 直式優先（手機投屏剛打開幾乎都是直的），同樣是直式就挑大的
            long score = (long)r.Width * r.Height + (r.Height > r.Width * 1.15 ? 1L << 40 : 0);
            if (score > bestScore) { bestScore = score; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    void Lock(IntPtr h)
    {
        if (IsIconic(h))
        {
            ShowWindow(h, SW_RESTORE); // 子視窗不能是最小化狀態
            Thread.Sleep(150);
        }
        origOwner = GetWindowLongPtr(h, GWLP_HWNDPARENT);
        origStyle = GetWindowLongPtr(h, GWL_STYLE);
        GetWindowRect(h, out origRect);

        long st = origStyle.ToInt64();
        st &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_MINIMIZE | WS_MAXIMIZE);
        st |= WS_CHILD;
        SetParent(h, host.Handle);
        SetWindowLongPtr(h, GWL_STYLE, new IntPtr(st));
        SetWindowPos(h, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        Hwnd = h;
        lastContent = Size.Empty;
    }

    /// <param name="Win">投屏視窗大小</param>
    /// <param name="Off">手機畫面相對投屏視窗左上角的位移（＝左、上裁掉多少）</param>
    /// <param name="Content">手機畫面大小</param>
    readonly record struct Geo(Size Win, Point Off, Size Content);

    Geo? Measure()
    {
        if (!GetWindowRect(Hwnd, out var win)) return null;
        float k = GetDpiForWindow(Hwnd) / 96f;
        if (k <= 0) k = 1;
        int l = (int)Math.Round(s.InsetLeft * k), t = (int)Math.Round(s.InsetTop * k);
        int w = win.Width - l - (int)Math.Round(s.InsetRight * k);
        int h = win.Height - t - (int)Math.Round(s.InsetBottom * k);
        if (w < 40 || h < 40) return null; // 切過頭了
        return new Geo(new Size(win.Width, win.Height), new Point(l, t), new Size(w, h));
    }

    void Layout(Geo g)
    {
        if (slot == null || boss) { host.Visible = false; return; }

        var o = viewportOrigin();
        var slotScreen = new Rectangle(o.X + (int)Math.Round(slot.X * slot.Dpr), o.Y + (int)Math.Round(slot.Y * slot.Dpr),
            g.Content.Width, g.Content.Height);
        var clip = Rectangle.FromLTRB(
            o.X + (int)Math.Round(slot.Cx * slot.Dpr), o.Y + (int)Math.Round(slot.Cy * slot.Dpr),
            o.X + (int)Math.Round((slot.Cx + slot.Cw) * slot.Dpr), o.Y + (int)Math.Round((slot.Cy + slot.Ch) * slot.Dpr));
        var vis = Rectangle.Intersect(slotScreen, clip);
        if (vis.Width <= 0 || vis.Height <= 0) { host.Visible = false; return; }

        var bounds = shell.RectangleToClient(vis);
        if (host.Bounds != bounds) host.Bounds = bounds;
        ApplyCorners(slotScreen, vis);
        if (!host.Visible)
        {
            host.Visible = true;
            host.BringToFront();
        }

        // 投屏視窗在容器裡的位置：手機畫面左上角對齊「那張圖」左上角
        var want = new Point(slotScreen.X - vis.X - g.Off.X, slotScreen.Y - vis.Y - g.Off.Y);
        var hostScreen = host.PointToScreen(Point.Empty);
        GetWindowRect(Hwnd, out var win);
        if (win.Left - hostScreen.X != want.X || win.Top - hostScreen.Y != want.Y)
            SetWindowPos(Hwnd, HWND_TOP, want.X, want.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>容器四角切圓：以「整張圖」畫圓角再換算到容器座標，被捲動切到的那一邊不會出現假圓角。</summary>
    void ApplyCorners(Rectangle slotScreen, Rectangle vis)
    {
        int r = slot == null ? 0 : (int)Math.Round(s.CornerRadius * slot.Dpr);
        var full = new Rectangle(slotScreen.X - vis.X, slotScreen.Y - vis.Y, slotScreen.Width, slotScreen.Height);
        string sig = $"{full}|{vis.Size}|{r}";
        if (sig == lastRgn) return;
        lastRgn = sig;
        var rgn = CreateRoundRectRgn(full.Left, full.Top, full.Right + 1, full.Bottom + 1, r * 2, r * 2);
        if (SetWindowRgn(host.Handle, rgn, true) == 0) DeleteObject(rgn);
    }

    static RECT NormalRect(IntPtr h)
    {
        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (IsIconic(h) && GetWindowPlacement(h, ref wp)) return wp.rcNormalPosition;
        GetWindowRect(h, out var r);
        return r;
    }
}
