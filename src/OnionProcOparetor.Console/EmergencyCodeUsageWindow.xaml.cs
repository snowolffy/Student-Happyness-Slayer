using System.Windows;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class EmergencyCodeUsageWindow : Window
{
    public EmergencyCodeUsageWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _ = LoadAsync(apiClient);
    }

    private async Task LoadAsync(ApiClient apiClient)
    {
        var items = await apiClient.GetEmergencyCodeUsageAsync();
        UsageListView.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
