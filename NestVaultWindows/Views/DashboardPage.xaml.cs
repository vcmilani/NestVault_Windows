using System.Linq;
using Microsoft.UI.Xaml.Controls;
using NestVaultWindows.Models;
using NestVaultWindows.ViewModels;

namespace NestVaultWindows.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        ViewModel = new DashboardViewModel(App.Api);
        InitializeComponent();
    }

    // Projection class for the DataTemplate — x:DataType requires a concrete type
    public sealed class BackupSummaryItem
    {
        public string Label { get; init; } = "";
        public string FormattedSize { get; init; } = "";
        public int    VersionCount { get; init; }
        public string FormattedLastVersion { get; init; } = "";

        public static BackupSummaryItem From(BackupSummary b) => new()
        {
            Label               = b.Label,
            FormattedSize       = b.FormattedSize,
            VersionCount        = b.VersionCount,
            FormattedLastVersion = b.LastVersionDate.HasValue
                ? b.LastVersionDate.Value.ToString("dd/MM/yyyy HH:mm")
                : "—"
        };
    }
}
