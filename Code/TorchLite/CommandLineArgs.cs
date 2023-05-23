// <copyright file="CommandLineArgs.cs" company="Microsoft Research">
// Copyright (c) Microsoft Research. All rights reserved.
// </copyright>

namespace TorchLite
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// TorchLite command line argument.
    /// </summary>
    public class CommandLineArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandLineArgs"/> class.
        /// </summary>
        /// <param name="args">Arguments.</param>
        public CommandLineArgs(string[] args)
        {
            this.ArgumentsValid = args.Length >= 1;

            if (!this.ArgumentsValid)
            {
                return;
            }

            string target = args[0];
            if (target.Contains("*"))
            {
                string path = Path.GetDirectoryName(target);
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = ".";
                }

                string filePattern = Path.GetFileName(target);
                this.Assemblies = new List<string>();

                foreach (var targetFile in Directory.GetFiles(path, filePattern))
                {
                    if (this.InstrumentationTargetValid(targetFile))
                    {
                        this.Assemblies.Add(targetFile);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Skipping invalid instrumentation target: {targetFile}");
                    }
                }

                if (this.Assemblies.Count == 0)
                {
                    Console.Error.WriteLine($"ERROR: Nothing to instrument.");
                    this.ArgumentsValid = false;
                }
            }
            else if (File.Exists(target))
            {
                if (this.InstrumentationTargetValid(target))
                {
                    this.Assemblies = new List<string>() { target };
                }
                else
                {
                    Console.Error.WriteLine($"ERROR: Not a valid instrumentation target: {target}");
                    this.ArgumentsValid = false;
                }
            }
            else if (Directory.Exists(target))
            {
                var validAssemblies = Directory.GetFiles(target).Where(x => this.InstrumentationTargetValid(x));
                if (validAssemblies.Count() > 0)
                {
                    this.Assemblies = new List<string>(validAssemblies);
                }
                else
                {
                    Console.Error.WriteLine($"No assemblies in the given directory {target}");
                    this.ArgumentsValid = false;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether command line arguments are valid.
        /// </summary>
        public bool ArgumentsValid { get; }

        /// <summary>
        /// Gets the list of assemblies to instrument.
        /// </summary>
        public List<string> Assemblies { get; }

        /// <summary>
        /// Gets a value indicating whether signed assemblies should also be instrumented.
        /// </summary>
        public bool InstrumentSigned { get; }

        /// <summary>
        /// Prints usage.
        /// </summary>
        public void Usage()
        {
            Console.WriteLine("Usage: <assembly|directory>");
        }

        private bool InstrumentationTargetValid(string filepath)
        {
            string fileExt = Path.GetExtension(filepath);
            if (string.Equals(".dll", fileExt, StringComparison.OrdinalIgnoreCase) || string.Equals(".exe", fileExt, StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(filepath);
                if (!string.Equals(fileName, Constants.RuntimeLibraryFile, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(fileName, Constants.InstrumenterFile, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
