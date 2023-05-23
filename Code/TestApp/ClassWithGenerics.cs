using System;

namespace TestApp
{
    public class ClassWithGenerics<T>
    {
        public string InstanceName { get; set; }
        public T MemoryAccessType { get; }

        public ClassWithGenerics(string name, T opType)
        {
            InstanceName = name;
            MemoryAccessType = opType;
        }

        public void Test()
        {
            Console.WriteLine($"{InstanceName} {MemoryAccessType}");
            object value = MemoryAccessType;
            T tValue = (T)value;

        }

    }
}
