namespace PhoneCastShell;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // 命令列：--install [名字]、--uninstall 做完就結束，不開視窗；--write-icon 路徑 是建置時產生 app.ico 用的
        int Arg(string name) => Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        string? Next(int i) => i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;

        if (Arg("--write-icon") is var wi and >= 0)
        {
            AppIcon.WriteIco(Next(wi) ?? "app.ico", "ring");
            return 0;
        }
        if (Arg("--install") is var ins and >= 0)
        {
            var s = Settings.Load();
            try
            {
                Installer.Install(s, Next(ins) ?? (s.ShortcutName.Length > 0 ? s.ShortcutName : Installer.DefaultName));
                return 0;
            }
            catch (Exception e) // 最常見：安裝的那份正開著，檔案被鎖住
            {
                MessageBox.Show("沒加成功，先把正在開的那個關掉再試一次。\n\n" + e.Message, "加到開始功能表");
                return 1;
            }
        }
        if (Arg("--uninstall") >= 0)
        {
            Installer.Uninstall(Settings.Load());
            return 0;
        }

        using var single = new Mutex(true, "PhoneCastShell.SingleInstance", out bool first);
        if (!first) return 0;

        var settings = Settings.Load();
        // 測試用：--process 名稱 → 這次也找這個程式的視窗，而且最優先
        if (Next(Arg("--process")) is { } proc) settings.TargetProcess = proc;

        ApplicationConfiguration.Initialize();
        var form = new MainForm(settings);

        // 不管怎麼結束，都先把投屏視窗還回去
        Application.ThreadException += (_, e) =>
        {
            form.ReleaseMirror();
            MessageBox.Show(e.Exception.ToString(), "出錯了");
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => form.ReleaseMirror();

        Application.Run(form);
        return 0;
    }
}
