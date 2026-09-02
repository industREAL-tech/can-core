using System;

namespace industREAL.CAN.CORE
{
    #region TYPES
    public enum CanInterface : byte
    {
        A = 0,
        B = 1
    }

    [Flags]
    public enum CanFlags : uint
    {
        None        = 0x00000000,

        // Frame type
        RemoteFrame = 0x20000000,

        // ID type
        ExtendedId  = 0x40000000,

        // Bit rate switching
        BrsOn       = 0x00100000,

        // Format
        FdCanFormat = 0x00200000,

        // Reserved-bit marker for control frames. Earlier versions encoded
        // this as 0xCAFFEE which overlapped FdCanFormat and BrsOn — every
        // control frame appeared as if FD+BRS. Use a single high reserved
        // bit so HasFlag remains correct for the real frame attributes.
        Control     = 0x80000000,
    }

    public enum CanFrameClass : byte
    {
        Data    = 0,
        Control = 1,
    }
    public enum CanDLC : byte
    {
        DLC_0 = 0,
        DLC_1 = 1,
        DLC_2 = 2,
        DLC_3 = 3,
        DLC_4 = 4,
        DLC_5 = 5,
        DLC_6 = 6,
        DLC_7 = 7,
        DLC_8 = 8,
        DLC_12 = 12,
        DLC_16 = 16,
        DLC_20 = 20,
        DLC_24 = 24,
        DLC_32 = 32,
        DLC_48 = 48,
        DLC_64 = 64
    }
    #endregion TYPES

    public sealed class CanFrame : ICloneable
    {
        public uint         _canID       { get; set; }
        public CanFlags     _flags       { get; set; }
        public UInt32       _frameID     { get; set; }
        public CanDLC       _dataLength  { get; set; }
        public CanInterface _channel     { get; set; }
        public byte[]       _data        { get; }

        /// Device monotonic microseconds at the moment the frame was
        /// observed by the gateway hardware (RX) or queued for TX.
        /// Zero for frames constructed locally without a stamp.
        public long         DeviceUs     { get; set; }

        /// Host wall-clock timestamp converted from <see cref="DeviceUs"/>
        /// via the driver's clock-sync filter. Nullable so locally-built
        /// frames are unambiguous.
        public DateTime?    HostUtc      { get; set; }

        public CanFrameClass Class
        {
            get => (_flags & CanFlags.Control) != 0 ? CanFrameClass.Control : CanFrameClass.Data;
            set => _flags = value == CanFrameClass.Control
                ? (_flags | CanFlags.Control)
                : (_flags & ~CanFlags.Control);
        }

        public CanFrame(CanDLC dlc)
        {
            if ((byte)dlc > 64)
            {
                throw new ArgumentException("CAN frame can not exceed 64 byte!");
            }
            _data            = new byte[(byte)dlc];
            this._dataLength = dlc;
            this._channel    = CanInterface.A;
            this._frameID    = 0;
        }

        /// Deep copy: the data buffer is duplicated so mutating the clone
        /// does not affect the original frame.
        public object Clone()
        {
            CanFrame copy = new CanFrame(_dataLength)
            {
                _canID    = _canID,
                _flags    = _flags,
                _frameID  = _frameID,
                _channel  = _channel,
                DeviceUs  = DeviceUs,
                HostUtc   = HostUtc,
            };
            Array.Copy(_data, copy._data, _data.Length);
            return copy;
        }
    }
}
