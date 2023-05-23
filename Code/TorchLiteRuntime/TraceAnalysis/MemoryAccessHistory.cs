namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Memory access history.
    /// </summary>
    public class MemoryAccessHistory
    {
        private readonly LinkedList<MemoryAccess> history = new LinkedList<MemoryAccess>();

        private readonly HashSet<int> accessedByThreads = new HashSet<int>();
        private readonly HashSet<int> readByThreads = new HashSet<int>();
        private readonly HashSet<int> writtenByThreads = new HashSet<int>();

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryAccessHistory"/> class.
        /// </summary>
        /// <param name="access">Memory access instance.</param>
        public MemoryAccessHistory(MemoryAccess access)
        {
            this.Access = access;
            this.hasHappensBeforeConflicts = false;
        }

        /// <summary>
        /// Gets memory id for null value.
        /// </summary>
        public static string NullValue => "00000000-0000-0000-0000-000000000000";

        /// <summary>
        /// Gets or sets current memory access.
        /// </summary>
        public MemoryAccess Access { get; set; }

        /// <summary>
        /// Gets or sets whether current access has happens-before conflicts.
        /// </summary>
        public bool hasHappensBeforeConflicts { get; set; }

        /// <summary>
        /// Adds a memory access to the history.
        /// </summary>
        /// <param name="access">A memory access operation.</param>
        public void Add(MemoryAccess access)
        {
            this.history.AddLast(access);
            this.accessedByThreads.Add(access.ThreadId);

            if (access.AccessType == MemoryAccessType.Read || access.AccessType == MemoryAccessType.Use)
            {
                this.readByThreads.Add(access.ThreadId);
            }
            else if (access.AccessType == MemoryAccessType.Write || access.AccessType == MemoryAccessType.Dispose)
            {
                this.writtenByThreads.Add(access.ThreadId);
            }
        }

        /// <summary>
        /// Decides if the memory access is potentially racy.
        /// </summary>
        /// <returns>True, if potentially racy. False otherwise.</returns>
        public bool IsPotentialRace()
        {
            return this.accessedByThreads.Count() != this.writtenByThreads.Count();
        }

        /// <summary>
        /// Decides if two memory accesses are in happens-before relation (i.e., write -> read)
        /// if the vector clock of one is strictly a prefix of the other.
        /// </summary>
        public bool IsParentChildThreadRel(MemoryAccess write, MemoryAccess read)
        {
#if HAPPENSBEFORE_ENABLED
            if (write.VectorClock.StartsWith(read.VectorClock) || read.VectorClock.StartsWith(write.VectorClock))
            {
                return true;
            }
#endif
            return false;
        }

        /// <summary>
        /// Finds all potentially racy accesses for the current memory access.
        /// </summary>
        /// <returns>A list of racy accesses.</returns>
        public IEnumerable<RacyAccess> GetPotentialRacyAccesses()
        {
            var nonNullToNullWrites = this.history.Where(x =>
                x.AccessType == MemoryAccessType.Write
                && x.OldValue != NullValue
                && x.CurrentValue == NullValue);

            var disposes = this.history.Where(x =>
                x.AccessType == MemoryAccessType.Dispose);

            var nullToNonNullWrites = this.history.Where(x =>
                x.AccessType == MemoryAccessType.Write
                && x.OldValue == NullValue
                && x.CurrentValue != NullValue);

            var threadMap = new HashSet<int>();

            foreach (var write in nullToNonNullWrites)
            {
                var current = this.history.Find(write).Next;
                while (current != null)
                {
                    var access = current.Value;

                    int gap = (int)Math.Ceiling(1.0 * Math.Abs(write.Timestamp - access.Timestamp) / TimeSpan.TicksPerMillisecond);
                    if (gap > Constants.NearMissThresholdMs)
                    {
                        break;
                    }

                    // Check if (1) the two memory accesses happen on different threads/tasks, and
                    // (2) the two threads are not in HB relation based on their vector clocks
                    if (((access.ThreadId != write.ThreadId) || (access.TaskId != write.TaskId)) && !IsParentChildThreadRel(write, access))
                    {
                        if (access.AccessType == MemoryAccessType.Write && access.CurrentValue != NullValue)
                        {
                            break;
                        }

                        /*
                        if (IsCompilerGenerated(access) && IsCompilerGenerated(write))
                        {
                            break;
                        }
                        */

                        if (access.AccessType == MemoryAccessType.Use || (access.AccessType == MemoryAccessType.Read && access.CallerMethod.Contains("Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing.DiagnosticsEventListener::OnEventWritten")))
                        {
                            yield return new RacyAccess(access, ReadType.Use, write, WriteType.NullToNonNull);
                            // break;
                        }
                    }

                    if (IsParentChildThreadRel(write, access))
                    {
                        this.hasHappensBeforeConflicts = true;
                    }

                    current = current.Next;
                }
            }

            foreach (var write in nonNullToNullWrites)
            {
                var current = this.history.Find(write).Previous;
                while (current != null)
                {
                    var access = current.Value;

                    int gap = (int)Math.Ceiling(1.0 * Math.Abs(write.Timestamp - access.Timestamp) / TimeSpan.TicksPerMillisecond);
                    if (gap > Constants.NearMissThresholdMs)
                    {
                        break;
                    }

                    // Check if (1) the two memory accesses happen on different threads, and
                    // (2) the two threads are not in HB relation based on their vector clocks
                    if (((access.ThreadId != write.ThreadId) || (access.TaskId != write.TaskId)) && !IsParentChildThreadRel(write, access))
                    {
                        /*
                        if (IsCompilerGenerated(access) && IsCompilerGenerated(write))
                        {
                            break;
                        }
                        */

                        if ((access.AccessType == MemoryAccessType.Read || access.AccessType == MemoryAccessType.Use) && !threadMap.Contains(access.ThreadId))
                        {
                            threadMap.Add(access.ThreadId);
                            yield return new RacyAccess(access, access.AccessType.Equals(MemoryAccessType.Read) ? ReadType.Read : ReadType.Use, write, WriteType.NonNullToNull);
                        }
                    }

                    if (IsParentChildThreadRel(write, access))
                    {
                        this.hasHappensBeforeConflicts = true;
                    }

                    current = current.Previous;
                }
            }

            threadMap.Clear();
            foreach (var d in disposes)
            {
                var current = this.history.Find(d).Previous;
                while (current != null)
                {
                    var access = current.Value;

                    int gap = (int)Math.Ceiling(1.0 * Math.Abs(d.Timestamp - access.Timestamp) / TimeSpan.TicksPerMillisecond);
                    if (gap > Constants.NearMissThresholdMs)
                    {
                        break;
                    }

                    // Check if (1) the two memory accesses happen on different threads, and
                    // (2) the two threads are not in HB relation based on their vector clocks
                    if (((access.ThreadId != d.ThreadId) || (access.TaskId != d.TaskId)) && !IsParentChildThreadRel(d, access))
                    {
                        /*
                        if (IsCompilerGenerated(access) && IsCompilerGenerated(d))
                        {
                            break;
                        }
                        */

                        if ((access.AccessType == MemoryAccessType.Read || access.AccessType == MemoryAccessType.Use) && !threadMap.Contains(access.ThreadId))
                        {
                            threadMap.Add(access.ThreadId);
                            var accessType = access.AccessType == MemoryAccessType.Read ? ReadType.Read : ReadType.Use;
                            yield return new RacyAccess(access, accessType, d, WriteType.Dispose);
                        }
                    }

                    if (IsParentChildThreadRel(d, access) == true)
                    {
                        this.hasHappensBeforeConflicts = true;
                    }

                    current = current.Previous;
                }
            }
        }

        private bool IsCompilerGenerated(MemoryAccess access)
        {
            // If both memory access are inside a compiler-generated FSM (e.g., used in async/await constructs)
            // it is extrimely likely the threads are syncronized and this is not a data race
            if (access.CallerMethod.Contains("::MoveNext"))
            {
                return true;
            }

            return false;
        }
    }
}
