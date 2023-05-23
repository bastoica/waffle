namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a memory access operation.
    /// </summary>
    public class MemoryAccess
    {
        private static Dictionary<int, int> threadLockDepth = new Dictionary<int, int>();

        /// <summary>
        /// Gets or sets memory id.
        /// </summary>
        public string MemoryId { get; set; }

        /// <summary>
        /// Gets or sets memory access type.
        /// </summary>
        public MemoryAccessType AccessType { get; set; }

        /// <summary>
        /// Gets or sets value of the memory before a write operation.
        /// </summary>
        public string OldValue { get; set; }

        /// <summary>
        /// Gets or sets value of the memory (after a write operation).
        /// </summary>
        public string CurrentValue { get; set; }

        /// <summary>
        /// Gets or sets timestamp of the memory access.
        /// </summary>
        public long Timestamp { get; set; }

        /// <summary>
        /// Gets or sets id of the thread accessing the memory.
        /// </summary>
        public int ThreadId { get; set; }

        /// <summary>
        /// Gets or sets id of the task (if any) accessing the memory.
        /// </summary>
        public int TaskId { get; set; }

        /// <summary>
        /// Gets or sets the vector clock value of the thread accessing the memory.
        /// </summary>
        public string VectorClock { get; set;  }

        /// <summary>
        /// Gets or sets method that makes the memory access.
        /// </summary>
        public string CallerMethod { get; set; }

        /// <summary>
        /// Gets or sets iLOffset where the memory access happens.
        /// </summary>
        public string CallerILOffset { get; set; }

        /// <summary>
        /// Gets or sets original log entry corresponding to the memory access.
        /// </summary>
        public string LogLine { get; set; }

        /// <summary>
        /// Gets or sets lock depth of the memory access.
        /// </summary>
        public int LockDepth { get; set; }

        /// <summary>
        /// Gets or sets the access counter for current object.
        /// </summary>
        public int PerObjectAccessCounter { get; set; }

        /// <summary>
        /// Gets or sets the overall access counter.
        /// </summary>
        public int GlobalAccessCounter { get; set; }

        /// <summary>
        /// Returns the field name that made the memory access (i.e., className::fieldName).
        /// </summary>
        public string GetFieldName() => MemoryId.Substring(MemoryId.IndexOf(Constants.ObjectIdSeparator) + 1);

        /// <summary>
        /// Returns the static program location of the current memory access.
        /// </summary>
        public string GetStaticLoc() => CallerMethod + "|" + CallerILOffset;

        /// <summary>
        /// Parse a runtime log line.
        /// </summary>
        /// <param name="line">A log line.</param>
        /// <returns>A memory access object, parsed from the log line.</returns>
        public static MemoryAccess ParseLogLine(string line)
        {
            MemoryAccess memoryAccess = new MemoryAccess();
            memoryAccess.LogLine = line;

            string[] tokens = line.Split("\t".ToCharArray());

            try
            {
                int i = -1;
                memoryAccess.Timestamp = long.Parse(tokens[++i]);
                memoryAccess.ThreadId = int.Parse(tokens[++i]);
                memoryAccess.TaskId = int.Parse(tokens[++i]);
                if (!threadLockDepth.ContainsKey(memoryAccess.ThreadId))
                {
                    threadLockDepth[memoryAccess.ThreadId] = 0;
                }
                memoryAccess.LockDepth = threadLockDepth[memoryAccess.ThreadId];
                memoryAccess.VectorClock = tokens[++i];
                string operationType = tokens[++i];

                //Console.WriteLine($"{memoryAccess.Timestamp}\t{memoryAccess.ThreadId}\t{memoryAccess.VectorClock}");

                switch (operationType)
                {
                    // Logger.Log($"BeforeFieldRead\t{uniqueFieldName}\t{objId}\t{caller}\t{ilOffset}");
                    case "BeforeFieldRead":
                        memoryAccess.AccessType = MemoryAccessType.Read;
                        memoryAccess.MemoryId = tokens[++i];
                        memoryAccess.CurrentValue = tokens[++i];
                        memoryAccess.OldValue = memoryAccess.CurrentValue;
                        memoryAccess.CallerMethod = tokens[++i];
                        memoryAccess.CallerILOffset = tokens[++i];
                        break;

                    // Logger.Log($"BeforeFieldWrite\t{uniqueFieldName}\t{currValueId}\t{newValueId}\t{caller}\t{ilOffset}");
                    case "BeforeFieldWrite":
                        memoryAccess.AccessType = MemoryAccessType.Write;
                        memoryAccess.MemoryId = tokens[++i];
                        memoryAccess.OldValue = tokens[++i];
                        memoryAccess.CurrentValue = tokens[++i];
                        memoryAccess.CallerMethod = tokens[++i];
                        memoryAccess.CallerILOffset = tokens[++i];
                        break;

                    // Logger.Log($"AfterFieldWrite\t{uniqueFieldName}\t{currValueId}\t{caller}\t{ilOffset}");
                    case "AfterFieldWrite":
                        memoryAccess.AccessType = MemoryAccessType.Write;
                        memoryAccess.MemoryId = tokens[++i];
                        memoryAccess.CurrentValue = tokens[++i];
                        memoryAccess.CallerMethod = tokens[++i];
                        memoryAccess.OldValue = memoryAccess.CurrentValue;
                        memoryAccess.CallerILOffset = tokens[++i];
                        break;

                    // Logger.Log($"BeforeMethodCall\t{FieldNameDict[objId]}\t{callee}\t{caller}\t{ilOffset}");
                    case "BeforeMethodCall":
                        memoryAccess.AccessType = MemoryAccessType.Use;
                        memoryAccess.MemoryId = tokens[++i];
                        var calleeMethod = tokens[++i];
                        memoryAccess.CallerMethod = tokens[++i];
                        memoryAccess.CallerILOffset = tokens[++i];

                        if (calleeMethod == "System.Threading.Monitor::Enter")
                        {
                            threadLockDepth[memoryAccess.ThreadId]++;
                            memoryAccess.AccessType = MemoryAccessType.Lock;
                        }
                        else if (calleeMethod == "System.Threading.Monitor::Exit")
                        {
                            threadLockDepth[memoryAccess.ThreadId]--;
                            memoryAccess.AccessType = MemoryAccessType.Lock;
                        }

                        break;
                    case "AfterMethodCall":
                        memoryAccess.MemoryId = tokens[++i];
                        var callee = tokens[++i];
                        memoryAccess.CallerMethod = tokens[++i];
                        memoryAccess.CallerILOffset = tokens[++i];
                        memoryAccess.OldValue = tokens[++i];
                        memoryAccess.AccessType = callee.EndsWith("::Dispose") ? MemoryAccessType.Dispose : MemoryAccessType.None;
                        break;
                }
            }
            catch
            {
                //System.Diagnostics.Debug.Assert(memoryAccess.MemoryId != null, "Malformed log line");
                return null;
            }

            return memoryAccess;
        }
    }
}
