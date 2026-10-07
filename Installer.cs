namespace PhoneCastShell;

/// <summary>
/// 「加到開始功能表」：把程式複製到固定的安裝資料夾，再在開始功能表放一個捷徑。
/// 不寫登錄檔、不需要系統管理員；移除＝刪捷徑和安裝資料夾。
/// </summary>
static class Installer
{
    public const string DefaultName = "手機投屏對話殼";
    const string ExeName = "PhoneCastShell.exe";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PhoneCastShell");

    static string ProgramsDir => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    public static bool RunningFromInstallDir =>
        Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\')
            .Equals(Path.GetFullPath(InstallDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static string ShortcutPath(string name) => Path.Combine(ProgramsDir, Clean(name) + ".lnk");

    public static bool IsInstalled(Settings s) => s.ShortcutName.Length > 0 && File.Exists(ShortcutPath(s.ShortcutName));

    public static void Install(Settings s, string name)
    {
        name = Clean(name);
        if (name.Length == 0) name = DefaultName;

        // 從別的地方（下載解壓縮的資料夾、開發資料夾）執行時，先把整個程式複製到安裝資料夾；已經在安裝資料夾裡就不用
        if (!RunningFromInstallDir) CopyDir(AppContext.BaseDirectory, InstallDir);

        var icon = Path.Combine(InstallDir, "app.ico");
        AppIcon.WriteIco(icon, s.Logo);

        if (s.ShortcutName.Length > 0 && s.ShortcutName != name) TryDelete(ShortcutPath(s.ShortcutName)); // 改名：拿掉舊的
        CreateShortcut(ShortcutPath(name), Path.Combine(InstallDir, ExeName), icon);
        s.ShortcutName = name;
        s.Save();
    }

    public static void Uninstall(Settings s)
    {
        if (s.ShortcutName.Length > 0) TryDelete(ShortcutPath(s.ShortcutName));
        s.ShortcutName = "";
        s.Save();
        // 正在從安裝資料夾執行時刪不掉自己，留著；設定檔、瀏覽器資料在 %LOCALAPPDATA%\PhoneCastShell，不動
        if (!RunningFromInstallDir)
            try { Directory.Delete(InstallDir, true); } catch { }
    }

    static void CreateShortcut(string lnk, string target, string icon)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("這台電腦沒有 WScript.Shell，無法建立捷徑");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(lnk);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.IconLocation = icon + ",0";
            link.Description = "手機投屏對話殼";
            link.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), true);
        }
    }

    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    static string Clean(string name) =>
        string.Concat(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
}
