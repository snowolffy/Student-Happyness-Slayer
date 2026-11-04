using System;
using System.Windows;
using System.Windows.Controls;
using OnionProcOparetor.Console.Models;
using OnionProcOparetor.Console.Services;

namespace OnionProcOparetor.Console;

public partial class EmergencyCodeUsageWindow : UserControl
{
    /// <summary>UI-3b (2026-11-03): หน้าต่างนี้ไม่มีผลลัพธ์ให้อ่าน - raise เมื่อกดปิดเท่านั้น</summary>
    public event Action? CloseRequested;

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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
