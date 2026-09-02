namespace industREAL.CAN.CORE
{
    public abstract class CanControllerBase
    {
        public abstract Task ConnectAsync();

        public abstract void SendFrame(CanFrame frame);

        public event EventHandler<CanFrame>? FrameReceived;

        protected void OnFrameReceived(CanFrame frame) =>
            FrameReceived?.Invoke(this, frame);
    }
    
}
