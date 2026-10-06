namespace Baba.Desktop;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        // Fully qualified: inside Baba.* the bare name "Application" means the Baba.Application namespace.
        System.Windows.Forms.Application.Run(new Form1());
    }    
}