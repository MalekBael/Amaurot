using System.Windows;

namespace Amaurot
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            // Force all timers and resources to be disposed
            try
            {
                // Stop all running processes
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch
            {
                // If kill fails, try Environment.Exit
                System.Environment.Exit(0);
            }

            base.OnExit(e);
        }
    }
}