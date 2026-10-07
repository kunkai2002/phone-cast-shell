using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using static PhoneCastShell.Native;

namespace PhoneCastShell;

/// <summary>
/// 第一次接上一個投屏視窗時，自動判斷要切掉多少（實體像素，相對客戶區）。
/// 1. 找子視窗：很多投屏軟體把影像放在一個獨立的子視窗裡（O+ 的 phoneCast 就是），找最裡面那個手機形狀的；
/// 2. 找不到就截一張圖，從四邊往內，把跟最外圈同色的整條（標題列、工具列）剝掉。
/// 兩個都判斷不出來就回 null，交給使用者手動調。
/// </summary>
static class InsetDetector
{
    public static Padding? Detect(IntPtr hwnd, Rectangle client) => FromChildren(hwnd, client) ?? FromPixels(hwnd, client.Size);

    static Padding? FromChildren(IntPtr hwnd, Rectangle client)
    {
        long clientArea = (long)client.Width * client.Height;
        Rectangle best = Rectangle.Empty;
        EnumChildWindows(hwnd, (c, _) =>
        {
            if (!IsWindowVisible(c) || !GetWindowRect(c, out var r)) return true;
            var rect = Rectangle.Intersect(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), client);
            long area = (long)rect.Width * rect.Height;
            if (area < clientArea * 0.4 || area > clientArea * 0.98 || !PhoneShaped(rect.Size)) return true;
            if (best.IsEmpty || area < (long)best.Width * best.Height) best = rect; // 最裡面那層
            return true;
        }, IntPtr.Zero);
        if (best.IsEmpty) return null;
        return new Padding(best.Left - client.Left, best.Top - client.Top, client.Right - best.Right, client.Bottom - best.Bottom);
    }

    static Padding? FromPixels(IntPtr hwnd, Size size)
    {
        if (size.Width < 100 || size.Height < 100) return null;
        using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            bool ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            g.ReleaseHdc(hdc);
            if (!ok) return null;
        }

        var data = bmp.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[size.Width * size.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        int W = size.Width, H = size.Height;
        if (px.All(p => (p & 0xFFFFFF) == 0)) return null; // 截到全黑＝這個視窗不讓 PrintWindow 截

        int top = Peel(H, y => Row(px, W, y), W);
        int bottom = Peel(H, y => Row(px, W, H - 1 - y), W);
        int left = Peel(W, x => Col(px, W, H, x), H);
        int right = Peel(W, x => Col(px, W, H, W - 1 - x), H);

        var content = new Size(W - left - right, H - top - bottom);
        if (content.Width < W * 0.4 || content.Height < H * 0.4) return null;
        if ((left | top | right | bottom) != 0 && !PhoneShaped(content)) return null;
        return new Padding(left, top, right, bottom);
    }

    /// <summary>從邊緣往內數，有幾條是「跟邊條同色」的（最多剝 25%）。</summary>
    static int Peel(int count, Func<int, IEnumerable<int>> line, int length)
    {
        // 最外 1～2 條常是視窗外框的亮線、陰影，顏色從第 3 條取，那兩條也不要求同色
        int edge = Dominant(line(Math.Min(2, count - 1)));
        int peeled = 0;
        for (int n = 0; n < count / 4; n++)
        {
            // 標題列、導覽列上有字和按鈕，佔到一條的四成也還算是那條列
            if (line(n).Count(p => Close(p, edge)) >= length * 0.6) peeled = n + 1;
            else if (n >= 2) break;
        }
        // 只剝了一兩條多半是外框線，不是標題列
        return peeled <= 2 ? 0 : peeled;
    }

    static IEnumerable<int> Row(int[] px, int w, int y) { for (int x = 0; x < w; x++) yield return px[y * w + x]; }
    static IEnumerable<int> Col(int[] px, int w, int h, int x) { for (int y = 0; y < h; y++) yield return px[y * w + x]; }

    static int Dominant(IEnumerable<int> line) =>
        line.GroupBy(p => p & 0xF0F0F0).OrderByDescending(g => g.Count()).First().First();

    static bool Close(int a, int b) =>
        Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) + Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF))
        + Math.Abs((a & 0xFF) - (b & 0xFF)) <= 30;

    /// <summary>直的或橫的手機：長邊是短邊的 1.3 倍以上。</summary>
    static bool PhoneShaped(Size s)
    {
        int lo = Math.Min(s.Width, s.Height), hi = Math.Max(s.Width, s.Height);
        return lo > 0 && hi >= lo * 1.3;
    }
}
