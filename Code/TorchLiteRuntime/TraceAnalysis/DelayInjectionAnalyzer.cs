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
    public class DelayInjectionAnalyzer
    {
        private readonly Dictionary<string, int> perObjectAccessCounter = new Dictionary<string, int>();
        private readonly Dictionary<string, int> globalAccessCounter = new Dictionary<string, int>();

        private List<MemoryAccess> execTrace = new List<MemoryAccess>();
        private Dictionary<string, List<DelayInjectionPoint>> delaySet = new Dictionary<string, List<DelayInjectionPoint>>();

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

        /// <summary>
        /// Initializes a new instance of the <see cref="ConflictsAnalyzer"/> class.
        /// </summary>
        /// <param name="logFile">A runtime log file.</param>
        public DelayInjectionAnalyzer(string logFile)
        {
            using (TextReader tr = new StreamReader(logFile))
            {
                string line;
                while ((line = tr.ReadLine()) != null)
                {

                    if (line.Contains("DelayInjection")) // trace log contains extra delay injection information
                    {
                        DelayInjectionPoint delayPoint = DelayInjectionPoint.ParseLogLine(line);
                        if (delayPoint != null && delayPoint.MemoryId != null)
                        {
                            if (!this.delaySet.ContainsKey(delayPoint.MemoryId))
                            {
                                this.delaySet[delayPoint.MemoryId] = new List<DelayInjectionPoint>();
                            }
                            this.delaySet[delayPoint.MemoryId].Add(delayPoint);
                        }
                    }
                    else
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

                        // Update access counters
                        access.PerObjectAccessCounter = this.perObjectAccessCounter.ContainsKey(access.MemoryId) ? ++this.perObjectAccessCounter[access.MemoryId] : this.perObjectAccessCounter[access.MemoryId] = 1;
                        access.GlobalAccessCounter = this.globalAccessCounter.ContainsKey(access.GetStaticLoc()) ? ++this.globalAccessCounter[access.GetStaticLoc()] : this.globalAccessCounter[access.GetStaticLoc()] = 1;

                        // Add to trace
                        this.execTrace.Add(access);
                    }
                }
            }
        }

        /// <summary>
        /// </summary>
        public static void SerializeDelayStats(IEnumerable<PerDelayStats> delays, string outputFile)
        {
            if (outputFile == null || delays == null)
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
        public IEnumerable<PerDelayStats> GetDelayStatsFor(List<RacyAccess> races)
        {
            Dictionary<string, List<RacyAccess>> nullToNonNullTable = new Dictionary<string, List<RacyAccess>>();
            Dictionary<string, List<RacyAccess>> nonNullToNullTable = new Dictionary<string, List<RacyAccess>>();

            foreach (var race in races)
            {
                if (race.WriteType == WriteType.NullToNonNull)
                {
                    string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset;
                    if (!nullToNonNullTable.ContainsKey(key))
                    {
                        nullToNonNullTable[key] = new List<RacyAccess>();
                    }
                    nullToNonNullTable[key].Add(race);
                }
                else if (race.WriteType == WriteType.NonNullToNull || race.WriteType == WriteType.Dispose)
                {
                    string key = race.Write.CallerMethod + "|" + race.Write.CallerILOffset;
                    if (!nonNullToNullTable.ContainsKey(key))
                    {
                        nonNullToNullTable[key] = new List<RacyAccess>();
                    }
                    nonNullToNullTable[key].Add(race);
                }
            }

            foreach (var op in execTrace)
            {
                if (delaySet.Count == 0)
                {
                    break;
                }

                if (delaySet.ContainsKey(op.MemoryId))
                {
                    var perObjAccesses = execTrace.Where(x => x.MemoryId == op.MemoryId);

                    foreach (var primary in delaySet[op.MemoryId])
                    {
                        PerDelayStats stats = new PerDelayStats();
                        string primaryKey = primary.CallerMethod + "|" + primary.CallerILOffset;

                        foreach (var alternate in perObjAccesses)
                        {
                            if (nullToNonNullTable.ContainsKey(primaryKey))
                            {
                                foreach (var racyPair in nullToNonNullTable[primaryKey])
                                {
                                    if (primary.Timestamp < alternate.Timestamp && racyPair.Read.CallerMethod.Equals(alternate.CallerMethod) && racyPair.Read.CallerILOffset.Equals(alternate.CallerILOffset))
                                    {
                                        stats.isValid = true;
                                        stats.uniqueId = primary.MemoryId + "|" + primary.CallerMethod + "|" + primary.CallerILOffset;
                                        stats.DelayMs = racyPair.AccessGapMs;
                                        stats.PointType = racyPair.WriteType;
                                        stats.MinGapMs = Math.Min(stats.MinGapMs, (long)(1.0 * Math.Abs(primary.Timestamp - alternate.Timestamp) / TimeSpan.TicksPerMillisecond));
                                        stats.MaxGapMs = Math.Max(stats.MaxGapMs, (long)(1.0 * Math.Abs(primary.Timestamp - alternate.Timestamp) / TimeSpan.TicksPerMillisecond));
                                        stats.hasInterference = false;
                                        stats.DelaysInjected = 1;
                                        stats.PotentialRacyPairs++;
                                    }
                                }
                            }
                        }

                        if (stats.isValid)
                        {
                            yield return stats;
                        }
                    }

                    foreach (var alternate in perObjAccesses)
                    {
                        PerDelayStats stats = new PerDelayStats();
                        string alternateKey = alternate.CallerMethod + "|" + alternate.CallerILOffset;

                        foreach (var primary in delaySet[op.MemoryId])
                        {
                            if (nonNullToNullTable.ContainsKey(alternateKey))
                            {
                                foreach (var racyPair in nonNullToNullTable[alternateKey])
                                {
                                    if (primary.Timestamp < alternate.Timestamp && racyPair.Read.CallerMethod.Equals(primary.CallerMethod) && racyPair.Read.CallerILOffset.Equals(primary.CallerILOffset))
                                    {
                                        stats.isValid = true;
                                        stats.uniqueId = alternate.MemoryId + "|" + alternate.CallerMethod + "|" + alternate.CallerILOffset;
                                        stats.DelayMs = racyPair.AccessGapMs;
                                        stats.PointType = racyPair.WriteType;
                                        stats.MinGapMs = Math.Min(stats.MinGapMs, (long)(1.0 * Math.Abs(primary.Timestamp - alternate.Timestamp) / TimeSpan.TicksPerMillisecond));
                                        stats.MaxGapMs = Math.Max(stats.MaxGapMs, (long)(1.0 * Math.Abs(primary.Timestamp - alternate.Timestamp) / TimeSpan.TicksPerMillisecond));
                                        stats.hasInterference = false;
                                        stats.DelaysInjected = 1;
                                        stats.PotentialRacyPairs++;
                                    }
                                }
                            }
                        }

                        if (stats.isValid)
                        {
                            yield return stats;
                        }
                    }

                    delaySet.Remove(op.MemoryId);
                }
            }
        }

        private bool isStaticAccess(MemoryAccess access)
        {
            return access.CallerMethod.Contains("::.cctor");
        }
    }
}
