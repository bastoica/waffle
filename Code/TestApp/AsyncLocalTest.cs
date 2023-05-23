using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    class AsyncLocalTest
    {
        public static void Test()
        {
            var cc = "callcontext";
            var tl = "theadlocal";

            MyThreadLocal.SetData("tl", tl);
            MyCallContext.SetData("cc", cc);
            Task.Run(() =>
                {
                    Console.WriteLine((string)MyThreadLocal.GetData("tl"));
                    Console.WriteLine((string)MyCallContext.GetData("cc"));
                    MyThreadLocal.SetData("tl", "garbageTL");
                    MyCallContext.SetData("cc", "garbageCC");
                }).Wait();

            Console.WriteLine((string)MyThreadLocal.GetData("tl"));
            Console.WriteLine((string)MyCallContext.GetData("cc"));
            MyThreadLocal.SetData("tl", "newTL");
            MyCallContext.SetData("cc", "newCC");

            Task.Run(() =>
            {
                Console.WriteLine((string)MyThreadLocal.GetData("tl"));
                Console.WriteLine((string)MyCallContext.GetData("cc"));
                MyThreadLocal.SetData("tl", "garbageTL");
                MyCallContext.SetData("cc", "garbageCC");
            }).Wait();

            Console.WriteLine((string)MyThreadLocal.GetData("tl"));
            Console.WriteLine((string)MyCallContext.GetData("cc"));


            var d1 = new object();
            var t1 = default(object);
            var t10 = default(object);
            var t11 = default(object);
            var t12 = default(object);
            var t13 = default(object);

            var d2 = new object();
            var t2 = default(object);
            var t20 = default(object);
            var t21 = default(object);
            var t22 = default(object);
            var t23 = default(object);




            Task.WaitAll(
                Task.Run(() =>
                {
                    MyCallContext.SetData("d1", d1);
                    new Thread(() => t10 = MyCallContext.GetData("d1")).Start();
                    Task.WaitAll(
                        Task.Run(() => t1 = MyCallContext.GetData("d1"))
                            .ContinueWith(t => Task.Run(() => t11 = MyCallContext.GetData("d1"))),
                        Task.Run(() => t12 = MyCallContext.GetData("d1")),
                        Task.Run(() => t13 = MyCallContext.GetData("d1"))
                    );
                }),
                Task.Run(() =>
                {
                    MyCallContext.SetData("d2", d2);
                    new Thread(() => t20 = MyCallContext.GetData("d2")).Start();
                    Task.WaitAll(
                        Task.Run(() => t2 = MyCallContext.GetData("d2"))
                            .ContinueWith(t => Task.Run(() => t21 = MyCallContext.GetData("d2"))),
                        Task.Run(() => t22 = MyCallContext.GetData("d2")),
                        Task.Run(() => t23 = MyCallContext.GetData("d2"))
                    );
                })
            );

            Console.WriteLine(d1 == t1);
            Console.WriteLine(d1 == t10);
            Console.WriteLine(d1 == t11);
            Console.WriteLine(d1 == t12);
            Console.WriteLine(d1 == t13);

            Console.WriteLine(d2 == t2);
            Console.WriteLine(d2 == t20);
            Console.WriteLine(d2 == t21);
            Console.WriteLine(d2 == t22);
            Console.WriteLine(d2 == t23);

            Console.WriteLine(MyCallContext.GetData("d1") == null);
            Console.WriteLine(MyCallContext.GetData("d2") == null);

            Console.ReadKey();
        }

    }

    /// <summary>
    /// Provides a way to set contextual data that flows with the call and 
    /// async context of a test or invocation.
    /// </summary>
    public static class MyCallContext
    {
        static ConcurrentDictionary<string, AsyncLocal<object>> alState = new ConcurrentDictionary<string, AsyncLocal<object>>();
        static ConcurrentDictionary<string, ThreadLocal<object>> tlState = new ConcurrentDictionary<string, ThreadLocal<object>>();

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void LogicalSetData(string name, object data) =>
            alState.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object LogicalGetData(string name) =>
            alState.TryGetValue(name, out AsyncLocal<object> data) ? data.Value : null;

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            tlState.GetOrAdd(name, _ => new ThreadLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name) =>
            tlState.TryGetValue(name, out ThreadLocal<object> data) ? data.Value : null;

    }

    public static class MyThreadLocal
    {
        static ConcurrentDictionary<string, ThreadLocal<object>> state = new ConcurrentDictionary<string, ThreadLocal<object>>();

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            state.GetOrAdd(name, _ => new ThreadLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name) =>
            state.TryGetValue(name, out ThreadLocal<object> data) ? data.Value : null;
    }

}
