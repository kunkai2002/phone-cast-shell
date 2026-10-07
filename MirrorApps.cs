namespace PhoneCastShell;

/// <summary>裁切量（DIP），相對投屏視窗的客戶區。</summary>
sealed class Insets
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Right { get; set; }
    public int Bottom { get; set; }

    public Insets() { }
    public Insets(int l, int t, int r, int b) => (Left, Top, Right, Bottom) = (l, t, r, b);
    public Insets Clone() => new(Left, Top, Right, Bottom);
}

/// <param name="Patterns">程式名稱（不含 .exe）包含其中任一字串就算，不分大小寫</param>
/// <param name="Insets">量過的裁切量；null＝第一次接上時自動判斷</param>
/// <param name="PortraitOnly">只認直式視窗：同一個程式還有主視窗、設定視窗時，用這條避免抓錯</param>
sealed record MirrorApp(string Name, string[] Patterns, Insets? Insets, bool PortraitOnly);

static class MirrorApps
{
    // 順序有意義：先比對到的先算（escrcpy、QtScrcpy 的名字裡都有 scrcpy，要排在 scrcpy 前面）
    static readonly MirrorApp[] Known =
    {
        new("O+互聯", new[] { "phoneCast" }, new Insets(4, 44, 4, 44), false),
        new("escrcpy", new[] { "escrcpy" }, null, true),
        new("QtScrcpy", new[] { "QtScrcpy" }, null, true),
        new("scrcpy", new[] { "scrcpy" }, new Insets(0, 0, 0, 0), false),
        new("Windows 手機連結", new[] { "PhoneExperienceHost", "YourPhone" }, null, true),
        new("華為／榮耀多屏協同", new[] { "PCManager", "HwMirror", "MultiScreen", "Huawei", "Honor", "MagicRing" }, null, true),
        new("小米互聯", new[] { "Xiaomi", "MiShare", "MiInterconnect", "MiPCManager", "HyperOS" }, null, true),
        new("vivo 辦公套件", new[] { "vivo" }, null, true),
        new("三星", new[] { "Samsung" }, null, true),
        new("AirDroid Cast", new[] { "AirDroid" }, null, true),
        new("ApowerMirror", new[] { "Apower" }, null, true),
        new("LetsView", new[] { "LetsView" }, null, true),
        new("Vysor", new[] { "Vysor" }, null, true),
        new("樂播投屏", new[] { "Lebo", "乐播" }, null, true),
        new("O+互聯主程式", new[] { "O+Connect" }, null, true),
    };

    // 標題像投屏視窗、但程式不在名單裡的，也收（只認直式）
    static readonly string[] TitleHints =
    {
        "手機畫面", "手机画面", "螢幕鏡像", "屏幕镜像", "手機鏡像", "手机镜像", "多屏協同", "多屏协同", "妙享", "Phone screen", "Screen mirror",
    };

    // 瀏覽器分頁標題可能剛好有「投屏」之類的字，不收
    static readonly string[] Excluded = { "chrome", "msedge", "firefox", "opera", "brave", "explorer", "PhoneCastShell" };

    public static MirrorApp? Match(string process, string title, string extraProcesses)
    {
        if (Excluded.Any(e => process.Equals(e, StringComparison.OrdinalIgnoreCase))) return null;

        foreach (var name in extraProcesses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (process.Equals(name, StringComparison.OrdinalIgnoreCase))
                return Known.FirstOrDefault(a => Hit(a, process)) ?? new MirrorApp(process, new[] { process }, null, false);

        var known = Known.FirstOrDefault(a => Hit(a, process));
        if (known != null) return known;

        if (TitleHints.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            return new MirrorApp(process, new[] { process }, null, true);
        return null;
    }

    static bool Hit(MirrorApp a, string process) =>
        a.Patterns.Any(p => process.Contains(p, StringComparison.OrdinalIgnoreCase));

    public static string SupportedList => string.Join("、", Known.Where(a => a.Name != "O+互聯主程式").Select(a => a.Name));
}
