using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Amaurot.Services.Entities;

namespace Amaurot
{
    /// <summary>
    /// Converter that determines whether to show the location pin emoji for quests
    /// Shows the pin only when:
    /// 1. Debug mode is enabled AND
    /// 2. The quest has location data (coordinates or place name)
    /// </summary>
    public class QuestHasLocationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // First check if debug mode is enabled
            if (!DebugModeManager.IsDebugModeEnabled)
            {
                return Visibility.Collapsed; // Hide pin when debug mode is off
            }

            // Then check if quest has location data
            if (value is QuestInfo quest)
            {
                bool hasCoordinates = quest.MapX != 0 || quest.MapY != 0;
                bool hasPlaceName = !string.IsNullOrEmpty(quest.PlaceName);
                bool hasMapId = quest.MapId > 0;

                // Show pin if quest has any location data
                return (hasCoordinates || hasPlaceName || hasMapId) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ConvertBack is not supported for QuestHasLocationConverter");
        }
    }
}