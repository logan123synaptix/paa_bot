using System.Windows;
using System.Windows.Controls;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services;

namespace SimplePLC.Studio.Controls;

/// <summary>
/// Interaction logic for ToastHostControl.xaml
/// </summary>
public partial class ToastHostControl : UserControl
{
    public ToastHostControl()
    {
        InitializeComponent();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToastNotification toast })
        {
            ToastNotificationService.Instance.Dismiss(toast);
        }
    }
}
