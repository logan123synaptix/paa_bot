using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using SimplePLC.Application.Enums;
using SimplePLC.Application.Models;
using SimplePLC.Domain.Builders;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;
using SimplePLC.Protocol.Enums;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services;
using SimplePLC.Studio.ViewModels;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Unit tests verifying hardware capability gating across device presets.
/// </summary>
[Collection("AppServicesDeployTests")]
public class HardwareCapabilityGatingTests
{
    [Fact]
    public void LogicEditor_CapabilityGating_RemoteIoDevice_DisablesMqtt_KeepsModbusMaster()
    {
        var remoteIoPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_RIO_884");
        var product = remoteIoPreset.BuildProductDefinition();

        var catalog = new TagCatalogViewModel();
        catalog.SyncWithProductDefinition(product);

        var editor = new LogicEditorViewModel(catalog, uiDispatcher: act => act());

        // Remote I/O không có chip mạng Ethernet/Wi-Fi -> khóa MQTT
        Assert.False(editor.IsMqttPublishSupported);
        Assert.False(editor.IsMqttSubscribeSupported);
        Assert.True(editor.MqttPublishDisabledTooltip.Contains("Yêu cầu") || editor.MqttPublishDisabledTooltip.Contains("Requires"));

        // Remote I/O có các cổng RS-485 khác -> hỗ trợ Modbus Master
        Assert.True(editor.IsModbusMasterSupported);
    }

    [Fact]
    public void LogicEditor_CapabilityGating_LegacyV1Device_DisablesModbusMaster()
    {
        var catalog = new TagCatalogViewModel();
        var resources = ProductResourceProfile.DefaultRemoteIo;
        var pureLogicProduct = DeviceProfileBuilder.Build(
            (ushort)SPLC_DeviceClass.REMOTE_IO,
            productVariant: 1,
            protocolVersion: 1,
            wireProfile: 1,
            maxRules: 100,
            runtimeTagCount: (ushort)resources.TotalTags,
            resources: resources,
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.RETENTIVE);

        catalog.SyncWithProductDefinition(pureLogicProduct);
        var editor = new LogicEditorViewModel(catalog, uiDispatcher: act => act());

        Assert.False(editor.IsModbusMasterSupported);
        Assert.True(editor.ModbusReadDisabledTooltip.Contains("Yêu cầu") || editor.ModbusReadDisabledTooltip.Contains("Requires"));
    }

    [Fact]
    public void LogicEditor_CapabilityGating_GatewayDevice_EnablesMqttAndModbus()
    {
        var gatewayPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_GW_220");
        var product = gatewayPreset.BuildProductDefinition();

        var catalog = new TagCatalogViewModel();
        catalog.SyncWithProductDefinition(product);

        var editor = new LogicEditorViewModel(catalog, uiDispatcher: act => act());

        Assert.True(editor.IsMqttPublishSupported);
        Assert.True(editor.IsMqttSubscribeSupported);
        Assert.True(editor.IsModbusMasterSupported);
    }

    [Fact]
    public void LogicEditor_CapabilityGating_DynamicSwitchPreset_UpdatesPropertiesLive()
    {
        var catalog = new TagCatalogViewModel();
        var editor = new LogicEditorViewModel(catalog, uiDispatcher: act => act());

        // Bắt đầu với Gateway (có MQTT & Modbus)
        var gatewayPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_GW_220");
        catalog.SyncWithProductDefinition(gatewayPreset.BuildProductDefinition());

        Assert.True(editor.IsMqttPublishSupported);
        Assert.True(editor.IsMqttSubscribeSupported);
        Assert.True(editor.IsModbusMasterSupported);

        // Chuyển sang Remote IO (không có mạng, nhưng có RS-485)
        var remoteIoPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_RIO_884");
        catalog.SyncWithProductDefinition(remoteIoPreset.BuildProductDefinition());

        Assert.False(editor.IsMqttPublishSupported);
        Assert.False(editor.IsMqttSubscribeSupported);
        Assert.True(editor.IsModbusMasterSupported);
    }

    [Fact]
    public void RuleCompiler_TargetDeviceLacksMqtt_EmitsDiagnosticWarning()
    {
        var compiler = new RuleCompiler();
        var nodes = new ObservableCollection<GraphNodeViewModel>();
        var connections = new ObservableCollection<ConnectionViewModel>();

        var diTag = new TagModel { Name = "DI0", Kind = TagKind.DiscreteInput, Index = 0 };
        var inNode = new InputNodeViewModel(diTag);
        var mqttNode = new MqttPublishNodeViewModel(diTag, "factory/telemetry");

        nodes.Add(inNode);
        nodes.Add(mqttNode);

        var catalog = new TagCatalogViewModel();
        catalog.AllTags.Add(diTag);

        var resources = ProductResourceProfile.DefaultRemoteIo;
        var productWithoutMqtt = DeviceProfileBuilder.Build(
            (ushort)SPLC_DeviceClass.REMOTE_IO,
            productVariant: 1,
            protocolVersion: 2,
            wireProfile: 2,
            maxRules: 100,
            runtimeTagCount: (ushort)resources.TotalTags,
            resources: resources,
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.RETENTIVE | SPLC_HardwareCapabilities.MODBUS_MASTER);

        var result = compiler.Compile(nodes, connections, catalog, productWithoutMqtt);

        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning &&
                                                (d.Message.Contains("MQTT Publish") || d.Message.Contains("MQTT")));
    }

    [Fact]
    public void RuleCompiler_TargetDeviceLacksModbusMaster_EmitsDiagnosticWarning()
    {
        var compiler = new RuleCompiler();
        var nodes = new ObservableCollection<GraphNodeViewModel>();
        var connections = new ObservableCollection<ConnectionViewModel>();

        var diTag = new TagModel { Name = "DI0", Kind = TagKind.DiscreteInput, Index = 0 };
        var inNode = new InputNodeViewModel(diTag);
        var mbNode = new ModbusReadNodeViewModel();

        nodes.Add(inNode);
        nodes.Add(mbNode);

        var catalog = new TagCatalogViewModel();
        catalog.AllTags.Add(diTag);

        var resources = ProductResourceProfile.DefaultRemoteIo;
        var productWithoutModbus = DeviceProfileBuilder.Build(
            (ushort)SPLC_DeviceClass.REMOTE_IO,
            productVariant: 1,
            protocolVersion: 1,
            wireProfile: 1,
            maxRules: 100,
            runtimeTagCount: (ushort)resources.TotalTags,
            resources: resources,
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.RETENTIVE);

        var result = compiler.Compile(nodes, connections, catalog, productWithoutModbus);

        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning &&
                                                (d.Message.Contains("Modbus Master") || d.Message.Contains("Modbus")));
    }

    [Fact]
    public void RemoteIo_WifiVariant_Preset_HasCorrectWifiAndModbusTcpCapabilities()
    {
        // Arrange & Act
        var wifiPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_RIO_440_WF");

        // Assert
        Assert.Equal((ushort)SPLC_DeviceClass.REMOTE_IO, (ushort)wifiPreset.DeviceClass);
        Assert.Equal((ushort)SPLC_RemoteIoVariant.VARIANT_RIO_440_WF, wifiPreset.DeviceVariant);
        Assert.Equal(4, wifiPreset.DigitalInputs);
        Assert.Equal(4, wifiPreset.DigitalOutputs);
        Assert.Equal(0, wifiPreset.AnalogInputs);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.WIFI) != 0);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.MODBUS_TCP) != 0);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.MODBUS_SLAVE) != 0);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.MODBUS_MASTER) != 0);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.MQTT_PUB) != 0);
        Assert.True((wifiPreset.HardwareCapabilities & SPLC_HardwareCapabilities.MQTT_SUB) != 0);

        var product = wifiPreset.BuildProductDefinition();
        Assert.True(product.SupportsWifi);
        Assert.True(product.SupportsMqttPublish);
        Assert.True(product.SupportsMqttSubscribe);
    }

    [Fact]
    public void MainViewModel_StartupWithoutProject_GatingHidesMqttAndPreservesModbus()
    {
        var mainVM = new MainViewModel(uiDispatcher: act => act());
        mainVM.StartupHubVM.ContinueBlankCommand.Execute(null);

        Assert.False(mainVM.LogicEditorVM.IsMqttCategoryVisible);
        Assert.False(mainVM.LogicEditorVM.IsMqttPublishSupported);
        Assert.False(mainVM.LogicEditorVM.IsMqttSubscribeSupported);
        Assert.True(mainVM.LogicEditorVM.IsModbusCategoryVisible);
        Assert.True(mainVM.LogicEditorVM.IsModbusMasterSupported);
    }

    [Fact]
    public void TagCatalogViewModel_BuildProductFromCurrentTags_DoesNotInjectFictitiousMqttCapabilities()
    {
        var catalog = new TagCatalogViewModel();
        var product = catalog.BuildProductFromCurrentTags(ProductDefinition.CreateRemoteIo8Di8Do4Ai());

        Assert.False(product.SupportsNetwork);
        Assert.False(product.SupportsMqttPublish);
        Assert.False(product.SupportsMqttSubscribe);
        Assert.True(product.SupportsModbusMaster);
    }

    [Fact]
    public void ProjectFileService_LoadRemoteIoProject_DoesNotRestoreFictitiousMqttCapabilities()
    {
        var mainVM = new MainViewModel(uiDispatcher: act => act());
        
        var project = new ProjectModel
        {
            Metadata = new ProjectMetadata
            {
                ProjectName = "TestRemoteIo",
                TargetDevice = "SynaptiX SYNX-RIO-884",
                DeviceClass = 1,
                ProductVariant = 1,
                HardwareCapabilities = (uint)(SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.RETENTIVE | SPLC_HardwareCapabilities.MODBUS_MASTER)
            },
            Tags = mainVM.TagCatalogVM.ExportToProjectData()
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_remoteio_{Guid.NewGuid():N}.synx");
        try
        {
            ProjectFileService.SaveProject(tempPath, project);
            mainVM.LoadProjectFile(tempPath);

            Assert.False(mainVM.LogicEditorVM.IsMqttCategoryVisible);
            Assert.False(mainVM.LogicEditorVM.IsMqttPublishSupported);
            Assert.False(mainVM.LogicEditorVM.IsMqttSubscribeSupported);
            Assert.True(mainVM.LogicEditorVM.IsModbusCategoryVisible);
            Assert.True(mainVM.LogicEditorVM.IsModbusMasterSupported);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void TopBar_WhenRemoteIoActive_DisablesModbusTcpAndProvidesHelpfulTooltip()
    {
        var mainVM = new MainViewModel(uiDispatcher: act => act());
        mainVM.StartupHubVM.ContinueBlankCommand.Execute(null);

        // Remote I/O mặc định (SYNX-RIO-884) không có Ethernet/Wi-Fi
        Assert.False(mainVM.IsModbusTcpSupported);
        Assert.False(mainVM.CanSelectModbusTcp);
        Assert.True(mainVM.ModbusTcpOptionToolTip.Contains("không hỗ trợ") || mainVM.ModbusTcpOptionToolTip.Contains("does not support"));

        // Cố gắng chuyển sang Modbus TCP phải bị từ chối
        mainVM.SelectTcpModeCommand.Execute(null);
        Assert.False(mainVM.IsTcpMode);
    }

    [Fact]
    public void TopBar_WhenWifiOrGatewayPresetLoaded_EnablesModbusTcp()
    {
        var prevProduct = AppServices.Instance.CurrentProduct;
        try
        {
            var mainVM = new MainViewModel(uiDispatcher: act => act());
            
            // Nạp cấu hình Gateway SYNX-GW-220 (hỗ trợ Ethernet + Wi-Fi)
            var gwPreset = HardwareDevicePreset.BuiltInPresets.First(p => p.Id == "SYNX_GW_220");
            AppServices.Instance.CurrentProduct = gwPreset.BuildProductDefinition();
            mainVM.TagCatalogVM.SyncWithProductDefinition(AppServices.Instance.CurrentProduct);
            mainVM.NotifyHardwareCapabilities();

            Assert.True(mainVM.IsModbusTcpSupported);
            Assert.True(mainVM.CanSelectModbusTcp);

            // Cho phép chuyển sang Modbus TCP
            mainVM.SelectTcpModeCommand.Execute(null);
            Assert.True(mainVM.IsTcpMode);
            Assert.Equal(LocalizationService.Tr("TooltipModbusTcpMode"), mainVM.ModbusTcpOptionToolTip);
        }
        finally
        {
            AppServices.Instance.CurrentProduct = prevProduct;
        }
    }

    [Fact]
    public void DeviceMenu_WhenRemoteIoActive_DisablesMqttWifiRtcAndProvidesTooltips()
    {
        var mainVM = new MainViewModel(uiDispatcher: act => act());
        mainVM.StartupHubVM.ContinueBlankCommand.Execute(null);

        // Menu thiết bị phải disable các tính năng không hỗ trợ
        Assert.False(mainVM.IsMqttSupported);
        Assert.False(mainVM.IsWifiSupported);
        Assert.False(mainVM.IsRtcClockSupported);

        // Tooltip phải giải thích nguyên nhân rõ ràng
        Assert.True(mainVM.MqttMenuToolTip.Contains("Yêu cầu") || mainVM.MqttMenuToolTip.Contains("Requires"));
        Assert.True(mainVM.WifiMenuToolTip.Contains("không trang bị") || mainVM.WifiMenuToolTip.Contains("not equipped"));
        Assert.True(mainVM.RtcClockMenuToolTip.Contains("không trang bị") || mainVM.RtcClockMenuToolTip.Contains("not equipped"));
    }

    [Fact]
    public async Task McuSimulator_DefaultRemoteIo884_DoesNotAdvertiseFictitiousNetworkCapabilities()
    {
        if (AppServices.Instance.LifecycleManager.CurrentState == ConnectionLifecycleState.Connected)
        {
            await AppServices.Instance.LifecycleManager.DisconnectAsync();
        }
        AppServices.Instance.ResetToDefaultConnectionFactory();
        var prevProduct = AppServices.Instance.CurrentProduct;
        AppServices.Instance.CurrentProduct = ProductDefinition.CreateRemoteIo8Di8Do4Ai();

        try
        {
            var mainVM = new MainViewModel(uiDispatcher: act => act());
            mainVM.StartupHubVM.ContinueBlankCommand.Execute(null);

            mainVM.SelectedPort = "SIMULATOR (VIRTUAL)";
            await mainVM.ToggleConnectCommand.ExecuteAsync(null);

            Assert.True(mainVM.IsConnected);
            Assert.False(mainVM.IsMqttSupported);
            Assert.False(mainVM.IsWifiSupported);
            Assert.False(mainVM.IsModbusTcpSupported);
            Assert.False(mainVM.IsRtcClockSupported);

            if (mainVM.IsConnected)
            {
                await mainVM.ToggleConnectCommand.ExecuteAsync(null);
            }
        }
        finally
        {
            if (AppServices.Instance.LifecycleManager.CurrentState == ConnectionLifecycleState.Connected)
            {
                await AppServices.Instance.LifecycleManager.DisconnectAsync();
            }
            AppServices.Instance.CurrentProduct = prevProduct;
        }
    }
}
