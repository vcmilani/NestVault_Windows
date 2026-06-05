using Microsoft.UI.Xaml.Controls;
using NestVault_Windows.ViewModels;

namespace NestVault_Windows.Views;

public sealed partial class BackupsPage : Page
{
    public BackupsViewModel ViewModel { get; }

    public BackupsPage()
    {
        ViewModel = new BackupsViewModel(App.Api);
        InitializeComponent();
    }
}
