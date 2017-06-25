using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TcpTestClient
{
    public class TcpTestClient : TcpTestClientBase
    {
        public TcpTestClient(int portNumber)
        {
            PortNumber = portNumber;
            Run();
        }

        protected override async Task Connect()
        {
            Client = new TcpClient();
            Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            Client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            Client.Client.SendTimeout = DefaultSendTimeout;

            await Client.ConnectAsync(IPAddress.Loopback, PortNumber);
        }
    }
}