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

        protected CanDeviceBase(uint canBaseID, string nickName = "")
        {
            this.canBaseID = canBaseID;
            this.nickName = nickName;
            this.lastSeen = DateTime.MinValue;
        }

        public virtual void HandleMessage(CanFrame pFrame)
        {
            if (pFrame._canID != canBaseID) return;

            this.lastSeen = DateTime.UtcNow;
        }


        public virtual CanFrame? getPeriodicOutput()
        {
            return null;
        }

        public bool isAvailable()
        {
            return (DateTime.UtcNow - this.lastSeen) <= TimeSpan.FromSeconds(1);
        }

        //ADD RTR GET FIRMWARE AND HW VERSION
    }
}
