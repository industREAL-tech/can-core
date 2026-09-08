using industREAL.CAN.CORE;
using Xunit;

namespace industREAL.CAN.CORE.Tests;

public class CanDeviceBaseTests
{
    /// <summary>Stand-in for a real driver: addressed as canBaseID | function.</summary>
    private sealed class TestDevice : CanDeviceBase
    {
        public int Handled;
        public TestDevice(uint baseId) : base(baseId) { }
        public override void HandleMessage(CanFrame f)
        {
            base.HandleMessage(f);
            if (Owns(f)) Handled++;
        }
        public new uint FunctionOf(CanFrame f) => base.FunctionOf(f);
    }

    /// <summary>A family that provably uses a narrower function field, to prove the
    /// override still works now that the low byte is the default.</summary>
    private sealed class NarrowFunctionDevice : CanDeviceBase
    {
        public NarrowFunctionDevice(uint baseId) : base(baseId) { }
        protected override uint FunctionMask => 0x0Fu;
    }

    private static CanFrame Frame(uint id) => new CanFrame(CanDLC.DLC_8) { _canID = id };

    [Fact]
    public void OwnsFramesCarryingAnyFunctionCodeOnItsBaseId()
    {
        // Regression: the base compared _canID != canBaseID exactly. Every device
        // ORs a function code into the low nibble, so the comparison never matched
        // any real frame.
        var dev = new TestDevice(0x0020000);

        foreach (uint fn in new uint[] { 0x0, 0x1, 0x2, 0x6, 0xF })
            Assert.True(dev.Owns(Frame(0x0020000 | fn)), $"function 0x{fn:X} rejected");
    }

    [Fact]
    public void OwnsTheWholeLowByte()
    {
        // The function field is the low BYTE, not the low nibble: the SSDM-05
        // firmware's receive filter accepts exactly base | 0x00 .. 0xFF, and its
        // services run past 0x10 (FirmwareVersion) up to 0x7F (generic Ack).
        //
        // This case previously asserted the OPPOSITE -- that a device based at
        // 0x20000 does not own 0x20010 -- which is where the four-bit mask and the
        // 0x10 base-ID stride agreed with each other and both were wrong. 0x20010
        // is this device's FirmwareVersion reply.
        var dev = new TestDevice(0x0020000);

        foreach (uint fn in new uint[] { 0x10, 0x11, 0x12, 0x13, 0x18, 0x7F, 0xFF })
            Assert.True(dev.Owns(Frame(0x0020000 | fn)), $"service 0x{fn:X} rejected");
    }

    [Fact]
    public void DoesNotOwnAnotherDevicesFrames()
    {
        // The next base is 0x100 away -- which is why widening the function field
        // to a byte cannot steal frames from a correctly addressed neighbour.
        var dev = new TestDevice(0x0020000);
        Assert.False(dev.Owns(Frame(0x0020100)));
        Assert.False(dev.Owns(Frame(0x0020200)));
        Assert.False(dev.Owns(Frame(0x0030000)));
    }

    [Fact]
    public void TwoDevicesSpacedByTenAliasEachOther()
    {
        // Why base IDs must be 0x100-aligned, as an executable statement rather than
        // a comment. Spaced by 0x10 -- as the retired ServoCanBaseID enum did -- the
        // two devices claim the same frames, and the one based lower claims the
        // other's telemetry as its own service replies.
        var low = new TestDevice(0x0020000);
        var high = new TestDevice(0x0020010);

        var stateReportOfHigh = Frame(0x0020010);          // high | StateReport 0x00
        Assert.True(high.Owns(stateReportOfHigh));
        Assert.True(low.Owns(stateReportOfHigh));          // ... and so does low

        var firmwareReplyOfLow = Frame(0x0020000 | 0x10);  // low | FirmwareVersion
        Assert.Equal(stateReportOfHigh._canID, firmwareReplyOfLow._canID);
    }

    [Fact]
    public void DoesNotOwnNull()
    {
        Assert.False(new TestDevice(0x100).Owns(null!));
    }

    [Fact]
    public void FunctionOfExtractsTheLowByte()
    {
        var dev = new TestDevice(0x0020000);
        Assert.Equal(0x6u, dev.FunctionOf(Frame(0x0020006)));
        Assert.Equal(0x0u, dev.FunctionOf(Frame(0x0020000)));
        Assert.Equal(0x11u, dev.FunctionOf(Frame(0x0020011)));   // ParamRead
        Assert.Equal(0x7Fu, dev.FunctionOf(Frame(0x002007F)));   // generic Ack
    }

    [Fact]
    public void HandleMessageStampsLivenessSoIsAvailableBecomesTrue()
    {
        // Regression: because the ownership test never matched, lastSeen was never
        // stamped and isAvailable() returned false for the lifetime of the process.
        var dev = new TestDevice(0x0020000);
        Assert.False(dev.isAvailable());
        Assert.Equal(DateTime.MinValue, dev.lastSeen);

        dev.HandleMessage(Frame(0x0020000 | 0x06));

        Assert.True(dev.isAvailable());
        Assert.Equal(1, dev.Handled);
        Assert.NotEqual(DateTime.MinValue, dev.lastSeen);
    }

    [Fact]
    public void HandleMessageIgnoresFramesForOtherDevices()
    {
        // 0x20100, not 0x20010: the latter is this device's own FirmwareVersion
        // reply. Using it here was the same four-bit assumption as the ownership
        // test, and it is the reason "another device" and "another service" were
        // indistinguishable.
        var dev = new TestDevice(0x0020000);
        dev.HandleMessage(Frame(0x0020100));

        Assert.False(dev.isAvailable());
        Assert.Equal(0, dev.Handled);
        Assert.Equal(DateTime.MinValue, dev.lastSeen);
    }

    [Fact]
    public void IsAvailableGoesFalseOnceTheWindowExpires()
    {
        var dev = new TestDevice(0x0020000);
        dev.HandleMessage(Frame(0x0020000));
        Assert.True(dev.isAvailable());

        Thread.Sleep(CanDeviceBase.AvailabilityWindow + TimeSpan.FromMilliseconds(150));
        Assert.False(dev.isAvailable());
    }

    [Fact]
    public void ANarrowerFunctionMaskNarrowsOwnership()
    {
        var dev = new NarrowFunctionDevice(0x0020000);
        Assert.True(dev.Owns(Frame(0x002000F)));
        Assert.False(dev.Owns(Frame(0x0020010)));
        Assert.False(dev.Owns(Frame(0x00200FF)));
    }

    [Fact]
    public void GetPeriodicOutputDefaultsToNull()
    {
        Assert.Null(new TestDevice(0x100).getPeriodicOutput());
    }
}
