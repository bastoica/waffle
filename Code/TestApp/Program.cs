using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp
{
    class Program
    {
        static void MainVectorClock(string[] args)
        {
            //AsyncLocalTest.Test();
            //TestLogicalClock.Test();
            TestLogicalClock.TestPatterns();
            Console.ReadKey();
        }

        static void Main(string[] args)
        {
            TestClass tc = new TestClass();
            tc.UseFields();

            new Thread(() =>
            {
                tc.UseFields();

                new Thread(() =>
                {
                    tc.UseFieldsInAnotherClass(new AnotherClass());

                    new Thread(() =>
                    {
                        tc.UseFieldsInAnotherClass2(new AnotherClass());
                    }).Start();


                }).Start();

            }).Start();

            new Thread(() =>
            {
                Thread.Sleep(1000);
                tc.UseFields2();

                new Thread(() =>
                {
                    tc.UseFieldsInAnotherClass(new AnotherClass());

                    new Thread(() =>
                    {
                        tc.UseFieldsInAnotherClass2(new AnotherClass());
                    }).Start();


                }).Start();
            }).Start();

            new Thread(() =>
            {
                Thread.Sleep(1000);
                tc.UseFields();

                new Thread(() =>
                {
                    tc.UseFieldsInAnotherClass(new AnotherClass());

                    new Thread(() =>
                    {
                        tc.UseFieldsInAnotherClass2(new AnotherClass());
                    }).Start();


                }).Start();
            }).Start();

            Thread.Sleep(5000);
        }

        /*
        static void Main(string[] args)
        {
            try
            {
                TestGeneric();
                TestToString1();
                TestToString2();
                Task t = Task.Run(() =>
                {
                    TestClass tc2 = new TestClass();
                    tc2.UseFields();
                    Thread.Sleep(1);
                    tc2.UseFieldsInAnotherClass(new AnotherClass());
                });

                TestClass tc = new TestClass();
                tc.UseFields();
                tc.UseFieldsInAnotherClass(new AnotherClass());
                t.Wait();

                // create object via newobj
                List<int> list = new List<int>(10);
                list.Add(10);

                //List<int> list2 = new List<int>();
                //list2.Add(10);

                //DateTime now = DateTime.Now;
                //Guid guid = Guid.NewGuid();

                //ClassWithGenerics<DateTime> testGeneric = new ClassWithGenerics<DateTime>("DateTime", DateTime.Now);
                //testGeneric.Test();

                ClassWithGenerics<List<int>> testGeneric = new ClassWithGenerics<List<int>>("List<int>", new List<int>());
                testGeneric.Test();

            }
            catch (Exception e)
            {
                Console.Error.WriteLine("This is a try-catch block");
            }

        }
        */
        

        static void TestToString1()
        {
            List<int> list = new List<int>();
            int i = 10;
            DateTime dt = DateTime.Now;
            Guid guid = Guid.NewGuid();

            Console.WriteLine(list.ToString());
            Console.WriteLine(i.ToString());
            Console.WriteLine(dt.ToString());
            Console.WriteLine(guid.ToString());
        }

        static void TestToString2()
        {
            List<int> list = new List<int>();
            int i = 10;
            DateTime dt = DateTime.Now;
            Guid guid = Guid.NewGuid();

            object o1 = list;
            OnStart(o1, "List.ToString()");
            Console.WriteLine(((List<int>)o1).ToString());

            object o2 = i;
            OnStart(o2, "Int.ToString()");
            Console.WriteLine(((int)o2).ToString());

            object o3 = dt;
            OnStart(o3, "Datetime.ToString()");
            Console.WriteLine(((DateTime)o3).ToString());

            object o4 = guid;
            OnStart(o4, "Guid.ToString()");
            Console.WriteLine(((Guid)o4).ToString());
        }

        static void TestGeneric()
        {
            GenericClass<string, int> gc = new GenericClass<string, int>();
            gc.T1Field = "hello";
            gc.T2Field = 10;
            gc.ListT2Field = new List<int>() { 1, 2, 3 };

            object o0 = gc.T1Field;
            object o1 = gc.T2Field;
            object o2 = gc.ListT2Field;

            GenericClass<string, int> gc2 = new GenericClass<string, int>();
            gc2.T1Field = (string)o0;
            gc2.T2Field = (int)o1;
            gc2.ListT2Field = (List<int>)o2;
        }

        static void OnStart(object instance, string api)
        {

        }
    }
}
