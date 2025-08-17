using System;
using System.IO;
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
                Current.Shutdown(0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during graceful shutdown: {ex.Message}");
                Environment.Exit(0);
            }

            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Make hardware acceleration configurable for Wine
            if (IsRunningOnWine())
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            }

            base.OnStartup(e);
        }

        private static bool IsRunningOnWine()
        {
            try
            {
                return Environment.GetEnvironmentVariable("WINEPREFIX") != null ||
                       Environment.GetEnvironmentVariable("WINE") != null ||
                       Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine"));
            }
            catch
            {
                return false;
            }
        }
    }
}