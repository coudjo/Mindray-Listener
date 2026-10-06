using System.IO.Ports;

namespace MindrayMiddleware
{
    public class SerialSettings
    {
        public string ComPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public Parity Parity { get; set; } = Parity.None;
        public StopBits StopBits { get; set; } = StopBits.One;
        public Handshake Handshake { get; set; } = Handshake.None;
        public int ReadTimeoutMs { get; set; } = 5000;
        public int WriteTimeoutMs { get; set; } = 5000;

        // How long to wait with no new bytes before treating a transmission as
        // complete. The analyzer gives no explicit "end of message" marker after
        // the histogram blocks, so completion is detected by a quiet period.
        public int InactivityTimeoutMs { get; set; } = 500;
    }
}