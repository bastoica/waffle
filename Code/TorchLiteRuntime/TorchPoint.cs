namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using global::TraceAnalysis;

    /// <summary>
    /// memory access runtime information.
    /// </summary>
    public class TorchPoint
    {

        /// <summary>
        /// Set of threads making the object access.
        /// </summary>
        private readonly HashSet<int> threadSet = new HashSet<int>();

        /// <summary>
        /// Set of threads making the object access.
        /// </summary>
        private readonly HashSet<int> taskSet = new HashSet<int>();

        /// <summary>
        /// TorchPoint constructor.
        /// </summary>
        public TorchPoint(long timestamp, MemoryAccessType opType, string parentObject, string fieldName, string callee, string caller, int ilOffset, Guid currentVal, Guid newVal)
        {
            this.Timestamp = timestamp;
            this.OpType = opType;
            this.ThreadId = Thread.CurrentThread.ManagedThreadId;
            this.TaskId = (Task.CurrentId != null) ? (int)Task.CurrentId : 0;
            this.ParentObject = parentObject;
            this.FieldName = fieldName;
            this.Callee = callee;
            this.Caller = caller;
            this.ILOffset = ilOffset;
            this.CurrentValue = currentVal;
            this.NewValue = newVal;
            this.isDelayed = false;
        }

        /// <summary>
        /// Gets or sets the vector clock.
        /// </summary>
        public string VectorClock { get; set; }

        /// <summary>
        /// Gets or sets the call context id.
        /// </summary>
        public string ParentObject { get; set; }
        
        /// <summary>
        /// Gets or sets the field name.
        /// </summary>
        public string FieldName { get; set; }

        /// <summary>
        /// Gets or sets the callee method.
        /// </summary>
        public string Callee { get; set; }

        /// <summary>
        /// Gets or sets the caller method.
        /// </summary>
        public string Caller { get; set; }

        /// <summary>
        /// Gets or sets IL offset.
        /// </summary>
        public int ILOffset { get; set; }

        /// <summary>
        /// Gets or sets IL offset.
        /// </summary>
        public Guid CurrentValue { get; set; }

        /// <summary>
        /// Gets or sets IL offset.
        /// </summary>
        public Guid NewValue { get; set; }

        /// <summary>
        /// Gets or sets the operation type.
        /// </summary>
        public MemoryAccessType OpType { get; set; }

        /// <summary>
        /// Gets or sets the assigned managed thread id.
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// Gets or sets the assigned managed task id.
        /// </summary>
        public int TaskId { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of when the memory access is hit.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the internalID for this instance.
        /// </summary>
        public int HitCount { get; set; }

        /// <summary>
        /// Gets or sets the stack trace.
        /// </summary>
        public string StackTrace { get; set; }

        /// <summary>
        /// Gets or sets a flag that indicates whether the Torch point is delayed.
        /// </summary>
        public bool isDelayed { get; set; }

        /// <summary>
        /// Returns the unique field ID based on the field name and parent object.
        /// </summary>
        /// <returns>System.string.</returns>
        public string GetObjectId() => $"{this.ParentObject}";

        /// <summary>
        /// Returns the unique field ID based on the field name and parent object.
        /// </summary>
        /// <returns>System.string.</returns>
        public string GetUniqueFieldId() => $"{this.ParentObject}{Constants.ObjectIdSeparator}{this.FieldName}";

        /// <summary>
        /// Returns the static code location.
        /// </summary>
        /// /// <returns>System.String.</returns>
        public string GetStaticCodeLoc() => this.Caller + "|" + this.ILOffset.ToString();

        /// <summary>
        /// Returns the static code location.
        /// </summary>
        /// /// <returns>System.String.</returns>
        public static string GetStaticCodeLoc(string caller, string ilOffset) => caller + "|" + ilOffset;

        /// <summary>
        /// Converts to string (w/ unique field ID).
        /// </summary>
        /// <returns>System.String.</returns>
        public string Serialze() => this.GetUniqueFieldId() + "\t" + this.Caller + "\t" + this.ILOffset.ToString() + "\t" +
                                        this.ThreadId.ToString() + "\t" + this.TaskId.ToString() + "\t" + this.HitCount.ToString();

        /// <summary>
        /// Converts to string (w/o unique field ID).
        /// </summary>
        /// <returns>System.String.</returns>
        public string SerializeWithFieldNameOnly() => this.FieldName + "|" + this.Caller + "|" + this.ILOffset.ToString() + "|" +
                                                        this.ThreadId.ToString() + "|" + this.TaskId.ToString() + "|" + this.HitCount.ToString();


        /// <summary>
        /// Adds the active thread ID of when the memory access is hit.
        /// </summary>
        public void AddToThreadSet(int tid)
        {
            this.threadSet.Add(tid);
        }

        /// <summary>
        /// Adds the active task ID of when the memory access is hit.
        /// </summary>
        public void AddToTaskSet(int tid)
        {
            this.taskSet.Add(tid);
        }

        /// <summary>
        /// Returns the caller portion from a static location.
        /// </summary>
        /// /// <returns>System.String.</returns>
        public static string ExtractCaller(string pLoc)
        {
            int index = pLoc.IndexOf("|");
            return pLoc.Substring(0, index);
        }

        /// <summary>
        /// Returns the ILOffset portion from a static location.
        /// </summary>
        /// /// <returns>System.String.</returns>
        public static string ExtractILOffset(string pLoc)
        {
            int index = pLoc.IndexOf("|");
            return pLoc.Substring(index + 1);
        }

        /// <summary>
        /// Returns method name only (no type).
        /// </summary>
        /// /// <returns>System.String.</returns>
        public static string ExtractMethodName(string caller)
        {
            int index = caller.LastIndexOf(":");
            return caller.Substring(index + 1);
        }
    }
}
