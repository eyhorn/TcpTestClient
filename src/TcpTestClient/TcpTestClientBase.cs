using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TcpTestClient
{
    public abstract class TcpTestClientBase : IDisposable
    {
        protected const int DefaultSendTimeout = 1000;
        private const int BufferSize = 1024;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly IList<PacketsWait> _packetWaits = new List<PacketsWait>();
        protected TcpClient Client;

        protected TcpTestClientBase()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            ConnectionEstablishedCompletionSource = new TaskCompletionSource<bool>();
        }

        public TaskCompletionSource<bool> ConnectionEstablishedCompletionSource { get; }
        public int PortNumber { get; protected set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            _cancellationTokenSource.Cancel();
            Client.Dispose();
            ClearPacketWaits();
        }

        protected void Run()
        {
            Task.Run(EstablishConnection, _cancellationTokenSource.Token)
                .ContinueWith(t => Stop());
        }

        protected virtual void Stop()
        {
        }

        private void ClearPacketWaits()
        {
            foreach (var packetsWait in _packetWaits)
                packetsWait.Dispose();
            _packetWaits.Clear();
        }

        public bool WaitForConnection(TimeSpan timeout)
        {
            return ConnectionEstablishedCompletionSource.Task.Wait(timeout);
        }

        public async Task WaitForDisconnect()
        {
            while (true)
                try
                {
                    var connectionIsNotActiveOrHasAvailableData = Client.Client.Poll(200, SelectMode.SelectRead);
                    var noDataIsAvailable = Client.Client.Available == 0;
                    if (connectionIsNotActiveOrHasAvailableData & noDataIsAvailable)
                        return;
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }
                catch
                {
                    return;
                }
        }

        public void SendMessage(byte[] message)
        {
            Client.Client.Send(message);
        }

        /// <summary>
        ///     Sends the specified message the specified number of times or indefinitely with a delay in between transmissions.
        /// </summary>
        /// <param name="message">The slave message to be sent</param>
        /// <param name="delay">The time to wait after sending a message</param>
        /// <param name="count">The number of messages to be sent. 0 - send indefinitely</param>
        /// <returns></returns>
        public async Task SendMessageAtInterval(byte[] message, TimeSpan delay, int count = 0)
        {
            var packetsSent = 0;
            while (!_cancellationTokenSource.IsCancellationRequested && Client.Connected &&
                   Client.Client.Connected
                   && (count == 0 || packetsSent < count))
            {
                SendMessage(message);
                try
                {
                    await Task.Delay(delay, _cancellationTokenSource.Token);
                    packetsSent++;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReceiveDataAsync()
        {
            var streamBuffer = new byte[BufferSize];
            var networkStream = Client.GetStream();

            while (!_cancellationTokenSource.IsCancellationRequested)
                try
                {
                    var bytesRead =
                        await
                            networkStream.ReadAsync(streamBuffer, 0, BufferSize,
                                _cancellationTokenSource.Token);
                    if (bytesRead == -1)
                        continue;

                    if (bytesRead == 0)
                        break;

                    var packet = new CapturedPacket(streamBuffer.Take(bytesRead).ToArray());
                    foreach (var packetsWait in _packetWaits)
                        packetsWait.ProcessPacket(packet);
                }
                catch (ObjectDisposedException)
                {
                    // We have been forcibly disconnected. 
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
        }

        public Task<ICollection<CapturedPacket>> WaitForPackets(Func<CapturedPacket, bool> predicate,
            int numberOfPacketsToWaitFor = 1)
        {
            var taskCompletionSource = new TaskCompletionSource<ICollection<CapturedPacket>>();
            var packetsWait = new PacketsWait(taskCompletionSource, predicate, numberOfPacketsToWaitFor);
            _packetWaits.Add(packetsWait);
            return taskCompletionSource.Task;
        }

        protected virtual Task Connect()
        {
            return Task.FromResult(default(object));
        }

        private async Task EstablishConnection()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    // As we cannot cancel this action, we will be closing the underlying
                    // connection to cause it to terminate. This is done on dispose and
                    // we'll catch the exception here.
                    await Connect();
                }
                catch (SocketException e)
                {
                    ConnectionEstablishedCompletionSource.TrySetException(e);
                    // Connection was closed
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // TCP connection was closed.
                    ConnectionEstablishedCompletionSource.TrySetCanceled();
                    return;
                }
                ConnectionEstablishedCompletionSource.TrySetResult(true);
                await ReceiveDataAsync();
            }
            Client?.Close();
        }


        private class PacketsWait : IDisposable
        {
            private readonly TaskCompletionSource<ICollection<CapturedPacket>> _completionSource;
            private readonly int _numberOfPacketsToWaitFor;
            private readonly IList<CapturedPacket> _packets;
            private readonly Func<CapturedPacket, bool> _predicate;
            private bool _isFinished;

            public PacketsWait(TaskCompletionSource<ICollection<CapturedPacket>> completionSource,
                Func<CapturedPacket, bool> predicate, int numberOfPacketsToWaitFor)
            {
                _completionSource = completionSource;
                _predicate = predicate;
                _numberOfPacketsToWaitFor = numberOfPacketsToWaitFor;
                _packets = new List<CapturedPacket>();
            }

            public void Dispose()
            {
                _completionSource.TrySetCanceled();
                _packets.Clear();
            }


            public void ProcessPacket(CapturedPacket packet)
            {
                if (_isFinished || !_predicate(packet)) return;

                _packets.Add(packet);

                if (_packets.Count < _numberOfPacketsToWaitFor) return;

                _completionSource.SetResult(_packets);
                _isFinished = true;
            }
        }
    }
}