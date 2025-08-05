using System.ComponentModel;

namespace Amaurot
{
    /// <summary>
    /// Static manager for debug mode state that can be accessed globally
    /// </summary>
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
                    DebugModeChanged?.Invoke();
                }
            }
        }

        public static event System.Action? DebugModeChanged;
    }
}