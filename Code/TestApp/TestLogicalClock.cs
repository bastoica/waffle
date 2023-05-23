using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using TorchLiteRuntime;

namespace TestApp
{
    class TestLogicalClock
    {
        static List<int> list;

        public static void TestPatterns()
        {
            Console.WriteLine($"Pattern 1===================");
            TestPattern1();
            Console.WriteLine($"Pattern 2===================");
            TestPattern2();
            Console.WriteLine($"Pattern 3===================");
            TestPattern3();
        }

        public static void TestPattern1()
        {
            list = new List<int>() { 1, 2, 3};
            Log("main thread, VC=[1]");

            Task t = Task.Factory.StartNew(() => {
                int y = list.Count();
                Log("child of main, VC=[1,1]");
                Task.WaitAll(
                    Task.Factory.StartNew(() => Log("child1 of child, VC=[1,1,1]")),
                    Task.Factory.StartNew(() => Log("child2 of child, VC=[1,1,2]")));
            });

            int x1 = list.Count();
            Log("main thread, before task join: VC=[1]");

            t.Wait();

            int x2 = list.Count();
            Log("main thead, after task join, VC=[1]");
        }

        static void TestPattern2()
        {
            list = new List<int>() { 1, 2, 3 };
            Log("field created in main thread");

            // Create an ActionBlock<int> that performs some work.
            var workerBlock = new ActionBlock<int>(_ => Foo());

            for (int i = 0; i < 5; i++)
            {
                workerBlock.Post(i);
            }
            workerBlock.Complete();

            // Wait for all messages to propagate through the network.
            workerBlock.Completion.Wait();

            int x = list.Count();
            Log("Field used within main thread");
        }


        static void Foo()
        {
            List<int> x = list;
            Log("Field used within Foo");
        }
        public static void TestPattern3()
        {
            Log("0");
            Task.WaitAll(
                Task.Run(() =>
                {
                    Log("0.1");
                    new Thread(() => Log("0.1.1")).Start();
                    Task.WaitAll(
                        Task.Run(() => Log("0.1.2")).ContinueWith(t => Task.Run(() => Log("0.1.3"))),
                        Task.Run(() => Log("0.1.4")),
                        Task.Run(() => Log("0.1.5"))
                    );
                }),
                Task.Run(() =>
                {
                    Log("0.2");
                    new Thread(() => Log("0.2.1")).Start();
                    Task.WaitAll(
                        Task.Run(() => Log("0.2.2"))
                            .ContinueWith(t => Task.Run(() => Log("0.2.3"))),
                        Task.Run(() => Log("0.2.4")),
                        Task.Run(() => Log("0.2.5"))
                    );
                }));

            Log("0");
        }

        static async Task SomeTask(string message)
        {
            Log(message);
            await Task.Delay(1000);
        }

        public static void Test2()
        {
            Console.WriteLine("Now:" + VectorClock.Now);
            Task.WhenAll(SomeWork("1"), SomeWork("2")).Wait();

            Console.ReadKey();
        }

        static void Log(string message)
        {
            Console.WriteLine($"{VectorClock.Now}: {message}");
        }
        static async Task SomeWork(string stackName)
        {
            Log($"<{stackName}-SomeWork>");
            await MoreWork("A");
            await MoreWork("B");
            Log($"</{stackName}-SomeWork>");
        }

        static async Task MoreWork(string stackName)
        {
            Log($"<{stackName}-MoreWork>");
            await Task.Delay(10);
            Log($"</{stackName}-MoreWork>");
        }
    }
}
