namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Runtime log analyzer.
    /// </summary>
    public class ConflictsAnalyzer
    {
        private readonly Dictionary<string, int> perObjectAccessCounter = new Dictionary<string, int>();
        private readonly Dictionary<string, int> globalAccessCounter = new Dictionary<string, int>();

        private Dictionary<string, MemoryAccessHistory> execTraceWithHistory = new Dictionary<string, MemoryAccessHistory>();

        private readonly long MAX_OVERLAP_WINDOW_TICKS = 10 * TimeSpan.TicksPerMillisecond; // 1ms

        public class PerDelayStats
        {
            public bool isValid;
            public string uniqueId;
            public WriteType PointType;
            public long DelayMs = 0;
            public long MinGapMs = long.MaxValue;
            public long MaxGapMs = 0;
            public int PotentialRacyPairs = 0;
            public int DelaysInjected = 0;
            public bool hasInterference = false;
        }

        enum RaceType
        {
            Init,
            Dispose,
            None,
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConflictsAnalyzer"/> class.
        /// </summary>
        /// <param name="logFile">A runtime log file.</param>
        public ConflictsAnalyzer(string logFile)
        {
            using (TextReader tr = new StreamReader(logFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {
                    MemoryAccess access = MemoryAccess.ParseLogLine(line);

                    if (access == null || access.MemoryId == null)
                    {
                        continue;
                    }
                    
                    // Remove memory accesses to static objects
                    if (isStaticAccess(access) == true)
                    {
                        continue;
                    }

                    if (!this.execTraceWithHistory.ContainsKey(access.MemoryId))
                    {
                        this.execTraceWithHistory[access.MemoryId] = new MemoryAccessHistory(access);
                    }

                    // Update access counters
                    access.PerObjectAccessCounter = this.perObjectAccessCounter.ContainsKey(access.MemoryId) ? ++this.perObjectAccessCounter[access.MemoryId] : this.perObjectAccessCounter[access.MemoryId] = 1;
                    access.GlobalAccessCounter = this.globalAccessCounter.ContainsKey(access.GetStaticLoc()) ? ++this.globalAccessCounter[access.GetStaticLoc()] : this.globalAccessCounter[access.GetStaticLoc()] = 1;

                    // Add to trace
                    this.execTraceWithHistory[access.MemoryId].Add(access);
                }
            }
        }

        /// <summary>
        /// Serialize potential races to a file.
        /// </summary>
        /// <param name="races">Race object.</param>
        /// <param name="outputFile">Output file.</param>
        public static void SerializeRaces(List<RacyAccess> races, string outputFile)
        {
            if (outputFile == null || races == null || races.Count == 0)
            {
                return;
            }

            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)WritePerObjectAccessCount\t(7)WriteGlobalAccessCount\t(8)ReadTimestamp\t(9)ReadType\t(10)ReadCaller\t(11)ReadILOffset\t(12)ReadPerObjectAccessCount\t(13)WriteGlobalAccessCount");
                foreach (var race in races)
                {
                    string fieldName = ExcludeObjectID(race.Write.MemoryId);
                    string line = $"{fieldName}\t{race.AccessGapMs}\t{race.Write.Timestamp}\t{race.WriteType}\t{race.Write.CallerMethod}\t{race.Write.CallerILOffset}\t{race.Write.PerObjectAccessCounter}\t{race.Write.GlobalAccessCounter}\t{race.Read.Timestamp}\t{race.ReadType}\t{race.Read.CallerMethod}\t{race.Read.CallerILOffset}\t{race.Read.PerObjectAccessCounter}\t{race.Read.GlobalAccessCounter}";
                    tw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Deserialize potential races.
        /// </summary>
        /// <param name="raceFile">Race file.</param>
        /// <returns>A list of potential races.</returns>
        public static List<RacyAccess> DeserializeRaces(string raceFile)
        {
            List<RacyAccess> races = new List<RacyAccess>();
            using (TextReader tr = new StreamReader(raceFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    // "#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)WritePerObjectAccessCount\t(7)WriteGlobalAccessCount\t(8)ReadTimestamp\t(9)ReadType\t(10)ReadCaller\t(11)ReadILOffset\t(12)ReadPerObjectAccessCount\t(13)WriteGlobalAccessCount"
                    string[] tokens = line.Split("\t".ToCharArray());
                    MemoryAccess write = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Write, CallerMethod = tokens[4], CallerILOffset = tokens[5] };
                    MemoryAccess read = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Read, CallerMethod = tokens[10], CallerILOffset = tokens[11] };
                    var writeType = (WriteType)Enum.Parse(typeof(WriteType), tokens[3]);
                    var readType = (ReadType)Enum.Parse(typeof(ReadType), tokens[7]);
                    var race = new RacyAccess(read, readType, write, writeType);
                    race.AccessGapMs = int.Parse(tokens[1]);
                    race.Write.Timestamp = long.Parse(tokens[2]);
                    race.Write.PerObjectAccessCounter = int.Parse(tokens[6]);
                    race.Write.GlobalAccessCounter = int.Parse(tokens[7]);
                    race.Read.Timestamp = long.Parse(tokens[8]);
                    race.Read.PerObjectAccessCounter = int.Parse(tokens[12]);
                    race.Read.GlobalAccessCounter = int.Parse(tokens[13]);
                    races.Add(race);
                }
            }

            return races;
        }

        /// <summary>
        /// Compute potentially racy fields.
        /// </summary>
        public void ComputePotentiallyRacyFields()
        {
            foreach (var objectId in this.execTraceWithHistory.Keys)
            {
                if (this.execTraceWithHistory[objectId].IsPotentialRace())
                {
                    Console.WriteLine(objectId);
                }
            }
        }

        /// <summary>
        /// Computes potential races.
        /// </summary>
        /// <returns>List of potential races.</returns>
        public List<RacyAccess> ComputePotentialRacyPairs()
        {
            List<RacyAccess> races = new List<RacyAccess>();
            foreach (var objectId in this.execTraceWithHistory.Keys)
            {
                races.AddRange(this.execTraceWithHistory[objectId].GetPotentialRacyAccesses());
            }

            return races;
        }

        /// <summary>
        /// Removes instances with time gap larger than a threshold.
        /// </summary>
        public void PruneRacyPairsWithLargeTimeGap(List<RacyAccess> races, int maxTimeGapMs)
        {
            for (int index = races.Count - 1; index >= 0; --index)
            {
                var race = races[index];

                if (race.AccessGapMs > maxTimeGapMs)
                {
                    races.RemoveAt(index);
                }
            }
        }
        /*public void PruneRacyPairsWithLargeTimeGap(List<RacyAccess> races, int maxTimeGapMs)
        {
            HashSet<string> toRemove = new HashSet<string>();

            foreach (var race in races)
            {
                if (race.AccessGapMs > maxTimeGapMs)
                {
                    string uniqueId = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;
                    toRemove.Add(uniqueId);
                }
            }

            for (int index = races.Count - 1; index >= 0; --index)
            {
                var race = races[index];
                string uniqueId = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;

                if (toRemove.Contains(uniqueId))
                {
                    races.RemoveAt(index);
                }
            }
        }*/

        /// <summary>
        /// Removes multiple instaces of the same racy pair
        /// and keeps the one with the largest time gap.
        /// </summary>
        public void PruneDuplicatesAndKeepLargestTimeGap(List<RacyAccess> races)
        {
            // Remove duplicates, keep only one static pair of instruvtions
            // per potential data race
            Dictionary<string, int> largestTimeGap = new Dictionary<string, int>();
            foreach (var race in races)
            {
                string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;
                if (!largestTimeGap.ContainsKey(key))
                {
                    largestTimeGap[key] = race.AccessGapMs;
                }

                largestTimeGap[key] = Math.Max(largestTimeGap[key], race.AccessGapMs);
            }

            for (int index = races.Count - 1; index >= 0; --index)
            {
                var race = races[index];
                string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;

                if (!largestTimeGap.ContainsKey(key) || race.AccessGapMs != largestTimeGap[key])
                {
                    races.RemoveAt(index);
                }
                else
                {
                    largestTimeGap.Remove(key);
                }
            }
        }

        private static string ExcludeObjectID(string fieldName)
        {
            int underscore = fieldName.IndexOf('_');
            return fieldName.Substring(underscore + 1);
        }

        private bool isStaticAccess(MemoryAccess access)
        {
            return access.CallerMethod.Contains("::.cctor");
        }

        /// <summary>
        /// Computes pairs of injection points that interfere with each other.
        /// </summary>
        /// <returns>List of potential races.</returns>
        public List<OverlappingDelay> ComputeInterferencePairs(List<RacyAccess> races)
        {
            Dictionary<int, List<MemoryAccess>> perThreadTPs = new Dictionary<int, List<MemoryAccess>>();
            foreach (var race in races)
            {
                switch (race.WriteType)
                {
                    case WriteType.NullToNonNull:
                        if (!perThreadTPs.ContainsKey(race.Write.ThreadId))
                        {
                            perThreadTPs[race.Write.ThreadId] = new List<MemoryAccess>();
                        }
                        perThreadTPs[race.Write.ThreadId].Add(race.Write);
                        break;

                    case WriteType.NonNullToNull:
                    case WriteType.Dispose:
                        if (!perThreadTPs.ContainsKey(race.Read.ThreadId))
                        {
                            perThreadTPs[race.Read.ThreadId] = new List<MemoryAccess>();
                        }
                        perThreadTPs[race.Read.ThreadId].Add(race.Read);
                        break;
                }
            }

            HashSet<string> overlapsSet = new HashSet<string>();
            List<OverlappingDelay> overlapsList = new List<OverlappingDelay>();
            foreach (var race in races)
            {
                switch (race.WriteType)
                {
                    case WriteType.NullToNonNull:
                        if (perThreadTPs.ContainsKey(race.Read.ThreadId))
                        {
                            foreach (var access in perThreadTPs[race.Read.ThreadId])
                            {
                                if ((race.Write.Timestamp <= (access.Timestamp - MAX_OVERLAP_WINDOW_TICKS)) && (access.Timestamp <= race.Read.Timestamp))
                                {
                                    string uniqueId = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + access.CallerMethod + "|" + access.CallerILOffset;
                                    if (!overlapsSet.Contains(uniqueId))
                                    {
                                        overlapsList.Add(new OverlappingDelay(race.Write, access));
                                        overlapsSet.Add(uniqueId);
                                    }
                                }
                            }
                        }
                        break;

                    case WriteType.NonNullToNull:
                    case WriteType.Dispose:
                        if (perThreadTPs.ContainsKey(race.Write.ThreadId))
                        {
                            foreach (var access in perThreadTPs[race.Write.ThreadId])
                            {
                                if ((race.Read.Timestamp <= (access.Timestamp - MAX_OVERLAP_WINDOW_TICKS)) && (access.Timestamp <= race.Write.Timestamp))
                                {
                                    string uniqueId = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + access.CallerMethod + "|" + access.CallerILOffset;
                                    if (!overlapsSet.Contains(uniqueId))
                                    {
                                        overlapsList.Add(new OverlappingDelay(race.Write, access));
                                        overlapsSet.Add(uniqueId);
                                    }
                                }
                            }
                        }
                        break;
                }
            }

            return overlapsList;
        }

        /// <summary>
        /// Serialize overlapping racy accesses to a file.
        /// </summary>
        /// <param name="races">Race object.</param>
        /// <param name="outputFile">Output file.</param>
        public static void SerializeOverlaps(List<OverlappingDelay> overlaps, string outputFile)
        {
            if (outputFile == null || overlaps == null || overlaps.Count == 0)
            {
                return;
            }

            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)FieldName\t(1)InterceptCaller\t(2)WriteILOffset\t(3)OverlapTimestamp\t(4)OverlapCaller\t(5)OverlapILOffset");
                foreach (var entry in overlaps)
                {
                    string fieldName = ExcludeObjectID(entry.Intercept.MemoryId);
                    string line = $"{fieldName}\t{entry.Intercept.CallerMethod}\t{entry.Intercept.CallerILOffset}\t{entry.Overlap.Timestamp}\t{entry.Overlap.CallerMethod}\t{entry.Overlap.CallerILOffset}";
                    tw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Deserialize overlapping racy accesses.
        /// </summary>
        /// <param name="raceFile">Race file.</param>
        /// <returns>A list of potential races.</returns>
        public static List<OverlappingDelay> DeserializeOverlaps(string overlapFile)
        {
            List<OverlappingDelay> overlaps = new List<OverlappingDelay>();
            using (TextReader tr = new StreamReader(overlapFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    // "#(0)FieldName\t(1)InterceptCaller\t(2)WriteILOffset\t(3)OverlapTimestamp\t(4)OverlapCaller\t(5)OverlapILOffset"
                    string[] tokens = line.Split("\t".ToCharArray());
                    MemoryAccess intercept = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Write, CallerMethod = tokens[1], CallerILOffset = tokens[2] };
                    MemoryAccess overlap = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Read, CallerMethod = tokens[3], CallerILOffset = tokens[4] };

                    var pair = new OverlappingDelay(intercept, overlap);
                    
                    overlaps.Add(pair);
                }
            }

            return overlaps;
        }
    }
}
