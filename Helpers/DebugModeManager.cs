using System;

namespace Amaurot
{
    public static class DebugModeManager
    {
        private static bool _isDebugModeEnabled = false;

        public static bool IsDebugModeEnabled
        {
            get => _isDebugModeEnabled;
            set
            {
                if (_isDebugModeEnabled != value)
                {
                    _isDebugModeEnabled = value;
                    DebugModeChanged?.Invoke(value);
                }
            }
        }

        public static event Action<bool>? DebugModeChanged;

        public static void LogDebug(string message)
        {
            if (_isDebugModeEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
            }
        }

        public static void LogVerbose(string message)
        {
            if (_isDebugModeEnabled)
            {
                System.Diagnostics.Debug.WriteLine($"[VERBOSE] {message}");
            }
        }
    }
}