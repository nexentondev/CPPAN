namespace CPPAN;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Never die silently: show the error and save it to %AppData%\CPPAN\crash.log
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash.Report(e.ExceptionObject as Exception);
        ApplicationConfiguration.Initialize(); // enables visual styles + DPI => native Windows 10 look
        Application.Run(new MainForm());
    }
}

static class Crash
{
    public static string FilePath => Path.Combine(Config.Data, "crash.log");

    public static void Report(Exception? e)
    {
        var text = e?.ToString() ?? "Unknown error";
        AppLog.Write("ERROR: " + text);
        try { File.AppendAllText(FilePath, DateTime.Now + "\r\n" + text + "\r\n\r\n"); } catch { }
        try
        {
            MessageBox.Show("CPPAN hit an error:\r\n\r\n" + (e?.Message ?? "unknown") + "\r\n\r\nDetails were saved to:\r\n" + FilePath,
                "CPPAN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
