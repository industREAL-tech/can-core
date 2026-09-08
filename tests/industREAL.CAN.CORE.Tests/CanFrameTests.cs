using industREAL.CAN.CORE;
using System.Collections.Generic;
using Xunit;

namespace industREAL.CAN.CORE.Tests;

public class CanFrameTests
{
    [Fact]
    public void Clone_DoesNotSharePayloadWithOriginal()
    {
        // Regression: Clone() was MemberwiseClone(), so the clone shared the
        // original's _data array and mutating either corrupted the other.
        var original = new CanFrame(CanDLC.DLC_8) { _canID = 0x123 };
        for (int i = 0; i < 8; i++) original._data[i] = (byte)(i + 1);

        var clone = (CanFrame)original.Clone();
        Assert.NotSame(original._data, clone._data);

        clone._data[0] = 0xFF;
        Assert.Equal(1, original._data[0]);

        original._data[7] = 0xAA;
        Assert.Equal(8, clone._data[7]);
    }

    [Fact]
    public void Clone_CopiesEveryField()
    {
        var original = new CanFrame(CanDLC.DLC_12)
        {
            _canID = 0x18FF50E5,
            _flags = CanFlags.ExtendedId | CanFlags.FdCanFormat,
            _frameID = 0xDEADBEEF,
            _channel = CanInterface.B,
        };
        for (int i = 0; i < 12; i++) original._data[i] = (byte)(0x40 + i);

        var clone = (CanFrame)original.Clone();

        Assert.Equal(original._canID, clone._canID);
        Assert.Equal(original._flags, clone._flags);
        Assert.Equal(original._frameID, clone._frameID);
        Assert.Equal(original._channel, clone._channel);
        Assert.Equal(original._dataLength, clone._dataLength);
        Assert.Equal(original._data, clone._data);
    }

    [Fact]
    public void Clone_PreservesFullBufferWhenDeclaredLengthWasReduced()
    {
        var original = new CanFrame(CanDLC.DLC_8);
        for (int i = 0; i < 8; i++) original._data[i] = (byte)(i + 1);
        original._dataLength = CanDLC.DLC_4;   // shorter than the allocation

        var clone = (CanFrame)original.Clone();

        Assert.Equal(CanDLC.DLC_4, clone._dataLength);
        Assert.Equal(8, clone._data.Length);
        Assert.Equal(original._data, clone._data);
    }

    [Fact]
    public void DataLength_MayBeReducedBelowTheAllocation()
    {
        var f = new CanFrame(CanDLC.DLC_8);
        f._dataLength = CanDLC.DLC_4;
        Assert.Equal(CanDLC.DLC_4, f._dataLength);
    }

    [Fact]
    public void DataLength_CannotExceedTheAllocation()
    {
        // Regression: the setter was unchecked and fully decoupled from _data, so
        // a serializer could be told to write more bytes than the frame owns.
        var f = new CanFrame(CanDLC.DLC_4);
        Assert.Throws<ArgumentOutOfRangeException>(() => f._dataLength = CanDLC.DLC_64);
        Assert.Equal(CanDLC.DLC_4, f._dataLength);
    }

    [Fact]
    public void Constructor_AllocatesExactlyTheRequestedLength()
    {
        foreach (var dlc in new[] { CanDLC.DLC_0, CanDLC.DLC_1, CanDLC.DLC_8,
                                    CanDLC.DLC_12, CanDLC.DLC_48, CanDLC.DLC_64 })
        {
            var f = new CanFrame(dlc);
            Assert.Equal((byte)dlc, f._data.Length);
            Assert.Equal(dlc, f._dataLength);
        }
    }

    private static readonly CanFlags[] HardwareBits =
        { CanFlags.RemoteFrame, CanFlags.ExtendedId, CanFlags.BrsOn, CanFlags.FdCanFormat };

    private static IEnumerable<CanFlags> AllHardwareCombinations()
    {
        for (int mask = 0; mask < (1 << 4); mask++)
        {
            CanFlags v = CanFlags.None;
            for (int i = 0; i < HardwareBits.Length; i++)
                if ((mask >> i & 1) != 0) v |= HardwareBits[i];
            yield return v;
        }
    }

    [Fact]
    public void ControlFlagCannotCollideWithAnyHardwareFlagCombination()
    {
        // This case was written against CONTROL = 0xCAFFEE, when the gateway
        // distinguished control frames by an exact flags == CONTROL test. The
        // member is now Control = 0x80000000, one reserved bit.
        foreach (CanFlags v in AllHardwareCombinations())
            Assert.NotEqual(CanFlags.Control, v);
    }

    [Fact]
    public void ControlFlagComposesWithHardwareFlagsInsteadOfReplacingThem()
    {
        // The point of the single bit, and what 0xCAFFEE actually got wrong. It
        // did not collide with any flag defined here, so nothing was misread as
        // FD or BRS -- but it was twenty bits, so HasFlag(CONTROL) demanded all
        // twenty back and any frame that had also picked up a real attribute
        // failed the test. One bit sets, tests and clears independently.
        foreach (CanFlags v in AllHardwareCombinations())
        {
            CanFlags withControl = v | CanFlags.Control;

            Assert.True(withControl.HasFlag(CanFlags.Control));
            foreach (CanFlags bit in HardwareBits)
                Assert.Equal(v.HasFlag(bit), withControl.HasFlag(bit));

            Assert.Equal(v, withControl & ~CanFlags.Control);
        }
    }

    [Fact]
    public void ClassPropertyReflectsTheControlFlag()
    {
        var f = new CanFrame(CanDLC.DLC_8) { _flags = CanFlags.ExtendedId };
        Assert.Equal(CanFrameClass.Data, f.Class);

        f.Class = CanFrameClass.Control;
        Assert.Equal(CanFrameClass.Control, f.Class);
        Assert.True(f._flags.HasFlag(CanFlags.ExtendedId));   // not clobbered

        f.Class = CanFrameClass.Data;
        Assert.Equal(CanFrameClass.Data, f.Class);
        Assert.Equal(CanFlags.ExtendedId, f._flags);
    }

    [Fact]
    public void CloneCarriesTheTimestampsAndStillDeepCopiesThePayload()
    {
        // The merge seam: the deep copy and the length guard came from one branch,
        // the hardware timestamps from another, and neither side's Clone knew
        // about the other's fields.
        var f = new CanFrame(CanDLC.DLC_8)
        {
            _canID = 0x0020006,
            _flags = CanFlags.ExtendedId,
            _channel = CanInterface.B,
            DeviceUs = 1234567,
            HostUtc = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
        };
        for (int i = 0; i < 8; i++) f._data[i] = (byte)(i + 1);
        f._dataLength = CanDLC.DLC_5;      // reduced below the allocation

        var c = (CanFrame)f.Clone();

        Assert.Equal(f._canID, c._canID);
        Assert.Equal(f._flags, c._flags);
        Assert.Equal(f._channel, c._channel);
        Assert.Equal(f.DeviceUs, c.DeviceUs);
        Assert.Equal(f.HostUtc, c.HostUtc);
        Assert.Equal(CanDLC.DLC_5, c._dataLength);   // reduced length preserved
        Assert.Equal(8, c._data.Length);             // whole buffer still copied
        Assert.Equal(f._data, c._data);

        c._data[0] = 0xFF;
        Assert.Equal(1, f._data[0]);                 // not aliased
    }
}
