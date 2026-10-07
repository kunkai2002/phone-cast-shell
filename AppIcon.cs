using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PhoneCastShell;

/// <summary>程式圖示：ring（圓環，公開版預設）或 spark（放射星）。視窗圖示、開始功能表捷徑圖示都從這裡畫。</summary>
static class AppIcon
{
    static readonly Color Accent = Color.FromArgb(0xD9, 0x77, 0x57);

    public static Bitmap Render(string logo, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float k = size / 64f;
        using var pen = new Pen(Accent, 7 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (logo == "spark")
        {
            for (int i = 0; i < 8; i++)
            {
                double a = Math.PI * i / 4 + 0.2;
                float len = (i % 2 == 0 ? 27 : 21) * k;
                g.DrawLine(pen, 32 * k, 32 * k, 32 * k + (float)Math.Cos(a) * len, 32 * k + (float)Math.Sin(a) * len);
            }
        }
        else
        {
            g.DrawEllipse(pen, 10 * k, 10 * k, 44 * k, 44 * k);
            using var fill = new SolidBrush(Accent);
            g.FillEllipse(fill, 24 * k, 24 * k, 16 * k, 16 * k);
        }
        return bmp;
    }

    public static Icon ForWindow(string logo)
    {
        using var bmp = Render(logo, 64);
        return Icon.FromHandle(bmp.GetHicon());
    }

    /// <summary>
    /// 寫一個多尺寸的 .ico。Icon.Save 會把透明度弄丟，所以自己寫：
    /// 256 存 PNG（檔案小），其他存傳統 32 位元 BMP——有些讀圖示的地方不吃小尺寸的 PNG。
    /// </summary>
    public static void WriteIco(string path, string logo)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var frames = sizes.Select(sz =>
        {
            using var bmp = Render(logo, sz);
            return sz >= 256 ? Png(bmp) : Dib(bmp);
        }).ToArray();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // 256 寫成 0
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(frames[i].Length); w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var p in frames) w.Write(p);
    }

    static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>圖示裡的 BMP：BITMAPINFOHEADER（高度寫兩倍）＋由下往上的 BGRA＋全 0 的 AND 遮罩。</summary>
    static byte[] Dib(Bitmap bmp)
    {
        int n = bmp.Width;
        int maskStride = ((n + 31) / 32) * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(n * n * 4 + maskStride * n); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            for (int x = 0; x < n; x++)
            {
                var c = bmp.GetPixel(x, y);
                w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
            }
        w.Write(new byte[maskStride * n]);
        return ms.ToArray();
    }
}
