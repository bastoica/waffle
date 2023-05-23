using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute.Exceptions;
using NUnit.Framework;

namespace NSubstitute.Acceptance.Specs
{
    public interface ICallback
    {
        void FirstCall(int value);
        void LastCall(int value);
    }

    class Dispatcher
    {
        private static int VALUE = -1;
        public static void Dispatch(ICallback callback)
        {
            Task.Factory.StartNew(() => {
                var val = Interlocked.Increment(ref VALUE);
                callback.FirstCall(val);
                callback.LastCall(val);
            });
        }
    }

    public class FailureTest
    {
        private AutoResetEvent firstCall;
        private AutoResetEvent lastCall;
        private ICallback callback;

        [SetUp]
        public void BeginTest()
        {
            firstCall = new AutoResetEvent(false);
            lastCall = new AutoResetEvent(false);

            callback = Substitute.For<ICallback>();
            callback.WhenForAnyArgs(
                obj => obj.FirstCall(0)).Do(args => {
                    firstCall.Set();
                });
            callback.WhenForAnyArgs(
                obj => obj.LastCall(0)).Do(args => {
                    lastCall.Set();
                });
        }

        [Test]
        public void Test()
        {
            for (int i = 0; i < 1000; ++i)
            {
                Dispatcher.Dispatch(callback);

                Assert.IsTrue(firstCall.WaitOne(TimeSpan.FromSeconds(10)));
                callback.Received(i + 1).FirstCall(Arg.Any<int>());

                Assert.IsTrue(lastCall.WaitOne(TimeSpan.FromSeconds(10)));
                callback.Received(i + 1).LastCall(Arg.Any<int>());
            }
        }
    }
}
