using industREAL.CAN.CORE;
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

    [Fact]
    public void ControlFlagCannotCollideWithAnyHardwareFlagCombination()
    {
        // The gateway distinguishes control frames by an exact flags == CONTROL
        // test, so no combination of the real HAL bits may equal it.
        CanFlags[] bits = { CanFlags.RemoteFrame, CanFlags.ExtendedId,
                            CanFlags.BrsOn, CanFlags.FdCanFormat };
        for (int mask = 0; mask < (1 << 4); mask++)
        {
            CanFlags v = CanFlags.None;
            for (int i = 0; i < bits.Length; i++)
                if ((mask >> i & 1) != 0) v |= bits[i];

            Assert.NotEqual(CanFlags.CONTROL, v);
        }
    }
}
