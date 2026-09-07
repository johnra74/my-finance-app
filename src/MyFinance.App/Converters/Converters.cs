using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyFinance.App.Converters;

/// <summary>Shows an element only while the bound value equals the named enum member.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string expected)
        {
            return Visibility.Collapsed;
        }

        return string.Equals(value.ToString(), expected, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hides an element when the bound value is null or an empty string.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Collapsed,
            string text when string.IsNullOrWhiteSpace(text) => Visibility.Collapsed,
            _ => Visibility.Visible,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when the bound count is zero — for empty-state messages.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Negates a boolean, for enabling controls while an operation is not running.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && !flag;
}

/// <summary>
/// Renders a <see cref="MyFinance.Core.Primitives.Money"/> in accounting form, with
/// negatives in parentheses as the register shows them.
/// </summary>
public sealed class MoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Core.Primitives.Money money => money.ToString(parameter as string ?? "A", culture),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Core.Primitives.Money.TryParse(value as string, culture, out Core.Primitives.Money money)
            ? money
            : DependencyProperty.UnsetValue;
}

/// <summary>Colours an amount: red when negative, default otherwise.</summary>
public sealed class MoneySignBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool negative = value is Core.Primitives.Money money && money.IsNegative;
        string key = negative ? "NegativeBrush" : "TextPrimaryBrush";

        return Application.Current?.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only while the bound boolean is false.</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows an element only when the bound count is greater than zero.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Colours the reconcile difference: green once it reaches zero, red while it has not.
/// </summary>
/// <remarks>
/// The difference reaching zero is the single thing the reconcile screen exists to tell the
/// user, so it gets a colour change rather than only a number.
/// </remarks>
public sealed class BalancedBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is bool balanced && balanced ? "PositiveBrush" : "NegativeBrush";
        return Application.Current?.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a fraction between zero and one into a width against the element's own container.
/// </summary>
/// <remarks>
/// Bar geometry is computed in the view model against a nominal scale; this is the last
/// step, mapping that fraction onto whatever width the panel actually got.
/// </remarks>
/// <summary>
/// Reads the (fraction, available length) pair a chart converter is given.
/// </summary>
/// <remarks>
/// The fraction arrives as a <see cref="double" /> from the chart geometry and as a
/// <see cref="decimal" /> from the budget figures, which are money and so are decimal all the
/// way through. Accepting both here keeps one bar template usable by both rather than
/// forcing one of them to lose precision earlier than it has to.
/// </remarks>
internal static class ChartFraction
{
    public static bool TryRead(object[]? values, out double fraction, out double available)
    {
        fraction = 0;
        available = 0;

        if (values is not [object first, object second])
        {
            return false;
        }

        if (!TryToDouble(first, out fraction) || !TryToDouble(second, out available))
        {
            return false;
        }

        return !double.IsNaN(fraction) && !double.IsNaN(available);
    }

    private static bool TryToDouble(object value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}

public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!ChartFraction.TryRead(values, out double fraction, out double available))
        {
            return 0d;
        }

        // A bar with a real but tiny value still gets a visible sliver, so "almost nothing"
        // and "nothing at all" do not look identical.
        double width = Math.Clamp(fraction, 0, 1) * Math.Max(0, available);
        return fraction > 0 ? Math.Max(width, 2d) : 0d;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a fraction between zero and one into a height against its container.</summary>
public sealed class FractionToHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!ChartFraction.TryRead(values, out double fraction, out double available))
        {
            return 0d;
        }

        double height = Math.Clamp(fraction, 0, 1) * Math.Max(0, available);
        return fraction > 0 ? Math.Max(height, 2d) : 0d;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Colours a budget meter by how much of it has been used.
/// </summary>
/// <remarks>
/// Status colours, never a series colour. They are always shown beside the figures and the
/// words that say the same thing, because a status colour must never carry meaning alone —
/// the warning step in particular sits below 3:1 on a white surface by design.
/// </remarks>
public sealed class BudgetStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            decimal progress when progress > 1m => "StatusCriticalBrush",
            decimal progress when progress >= 0.9m => "StatusWarningBrush",
            decimal => "StatusGoodBrush",
            _ => "ChartSeries1Brush",
        };

        return Application.Current?.TryFindResource(key) ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
