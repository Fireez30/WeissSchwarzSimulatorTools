using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace WeissSchwarzSimulatorTools.App.Controls;

/// <summary>Colours of the card colours (Yellow, Blue…), from the theme.</summary>
public static class CardColors
{
    public static IBrush Brush(CardColor color)
    {
        var key = color switch
        {
            CardColor.Yellow => "CardYellow",
            CardColor.Blue => "CardBlue",
            CardColor.Red => "CardRed",
            CardColor.Green => "CardGreen",
            CardColor.Purple => "CardPurple",
            _ => "CardNoColor",
        };
        var app = Application.Current!;
        return app.TryGetResource(key, app.ActualThemeVariant, out var brush) && brush is IBrush b ? b : Brushes.Gray;
    }
}

/// <summary>A theme brush by resource key, for code that builds controls.</summary>
public static class ThemeBrushes
{
    public static IBrush Brush(string key)
    {
        var app = Application.Current!;
        return app.TryGetResource(key, app.ActualThemeVariant, out var brush) && brush is IBrush b ? b : Brushes.Gray;
    }
}

public sealed class CardColorConverter : IValueConverter
{
    public static readonly CardColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CardColor color ? CardColors.Brush(color) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TriggerIconConverter : IValueConverter
{
    public static readonly TriggerIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Trigger t ? Pictures.TriggerIcon(t) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
