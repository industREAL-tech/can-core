using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace industREAL.CAN.CORE
{
    public abstract class CanDeviceBase
    {
        public uint canBaseID { get; init; }
        public string? HardwareName { get; private set; }
        public float? HardwareVersion { get; private set; }
        public float? FirmwareVersion { get; private set; }
        public DateTime lastSeen { get; private set; }

        public string? nickName;

        /// <summary>
        /// How long after the last accepted frame the device still counts as
        /// present.
        /// </summary>
        public static readonly TimeSpan AvailabilityWindow = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Bits of the CAN identifier that carry the function code rather than
        /// the device address. Devices on this bus are addressed as
        /// <c>canBaseID | function</c>, so identity comparisons have to mask the
        /// function off. Override only for a family that provably uses fewer bits.
        /// </summary>
        /// <remarks>
        /// This was 0x0F -- a four-bit function field -- which no device family on
        /// this platform actually uses. Every base address is allocated on a 0x100
        /// stride (servo 0x20000, linear actuator 0x40100/0x40200/..., keypad
        /// 0x50100/0x50200), which only makes sense if the function occupies the
        /// low BYTE, and the SSDM-05 firmware confirms it: its receive filter
        /// accepts exactly <c>base | 0x00 .. 0xFF</c>, and it defines services from
        /// 0x00 up to 0x7F.
        ///
        /// With a four-bit mask, <see cref="Owns"/> rejected every service at or
        /// above 0x10 -- FirmwareVersion 0x10, ParamRead 0x11, ParamWrite 0x12,
        /// Store 0x13, and the generic Ack 0x7F -- as belonging to some other
        /// device. A host could send those requests and would then discard the
        /// replies, so parameter access and every acknowledged command silently
        /// timed out.
        ///
        /// Widening it cannot take frames away from another device: the next base
        /// is 0x100 higher either way.
        /// </remarks>
        protected virtual uint FunctionMask => 0xFFu;

        protected CanDeviceBase(uint canBaseID, string nickName = "")
        {
            this.canBaseID = canBaseID;
            this.nickName = nickName;
            this.lastSeen = DateTime.MinValue;
        }

        /// <summary>Function code carried in the low bits of <paramref name="pFrame"/>.</summary>
        protected uint FunctionOf(CanFrame pFrame) => pFrame._canID & FunctionMask;

        /// <summary>
        /// Does this frame belong to this device?
        /// </summary>
        /// <remarks>
        /// This used to be an exact <c>_canID != canBaseID</c> comparison, which
        /// no real frame could satisfy: every device ORs a function code into the
        /// low nibble, so the test always failed, <see cref="lastSeen"/> was never
        /// stamped, and <see cref="isAvailable"/> returned false forever for every
        /// device in the system.
        /// </remarks>
        public bool Owns(CanFrame pFrame)
        {
            if (pFrame is null) return false;
            uint mask = ~FunctionMask;
            return (pFrame._canID & mask) == (canBaseID & mask);
        }

        public virtual void HandleMessage(CanFrame pFrame)
        {
            if (!Owns(pFrame)) return;

            this.lastSeen = DateTime.UtcNow;
        }


        public virtual CanFrame? getPeriodicOutput()
        {
            return null;
        }

        public bool isAvailable()
        {
            return (DateTime.UtcNow - this.lastSeen) <= AvailabilityWindow;
        }

        //ADD RTR GET FIRMWARE AND HW VERSION
    }
}
