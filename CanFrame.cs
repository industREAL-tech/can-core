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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public sealed class CanFrame : ICloneable
    {
        public uint _canID { get; set; }
        public CanFlags _flags { get; set; }
        public UInt32 _frameID { get; set; }
        public CanDLC _dataLength { get; set; }
        public CanInterface _channel { get; set; }
        public byte[] _data { get; }

        //TEST

        public CanFrame(CanDLC dlc)
        {
            if ((byte)dlc > 64)
            {
                throw new ArgumentException("CAN frame can not exceed 64 byte!");
            }
            _data = new byte[(byte)dlc];
            this._dataLength = dlc;
            this._channel = CanInterface.A;
            this._frameID = 0; //TDOD: Write generator function
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
