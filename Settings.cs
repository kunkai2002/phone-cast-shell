using System.Text.Json;

namespace PhoneCastShell;

sealed class Settings
{
    const int CurrentVersion = 2;

    /// <summary>設定檔格式版本；舊版（v1 用 O+Connect、裁切全 0）讀進來時換成 v2 的預設值。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>投屏視窗屬於哪個程式（不含 .exe，逗號分隔）。O+互聯的投屏畫面是另一個 Qt 程式 phoneCast.exe。手動指定別的視窗時會改成那個程式。</summary>
    public string TargetProcess { get; set; } = "phoneCast,O+Connect";

    /// <summary>從投屏視窗外框往內切掉多少（DIP，跟著縮放比例換算）。預設值是 phoneCast 實測：上標題列 44、下導覽列 44、左右白邊各 4。</summary>
    public int InsetLeft { get; set; } = 4;
    public int InsetTop { get; set; } = 44;
    public int InsetRight { get; set; } = 4;
    public int InsetBottom { get; set; } = 44;

    public string BossHotkey { get; set; } = "Alt+Q";

    /// <summary>外觀上的名稱與圖示：AssistantName 出現在側欄、輸入框、底部提示；Logo＝"ring"（圓環）或 "spark"（放射星）。</summary>
    public string WindowTitle { get; set; } = "AI 助手";
    public string AssistantName { get; set; } = "AI 助手";
    public string ModelName { get; set; } = "標準模型";
    public string UserName { get; set; } = "使用者";
    public string Logo { get; set; } = "ring";
    public float CornerRadius { get; set; } = 14;

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhoneCastShell");
    static string FilePath => Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
                if (!File.ReadAllText(FilePath).Contains("\"Version\"") || s.Version < CurrentVersion)
                {
                    var d = new Settings();
                    s.TargetProcess = d.TargetProcess;
                    (s.InsetLeft, s.InsetTop, s.InsetRight, s.InsetBottom) = (d.InsetLeft, d.InsetTop, d.InsetRight, d.InsetBottom);
                    s.Version = CurrentVersion;
                    s.Save();
                }
                return s;
            }
        }
        catch { /* 檔案壞了就用預設值，下次存檔會蓋掉 */ }
        return new Settings();
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
