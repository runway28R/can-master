namespace PcanCanMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppLog.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error($"Unhandled application exception. IsTerminating={args.IsTerminating}", args.ExceptionObject as Exception);
        Application.ThreadException += (_, args) =>
            AppLog.Error("Unhandled Windows UI thread exception", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            AppLog.Info("Initializing Windows Forms.");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            ApplicationConfiguration.Initialize();
            AppLog.Info("Creating main window.");
            Application.Run(new MainForm());
            AppLog.Info("Application closed normally.");
        }
        catch (Exception exception)
        {
            AppLog.Error("Fatal exception escaped the application entry point.", exception);
            try
            {
                MessageBox.Show(
                    $"PCAN CAN Monitor could not start or has stopped unexpectedly.\n\nA diagnostic log was written to:\n{AppLog.LogPath}",
                    "PCAN CAN Monitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // The process may be failing before Windows Forms can show a dialog.
            }
        }
    }
}
