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

        public List<MemoryAccess> traceSequence = new List<MemoryAccess>();
        private Dictionary<string, MemoryAccessHistory> traceHashTable = new Dictionary<string, MemoryAccessHistory>();

        public List<RacyAccess> dataRaceList = new List<RacyAccess>();
        private List<SyncOperation> syncOpList = new List<SyncOperation>();

        private readonly long MAX_OVERLAP_WINDOW_TICKS = 1 * TimeSpan.TicksPerMillisecond; // 1ms

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
                    if (IsStaticAccess(access) == true)
                    {
                        continue;
                    }

                    if (!this.traceHashTable.ContainsKey(access.MemoryId))
                    {
                        this.traceHashTable[access.MemoryId] = new MemoryAccessHistory(access);
                    }

                    // Update access counters
                    access.PerObjectAccessCounter = this.perObjectAccessCounter.ContainsKey(access.MemoryId) ? ++this.perObjectAccessCounter[access.MemoryId] : this.perObjectAccessCounter[access.MemoryId] = 1;
                    access.GlobalAccessCounter = this.globalAccessCounter.ContainsKey(access.GetStaticLoc()) ? ++this.globalAccessCounter[access.GetStaticLoc()] : this.globalAccessCounter[access.GetStaticLoc()] = 1;

                    // Add to trace
                    this.traceSequence.Add(access);
                    this.traceHashTable[access.MemoryId].Add(access);
                }
            }
        }

        /// <summary>
        /// Serialize potential races to a file.
        /// </summary>
        /// <param name="races">Race object.</param>
        /// <param name="outputFile">Output file.</param>
        public void SerializeRaces(string outputFile)
        {
            if (outputFile == null || this.dataRaceList.Count == 0)
            {
                return;
            }

            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)WritePerObjectAccessCount\t(7)WriteGlobalAccessCount\t(8)ReadTimestamp\t(9)ReadType\t(10)ReadCaller\t(11)ReadILOffset\t(12)ReadPerObjectAccessCount\t(13)WriteGlobalAccessCount");
                foreach (var race in this.dataRaceList)
                {
                    string fieldName = ExcludeObjectID(race.Write.MemoryId);
                    string line = $"{fieldName}\t{race.AccessGapMs}\t{race.Write.Timestamp}\t{race.WriteType}\t{race.Write.CallerMethod}\t{race.Write.CallerILOffset}\t{race.Write.PerObjectAccessCounter}\t{race.Write.GlobalAccessCounter}\t{race.Read.Timestamp}\t{race.ReadType}\t{race.Read.CallerMethod}\t{race.Read.CallerILOffset}\t{race.Read.PerObjectAccessCounter}\t{race.Read.GlobalAccessCounter}";
                    tw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Serialize potential races to a file.
        /// </summary>
        /// <param name="raceListFromFile">List of data races read from a file.</param>
        /// <param name="outputFile">Output file.</param>
        public static void SerializeRaces(List<RacyAccess> raceListFromFile, string outputFile)
        {
            if (outputFile == null || raceListFromFile.Count == 0)
            {
                return;
            }

            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)WritePerObjectAccessCount\t(7)WriteGlobalAccessCount\t(8)ReadTimestamp\t(9)ReadType\t(10)ReadCaller\t(11)ReadILOffset\t(12)ReadPerObjectAccessCount\t(13)WriteGlobalAccessCount");
                foreach (var race in raceListFromFile)
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

                    try
                    {
                        // "#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)WritePerObjectAccessCount\t(7)WriteGlobalAccessCount\t(8)ReadTimestamp\t(9)ReadType\t(10)ReadCaller\t(11)ReadILOffset\t(12)ReadPerObjectAccessCount\t(13)WriteGlobalAccessCount"
                        string[] tokens = line.Split("\t".ToCharArray());
                        MemoryAccess write = new MemoryAccess() { MemoryId = tokens[0], AccessType = StringToMemoryAccessType(tokens[3]), CallerMethod = tokens[4], CallerILOffset = tokens[5] };
                        MemoryAccess read = new MemoryAccess() { MemoryId = tokens[0], AccessType = StringToMemoryAccessType(tokens[9]), CallerMethod = tokens[10], CallerILOffset = tokens[11] };
                        var writeType = (WriteType)Enum.Parse(typeof(WriteType), tokens[3]);
                        var readType = (ReadType)Enum.Parse(typeof(ReadType), tokens[9]);
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
                    catch (Exception)
                    {
                        // do nothing
                        continue;
                    }
                }
            }

            return races;
        }

        /// <summary>
        /// Compute potentially racy fields.
        /// </summary>
        public void ComputePotentiallyRacyFields()
        {
            foreach (var objectId in this.traceHashTable.Keys)
            {
                if (this.traceHashTable[objectId].IsPotentialRace())
                {
                    Console.WriteLine(objectId);
                }
            }
        }

        /// <summary>
        /// Computes potential races.
        /// </summary>
        /// <returns>List of potential races.</returns>
        public void ComputePotentialRacyPairs(bool checkHB)
        {
            foreach (var objectId in this.traceHashTable.Keys)
            {
                this.dataRaceList.AddRange(this.traceHashTable[objectId].GetPotentialRacyAccesses(checkHB));
            }
        }

        /// <summary>
        /// Removes instances with time gap larger than a threshold.
        /// </summary>
        public void PruneRacyPairsWithLargeTimeGap(int maxTimeGapMs)
        {
            for (int index = this.dataRaceList.Count - 1; index >= 0; --index)
            {
                var race = this.dataRaceList[index];

                if (race.AccessGapMs > maxTimeGapMs)
                {
                    this.dataRaceList.RemoveAt(index);
                }

                if (race.Write.CallerMethod.Contains("::MoveNext") && race.Read.CallerMethod.Contains("::MoveNext"))
                {
                    this.dataRaceList.RemoveAt(index);
                }
            }
        }

        /// <summary>
        /// Removes multiple instaces of the same racy pair
        /// and keeps those with the largest time gap.
        /// </summary>
        public void PruneDuplicatesAndKeepLargestTimeGap()
        {
            Dictionary<string, int> largestTimeGap = new Dictionary<string, int>();
            foreach (var race in this.dataRaceList)
            {
                string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;
                if (!largestTimeGap.ContainsKey(key))
                {
                    largestTimeGap[key] = race.AccessGapMs;
                }

                largestTimeGap[key] = Math.Max(largestTimeGap[key], race.AccessGapMs);
            }

            for (int index = this.dataRaceList.Count - 1; index >= 0; --index)
            {
                var race = this.dataRaceList[index];
                string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + race.Read.CallerMethod + "|" + race.Read.CallerILOffset;

                if (!largestTimeGap.ContainsKey(key) || race.AccessGapMs != largestTimeGap[key])
                {
                    this.dataRaceList.RemoveAt(index);
                }
                else
                {
                    largestTimeGap.Remove(key);
                }
            }
        }

        /// <summary>
        /// Removes multiple instaces of the same racy pair
        /// and keeps those with the largest time gap.
        /// </summary>
        public void PruneDuplicateInjectionPoints()
        {
            Dictionary<string, int> largestTimeGap = new Dictionary<string, int>();
            foreach (var race in this.dataRaceList)
            {

                string key = (race.WriteType == WriteType.NullToNonNull) ? race.Write.CallerMethod + "|" + race.Write.CallerILOffset :
                                                                           race.Read.CallerMethod + "|" + race.Read.CallerILOffset;

                if (!largestTimeGap.ContainsKey(key))
                {
                    largestTimeGap[key] = race.AccessGapMs;
                }

                largestTimeGap[key] = Math.Max(largestTimeGap[key], race.AccessGapMs);
            }

            for (int index = this.dataRaceList.Count - 1; index >= 0; --index)
            {
                var race = this.dataRaceList[index];
                string key = (race.WriteType == WriteType.NullToNonNull) ? race.Write.CallerMethod + "|" + race.Write.CallerILOffset :
                                                                           race.Read.CallerMethod + "|" + race.Read.CallerILOffset;

                if (!largestTimeGap.ContainsKey(key) || race.AccessGapMs != largestTimeGap[key])
                {
                    this.dataRaceList.RemoveAt(index);
                }
                else
                {
                    largestTimeGap.Remove(key);
                }
            }
        }

        private static string ExcludeObjectID(string fieldName)
        {
            int underscore = fieldName.IndexOf(Constants.ObjectIdSeparator);
            return underscore < 0 ? fieldName : fieldName.Substring(underscore + 1);
        }

        private bool IsStaticAccess(MemoryAccess access)
        {
            return access.CallerMethod.Contains("::.cctor");
        }

        /// <summary>
        /// Computes pairs of injection points that interfere with each other.
        /// </summary>
        /// <returns>List of potential races.</returns>
        public List<OverlappingDelay> ComputeInterferencePairs()
        {
            HashSet<string> perThreadTP = new HashSet<string>();

            foreach (var race in this.dataRaceList)
            {
                switch (race.WriteType)
                {
                    case WriteType.NullToNonNull:
                        perThreadTP.Add(race.Write.GetStaticLoc());
                        break;

                    case WriteType.NonNullToNull:
                    case WriteType.Dispose:
                        perThreadTP.Add(race.Read.GetStaticLoc());
                        break;
                }
            }

            List<MemoryAccess> tpList = new List<MemoryAccess>();
            foreach (var access in this.traceSequence)
            {
                if (perThreadTP.Contains(access.GetStaticLoc()))
                {
                    tpList.Add(access);
                }
            }

            Dictionary<string, OverlappingDelay> overlapsSet = new Dictionary<string, OverlappingDelay>();
            foreach (var access in tpList)
            {
                if (perThreadTP.Contains(access.GetStaticLoc()))
                {
                    foreach (var race in this.dataRaceList)
                    {
                        switch (race.WriteType)
                        {
                            case WriteType.NullToNonNull:
                                if ((access.ThreadId == race.Read.ThreadId) && (race.Write.Timestamp <= (access.Timestamp - MAX_OVERLAP_WINDOW_TICKS)) && (access.Timestamp <= race.Read.Timestamp))
                                {
                                    string uniqueId = race.Write.CallerMethod + "|" + race.Write.CallerILOffset + "|" + access.CallerMethod + "|" + access.CallerILOffset;
                                    if (!overlapsSet.ContainsKey(uniqueId))
                                    {
                                        overlapsSet[uniqueId] = new OverlappingDelay(race.Write, access);
                                    }
                                    overlapsSet[uniqueId].DynamicCounter += 1;
                                    overlapsSet[uniqueId].OverlapLength += (Constants.NearMissThresholdMs - (long)Math.Ceiling(1.0 * Math.Abs(access.Timestamp - race.Write.Timestamp) / TimeSpan.TicksPerMillisecond));
                                }
                                break;

                            case WriteType.NonNullToNull:
                            case WriteType.Dispose:
                                if ((access.ThreadId == race.Write.ThreadId) && (race.Read.Timestamp <= (access.Timestamp - MAX_OVERLAP_WINDOW_TICKS)) && (access.Timestamp <= race.Write.Timestamp))
                                {
                                    string uniqueId = race.Read.CallerMethod + "|" + race.Read.CallerILOffset + "|" + access.CallerMethod + "|" + access.CallerILOffset;
                                    if (!overlapsSet.ContainsKey(uniqueId))
                                    {
                                        overlapsSet[uniqueId] = new OverlappingDelay(race.Read, access);
                                    }
                                    overlapsSet[uniqueId].DynamicCounter += 1;
                                    overlapsSet[uniqueId].OverlapLength += (Constants.NearMissThresholdMs - (long)Math.Ceiling(1.0 * Math.Abs(access.Timestamp - race.Read.Timestamp) / TimeSpan.TicksPerMillisecond));
                                }
                                break;
                        }
                    }
                }
            }

            return overlapsSet.Values.ToList();
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
                tw.WriteLine("#(0)FieldName\t(1)InterceptCaller\t(2)InterceptILOffset\t(3)OverlapCaller\t(4)OverlapILOffset");
                foreach (var entry in overlaps)
                {
                    string fieldName = ExcludeObjectID(entry.Intercept.MemoryId);
                    string line = $"{fieldName}\t{entry.Intercept.CallerMethod}\t{entry.Intercept.CallerILOffset}\t{entry.Overlap.CallerMethod}\t{entry.Overlap.CallerILOffset}";
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

                    // "#(0)FieldName\t(1)InterceptCaller\t(2)InterceptILOffset\t(3)OverlapCaller\t(4)OverlapILOffset"
                    string[] tokens = line.Split("\t".ToCharArray());
                    MemoryAccess intercept = new MemoryAccess() { MemoryId = tokens[0], AccessType = MemoryAccessType.Write, CallerMethod = tokens[1], CallerILOffset = tokens[2] };
                    MemoryAccess overlap = new MemoryAccess() { MemoryId = tokens[0], AccessType = MemoryAccessType.Read, CallerMethod = tokens[3], CallerILOffset = tokens[4] };

                    var pair = new OverlappingDelay(intercept, overlap);

                    overlaps.Add(pair);
                }
            }

            return overlaps;
        }

        /// <summary>
        /// </summary>
        /// <param name="raceFile">Race file.</param>
        /// <returns>A list of potential races.</returns>
        public void DeserializeSyncOperations(string syncFile)
        {
            using (TextReader tr = new StreamReader(syncFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    // "#(0)SyncType\t(1)SyncSite"
                    string[] tokens = line.Split("\t".ToCharArray());
                    this.syncOpList.Add(new SyncOperation(tokens[0].Equals("Acquire") ? SyncType.Acquire : SyncType.Release, tokens[1]));
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="opType">The operation type in string format.</param>
        /// <returns>The operation type in enum MemoryAccessType.</returns>
        public static MemoryAccessType StringToMemoryAccessType(string opType)
        {
            switch (opType)
            {
                case "Write":
                case "NullToNonNull":
                case "NonNullToNull":
                    return MemoryAccessType.Write;

                case "Read":
                    return MemoryAccessType.Read;

                case "Use":
                    return MemoryAccessType.Use;

                case "Dispose":
                    return MemoryAccessType.Dispose;

                default:
                    return MemoryAccessType.None;
            }
        }
    }
}
