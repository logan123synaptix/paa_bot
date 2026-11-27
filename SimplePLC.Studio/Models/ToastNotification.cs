using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace SimplePLC.Studio.Models;

/// <summary>
/// Phân loại mức độ ưu tiên của Toast Notification công nghiệp.
/// </summary>
public enum ToastType
{
    Success,
    Info,
    Warning,
    Error
}

/// <summary>
/// Đại diện cho một thông báo Toast thời gian thực trên giao diện SynaptiX IDE.
/// </summary>
public class ToastNotification : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; } = ToastType.Info;
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3.5);
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public string? ActionText { get; set; }
    public ICommand? ActionCommand { get; set; }
    public Action? DismissAction { get; set; }

    private bool _isDismissed;
    public bool IsDismissed
    {
        get => _isDismissed;
        set
        {
            if (_isDismissed != value)
            {
                _isDismissed = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
