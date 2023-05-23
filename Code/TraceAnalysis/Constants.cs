﻿// <copyright file="Constants.cs" company="Microsoft Research">
// Copyright (c) Microsoft Research. All rights reserved.
// </copyright>

namespace TraceAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Various constants.
    /// </summary>
    public class Constants
    {
        /// <summary>
        /// </summary>
        public static string NullValue => "00000000-0000-0000-0000-000000000000";

        /// <summary>
        /// </summary>
        public static string ObjectIdSeparator => "@";

        /// <summary
        /// </summary>
        public static int NearMissThresholdMs = 100;

        /// <summary>
        /// </summary>
        public static double NearMissWindowPercentage = 0.5;

        /// <summary>
        /// </summary>
        public static int MinDelayValueMs = 10;

        /// <summary>
        /// Sometimes we cannot disable extenral interference
        /// for some out-of-the-box Windows distros. So we apply
        /// a small variance mainly to confuse Windows Defender.
        /// </summary>
        public static long ExternalInterferenceLambda = 5 * TimeSpan.TicksPerMillisecond;

        /// <summary>
        /// </summary>
        public static double ZeroProbability = 0.0001;

    }
}
