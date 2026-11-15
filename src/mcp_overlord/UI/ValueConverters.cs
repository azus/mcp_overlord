using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mcp_overlord.UI
{
    /// <summary>Collapses a control when the bound string is empty.</summary>
    public sealed class StringToVisibilityConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <inheritdoc />
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>Inverse of <see cref="System.Windows.Controls.BooleanToVisibilityConverter"/>.</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool visible = value is bool flag && flag;
            return visible ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <inheritdoc />
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>Full opacity when the field is included, dimmed when it is left out of the file.</summary>
    public sealed class IncludeOpacityConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is bool included && included ? 1.0 : 0.45;
        }

        /// <inheritdoc />
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>
        /// Passes a length through when it is a positive finite number.
        /// Otherwise returns a finite stand-in so a body list is measured with a real height and stays virtualized.
        /// </summary>
        public sealed class PositiveLengthConverter : IValueConverter
        {
            private const double STAND_IN_LENGTH = 160;

            /// <inheritdoc />
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                if (value is double length && !double.IsNaN(length) && !double.IsInfinity(length) && length > 1)
                {
                    return length;
                }

                return STAND_IN_LENGTH;
            }

        /// <inheritdoc />
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    /// <summary>True when a body-view name matches the converter parameter.</summary>
    public sealed class BodyViewEqualsConverter : IValueConverter
    {
        /// <inheritdoc />
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool selected && selected)
            {
                return parameter ?? "Json";
            }

            return Binding.DoNothing;
        }
    }
}
