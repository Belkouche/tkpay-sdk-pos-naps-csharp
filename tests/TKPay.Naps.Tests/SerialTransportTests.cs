using TKPay.Naps.Connection;
using TKPay.Naps.Models;
using Xunit;

namespace TKPay.Naps.Tests;

public class PortDiscoverySelectionTests
{
    [Fact]
    public void SelectIngenicoPort_PrefersVidMatch()
    {
        var candidates = new[]
        {
            new SerialPortCandidate("/dev/cu.usbmodemAAA", 0x1234),
            new SerialPortCandidate("/dev/cu.usbmodemBBB", PortDiscovery.IngenicoVendorId),
            new SerialPortCandidate("/dev/cu.usbserialCCC", 0x0403),
        };

        Assert.Equal("/dev/cu.usbmodemBBB", PortDiscovery.SelectIngenicoPort(candidates));
    }

    [Fact]
    public void SelectIngenicoPort_VidMatchWinsOverEarlierCdcAcm()
    {
        // A CDC-ACM device appears first, but the VID match must still be chosen.
        var candidates = new[]
        {
            new SerialPortCandidate("/dev/ttyACM0", null),
            new SerialPortCandidate("COM7", PortDiscovery.IngenicoVendorId),
        };

        Assert.Equal("COM7", PortDiscovery.SelectIngenicoPort(candidates));
    }

    [Fact]
    public void SelectIngenicoPort_FallsBackToCdcAcm_UsbModem()
    {
        var candidates = new[]
        {
            new SerialPortCandidate("/dev/cu.Bluetooth-Incoming", null),
            new SerialPortCandidate("/dev/cu.usbmodem1401", 0x2222),
        };

        Assert.Equal("/dev/cu.usbmodem1401", PortDiscovery.SelectIngenicoPort(candidates));
    }

    [Fact]
    public void SelectIngenicoPort_FallsBackToCdcAcm_TtyAcm()
    {
        var candidates = new[]
        {
            new SerialPortCandidate("/dev/ttyUSB0", 0x0403),
            new SerialPortCandidate("/dev/ttyACM0", 0x9999),
        };

        Assert.Equal("/dev/ttyACM0", PortDiscovery.SelectIngenicoPort(candidates));
    }

    [Fact]
    public void SelectIngenicoPort_NoMatch_ReturnsNull()
    {
        var candidates = new[]
        {
            new SerialPortCandidate("/dev/ttyUSB0", 0x0403),
            new SerialPortCandidate("COM1", null),
        };

        Assert.Null(PortDiscovery.SelectIngenicoPort(candidates));
    }

    [Fact]
    public void SelectIngenicoPort_EmptyList_ReturnsNull()
    {
        Assert.Null(PortDiscovery.SelectIngenicoPort(Array.Empty<SerialPortCandidate>()));
    }

    [Fact]
    public void IngenicoVendorId_Is0B00()
    {
        Assert.Equal(0x0B00, PortDiscovery.IngenicoVendorId);
        Assert.Equal(2816, PortDiscovery.IngenicoVendorId);
    }
}

public class TransportProtocolDefaultTests
{
    [Fact]
    public void Serial_DefaultsToV1()
    {
        var config = NapsConfig.Serial();
        Assert.Equal(TransportType.UsbSerial, config.Transport);
        Assert.Equal(ProtocolVersion.V1, config.ProtocolVersion);
        Assert.Equal('?', config.Terminator);
    }

    [Fact]
    public void Tcp_DefaultsToV2()
    {
        var config = new NapsConfig("192.168.1.100");
        Assert.Equal(TransportType.TcpSocket, config.Transport);
        Assert.Equal(ProtocolVersion.V2, config.ProtocolVersion);
        Assert.Equal('!', config.Terminator);
    }

    [Fact]
    public void Serial_ExplicitV2_Overrides()
    {
        var config = NapsConfig.Serial(protocolVersion: ProtocolVersion.V2);
        Assert.Equal(ProtocolVersion.V2, config.ProtocolVersion);
        Assert.Equal('!', config.Terminator);
    }

    [Fact]
    public void Tcp_ExplicitV1_Overrides()
    {
        var config = new NapsConfig("192.168.1.100", protocolVersion: ProtocolVersion.V1);
        Assert.Equal(ProtocolVersion.V1, config.ProtocolVersion);
        Assert.Equal('?', config.Terminator);
    }

    [Theory]
    [InlineData(TransportType.UsbSerial, ProtocolVersion.V1)]
    [InlineData(TransportType.TcpSocket, ProtocolVersion.V2)]
    public void ResolveDefaultProtocol_PerTransport(TransportType transport, ProtocolVersion expected)
    {
        Assert.Equal(expected, NapsConfig.ResolveDefaultProtocol(transport));
    }

    [Fact]
    public void Serial_Defaults_PortNameNull_Baud115200()
    {
        var config = NapsConfig.Serial();
        Assert.Null(config.SerialPortName);
        Assert.Equal(115200, config.SerialBaudRate);
    }

    [Fact]
    public void Serial_CustomPortAndBaud_Preserved()
    {
        var config = NapsConfig.Serial("/dev/cu.usbmodem1401", baudRate: 9600);
        Assert.Equal("/dev/cu.usbmodem1401", config.SerialPortName);
        Assert.Equal(9600, config.SerialBaudRate);
    }

    [Fact]
    public void Serial_BlankPortName_NormalizedToNull()
    {
        var config = NapsConfig.Serial("   ");
        Assert.Null(config.SerialPortName);
    }
}

public class SerialReadyWaitTests
{
    private static readonly DateTime Opened = new(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstFrameWaitsUntilPortOpen32Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(32), SerialConnection.RemainingWait(Opened, default, Opened, false));
        Assert.Equal(TimeSpan.FromSeconds(2), SerialConnection.RemainingWait(Opened, default, Opened.AddSeconds(30), false));
    }

    [Fact]
    public void NoWaitOncePortHasBeenOpen32Seconds()
    {
        Assert.Equal(TimeSpan.Zero, SerialConnection.RemainingWait(Opened, default, Opened.AddSeconds(32), false));
        Assert.Equal(TimeSpan.Zero, SerialConnection.RemainingWait(Opened, default, Opened.AddMinutes(10), false));
    }

    [Fact]
    public void ConfirmationWaitsHalfASecondAfterTheLastReply()
    {
        var lastRx = Opened.AddSeconds(50);
        Assert.Equal(TimeSpan.FromMilliseconds(500), SerialConnection.RemainingWait(Opened, lastRx, lastRx, true));
        Assert.Equal(TimeSpan.Zero, SerialConnection.RemainingWait(Opened, lastRx, lastRx.AddSeconds(1), true));
    }

    [Fact]
    public void NewOperationWaitsThreeSecondsAfterTheLastReply()
    {
        // Lane3600: a TM 003 right after TM 102, or a TM 001 0.5 s after TM 109, was lost.
        var lastRx = Opened.AddSeconds(50);
        Assert.Equal(TimeSpan.FromSeconds(3), SerialConnection.RemainingWait(Opened, lastRx, lastRx, false));
        Assert.Equal(TimeSpan.FromSeconds(2), SerialConnection.RemainingWait(Opened, lastRx, lastRx.AddSeconds(1), false));
        Assert.Equal(TimeSpan.Zero, SerialConnection.RemainingWait(Opened, lastRx, lastRx.AddSeconds(3), false));
    }

    [Theory]
    [InlineData("001003002003007", true)]
    [InlineData("001003004003007", true)]
    [InlineData("001003001002003", false)]
    [InlineData("001003003003007", false)]
    [InlineData("001003009003007", false)]
    public void IsPhase2_OnlyForPaymentAndVoidConfirmations(string frame, bool expected) =>
        Assert.Equal(expected, SerialConnection.IsPhase2(frame));
}
