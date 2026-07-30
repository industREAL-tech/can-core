using System.Runtime.InteropServices;

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
        None = 0x00000000,

        // Frame type
        RemoteFrame = 0x20000000,

        // ID type
        ExtendedId = 0x40000000,

        // Bit rate switching
        BrsOn = 0x00100000,

        // Format
        FdCanFormat = 0x00200000,

        CONTROL = 0xCAFFEE
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

    /// <summary>
    /// One CAN / CAN-FD frame.
    /// </summary>
    /// <remarks>
    /// The <c>StructLayout</c> attribute is decorative: this is a managed class
    /// with properties and a reference-typed payload, so it is never blitted.
    /// Serialization is done explicitly by the transport driver.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public sealed class CanFrame : ICloneable
    {
        private CanDLC _length;

        public uint _canID { get; set; }
        public CanFlags _flags { get; set; }
        public UInt32 _frameID { get; set; }

        /// <summary>
        /// Declared payload length. <see cref="_data"/> is allocated once at
        /// construction and is the authoritative buffer, so this may be reduced
        /// to send a shorter frame but never raised above the buffer: doing so
        /// used to let a serializer advertise more bytes than the frame owns and
        /// read past the payload.
        /// </summary>
        public CanDLC _dataLength
        {
            get => _length;
            set
            {
                if ((byte)value > _data.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(_dataLength),
                        $"Cannot declare {(byte)value} bytes on a frame allocated for {_data.Length}.");
                }
                _length = value;
            }
        }

        public CanInterface _channel { get; set; }
        public byte[] _data { get; }

        public CanFrame(CanDLC dlc)
        {
            if ((byte)dlc > 64)
            {
                throw new ArgumentException("CAN frame can not exceed 64 byte!");
            }
            _data = new byte[(byte)dlc];
            this._length = dlc;
            this._channel = CanInterface.A;
            this._frameID = 0; //TDOD: Write generator function
        }

        /// <summary>
        /// Deep copy. <c>MemberwiseClone</c> alone left the clone sharing the
        /// original's <see cref="_data"/> array, so mutating either frame's
        /// payload silently corrupted the other - and this is the only copy
        /// primitive the type offers, so anything that cloned to hand a frame to
        /// another thread was aliasing it instead.
        /// </summary>
        public object Clone()
        {
            // Size the clone from the BUFFER, not from _dataLength: the declared
            // length may have been reduced below the allocation, and the clone
            // must still be able to hold the whole payload.
            var copy = new CanFrame((CanDLC)_data.Length)
            {
                _canID   = _canID,
                _flags   = _flags,
                _frameID = _frameID,
                _channel = _channel,
            };
            Buffer.BlockCopy(_data, 0, copy._data, 0, _data.Length);
            copy._length = _length;
            return copy;
        }
    }
}
