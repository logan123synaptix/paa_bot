using SimplePLC.Domain.Enums;

namespace SimplePLC.Application.Models;

/// <summary>
/// Điểm cuối (Endpoint) kết nối vật lý hoặc logic tới vi điều khiển PLC.
/// </summary>
public abstract record DeviceEndpoint
{
    public abstract string DisplayName { get; }
}

/// <summary>
/// Kết nối trực tiếp qua cổng Native USB CDC (TinyUSB) trên vi điều khiển STM32.
/// </summary>
public sealed record UsbCdcEndpoint(string PortName) : DeviceEndpoint
{
    public override string DisplayName => $"USB CDC ({PortName})";

    public override string ToString() => PortName;
}

/// <summary>
/// Kết nối tới thiết bị giả lập trong bộ nhớ (In-memory Simulator).
/// Có thể tùy biến năng lực phần cứng khởi tạo phù hợp với mục tiêu dự án.
/// </summary>
public sealed record SimulatorEndpoint(
    SPLC_HardwareCapabilities? InitialCapabilities = null,
    ushort? DeviceClass = null,
    ushort? DeviceVariant = null) : DeviceEndpoint
{
    public override string DisplayName => "SIMULATOR (VIRTUAL)";

    public override string ToString() => "SIMULATOR (VIRTUAL)";
}

/// <summary>
/// Kết nối tới vi điều khiển PLC qua mạng Modbus TCP (Ethernet / Wi-Fi).
/// </summary>
public sealed record TcpEndpoint(string Host, int Port = 502, int TimeoutMs = 3000) : DeviceEndpoint
{
    public override string DisplayName => $"Modbus TCP ({Host}:{Port})";

    public override string ToString() => $"{Host}:{Port}";
}
