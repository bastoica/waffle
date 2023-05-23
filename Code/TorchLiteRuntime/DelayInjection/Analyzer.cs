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
    public class Analyzer
    {
        private List<MemoryAccess> execTrace = new List<MemoryAccess>();

        private Dictionary<string, MemoryAccessHistory> execTraceWithHistory = new Dictionary<string, MemoryAccessHistory>();

        private Dictionary<string, List<DelayInjectionPoint>> allDelays = new Dictionary<string, List<DelayInjectionPoint>>();

        public class PerDelayStats
        {
            public string uniqueId;
            public WriteType PointType;
            public long DelayMs = 0;
            public long MinGapMs = 1L;
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
        /// Initializes a new instance of the <see cref="Analyzer"/> class.
        /// </summary>
        /// <param name="logFile">A runtime log file.</param>
        public Analyzer(string logFile)
        {
            using (TextReader tr = new StreamReader(logFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {
                
                    if (line.Contains("DelayInjection")) // trace log contains extra delay injection information
                    {
                        DelayInjectionPoint delayPoint = DelayInjectionPoint.ParseLogLine(line);
                        if (delayPoint != null)
                        {
                            if (!this.allDelays.ContainsKey(delayPoint.MemoryId))
                            {
                                this.allDelays[delayPoint.MemoryId] = new List<DelayInjectionPoint>();
                            }
                            this.allDelays[delayPoint.MemoryId].Add(delayPoint);
                        }
                    }
                    else
                    {
                        MemoryAccess access = null;  access = MemoryAccess.ParseLogLine(line);

                        if (access == null)
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

                        this.execTrace.Add(access);
                    }
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
                tw.WriteLine("#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)ReadTimestamp\t(7)ReadType\t(8)ReadCaller\t(9)ReadILOffset");
                foreach (var race in races)
                {
                    if (isCompilerGenerated(race))
                    {
                        continue;
                    }

                    string fieldName = ExcludeObjectID(race.Write.MemoryId);
                    string line = $"{fieldName}\t{race.AccessGapMs}\t{race.Write.Timestamp}\t{race.WriteType}\t{race.Write.CallerMethod}\t{race.Write.CallerILOffset}\t{race.Read.Timestamp}\t{race.ReadType}\t{race.Read.CallerMethod}\t{race.Read.CallerILOffset}";
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

                    // (#(0)FieldName\t(1)AccessGapMs\t(2)WriteTimestamp\t(3)WriteType\t(4)WriteCaller\t(5)WriteILOffset\t(6)ReadTimestamp\t(7)ReadType\t(8)ReadCaller\t(9)ReadILOffset");
                    string[] tokens = line.Split("\t".ToCharArray());
                    MemoryAccess write = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Write, CallerMethod = tokens[4], CallerILOffset = tokens[5] };
                    MemoryAccess read = new MemoryAccess() { MemoryId = tokens[0], AccessType = OperationType.Read, CallerMethod = tokens[8], CallerILOffset = tokens[9] };
                    var writeType = (WriteType)Enum.Parse(typeof(WriteType), tokens[3]);
                    var readType = (ReadType)Enum.Parse(typeof(ReadType), tokens[7]);
                    var race = new RacyAccess(read, readType, write, writeType);
                    race.AccessGapMs = int.Parse(tokens[1]);
                    race.Write.Timestamp = long.Parse(tokens[2]);
                    race.Read.Timestamp = long.Parse(tokens[6]);
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
            List<RacyAccess> racyAccesses = new List<RacyAccess>();
            foreach (var objectId in this.execTraceWithHistory.Keys)
            {
                racyAccesses.AddRange(this.execTraceWithHistory[objectId].GetPotentialRacyAccesses());
            }

            return racyAccesses;
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

        private static bool isCompilerGenerated(RacyAccess race)
        {
            // If both memory access are inside a compiler-generated FSM (e.g., used in async/await constructs)
            // it is extrimely likely the threads are syncronized and this is not a data race
            if (race.Read.CallerMethod.Contains("::MoveNext") && race.Write.CallerMethod.Contains("::MoveNext"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// </summary>
        public static void SerializeDelayStats(List<PerDelayStats> delays, string outputFile)
        {
            if (outputFile == null || delays == null || delays.Count == 0)
            {
                return;
            }

            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)FieldName\t(1)DelayType\t(2)DelayMs\t(3)MinGapMs\t(4)MaxGapMs\t(5)DelaysInjected\t(6)PotentialRacyPairs");
                foreach (var delay in delays)
                {
                    string line = $"{delay.uniqueId}\t{delay.PointType}\t{delay.DelayMs}\t{delay.MinGapMs}\t{delay.MaxGapMs}\t{delay.DelaysInjected}\t{delay.PotentialRacyPairs}";
                    tw.WriteLine(line);
                }
            }
        }

        /// <summary>
        /// Check performance of each injected delay.
        /// </summary>
        public IEnumerable<PerDelayStats> GetDelayStats()
        {
            foreach (var op in execTrace)
            {
                if (allDelays.ContainsKey(op.MemoryId))
                {
                    var perObjAccesses = execTrace.Where(x => x.MemoryId == op.MemoryId);

                    foreach (var primary in allDelays[op.MemoryId])
                    {
                        var stat = new PerDelayStats();

                        if (primary.AccessType == OperationType.Write) // NullToNonNull
                        {
                            foreach (var alternate in perObjAccesses)
                            {
                                if (isPotentialRace(primary, alternate))
                                {
                                    stat.PointType = WriteType.NullToNonNull;
                                    stat.DelayMs = primary.DelayMs;
                                    stat.MinGapMs = Math.Min(stat.MinGapMs, alternate.Timestamp - primary.Timestamp);
                                    stat.MaxGapMs = Math.Max(stat.MaxGapMs, alternate.Timestamp - primary.Timestamp);
                                    stat.PotentialRacyPairs++;
                                    // stat.hasInterference = CheckInterference();
                                }
                            }
                        }

                        stat.DelaysInjected = 1;
                        stat.uniqueId = primary.MemoryId + "|" + primary.CallerMethod + "|" + primary.CallerILOffset.ToString();
                        yield return stat;
                    }

                    foreach (var alternate in perObjAccesses)
                    {
                        if (alternate.AccessType == OperationType.Write) // NonNullToNull
                        {
                            var stat = new PerDelayStats();

                            foreach (var primary in allDelays[op.MemoryId])
                            {
                                if (isPotentialRace(primary, alternate))
                                {
                                    stat.PointType = WriteType.NonNullToNull;
                                    stat.DelayMs = primary.DelayMs;
                                    stat.MinGapMs = Math.Min(stat.MinGapMs, alternate.Timestamp - primary.Timestamp);
                                    stat.MaxGapMs = Math.Max(stat.MaxGapMs, alternate.Timestamp - primary.Timestamp);
                                    stat.PotentialRacyPairs++;
                                    stat.DelaysInjected++;
                                    // stat.hasInterference = CheckInterference();
                                }
                            }

                            stat.uniqueId = alternate.MemoryId + "|" + alternate.CallerMethod + "|" + alternate.CallerILOffset.ToString();
                            yield return stat;
                        }
                    }
                }
            }
        }

        bool isPotentialRace(DelayInjectionPoint primary, MemoryAccess alternate)
        {
            if (primary.AccessType == OperationType.Write)
            {
                return (alternate.AccessType == OperationType.Use && alternate.ThreadId != primary.ThreadId && alternate.Timestamp >= primary.Timestamp);
            }
            else if (primary.AccessType == OperationType.Use)
            {
                return (alternate.AccessType == OperationType.Write && alternate.ThreadId != primary.ThreadId && alternate.Timestamp >= primary.Timestamp);
            }

            return false;
        }
    }
}
