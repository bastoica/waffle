namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a memory access operation.
    /// </summary>
    public class DelayInjectionPoint
    {

        /// <summary>
        /// Gets or sets the delay value (in milliseconds).
        /// </summary>
        public long DelayMs { get; set; }

        /// <summary>
        /// Gets or sets memory id.
        /// </summary>
        public string MemoryId { get; set; }

        /// <summary>
        /// Gets or sets memory access type.
        /// </summary>
        public OperationType AccessType { get; set; }

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
        /// Gets or sets method that makes the memory access.
        /// </summary>
        public string CallerMethod { get; set; }

        /// <summary>
        /// Gets or sets iLOffset where the memory access happens.
        /// </summary>
        public int CallerILOffset { get; set; }

        /// <summary>
        /// Gets or sets original log entry corresponding to the memory access.
        /// </summary>
        public string LogLine { get; set; }

        /// <summary>
        /// Parse a runtime log line.
        /// </summary>
        /// <param name="line">A log line.</param>
        /// <returns>A memory access object, parsed from the log line.</returns>
        public static DelayInjectionPoint ParseLogLine(string line)
        {
            DelayInjectionPoint delayPoint = new DelayInjectionPoint();
            delayPoint.LogLine = line;

            string[] tokens = line.Split("\t".ToCharArray());

            try
            {
                string stripped = line.Remove(line.IndexOf($"{tokens[4]}\t")).Remove(line.IndexOf($"{tokens[5]}\t"));

                int i = -1;
                delayPoint.Timestamp = long.Parse(tokens[++i]);
                delayPoint.ThreadId = int.Parse(tokens[++i]);
                delayPoint.TaskId = int.Parse(tokens[++i]);

                //Logger.Log($"TsvdDelayInjection\t{targetPlan.DelayMs}\t{targetTp.OpType}\t{targetTp.GetUniqueFieldId()}\t{targetTp.Caller}\t{targetTp.ILOffset}"
                i = 4;
                delayPoint.DelayMs = long.Parse(tokens[++i]);
                Enum.TryParse(tokens[++i], out OperationType opType);
                delayPoint.AccessType = opType;
                delayPoint.MemoryId = tokens[++i];
                delayPoint.CallerMethod = tokens[++i];
                delayPoint.CallerILOffset = int.Parse(tokens[++i]);
            }
            catch
            {
                //System.Diagnostics.Debug.Assert(memoryAccess.MemoryId != null, "Malformed log line");
                return null;
            }

            return delayPoint;
        }
    }
}
