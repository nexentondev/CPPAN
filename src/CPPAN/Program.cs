namespace CPPAN;
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize(); // enables visual styles + DPI => native Windows 10 look
        Application.Run(new MainForm());
    }
}
