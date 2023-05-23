namespace TorchLiteRuntime
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Linq;

    public class PersistentConfig
    {
        public static void LoadConfigFile()
        {
            if (File.Exists(Constants.ConfigFile))
            {
                using (TextReader tr = new StreamReader(Constants.ConfigFile))
                {
                    string line;
                    while ((line = tr.ReadLine()) != null)
                    {
                        string number = new string(line.Where(c => Char.IsDigit(c) || c == '.').ToArray());

                        if (line.StartsWith("NearMissWindowMs"))
                        {
                            Constants.NearMissWindowMs = int.Parse(number);
                        }

                        if (line.StartsWith("MaxDelayValueMs"))
                        {
                            Constants.MaxDelayValueMs = int.Parse(number);
                        }

                        if (line.StartsWith("ProbDecayStep"))
                        {
                            Constants.ProbDecayStep = double.Parse(number);
                        }
                    }
                }
            }
        }

        public static void WriteOutProbs(Dictionary<string, DelayInjectionPlan> delayInjectionPlan, string outputFile)
        {
            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)Caller\t(1)ILOffset\t(2)Probability");
                foreach (var k in delayInjectionPlan.Keys)
                {
                    DelayInjectionPlan plan = delayInjectionPlan[k];
                    string line = $"{plan.targetStaticTp.Caller}\t{plan.targetStaticTp.ILOffset}\t{plan.DelayProbability}";
                    tw.WriteLine(line);
                }
            }
        }

        public static Dictionary<string, double> ReadInProbs(string inputFile)
        {
            Dictionary<string, double> probsTable = new Dictionary<string, double>();

            if (File.Exists(inputFile))
            {
                using (TextReader tr = new StreamReader(inputFile))
                {
                    string line;
                    while ((line = tr.ReadLine()) != null)
                    {
                        if (line.StartsWith("#"))
                        {
                            continue;
                        }

                        // #(0)Caller\t(1)ILOffset\t(2)Probability
                        string[] tokens = line.Split("\t".ToCharArray());
                        // TODO: merge this with TorchPoint.GetStaticCodeLoc
                        string staticLoc = $"{tokens[0]}|{tokens[1]}";

                        probsTable[staticLoc] = double.Parse(tokens[2]);
                    }
                }
            }

            return probsTable;
        }

        public static void WriteOutStats(string line, string outputFile)
        {
            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)TotalDelayMs\t(1)TotalDelayCount");
                tw.WriteLine(line);
            }
        }
        public static void WriteOutSyncSegmentStats(Dictionary<int, List<int>> perThreadSyncSegments, string outputFile)
        {
            using (TextWriter tw = new StreamWriter(outputFile))
            {
                tw.WriteLine("#(0)ThreadId\t(1)Avg\t(2)Min\t(3)Max\t(4)Avg-inner\t(5)Min-inner\t(6)Max-inner");
                foreach (var tid in perThreadSyncSegments.Keys)
                {
                    List<int> seg = perThreadSyncSegments[tid];
                    string series = "";

                    if (seg.Count == 0)
                    {
                        continue;
                    }

                    int min = seg[seg.Count - 1];
                    int max = seg[seg.Count - 1];
                    double avg = 0.0;
                    foreach (var counter in seg)
                    {
                        min = Math.Min(min, counter);
                        max = Math.Max(max, counter);
                        avg += (double)counter;

                        series += $"{counter}\t";
                    }
                    avg /= (double)seg.Count;

                    int min_inner = 0;
                    int max_inner = 0;
                    double avg_inner = 0.0;
                    for (int i = 1; i < seg.Count - 1; ++i)
                    {
                        min_inner = Math.Min(min_inner, seg[i]);
                        max_inner = Math.Max(max_inner, seg[i]);
                        avg_inner += (double)seg[i];
                    }
                    if (seg.Count > 2)
                    {
                        avg_inner /= (double)(seg.Count - 2);
                    }

                    string line = $"{tid}\t{avg}\t{min}\t{max}\t{avg_inner}\t{min_inner}\t{max_inner}\t{series}";
                    tw.WriteLine(line);
                }
            }
        }

        public static HashSet<string> ReadInSyncOps(string inputFile)
        {
            HashSet<string> syncOps = new HashSet<string>();

            if (File.Exists(inputFile))
            {
                using (TextReader tr = new StreamReader(inputFile))
                {
                    string line;
                    while ((line = tr.ReadLine()) != null)
                    {
                        syncOps.Add(line);
                    }
                }
            }

            return syncOps;
        }
    }
}