using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Amaurot
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch
            {
                System.Environment.Exit(0);
            }

            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            base.OnStartup(e);
        }
    }
}