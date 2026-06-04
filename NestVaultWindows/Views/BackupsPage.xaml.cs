using Microsoft.UI.Xaml.Controls;
using NestVaultWindows.ViewModels;

namespace NestVaultWindows.Views;

public sealed partial class BackupsPage : Page
{
    public BackupsViewModel ViewModel { get; }

    public BackupsPage()
    {
        ViewModel = new BackupsViewModel(App.Api);
        InitializeComponent();
    }
}
