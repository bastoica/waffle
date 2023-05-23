namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Diagnostics;

    public enum AnalysisType
    {
        /// <summary>
        /// Compute a list of potential data races.
        /// </summary>
        FindAllPotentialRaces,

        /// <summary>
        /// Compute a list of unique (based on source code location) potential data races.
        /// </summary>
        FindUniquePotentialRaces,

        /// <summary>
        /// Compute a list of potential data races with unique delay injection points.
        /// </summary>
        FindPotentialRacesWithUniqueInjectionPoints,

        /// <summary>
        /// Compute a list of unique (statically) potential data races.
        /// </summary>
        GetStats,
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            // System.Diagnostics.Debugger.Launch();

            string logFile = args[0];
            AnalysisType analysisType = AnalysisType.FindAllPotentialRaces;
            Enum.TryParse(args[1], out analysisType);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            switch (analysisType)
            {
                case AnalysisType.FindAllPotentialRaces:
                    {
                        // Run trace analysis
                        ConflictsAnalyzer analyzer = new ConflictsAnalyzer(logFile);

                        // Compute potential races
                        analyzer.ComputePotentialRacyPairs(/*checkHB=*/ false);

                        // Save racy pairs on disk
                        string outFile = Path.Combine(Path.GetDirectoryName(logFile), "Candidates.wfl");
                        analyzer.SerializeRaces(outFile);
                        Console.WriteLine($"Potential data races:\t{analyzer.dataRaceList.Count}");
                    }
                    break;

                case AnalysisType.FindUniquePotentialRaces:
                    {
                        // Run trace analysis
                        ConflictsAnalyzer analyzer = new ConflictsAnalyzer(logFile);

                        // Compute potential races
                        analyzer.ComputePotentialRacyPairs(/*checkHB=*/ true);

                        // Race list time gap pruning
                        analyzer.PruneRacyPairsWithLargeTimeGap(Constants.NearMissThresholdMs);

                        // Compute interference points
                        var overlaps = analyzer.ComputeInterferencePairs();

                        // Race list duplicates pruning
                        analyzer.PruneDuplicatesAndKeepLargestTimeGap();

                        // Save the list to disk
                        string outFile = Path.Combine(Path.GetDirectoryName(logFile), "Candidates.wfl");
                        analyzer.SerializeRaces(outFile);
                        Console.WriteLine($"Potential data races:\t{analyzer.dataRaceList.Count}");

                        // Save overlapping pairs to disk
                        outFile = Path.Combine(Path.GetDirectoryName(logFile), "Overlaps.wfl");
                        ConflictsAnalyzer.SerializeOverlaps(overlaps, outFile);
                        Console.WriteLine($"Overlapping pairs:\t{overlaps.Count}");
                    }
                    break;

                case AnalysisType.FindPotentialRacesWithUniqueInjectionPoints:
                    {
                        // Run trace analysis
                        ConflictsAnalyzer analyzer = new ConflictsAnalyzer(logFile);

                        // Compute potential races
                        analyzer.ComputePotentialRacyPairs(/*checkHB=*/ true);

                        // Race list time gap pruning
                        analyzer.PruneRacyPairsWithLargeTimeGap(Constants.NearMissThresholdMs);

                        // Race list duplicates pruning
                        analyzer.PruneDuplicateInjectionPoints();

                        // Save the list to disk
                        string outFile = Path.Combine(Path.GetDirectoryName(logFile), "Candidates.wfl");
                        analyzer.SerializeRaces(outFile);
                        Console.WriteLine($"Potential data races:\t{analyzer.dataRaceList.Count}");
                    }
                    break;

                case AnalysisType.GetStats:
                    {
                        // Stat counters
                        HashSet<string> noHbStaticAlloc = new HashSet<string>();
                        HashSet<string> noHbStaticDealloc = new HashSet<string>();
                        HashSet<string> hbStaticAlloc = new HashSet<string>();
                        HashSet<string> hbStaticDealloc = new HashSet<string>();
                        long noHbAllocInst = 0;
                        long noHbDeallocInst = 0;
                        long hbAllocInst = 0;
                        long hbDeallocInst = 0;
                        long interfCount = 0;
                        double overlapPerc = 0.0;

                        // Run trace analysis w/o HB analysis
                        ConflictsAnalyzer analyzerNoHB = new ConflictsAnalyzer(logFile);
                        analyzerNoHB.ComputePotentialRacyPairs(/*checkHB=*/ false);
                        analyzerNoHB.PruneRacyPairsWithLargeTimeGap(Constants.NearMissThresholdMs);

                        HashSet<string> isVisited = new HashSet<string>();
                        foreach (var race in analyzerNoHB.dataRaceList)
                        {
                            if (race.WriteType == WriteType.NullToNonNull)
                            {
                                if (isVisited.Contains(race.Write.GetStaticLoc()))
                                {
                                    continue;
                                }

                                isVisited.Add(race.Write.GetStaticLoc());
                            }
                            else if (race.WriteType == WriteType.NonNullToNull || race.WriteType == WriteType.Dispose)
                            {
                                if (isVisited.Contains(race.Read.GetStaticLoc()))
                                {
                                    continue;
                                }

                                isVisited.Add(race.Read.GetStaticLoc());
                            }
                            
                            foreach (var access in analyzerNoHB.traceSequence)
                            {
                                // Check if allocation part of use-before-init
                                if (race.WriteType == WriteType.NullToNonNull)
                                {
                                    if (access.AccessType == MemoryAccessType.Write && access.CallerMethod.Equals(race.Write.CallerMethod) && access.CallerILOffset.Equals(race.Write.CallerILOffset) && access.OldValue == Constants.NullValue && access.CurrentValue != Constants.NullValue)
                                    {
                                        noHbAllocInst++;
                                        noHbStaticAlloc.Add(access.GetStaticLoc());
                                    }
                                }

                                // Check if use part of use-after-free
                                if (race.WriteType == WriteType.NonNullToNull || race.WriteType == WriteType.Dispose)
                                {
                                    if ((access.AccessType == MemoryAccessType.Use || access.AccessType == MemoryAccessType.Read) && access.CallerMethod.Equals(race.Read.CallerMethod) && access.CallerILOffset.Equals(race.Read.CallerILOffset))
                                    {
                                        noHbDeallocInst++;
                                        noHbStaticDealloc.Add(access.GetStaticLoc());
                                    }
                                }
                            }
                        }


                        // Run trace analysis w/ HB analysis
                        ConflictsAnalyzer analyzerHB = new ConflictsAnalyzer(logFile);
                        analyzerHB.ComputePotentialRacyPairs(/*checkHB=*/ true);
                        analyzerHB.PruneRacyPairsWithLargeTimeGap(Constants.NearMissThresholdMs);
                        var interfList = analyzerHB.ComputeInterferencePairs();

                        HashSet<string> interTable = new HashSet<string>();
                        long ctr = 0;
                        long len = 0;
                        foreach (var entry in interfList)
                        {
                            len += entry.OverlapLength;
                            ctr += entry.DynamicCounter;

                            interTable.Add(entry.Intercept.GetStaticLoc());
                        }

                        overlapPerc = (1.0 * len) / (double)(ctr * Constants.NearMissThresholdMs);

                        isVisited.Clear();
                        foreach (var race in analyzerHB.dataRaceList)
                        {
                            if (race.WriteType == WriteType.NullToNonNull)
                            {
                                if (isVisited.Contains(race.Write.GetStaticLoc()))
                                {
                                    continue;
                                }

                                isVisited.Add(race.Write.GetStaticLoc());
                            }
                            else if (race.WriteType == WriteType.NonNullToNull || race.WriteType == WriteType.Dispose)
                            {
                                if (isVisited.Contains(race.Read.GetStaticLoc()))
                                {
                                    continue;
                                }

                                isVisited.Add(race.Read.GetStaticLoc());
                            }

                            foreach (var access in analyzerHB.traceSequence)
                            {
                                // Check if allocation part of use-before-init
                                if (race.WriteType == WriteType.NullToNonNull)
                                {
                                    if (access.AccessType == MemoryAccessType.Write && access.CallerMethod.Equals(race.Write.CallerMethod) && access.CallerILOffset.Equals(race.Write.CallerILOffset) && access.OldValue == Constants.NullValue && access.CurrentValue != Constants.NullValue)
                                    {
                                        hbAllocInst++;
                                        hbStaticAlloc.Add(access.GetStaticLoc());

                                        if (interTable.Contains(access.GetStaticLoc()))
                                        {
                                            interfCount += 1;
                                        }
                                    }
                                }

                                // Check if use part of use-after-free
                                if (race.WriteType == WriteType.NonNullToNull || race.WriteType == WriteType.Dispose)
                                {
                                    if ((access.AccessType == MemoryAccessType.Use || access.AccessType == MemoryAccessType.Read) && access.CallerMethod.Equals(race.Read.CallerMethod) && access.CallerILOffset.Equals(race.Read.CallerILOffset))
                                    {
                                        hbDeallocInst++;
                                        hbStaticDealloc.Add(access.GetStaticLoc());

                                        if (interTable.Contains(access.GetStaticLoc()))
                                        {
                                            interfCount += 1;
                                        }
                                    }
                                }
                            }
                        }

                        if (!(noHbAllocInst == 0 && noHbStaticAlloc.Count == 0 && noHbDeallocInst == 0 && noHbStaticDealloc.Count == 0 && hbAllocInst == 0 && hbStaticAlloc.Count == 0 && hbDeallocInst == 0 && hbStaticDealloc.Count == 0))
                        {
                            Console.WriteLine($"{logFile}," +
                                $"NoHbAllocInsts,{noHbAllocInst},NoHbAllocCount,{noHbStaticAlloc.Count},NoHbDeallocInsts,{noHbDeallocInst},NoHbDeallocCount,{noHbStaticDealloc.Count}," +
                                $"HbAllocInsts,{hbAllocInst},HbAllocCount,{hbStaticAlloc.Count},HbDeallocInsts,{hbDeallocInst},HbDeallocCount,{hbStaticDealloc.Count}," +
                                $"InterfInstanceCount,{interfCount},InterfCount,{interTable.Count},OverlapPerc,{overlapPerc}"
                                );
                        }
                    }
                    break;

                default:
                    break;
            }

            sw.Stop();
            //Console.WriteLine("TraceAnalysis-elapsed= {0}", sw.Elapsed.Milliseconds);
        }
    }
}
