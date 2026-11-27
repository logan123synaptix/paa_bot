using System.Linq;
using SimplePLC.Protocol.Enums;
using SimplePLC.Studio.ViewModels;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Unit tests for DirectWriteConfigDialogViewModel verifying initialization,
/// contextual visibility, default parameters, and save lifecycle.
/// </summary>
public class DirectWriteConfigDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesValuesFromNode()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do2, DirectWriteMode.DigitalOutput)
        {
            IsActiveHigh = true,
            FailSafeState = 0,
            PwmFrequencyHz = 2500,
            PulseWidthUs = 100,
            TriggerMode = DirectWriteTriggerMode.Continuous
        };

        var vm = new DirectWriteConfigDialogViewModel(node);

        Assert.Equal(DirectWriteTargetPin.Do2, vm.TargetPin);
        Assert.Equal(DirectWriteMode.DigitalOutput, vm.Mode);
        Assert.True(vm.IsActiveHigh);
        Assert.Equal(0, vm.FailSafeState);
        Assert.Equal(2500u, vm.PwmFrequencyHz);
        Assert.Equal(100u, vm.PulseWidthUs);
        Assert.Equal(DirectWriteTriggerMode.Continuous, vm.TriggerMode);
    }

    [Fact]
    public void AvailableTargetPins_ContainsOnlyGenericIndustrialPins()
    {
        var node = new DirectWriteNodeViewModel();
        var vm = new DirectWriteConfigDialogViewModel(node);

        Assert.Equal(8, vm.AvailableTargetPins.Count);
        var pinNames = vm.AvailableTargetPins.Select(p => p.DisplayName).ToList();

        // Must not contain any custom application terms
        Assert.DoesNotContain(pinNames, p => p.Contains("MOSFET"));
        Assert.DoesNotContain(pinNames, p => p.Contains("LED Data"));
        Assert.DoesNotContain(pinNames, p => p.Contains("Sort-to-Light"));
        Assert.DoesNotContain(pinNames, p => p.Contains("OUT"));
        Assert.DoesNotContain(pinNames, p => p.Contains("FAST"));

        // Must contain standard industrial digital outputs DO0..DO7
        Assert.Contains(pinNames, p => p == "DO0");
        Assert.Contains(pinNames, p => p == "DO7");
    }

    [Fact]
    public void DigitalOutputMode_ConfiguresActiveLevelAndFailSafeState_Cleanly()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do0, DirectWriteMode.DigitalOutput)
        {
            IsActiveHigh = false,
            FailSafeState = 1
        };

        var vm = new DirectWriteConfigDialogViewModel(node);

        Assert.True(vm.IsDigitalOutputMode);
        Assert.False(vm.IsActiveHigh);
        Assert.Equal(1, vm.FailSafeState);
        Assert.Contains("DO0", vm.SummaryBadgeText);
        Assert.Contains("Digital Output", vm.SummaryBadgeText);
    }

    [Fact]
    public void PtoMode_ConfiguresPulsesAndFrequency_Cleanly()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do0, DirectWriteMode.PulseTrain)
        {
            ChannelCount = 500,
            PwmFrequencyHz = 2500,
            PulseWidthUs = 50,
            TriggerMode = DirectWriteTriggerMode.OnTriggerRisingEdge
        };

        var vm = new DirectWriteConfigDialogViewModel(node);

        Assert.True(vm.IsPulseTrainMode);
        Assert.Equal((ushort)500, vm.FallbackPulses);
        Assert.Equal(2500u, vm.PwmFrequencyHz);
        Assert.Equal(50u, vm.PulseWidthUs);
        Assert.Equal(DirectWriteTriggerMode.OnTriggerRisingEdge, vm.TriggerMode);
        Assert.Contains("PTO", vm.SummaryBadgeText);
        Assert.Contains("500 Pulses", vm.SummaryBadgeText);
    }

    [Fact]
    public void AddressableBusMode_ConfiguresGenericParameters_Cleanly()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do1, DirectWriteMode.AddressableBus)
        {
            ChannelCount = 64,
            IsExclusiveChannel = true,
            DefaultColorRgb = 0x00FF00
        };

        var vm = new DirectWriteConfigDialogViewModel(node);

        Assert.True(vm.IsAddressableBusMode);
        Assert.Equal((ushort)64, vm.ChannelCount);
        Assert.True(vm.IsExclusiveChannel);
        Assert.Equal("#00FF00", vm.DefaultColorHex);
        Assert.Contains("Bus", vm.SummaryBadgeText);
        Assert.Contains("64 Ch", vm.SummaryBadgeText);
    }

    [Fact]
    public void ModeChange_UpdatesDefaultTriggerMode_Deterministically()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do0, DirectWriteMode.DigitalOutput);
        var vm = new DirectWriteConfigDialogViewModel(node);

        // DO initial from node is OnTriggerRisingEdge
        Assert.Equal(DirectWriteTriggerMode.OnTriggerRisingEdge, vm.TriggerMode);

        // Switching to PWM sets TriggerMode to Continuous
        vm.Mode = DirectWriteMode.PwmDuty;
        Assert.Equal(DirectWriteTriggerMode.Continuous, vm.TriggerMode);

        // Switching to PTO sets TriggerMode to OnTriggerRisingEdge
        vm.Mode = DirectWriteMode.PulseTrain;
        Assert.Equal(DirectWriteTriggerMode.OnTriggerRisingEdge, vm.TriggerMode);

        // Switching to AddressableBus sets TriggerMode to Continuous
        vm.Mode = DirectWriteMode.AddressableBus;
        Assert.Equal(DirectWriteTriggerMode.Continuous, vm.TriggerMode);
    }

    [Fact]
    public void Apply_TransfersPropertiesBackToNode()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do1, DirectWriteMode.DigitalOutput);
        var vm = new DirectWriteConfigDialogViewModel(node)
        {
            TargetPin = DirectWriteTargetPin.Do3,
            Mode = DirectWriteMode.PwmDuty,
            PwmFrequencyHz = 5000,
            DutyMin = 10,
            DutyMax = 90,
            PulseWidthUs = 80,
            TriggerMode = DirectWriteTriggerMode.Continuous,
            DefaultColorHex = "#AABBCC"
        };

        vm.Apply();

        Assert.Equal(DirectWriteTargetPin.Do3, node.TargetPin);
        Assert.Equal(DirectWriteMode.PwmDuty, node.Mode);
        Assert.Equal(5000u, node.PwmFrequencyHz);
        Assert.Equal((ushort)10, node.DutyMin);
        Assert.Equal((ushort)90, node.DutyMax);
        Assert.Equal(80u, node.PulseWidthUs);
        Assert.Equal(DirectWriteTriggerMode.Continuous, node.TriggerMode);
        Assert.Equal("Duty", node.ChannelConnector.Title); // Connector title dynamically updated
    }

    [Fact]
    public void AvailableTargetPins_WhenHardwareHas2DigitalOutputs_OnlyExposesDo0AndDo1_AndClampsOutOfRangePin()
    {
        // Target hardware has only 2 DO (like Datalogger 4DI/2DO/4AI)
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do5, DirectWriteMode.DigitalOutput);
        var vm = new DirectWriteConfigDialogViewModel(node, maxDigitalOutputs: 2);

        Assert.Equal(2, vm.AvailableTargetPins.Count);
        var pinNames = vm.AvailableTargetPins.Select(p => p.DisplayName).ToList();
        Assert.Equal(["DO0", "DO1"], pinNames);

        // Subtitle must reflect actual hardware DO range
        Assert.Contains("DO0..DO1", vm.Subtitle);

        // Out of range pin DO5 must be clamped to Do0
        Assert.Equal(DirectWriteTargetPin.Do0, vm.TargetPin);
        Assert.True(vm.HasAvailablePins);
    }

    [Fact]
    public void AvailableTargetPins_WhenHardwareHas4DigitalOutputs_OnlyExposesDo0To3()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do2, DirectWriteMode.DigitalOutput);
        var vm = new DirectWriteConfigDialogViewModel(node, maxDigitalOutputs: 4);

        Assert.Equal(4, vm.AvailableTargetPins.Count);
        var pinNames = vm.AvailableTargetPins.Select(p => p.DisplayName).ToList();
        Assert.Equal(["DO0", "DO1", "DO2", "DO3"], pinNames);
        Assert.Contains("DO0..DO3", vm.Subtitle);
        Assert.Equal(DirectWriteTargetPin.Do2, vm.TargetPin);
    }

    [Fact]
    public void AvailableTargetPins_WhenHardwareHas1DigitalOutput_ShowsOnlyDo0()
    {
        var node = new DirectWriteNodeViewModel(DirectWriteTargetPin.Do0, DirectWriteMode.DigitalOutput);
        var vm = new DirectWriteConfigDialogViewModel(node, maxDigitalOutputs: 1);

        Assert.Single(vm.AvailableTargetPins);
        Assert.Equal("DO0", vm.AvailableTargetPins[0].DisplayName);
        Assert.Contains("DO0", vm.Subtitle);
        Assert.DoesNotContain("DO0..", vm.Subtitle);
    }

    [Theory]
    [InlineData("vi")]
    [InlineData("en")]
    public void DirectWrite_BilingualTooltips_AreFullyLocalized(string culture)
    {
        var originalLang = Services.LocalizationService.Instance.CurrentLanguage;
        try
        {
            Services.LocalizationService.Instance.CurrentLanguage = culture;

            var keys = new[]
            {
                "DirectWriteTargetPinTooltip",
                "DirectWriteModeTooltip",
                "DirectWritePwmFreqTooltip",
                "DirectWriteDutyRangeTooltip",
                "DirectWritePulseWidthTooltip",
                "DirectWriteTriggerModeTooltip",
                "DirectWriteActiveLevelTooltip",
                "DirectWriteFailSafeTooltip",
                "DirectWriteColorHexTooltip",
                "DirectWriteBtnApplyTooltip",
                "DirectWriteBtnCancelTooltip",
                "InspectorDirectWriteConfigureTooltip"
            };

            foreach (var key in keys)
            {
                var text = Services.LocalizationService.Tr(key);
                Assert.False(string.IsNullOrWhiteSpace(text), $"Key '{key}' must have non-empty translation for culture '{culture}'");
                Assert.False(text.StartsWith("!"), $"Key '{key}' must not be missing in culture '{culture}'");
            }
        }
        finally
        {
            Services.LocalizationService.Instance.CurrentLanguage = originalLang;
        }
    }
}
