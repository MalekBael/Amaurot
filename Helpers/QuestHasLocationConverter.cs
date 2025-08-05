using System;
using System.Globalization;
using System.Windows.Data;

namespace Amaurot
{
    public class QuestHasLocationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is QuestInfo quest)
            {
                // Consider a quest to have location if it has a valid MapId or PlaceName
                return quest.MapId > 0 || !string.IsNullOrEmpty(quest.PlaceName);
            }

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}