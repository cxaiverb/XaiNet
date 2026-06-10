using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace XaiNet2.Helpers
{
    internal class BitsToHumanConverter : IValueConverter
    {
        static string BitsToHumanReadable(long bits)
        {
            string[] units = { "b", "Kb", "Mb", "Gb", "Tb", "Pb", "Eb" };
            double size = bits;
            int unitIndex = 0;

            while (size >= 1000 && unitIndex < units.Length - 1)
            {
                size /= 1000;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Used both in bindings and in LiveCharts tooltip formatters, so return an empty string
            // rather than throwing when handed an unexpected value.
            if (value is long bits)
            {
                return BitsToHumanReadable(bits);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
