using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimplePLC.Protocol.Enums;
using SimplePLC.Studio.Services;

namespace SimplePLC.Studio.ViewModels;

/// <summary>
/// ViewModel phục vụ hộp thoại cấu hình khối DIRECT_WRITE (PULSE_OUT).
/// Thiết kế chuẩn giao diện công nghiệp Fluent 2 / ISA-101 (Single Column, Contextual, Zero-Fluff).
/// </summary>
public partial class DirectWriteConfigDialogViewModel : ObservableObject
{
    public DirectWriteNodeViewModel Node { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private DirectWriteTargetPin _targetPin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    [NotifyPropertyChangedFor(nameof(IsDigitalOutputMode))]
    [NotifyPropertyChangedFor(nameof(IsPwmMode))]
    [NotifyPropertyChangedFor(nameof(IsPulseTrainMode))]
    [NotifyPropertyChangedFor(nameof(IsAddressableBusMode))]
    private DirectWriteMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    [NotifyPropertyChangedFor(nameof(TriggerDescriptionText))]
    private DirectWriteTriggerMode _triggerMode;

    // --- Mode 0: Digital Output (DO) Settings ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private bool _isActiveHigh = true;

    [ObservableProperty]
    private byte _failSafeState = 0;

    // --- Mode 1: PWM Duty Settings ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private uint _pwmFrequencyHz = 1000;

    [ObservableProperty]
    private ushort _dutyMin = 0;

    [ObservableProperty]
    private ushort _dutyMax = 100;

    // --- Mode 2: Pulse Train (PTO) Settings ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private uint _pulseWidthUs = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private uint _maxFrequencyHz = 20000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private ushort _fallbackPulses = 1000;

    // --- Mode 3: Addressable Actuator Bus Settings ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private ushort _channelCount = 18;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryBadgeText))]
    private bool _isExclusiveChannel = true;

    [ObservableProperty]
    private string _defaultColorHex = "#00FF00";

    public bool IsDigitalOutputMode => Mode == DirectWriteMode.DigitalOutput;
    public bool IsPwmMode => Mode == DirectWriteMode.PwmDuty;
    public bool IsPulseTrainMode => Mode == DirectWriteMode.PulseTrain;
    public bool IsAddressableBusMode => Mode == DirectWriteMode.AddressableBus;

    public string SummaryBadgeText
    {
        get
        {
            string pinName = FormatTargetPin(TargetPin);
            return Mode switch
            {
                DirectWriteMode.DigitalOutput => $"{pinName} | Digital Output (DO) | {(IsActiveHigh ? "Active-High" : "Active-Low")}",
                DirectWriteMode.PwmDuty => $"{pinName} | PWM | {PwmFrequencyHz} Hz ({DutyMin}%..{DutyMax}%)",
                DirectWriteMode.PulseTrain => $"{pinName} | PTO | {FallbackPulses} Pulses @ {PwmFrequencyHz} Hz ({PulseWidthUs} µs)",
                DirectWriteMode.AddressableBus => $"{pinName} | Bus | {ChannelCount} Ch" + (IsExclusiveChannel ? " (Exclusive)" : ""),
                _ => $"{pinName} | {Mode}"
            };
        }
    }

    public int MaxDigitalOutputs { get; }
    public bool HasAvailablePins => AvailableTargetPins.Count > 0;

    /// <summary>
    /// Mô tả chi tiết hành vi kích hoạt ngõ ra theo cơ chế được chọn.
    /// </summary>
    public string TriggerDescriptionText => TriggerMode switch
    {
        DirectWriteTriggerMode.Continuous => LocalizationService.Instance.IsVietnamese
            ? "Cập nhật liên tục bám theo chu kỳ quét (Cyclic)"
            : "Continuous cyclic output synchronous to PLC scan",
        DirectWriteTriggerMode.OnTriggerRisingEdge => LocalizationService.Instance.IsVietnamese
            ? "Kích hoạt phát xung khi có sườn lên chân [Trig]"
            : "Triggered on rising edge (0->1) of [Trig] pin",
        _ => string.Empty
    };

    public string Subtitle
    {
        get
        {
            bool isVi = LocalizationService.Instance.IsVietnamese;
            string rangeText = MaxDigitalOutputs switch
            {
                <= 0 => isVi ? "(Không có ngõ ra DO)" : "(No DO available)",
                1 => "DO0",
                _ => $"DO0..DO{Math.Min(7, MaxDigitalOutputs - 1)}"
            };

            return isVi
                ? $"Điều khiển bộ phát xung tốc độ cao, PWM và bus chấp hành thông minh trên {rangeText}"
                : $"High-speed pulse, PWM, PTO, and smart actuator bus on {rangeText}";
        }
    }

    public IReadOnlyList<OptionItem<DirectWriteTargetPin>> AvailableTargetPins { get; }

    public IReadOnlyList<OptionItem<DirectWriteMode>> AvailableModes { get; } = new List<OptionItem<DirectWriteMode>>
    {
        new(DirectWriteMode.DigitalOutput, LocalizationService.Tr("DirectWriteModeDo") ?? "Digital Output (DO)"),
        new(DirectWriteMode.PwmDuty, LocalizationService.Tr("DirectWriteModePwm") ?? "PWM Duty Cycle (PWM)"),
        new(DirectWriteMode.PulseTrain, LocalizationService.Tr("DirectWriteModePulse") ?? "Pulse Train Output (PTO)"),
        new(DirectWriteMode.AddressableBus, LocalizationService.Tr("DirectWriteModeBus") ?? "Addressable Actuator Bus")
    };

    public IReadOnlyList<OptionItem<DirectWriteTriggerMode>> AvailableTriggerModes { get; } = new List<OptionItem<DirectWriteTriggerMode>>
    {
        new(DirectWriteTriggerMode.Continuous, LocalizationService.Tr("DirectWriteTrigContinuous") ?? "Continuous Cyclic Output"),
        new(DirectWriteTriggerMode.OnTriggerRisingEdge, LocalizationService.Tr("DirectWriteTrigEdge") ?? "On Rising Edge (Trig)")
    };

    public IReadOnlyList<OptionItem<bool>> AvailableActiveLevels { get; } = new List<OptionItem<bool>>
    {
        new(true, LocalizationService.Tr("DirectWriteActiveHigh") ?? "Active High (1 = 24V ON, 0 = OFF)"),
        new(false, LocalizationService.Tr("DirectWriteActiveLow") ?? "Active Low (0 = 24V ON, 1 = OFF)")
    };

    public IReadOnlyList<OptionItem<byte>> AvailableFailSafeStates { get; } = new List<OptionItem<byte>>
    {
        new(0, LocalizationService.Tr("DirectWriteFailSafeOff") ?? "0 (OFF / De-energized safe)"),
        new(1, LocalizationService.Tr("DirectWriteFailSafeOn") ?? "1 (ON / Energized safe)")
    };

    public event Action<bool>? RequestClose;

    public DirectWriteConfigDialogViewModel(DirectWriteNodeViewModel node, int? maxDigitalOutputs = null)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));

        MaxDigitalOutputs = maxDigitalOutputs.HasValue && maxDigitalOutputs.Value >= 0
            ? maxDigitalOutputs.Value
            : ResolveHardwareDigitalOutputCount();

        var pins = new List<OptionItem<DirectWriteTargetPin>>();
        int limit = Math.Clamp(MaxDigitalOutputs, 0, 8);
        for (int i = 0; i < limit; i++)
        {
            var pinEnum = (DirectWriteTargetPin)i;
            pins.Add(new OptionItem<DirectWriteTargetPin>(pinEnum, $"DO{i}"));
        }
        AvailableTargetPins = pins;

        TargetPin = node.TargetPin;
        if ((int)TargetPin >= limit && limit > 0)
        {
            TargetPin = DirectWriteTargetPin.Do0;
        }

        Mode = node.Mode;
        TriggerMode = node.TriggerMode == DirectWriteTriggerMode.OnChangeOfValue
            ? DirectWriteTriggerMode.Continuous
            : node.TriggerMode;

        IsActiveHigh = node.IsActiveHigh;
        FailSafeState = node.FailSafeState;

        PwmFrequencyHz = Math.Max((uint)10, node.PwmFrequencyHz);
        DutyMin = node.DutyMin;
        DutyMax = Math.Max((ushort)1, node.DutyMax);

        PulseWidthUs = Math.Max((uint)1, node.PulseWidthUs);
        MaxFrequencyHz = Math.Max((uint)100, node.MaxFrequencyHz);
        FallbackPulses = node.ChannelCount > 0 ? node.ChannelCount : (ushort)1000;

        ChannelCount = Math.Max((ushort)1, node.ChannelCount);
        IsExclusiveChannel = node.IsExclusiveChannel;
        DefaultColorHex = $"#{node.DefaultColorRgb:X6}";
    }

    partial void OnModeChanged(DirectWriteMode value)
    {
        // Enforce sensible default TriggerMode per operating mode
        switch (value)
        {
            case DirectWriteMode.DigitalOutput:
            case DirectWriteMode.PwmDuty:
            case DirectWriteMode.AddressableBus:
                TriggerMode = DirectWriteTriggerMode.Continuous;
                break;
            case DirectWriteMode.PulseTrain:
                TriggerMode = DirectWriteTriggerMode.OnTriggerRisingEdge;
                break;
        }
    }

    public static int ResolveHardwareDigitalOutputCount()
    {
        var session = AppServices.Instance.SessionManager.CurrentSession;
        if (session?.ResourceInfo != null && session.ResourceInfo.DigitalOutputCount > 0)
        {
            return session.ResourceInfo.DigitalOutputCount;
        }

        var product = AppServices.Instance.CurrentProduct;
        if (product?.Resources != null && product.Resources.DigitalOutputs > 0)
        {
            return product.Resources.DigitalOutputs;
        }

        return 8;
    }

    public static string FormatTargetPin(DirectWriteTargetPin pin) => pin switch
    {
        DirectWriteTargetPin.Do0 => "DO0",
        DirectWriteTargetPin.Do1 => "DO1",
        DirectWriteTargetPin.Do2 => "DO2",
        DirectWriteTargetPin.Do3 => "DO3",
        DirectWriteTargetPin.Do4 => "DO4",
        DirectWriteTargetPin.Do5 => "DO5",
        DirectWriteTargetPin.Do6 => "DO6",
        DirectWriteTargetPin.Do7 => "DO7",
        _ => $"DO{(byte)pin}"
    };

    public void Apply()
    {
        Node.TargetPin = TargetPin;
        Node.Mode = Mode;
        Node.TriggerMode = TriggerMode;

        Node.IsActiveHigh = IsActiveHigh;
        Node.FailSafeState = FailSafeState;

        Node.PwmFrequencyHz = Math.Max((uint)10, PwmFrequencyHz);
        Node.DutyMin = DutyMin;
        Node.DutyMax = Math.Max((ushort)1, DutyMax);

        Node.PulseWidthUs = Math.Max((uint)1, PulseWidthUs);
        Node.MaxFrequencyHz = Math.Max((uint)100, MaxFrequencyHz);

        if (Mode == DirectWriteMode.PulseTrain)
        {
            Node.ChannelCount = FallbackPulses;
        }
        else
        {
            Node.ChannelCount = Math.Max((ushort)1, ChannelCount);
        }

        Node.IsExclusiveChannel = IsExclusiveChannel;

        var clean = DefaultColorHex.Trim().TrimStart('#');
        if (uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsedColor))
        {
            Node.DefaultColorRgb = parsedColor;
        }

        Node.UpdateConnectorTitles();
    }

    [RelayCommand]
    private void Save()
    {
        Apply();
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
