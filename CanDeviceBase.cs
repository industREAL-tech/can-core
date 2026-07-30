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
        /// <c>canBaseID | function</c> with the function in the low nibble, so
        /// identity comparisons have to mask it off. Override if a device family
        /// uses a wider function field.
        /// </summary>
        protected virtual uint FunctionMask => 0x0Fu;

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
