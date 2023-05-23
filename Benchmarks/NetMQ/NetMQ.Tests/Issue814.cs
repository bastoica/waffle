using System;
using System.Threading;
using Xunit;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetMQTest
{
    public class DataRaceIssue814 : IDisposable
    {

        private DealerSocket requestDealerSocket;
        private PairSocket requestForwarderSocket;

        private CancellationTokenSource cts = new CancellationTokenSource();
        private bool disposed;

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            if (disposing)
            {
                Thread.Sleep(1000);
                cts.Cancel();              // Allows NetMQRuntime.Run() to return
                cts.Dispose();
            }

            disposed = true;
        }

        [Fact]
        public void Issue814Start()
        {
            // System.Diagnostics.Debugger.Launch();

            var task = Task.Run(() =>
            {
                using (requestDealerSocket = new DealerSocket())
                using (var eventDealerSocket = new DealerSocket())
                using (requestForwarderSocket = new PairSocket("@inproc://request"))
                using (var requestInitiatorSocket = new PairSocket(">inproc://request"))
                using (var netMqRuntime = new NetMQRuntime())
                {
                    netMqRuntime.Run(cts.Token, ForwardRequestToServerAsync(cts.Token), ListenForRequestAckFromServerAsync(cts.Token));
                }
                // NetMQRuntime disposed here

            });

            Task.WaitAll();
        }

        async Task ForwardRequestToServerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await requestForwarderSocket.ReceiveFrameBytesAsync();
                NetMQMessage message = new NetMQMessage();
                // populate message
                requestDealerSocket.SendMultipartMessage(message);
            }
        }

        async Task ListenForRequestAckFromServerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await ReceiveMultipartMessageBytesAsync(requestDealerSocket);
                // do work
            }
        }

        async Task<List<byte[]>> ReceiveMultipartMessageBytesAsync(DealerSocket socket)
        {
            List<byte[]> message = new List<byte[]>();
            (byte[], bool) response;

            do
            {
                response = await socket.ReceiveFrameBytesAsync();
                message.Add(response.Item1);
            }
            while (response.Item2);

            return message;
        }
    }
}