using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Unit tests for <see cref="ToastNotificationService"/> and <see cref="IToastNotificationService"/>,
/// verifying compliance with IEC 61508-3 triad testing and non-blocking toast notification architecture.
/// </summary>
public class ToastNotificationServiceTests
{
    [Fact]
    public void ToastNotificationService_Implements_IToastNotificationService()
    {
        IToastNotificationService service = new ToastNotificationService();
        Assert.NotNull(service);
        Assert.NotNull(service.ActiveToasts);
    }

    [Fact]
    public void ShowSuccess_AddsSuccessToastWithExpectedProperties()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowSuccess("Operation succeeded", "Custom Success Title");

        Assert.Single(service.ActiveToasts);
        var toast = service.ActiveToasts[0];
        Assert.Equal(ToastType.Success, toast.Type);
        Assert.Equal("Custom Success Title", toast.Title);
        Assert.Equal("Operation succeeded", toast.Message);
        Assert.False(toast.IsDismissed);
    }

    [Fact]
    public void ShowInfo_AddsInfoToastWithDefaultTitle()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowInfo("System info notice");

        Assert.Single(service.ActiveToasts);
        var toast = service.ActiveToasts[0];
        Assert.Equal(ToastType.Info, toast.Type);
        Assert.Equal("Thông tin", toast.Title);
        Assert.Equal("System info notice", toast.Message);
    }

    [Fact]
    public void ShowWarning_AddsWarningToastWithExpectedDuration()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowWarning("Potential overload detected", "Device Alert");

        Assert.Single(service.ActiveToasts);
        var toast = service.ActiveToasts[0];
        Assert.Equal(ToastType.Warning, toast.Type);
        Assert.Equal("Device Alert", toast.Title);
        Assert.True(toast.Duration >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void ShowError_AddsErrorToastWithLongestDuration()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowError("Communication failed", "COM Fault");

        Assert.Single(service.ActiveToasts);
        var toast = service.ActiveToasts[0];
        Assert.Equal(ToastType.Error, toast.Type);
        Assert.Equal("COM Fault", toast.Title);
        Assert.True(toast.Duration >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ShowAction_ExecutesCallbackAndDismissesToast()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        bool actionExecuted = false;
        service.ShowAction(
            "New update available",
            "Update Now",
            () => actionExecuted = true,
            "Update Ready");

        Assert.Single(service.ActiveToasts);
        var toast = service.ActiveToasts[0];
        Assert.Equal("Update Now", toast.ActionText);
        Assert.NotNull(toast.ActionCommand);

        toast.ActionCommand.Execute(null);

        Assert.True(actionExecuted);
        Assert.True(toast.IsDismissed);
        Assert.Empty(service.ActiveToasts);
    }

    [Fact]
    public void MaxActiveToasts_EnforcesFifoEviction()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        for (int i = 1; i <= 6; i++)
        {
            service.ShowInfo($"Message {i}", $"Title {i}");
        }

        // Maximum allowed active toasts is 4
        Assert.Equal(4, service.ActiveToasts.Count);
        Assert.Equal("Title 3", service.ActiveToasts[0].Title);
        Assert.Equal("Title 6", service.ActiveToasts[3].Title);
    }

    [Fact]
    public void Dismiss_RemovesSpecificToast()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowInfo("Message A", "Title A");
        service.ShowInfo("Message B", "Title B");

        Assert.Equal(2, service.ActiveToasts.Count);
        var first = service.ActiveToasts[0];

        service.Dismiss(first);

        Assert.True(first.IsDismissed);
        Assert.Single(service.ActiveToasts);
        Assert.Equal("Title B", service.ActiveToasts[0].Title);
    }

    [Fact]
    public void DismissAll_ClearsAllActiveToasts()
    {
        var service = new ToastNotificationService();
        service.ShowInfo("Msg 1");
        service.ShowWarning("Msg 2");
        service.ShowError("Msg 3");

        Assert.NotEmpty(service.ActiveToasts);

        service.DismissAll();

        Assert.Empty(service.ActiveToasts);
    }

    [Fact]
    public void Show_ConcurrentCallsFromMultipleThreads_ThreadSafeAndBounded()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        System.Threading.Tasks.Parallel.For(0, 50, i =>
        {
            if (i % 3 == 0) service.ShowInfo($"Concurrent Info {i}");
            else if (i % 3 == 1) service.ShowWarning($"Concurrent Warn {i}");
            else service.ShowError($"Concurrent Error {i}");
        });

        // Maximum allowed active toasts is bounded to 4 (FIFO eviction)
        Assert.True(service.ActiveToasts.Count <= 4);
    }

    [Fact]
    public void Show_NullOrWhitespaceMessageAndTitle_UsesSensibleDefaultsWithoutThrowing()
    {
        var service = new ToastNotificationService();
        service.DismissAll();

        service.ShowInfo("", null);
        service.ShowWarning("   ", "   ");

        Assert.Equal(2, service.ActiveToasts.Count);
        Assert.NotNull(service.ActiveToasts[0].Title);
        Assert.NotNull(service.ActiveToasts[1].Title);
    }
}
