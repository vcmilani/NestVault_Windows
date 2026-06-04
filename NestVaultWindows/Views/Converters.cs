using System;
using System.Collections;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using NestVaultWindows.Services;
using Windows.UI;

namespace NestVaultWindows.Views;

// bool → Visibility
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, string ___)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type _, object __, string ___)
        => value is Visibility.Visible;
}

// !bool → Visibility
public sealed class BoolNegToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, string ___)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type _, object __, string ___)
        => value is Visibility.Collapsed;
}

// !bool
public sealed class BoolNegateConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, string ___)
        => value is bool b && !b;
    public object ConvertBack(object value, Type _, object __, string ___)
        => value is bool b && !b;
}

// int/count → Visibility (>0 = visible)
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, string ___)
    {
        if (value is int i) return i > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is ICollection c) return c.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type _, object __, string ___) => 0;
}

// bool → Green/Red Color (for enabled dot)
public sealed class BoolToGreenRedColorConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, string ___)
        => value is true
            ? Color.FromArgb(255, 52, 199, 89)   // green
            : Color.FromArgb(255, 255, 69, 58);  // red
    public object ConvertBack(object value, Type _, object __, string ___) => false;
}

// bool → red SolidColorBrush (for error counts)
public sealed class BoolToRedBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Red    = new(Color.FromArgb(255, 255, 69, 58));
    private static readonly SolidColorBrush Inherit = new(Colors.Transparent);

    public object Convert(object value, Type _, object __, string ___)
        => value is true ? Red : new SolidColorBrush(Colors.White);  // white in dark theme
    public object ConvertBack(object value, Type _, object __, string ___) => false;
}

// LogKind → SolidColorBrush
public sealed class LogKindToColorBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Info    = new(Color.FromArgb(255, 200, 200, 200));
    private static readonly SolidColorBrush Success = new(Color.FromArgb(255, 52, 199, 89));
    private static readonly SolidColorBrush Warning = new(Color.FromArgb(255, 255, 159, 10));
    private static readonly SolidColorBrush Error   = new(Color.FromArgb(255, 255, 69, 58));

    public object Convert(object value, Type _, object __, string ___)
        => value is BackupRunner.LogKind kind
            ? kind switch
            {
                BackupRunner.LogKind.Success => Success,
                BackupRunner.LogKind.Warning => Warning,
                BackupRunner.LogKind.Error   => Error,
                _                             => Info
            }
            : Info;
    public object ConvertBack(object v, Type _, object __, string ___) => v;
}
