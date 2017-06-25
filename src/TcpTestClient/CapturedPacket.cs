using System;

namespace TcpTestClient
{
    public class CapturedPacket
    {
        public CapturedPacket(byte[] packet)
        {
            Packet = packet;
            Time = DateTime.Now;
        }

        public byte[] Packet { get; }
        public DateTime Time { get; }
    }
}