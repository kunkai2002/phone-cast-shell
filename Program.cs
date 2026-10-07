namespace PhoneCastShell;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var single = new Mutex(true, "PhoneCastShell.SingleInstance", out bool first);
        if (!first) return;

        var settings = Settings.Load();
        // 測試用：--process 名稱 → 這次改找別的程式的視窗（不存檔）
        int i = Array.IndexOf(args, "--process");
        if (i >= 0 && i + 1 < args.Length)
        {
            settings.TargetProcess = args[i + 1];
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm(settings);

        // 不管怎麼結束，都先把投屏視窗還回去（不然它會一直是被切掉的樣子）
        Application.ThreadException += (_, e) =>
        {
            form.ReleaseMirror();
            MessageBox.Show(e.Exception.ToString(), "出錯了");
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => form.ReleaseMirror();

        Application.Run(form);
    }
}
