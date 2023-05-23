// <copyright file="Constants.cs" company="Microsoft Research">
// Copyright (c) Microsoft Research. All rights reserved.
// </copyright>

namespace TorchLiteRuntime
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Various constants.
    /// </summary>
    public class Constants
    {
        private static string logDirectory = ".";

        /// <summary>
        /// Gets config file name.
        /// </summary>
        /// public static string LogFileName => Path.Combine(logDirectory, logNameWithTimestmap);
        public static string ConfigFile => Path.Combine(logDirectory, "TorchParams.conf");

        /// <summary>
        /// Gets log file name.
        /// </summary>
        /// public static string LogFileName => Path.Combine(logDirectory, logNameWithTimestmap);
        public static string LogFileName => Path.Combine(logDirectory, "Runtime.wfl");

        /// <summary>
        /// Gets race file name.
        /// </summary>
        public static string RaceFileName => Path.Combine(logDirectory, "Candidates.wfl");

        /// <summary>
        /// Gets overlap file name.
        /// </summary>
        public static string OverlapFileName => Path.Combine(logDirectory, "Overlaps.wfl");

        /// <summary>
        /// Gets delay probability file name.
        /// </summary>
        public static string DelayProbsFileName => Path.Combine(logDirectory, "Probs.wfl");

        /// <summary>
        /// </summary>
        public static string SyncOpsFileName => Path.Combine(logDirectory, "SyncOps.wfl");

        /// <summary>
        /// </summary>
        public static string SegStatsFileName => Path.Combine(logDirectory, "SyncStats.wfl");

        /// <summary>
        /// </summary>
        public static string StatsFileName => Path.Combine(logDirectory, "Stats.wfl");

        /// <summary>
        /// </summary>
        public static string NullValue => "00000000-0000-0000-0000-000000000000";

        /// <summary>
        /// </summary>
        public static Guid NullGuid => Guid.Empty;

        /// <summary>
        /// </summary>
        public static string ObjectIdSeparator => "@";

        /// <summary>
        /// </summary>
        public static double NearMissWindowPercentage = 0.5;

        /// <summary>
        /// </summary>
        public static double DelayFactor = 1.15;

        /// <summary
        /// </summary>
        [DefaultValue(100)]
        public static int NearMissWindowMs = 100;

        /// <summary
        /// </summary>
        public static int NearMissWindowCount = 100;

        /// <summary
        /// </summary>
        [DefaultValue(100)]
        public static int MaxDelayValueMs = 100;

        /// <summary>
        /// </summary>
        [DefaultValue(10)]
        public static int MinDelayValueMs = 1;

        /// <summary>
        /// </summary>
        public static int DelayHistoryCount = 10;

        /// <summary>
        /// </summary>
        public static double ZeroProbability = 0.001;

        /// <summary>
        /// </summary>
        [DefaultValue(0.1)]
        public static double ProbDecayStep = 0.1;

        /// <summary>
        /// </summary>
        public static double ProbabilityDecay(double oldProb) => (oldProb > Constants.ZeroProbability) ? oldProb - Constants.ProbDecayStep : 1.0;

        /// <summary>
        /// Gets the name of the file whose presence indicates where the logs should be written.
        /// </summary>
        public static string LogDirMarkerFile { get; } = "TorchLogDir.txt";

        /// <summary>
        /// Gets or sets a global flag to disable all logging.
        /// </summary>
        public static bool DisableLogging { get; set;  } = false;

        static Constants()
        {
            logDirectory = GetLogDirectory();
        }

        private static string GetLogDirectory()
        {
            DirectoryInfo di = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (di != null)
            {
                if (di.EnumerateFiles(LogDirMarkerFile).Any())
                {
                    return di.FullName;
                }

                di = di.Parent;
            }

            return di.FullName;
        }
    }
}
