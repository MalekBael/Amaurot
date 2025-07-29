using System;
using System.Globalization;
using System.Windows.Data;

namespace map_editor
{
    public class QuestHasLocationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is QuestInfo quest)
            {
                // Check if quest has location data
                bool hasLocationData = quest.MapId > 0 || !string.IsNullOrEmpty(quest.PlaceName);

                // Only show pin if quest has location data AND debug mode is enabled
                return hasLocationData && DebugModeManager.IsDebugModeEnabled;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}