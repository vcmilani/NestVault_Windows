using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NestVaultWindows.Views.Controls;

public sealed partial class StatCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(StatCard),
            new PropertyMetadata("", (d, _) => ((StatCard)d).TitleText.Text = ((StatCard)d).Title));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatCard),
            new PropertyMetadata("", (d, _) => ((StatCard)d).ValueText.Text = ((StatCard)d).Value));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(StatCard),
            new PropertyMetadata("", (d, _) => ((StatCard)d).SubtitleText.Text = ((StatCard)d).Subtitle));

    public string Title    { get => (string)GetValue(TitleProperty);    set => SetValue(TitleProperty, value); }
    public string Value    { get => (string)GetValue(ValueProperty);    set => SetValue(ValueProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public StatCard() { InitializeComponent(); }
}
