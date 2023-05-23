namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Mail;

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
        GetDelayStats,
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            // System.Diagnostics.Debugger.Launch();

            string logFile = args[0];
            AnalysisType analysisType = AnalysisType.FindAllPotentialRaces;
            Enum.TryParse(args[1], out analysisType);

            switch (analysisType)
            {
                case AnalysisType.FindAllPotentialRaces:
                    {
                        // Run trace analysis
                        ConflictsAnalyzer analyzer = new ConflictsAnalyzer(logFile);

                        // Compute potential races
                        analyzer.ComputePotentialRacyPairs();

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
                        analyzer.ComputePotentialRacyPairs();

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
                        analyzer.ComputePotentialRacyPairs();

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

                case AnalysisType.GetDelayStats:
                    {
                        // Run trace analysis
                        DelayInjectionAnalyzer analyzer = new DelayInjectionAnalyzer(logFile);

                        if (File.Exists("Candidates.wfl"))
                        {
                            List<RacyAccess> races = ConflictsAnalyzer.DeserializeRaces("Candidates.wfl");

                            string outFile = Path.Combine(Path.GetDirectoryName(logFile), "Delays.wfl");
                            var delays = analyzer.GetDelayStatsFor(races);

                            DelayInjectionAnalyzer.SerializeDelayStats(delays, outFile);
                        }
                    }
                    break;

                default:
                    break;
            }
        }
    }
}
