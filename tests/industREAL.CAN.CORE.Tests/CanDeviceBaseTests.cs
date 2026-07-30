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

    private sealed class WideFunctionDevice : CanDeviceBase
    {
        public WideFunctionDevice(uint baseId) : base(baseId) { }
        protected override uint FunctionMask => 0xFFu;
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
    public void DoesNotOwnAnotherDevicesFrames()
    {
        var dev = new TestDevice(0x0020000);
        Assert.False(dev.Owns(Frame(0x0020010)));
        Assert.False(dev.Owns(Frame(0x0030000)));
    }

    [Fact]
    public void DoesNotOwnNull()
    {
        Assert.False(new TestDevice(0x100).Owns(null!));
    }

    [Fact]
    public void FunctionOfExtractsTheLowNibble()
    {
        var dev = new TestDevice(0x0020000);
        Assert.Equal(0x6u, dev.FunctionOf(Frame(0x0020006)));
        Assert.Equal(0x0u, dev.FunctionOf(Frame(0x0020000)));
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
        var dev = new TestDevice(0x0020000);
        dev.HandleMessage(Frame(0x0020010));

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
    public void AWiderFunctionMaskWidensOwnership()
    {
        var dev = new WideFunctionDevice(0x0020000);
        Assert.True(dev.Owns(Frame(0x00200FF)));
        Assert.False(dev.Owns(Frame(0x0020100)));
    }

    [Fact]
    public void GetPeriodicOutputDefaultsToNull()
    {
        Assert.Null(new TestDevice(0x100).getPeriodicOutput());
    }
}
