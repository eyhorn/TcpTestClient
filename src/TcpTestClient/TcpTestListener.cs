using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TcpTestClient
{
    public class TcpTestListener : TcpTestClientBase
    {
        private readonly TcpListener _tcpListener;

        public TcpTestListener(int portNumber)
        {
            PortNumber = portNumber;
            _tcpListener = StartListener(PortNumber);
            Run();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            base.Dispose(true);
            _tcpListener.Stop();
        }

        protected override async Task Connect()
        {
            Client = await _tcpListener.AcceptTcpClientAsync();
        }

        protected override void Stop()
        {
            _tcpListener.Stop();
        }

        private static TcpListener StartListener(int portNumber)
        {
            var listener = new TcpListener(IPAddress.Loopback, portNumber);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            listener.Start();
            return listener;
        }
    }
}