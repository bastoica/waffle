// <copyright file="Constants.cs" company="Microsoft Research">
// Copyright (c) Microsoft Research. All rights reserved.
// </copyright>

namespace TorchLiteInstrumenter
{
    using System.Collections.Generic;

    /// <summary>
    /// Instrumentation constants.
    /// </summary>
    public class Constants
    {
        /// <summary>
        /// Gets runtime library name.
        /// </summary>
        public static string RuntimeLibrary { get; } = "TorchLiteRuntime";

        /// <summary>
        /// Gets runtime library file name.
        /// </summary>
        public static string RuntimeLibraryFile { get; } = RuntimeLibrary + ".dll";

        /// <summary>
        ///  Gets list of dependent assemblies that need to be copied to instrumented assemblies.
        /// </summary>
        public static HashSet<string> DependentAssemblies { get; } = new HashSet<string>()
        {
            RuntimeLibrary,
            "Microsoft.Torch.Log4Net4Torch",
        };

        /// <summary>
        /// Gets instrumenter file name.
        /// </summary>
        public static string InstrumenterFile { get; } = "TorchLiteInstrumenter.exe";

        /// <summary>
        /// Gets the name of the file whose presence indicates where the logs should be written.
        /// </summary>
        public static string LogDirMarkerFile { get; } = "TorchLogDir.txt";

        /// <summary>
        /// Gets the list of interfaces whose implementation types are not instrumented.
        /// </summary>
        public static HashSet<string> BlackListInterface { get; } = new HashSet<string>()
        {
            // "System.Runtime.CompilerServices.IAsyncStateMachine",
        };

        /// <summary>
        /// Gets the blacklist of methods that are excluded during instrumentation.
        /// </summary>
        public static List<string> MethodPrefixBlackList { get; } = new List<string>()
        {
            "Rhino.Mocks",
        };
    }
}
