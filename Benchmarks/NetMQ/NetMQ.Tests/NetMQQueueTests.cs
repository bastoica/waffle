﻿#if !NET35
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetMQ.Sockets;
using Xunit;

namespace NetMQ.Tests
{
    public class NetMQQueueTests : IClassFixture<CleanupAfterFixture>
    {
        public NetMQQueueTests() => NetMQConfig.Cleanup();

        [Fact]
        public void EnqueueDequeue()
        {
            using (var queue = new NetMQQueue<int>())
            {
                queue.Enqueue(1);

                Assert.Equal(1, queue.Dequeue());
            }
        }

        [Fact]
        public void EnqueueShouldNotBlockWhenCapacityIsZero()
        {
            using (var mockSocket = new PairSocket())
            using (var queue = new NetMQQueue<int>())
            {
                int socketWatermarkCapacity = mockSocket.Options.SendHighWatermark + mockSocket.Options.ReceiveHighWatermark;

                Task task1 = Task.Run(() =>
                {
                    for (int i = 0; i < socketWatermarkCapacity + 100; i++)
                    {
                        queue.Enqueue(i);
                    }
                });

                Task task2 = Task.Run(() =>
                {
                    for (int i = 0; i < socketWatermarkCapacity + 100; i++)
                    {
                        queue.Dequeue();
                    }
                });

                bool completed = task1.Wait(TimeSpan.FromSeconds(30));
                //Assert.True(completed, "Enqueue task should have completed " + socketWatermarkCapacity + " enqueue within 25 seconds");
            }
        }

        [Fact]
        public void TryDequeue()
        {
            using (var queue = new NetMQQueue<int>())
            {
                Assert.False(queue.TryDequeue(out int result, TimeSpan.FromMilliseconds(100)));

                queue.Enqueue(1);

                Assert.True(queue.TryDequeue(out result, TimeSpan.FromMilliseconds(100)));
                Assert.Equal(1, result);
            }
        }

        [Fact]
        public void WithPoller()
        {
            using (var queue = new NetMQQueue<int>())
            using (var poller = new NetMQPoller { queue })
            {
                var manualResetEvent = new ManualResetEvent(false);

                queue.ReceiveReady += (sender, args) =>
                {
                    Assert.Equal(1, queue.Dequeue());
                    manualResetEvent.Set();
                };

                poller.RunAsync();

                Assert.False(manualResetEvent.WaitOne(100));
                queue.Enqueue(1);
                Assert.True(manualResetEvent.WaitOne(100));
            }
        }
    }
}
#endif
