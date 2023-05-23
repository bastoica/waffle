namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Runtime.Remoting.Messaging;
    using System.Threading;

    /// <summary>
    /// Implements a logical clock to track happens-before relationship between parent-child threads/tasks.
    /// </summary>
    public class VectorClockRef
    {
        public VectorClockRef parentRef;
        public string vectorClock;

        public VectorClockRef()
        {
            vectorClock = "1";
        }

        private void IncTimestamp()
        {
            vectorClock += ".0";
        }

        public void MergeClocks()
        {
            if (parentRef != null)
            {
                vectorClock = parentRef.vectorClock + "." + vectorClock;
                parentRef.IncTimestamp();
            }
        }
    }

    /// <summary>
    /// Implements a logical clock to track happens-before relationship between parent-child threads/tasks.
    /// </summary>
    public class VectorClock
    {
        private static readonly string Name = Guid.NewGuid().ToString("N");
        private static readonly string InitFlag = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets the logical clock value of the current task/thread.
        /// </summary>
        public static string Now
        {
            get
            {
                InitThreadIfNeeded();
                return ThreadContext.vectorClock;
            }
        }

        /// <summary>
        /// Initializes the logical clock for the current thread if it has not been initialized already.
        /// </summary>
        public static void InitThreadIfNeeded()
        {
            if (CallContext.GetData(InitFlag) == null)
            {
                CallContext.SetData(InitFlag, "1");

                VectorClockRef currentThread = new VectorClockRef();
                if (ThreadContext != null)
                {
                    currentThread.parentRef = ThreadContext;
                    currentThread.MergeClocks();
                }
                ThreadContext = currentThread;
            }
        }

        private static VectorClockRef ThreadContext
        {
            get
            {
                var ret = CallContext.LogicalGetData(Name) as MarshalWrapper;
                return ret == null ? null : ret.Value;
            }

            set
            {
                CallContext.LogicalSetData(Name, new MarshalWrapper { Value = value });
            }
        }

        private sealed class MarshalWrapper : MarshalByRefObject
        {
            public VectorClockRef Value { get; set; }
        }
    }
}