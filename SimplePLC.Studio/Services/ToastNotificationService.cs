using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using SimplePLC.Studio.Models;

namespace SimplePLC.Studio.Services;

/// <summary>
/// Dịch vụ quản lý thông báo Toast công nghiệp toàn cục, thay thế các MessageBox modal blocking.
/// </summary>
public sealed class ToastNotificationService : IToastNotificationService
{
    private static readonly Lazy<ToastNotificationService> _lazy = new(() => new ToastNotificationService());
    public static ToastNotificationService Instance => _lazy.Value;

    private readonly ObservableCollection<ToastNotification> _toasts = new();
    public ReadOnlyObservableCollection<ToastNotification> ActiveToasts { get; }

    private readonly Dispatcher _dispatcher;
    private const int MaxActiveToasts = 4;

    public ToastNotificationService()
    {
        ActiveToasts = new ReadOnlyObservableCollection<ToastNotification>(_toasts);
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void ShowSuccess(string message, string? title = null)
    {
        Show(new ToastNotification
        {
            Type = ToastType.Success,
            Title = title ?? "Thành công",
            Message = message,
            Duration = TimeSpan.FromSeconds(3.5)
        });
    }

    public void ShowInfo(string message, string? title = null)
    {
        Show(new ToastNotification
        {
            Type = ToastType.Info,
            Title = title ?? "Thông tin",
            Message = message,
            Duration = TimeSpan.FromSeconds(3.5)
        });
    }

    public void ShowWarning(string message, string? title = null)
    {
        Show(new ToastNotification
        {
            Type = ToastType.Warning,
            Title = title ?? "Cảnh báo",
            Message = message,
            Duration = TimeSpan.FromSeconds(4.5)
        });
    }

    public void ShowError(string message, string? title = null)
    {
        Show(new ToastNotification
        {
            Type = ToastType.Error,
            Title = title ?? "Lỗi",
            Message = message,
            Duration = TimeSpan.FromSeconds(5.5)
        });
    }

    public void ShowAction(string message, string actionText, Action onAction, string? title = null)
    {
        var toast = new ToastNotification
        {
            Type = ToastType.Info,
            Title = title ?? "Hành động",
            Message = message,
            ActionText = actionText,
            Duration = TimeSpan.FromSeconds(5.0)
        };

        toast.ActionCommand = new RelayCommand(() =>
        {
            onAction?.Invoke();
            Dismiss(toast);
        });

        Show(toast);
    }

    private void Show(ToastNotification toast)
    {
        void AddToast()
        {
            toast.DismissAction = () => Dismiss(toast);

            // Giới hạn số lượng toast hiển thị đồng thời
            while (_toasts.Count >= MaxActiveToasts)
            {
                _toasts.RemoveAt(0);
            }

            _toasts.Add(toast);

            // Auto dismiss timer
            Task.Delay(toast.Duration).ContinueWith(_ =>
            {
                _dispatcher.InvokeAsync(() =>
                {
                    if (!toast.IsDismissed)
                    {
                        Dismiss(toast);
                    }
                });
            });
        }

        if (_dispatcher.CheckAccess())
        {
            AddToast();
        }
        else
        {
            _dispatcher.InvokeAsync(AddToast);
        }
    }

    public void Dismiss(ToastNotification toast)
    {
        void RemoveToast()
        {
            toast.IsDismissed = true;
            _toasts.Remove(toast);
        }

        if (_dispatcher.CheckAccess())
        {
            RemoveToast();
        }
        else
        {
            _dispatcher.InvokeAsync(RemoveToast);
        }
    }

    public void DismissAll()
    {
        void ClearToasts()
        {
            foreach (var t in _toasts)
            {
                t.IsDismissed = true;
            }
            _toasts.Clear();
        }

        if (_dispatcher.CheckAccess())
        {
            ClearToasts();
        }
        else
        {
            _dispatcher.InvokeAsync(ClearToasts);
        }
    }
}
