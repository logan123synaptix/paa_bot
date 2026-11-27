using System;
using System.Collections.ObjectModel;
using SimplePLC.Studio.Models;

namespace SimplePLC.Studio.Services;

/// <summary>
/// Giao diện dịch vụ hiển thị thông báo Toast không chặn luồng (Non-blocking) cho SynaptiX IDE.
/// </summary>
public interface IToastNotificationService
{
    ReadOnlyObservableCollection<ToastNotification> ActiveToasts { get; }

    void ShowSuccess(string message, string? title = null);
    void ShowInfo(string message, string? title = null);
    void ShowWarning(string message, string? title = null);
    void ShowError(string message, string? title = null);
    void ShowAction(string message, string actionText, Action onAction, string? title = null);

    void Dismiss(ToastNotification toast);
    void DismissAll();
}
