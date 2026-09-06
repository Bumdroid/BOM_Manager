using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BOMManager.UI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool val = value is bool b && b;
            if (Invert) val = !val;
            return val ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ModifiedBackgroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush AmberBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
        private static readonly SolidColorBrush TransparentBrush = Brushes.Transparent;

        static ModifiedBackgroundConverter()
        {
            AmberBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isModified && isModified)
            {
                return AmberBrush;
            }
            return TransparentBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ModifiedForegroundConverter : IValueConverter
    {
        private static readonly SolidColorBrush AmberTextBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));
        private static readonly SolidColorBrush DefaultTextBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        private static readonly SolidColorBrush SubassemblyBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x8A));

        static ModifiedForegroundConverter()
        {
            AmberTextBrush.Freeze();
            DefaultTextBrush.Freeze();
            SubassemblyBrush.Freeze();
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isModified && isModified)
            {
                return AmberTextBrush;
            }
            if (parameter is string paramStr && paramStr == "Subassembly")
            {
                return SubassemblyBrush;
            }
            return DefaultTextBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TransparencyEmojiConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOpaque && !isOpaque)
            {
                return "🔴";
            }
            return "🟢";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TreeTriangleGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded && isExpanded)
            {
                return "▼";
            }
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LevelToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level && level > 0)
            {
                return new Thickness(level * 16, 0, 0, 0);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
