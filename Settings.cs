using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhoneCastShell;

sealed class Settings
{
    const int CurrentVersion = 3;

    /// <summary>設定檔格式版本：v2 只有一組裁切量（給 O+），v3 起每個投屏程式各存一組。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>名單（MirrorApps）以外、也要自動找的程式名稱（不含 .exe，逗號分隔）。手動指定時會自動加進來。</summary>
    public string TargetProcess { get; set; } = "";

    /// <summary>每個投屏程式的裁切量，鍵＝程式名稱小寫。第一次接上時自動判斷，之後照這裡。</summary>
    public Dictionary<string, Insets> Profiles { get; set; } = new();

    // v2 的單組裁切量，只在讀舊檔時用來搬家
    public int? InsetLeft { get; set; }
    public int? InsetTop { get; set; }
    public int? InsetRight { get; set; }
    public int? InsetBottom { get; set; }

    public string BossHotkey { get; set; } = "Alt+Q";

    /// <summary>外觀上的名稱與圖示：AssistantName 出現在側欄、輸入框、底部提示；Logo＝"ring"（圓環）或 "spark"（放射星）。</summary>
    public string WindowTitle { get; set; } = "AI 助手";
    public string AssistantName { get; set; } = "AI 助手";
    public string ModelName { get; set; } = "標準模型";
    public string UserName { get; set; } = "使用者";
    public string Logo { get; set; } = "ring";
    public float CornerRadius { get; set; } = 14;

    /// <summary>開始功能表捷徑的名字；空白＝還沒加到開始功能表。</summary>
    public string ShortcutName { get; set; } = "";

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneCastShell");
    static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文、+ 號照原樣寫，手改比較好讀
    };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<Settings>(text, Json) ?? new Settings();
                if (!text.Contains("\"Version\"")) s.Version = 1;
                if (s.Version < CurrentVersion) s.Migrate();
                return s;
            }
        }
        catch { /* 檔案壞了就用預設值，下次存檔會蓋掉 */ }
        return new Settings();
    }

    void Migrate()
    {
        // v1 的裁切量是 0 而且找錯程式；v2 的是給 O+ 投屏視窗量好的，搬進 phonecast 那一組
        if (Version == 2 && InsetTop is { } t)
            Profiles["phonecast"] = new Insets(InsetLeft ?? 0, t, InsetRight ?? 0, InsetBottom ?? 0);
        InsetLeft = InsetTop = InsetRight = InsetBottom = null;
        // v1、v2 預設寫進去的程式名稱現在由內建名單負責
        if (TargetProcess is "phoneCast,O+Connect" or "O+Connect" or "phoneCast") TargetProcess = "";
        Version = CurrentVersion;
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}
