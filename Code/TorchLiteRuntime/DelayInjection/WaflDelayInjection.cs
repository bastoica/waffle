#if WaflPolicy

namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    using global::TraceAnalysis;

    class DelayInjectionPolicy
    {
        /// <summary>
        /// </summary>
        private FileLogger Logger;

        /// <summary>
        /// </summary>
        private readonly Dictionary<string, DelayInjectionPlan> delayInjectionPlan = new Dictionary<string, DelayInjectionPlan>();

        /// <summary>
        /// </summary>
        private readonly HashSet<string> planForBeforeWrite = new HashSet<string>();

        /// <summary>
        /// </summary>
        private readonly HashSet<string> planForBeforeUse = new HashSet<string>();

        /// <summary>
        /// </summary>
        private readonly Dictionary<int, TorchPoint> perThreadLastTP = new Dictionary<int, TorchPoint>();

        /// <summary>
        /// </summary>
        private readonly Dictionary<int, Dictionary<string, TorchPoint>> perThreadActiveDelays = new Dictionary<int, Dictionary<string, TorchPoint>>();

        /// <summary>
        /// </summary>
        private readonly Dictionary<string, HashSet<string>> interferenceSet = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// </summary>
        private readonly Queue<TorchPoint> lastDelayQueue = new Queue<TorchPoint>();

        /// <summary>
        /// </summary>
        private readonly object activeDelaysLock = new object();

        /// <summary>
        /// </summary>
        private Dictionary<string, RacyAccess> candidateDataRaceList = new Dictionary<string, RacyAccess>();

        /// <summary>
        /// </summary>
        struct Accounting {
            public long totalDelayMs;
            public long totalDelayCount;
        };
        private Accounting Bookkeeping = new Accounting();

        /// <summary>
        /// </summary>
        private static readonly Random Random = new Random();

        public DelayInjectionPolicy(FileLogger logEntity, HighResolutionDateTime clock)
        {
            // System.Diagnostics.Debugger.Launch();

            this.Logger = logEntity;
            this.Bookkeeping.totalDelayMs = 0;
            this.Bookkeeping.totalDelayCount = 0;

            this.HookShutdown();
            this.LoadRaces();
            this.LoadOverlaps();
        }

        /// <summary>
        /// </summary>
        /// <param name="tp"></param>
        public void BeforeFieldWrite(TorchPoint tp)
        {

            string staticCodeLoc = tp.GetStaticCodeLoc();
            if (planForBeforeWrite.Contains(staticCodeLoc))
            {
                var plan = delayInjectionPlan[staticCodeLoc];
                if (plan.targetStaticTp.Caller.Equals(tp.Caller) && plan.targetStaticTp.ILOffset == tp.ILOffset)
                {
                    TryToDelay(tp, plan);
                }
            }

            lock (this.perThreadLastTP)
            {
                this.perThreadLastTP[tp.ThreadId] = tp;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="tp"></param>
        public void AfterFieldWrite(TorchPoint tp)
        {
            return; // do nothing
        }

        /// <summary>
        /// </summary>
        /// <param name="tp"></param>
        public void BeforeFieldRead(TorchPoint tp)
        {
            lock (this.perThreadLastTP)
            {
                this.perThreadLastTP[tp.ThreadId] = tp;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="tp"></param>
        public void BeforeMethodCall(TorchPoint tp)
        {
            string staticCodeLoc = tp.GetStaticCodeLoc();
            if (planForBeforeUse.Contains(staticCodeLoc))
            {
                var plan = delayInjectionPlan[staticCodeLoc];
                if (plan.targetStaticTp.Caller.Equals(tp.Caller) && plan.targetStaticTp.ILOffset == tp.ILOffset)
                {
                    TryToDelay(tp, plan);
                }
            }

            lock (this.perThreadLastTP)
            {
                this.perThreadLastTP[tp.ThreadId] = tp;
            }
        }

        /// <summary>
        /// This method attempts to delay the current thread,
        /// and succeeds only when no other delay is currently ongoing.
        /// </summary>
        /// <param name="plan"></param>
        private void TryToDelay(TorchPoint targetTp, DelayInjectionPlan targetPlan)
        {
            // Check if another thread has claimed the current sleep window
            if (Random.NextDouble() < targetPlan.DelayProbability)
            {
                Thread.BeginCriticalRegion();

                // Check if another thread has claimed the current sleep window
                if (IsInterference(targetTp) == false)
                {
#if TraceGen
                    Logger.Log($"WaflDelayInjection\t{targetPlan.DelayMs}\t{targetTp.OpType}\t{targetTp.GetUniqueFieldId()}\t{targetTp.Caller}\t{targetTp.ILOffset}", /* bool isBufferingActive = */ true);
#endif

                    // Claim the current delay window, put the thread to sleep
                    OnDelayStart(targetTp);

                    // Delay the current thread
                    Thread.Sleep(targetPlan.DelayMs);

                    // Add to delay set
                    lock (this.lastDelayQueue)
                    {
                        this.lastDelayQueue.Enqueue(targetTp);
                        if (this.lastDelayQueue.Count > Constants.DelayHistoryCount)
                        {
                            this.lastDelayQueue.Dequeue();
                        }
                    }

                    // Keep delay statistics
                    Bookkeeping.totalDelayCount++;
                    Bookkeeping.totalDelayMs += targetPlan.DelayMs;

                    // Release cuurent delay window and perform post-delay checks and cleanup
                    OnDelayEnd(targetTp, targetPlan);
                }
                else
                {
#if TraceGen
                    Logger.Log($"DelayInterferenceDetected\t{targetTp.GetUniqueFieldId()}\t{targetTp.Caller}\t{targetTp.ILOffset}", /* bool isBufferingActive = */ true);
#endif
                }

                Thread.EndCriticalRegion();
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        private void OnDelayStart(TorchPoint targetTp)
        {
            lock (activeDelaysLock)
            {
                int currentThread = targetTp.ThreadId;
                if (!perThreadActiveDelays.ContainsKey(currentThread))
                {
                    perThreadActiveDelays[currentThread] = new Dictionary<string, TorchPoint>();
                }

                // Add TorchPoint to active delay list
                var perThreadDelaySet = perThreadActiveDelays[currentThread];
                string uniqueFieldId = targetTp.GetUniqueFieldId();
                perThreadDelaySet[uniqueFieldId] = targetTp;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        /// <param name="delayDurationMs"></param>
        private void OnDelayEnd(TorchPoint targetTp, DelayInjectionPlan targetPlan)
        {
            lock (activeDelaysLock)
            {
                int currentThread = targetTp.ThreadId;
                if (perThreadActiveDelays.ContainsKey(currentThread))
                {

                    string uniqueFieldId = targetTp.GetUniqueFieldId();
                    if (perThreadActiveDelays[currentThread].ContainsKey(uniqueFieldId))
                    {
                        // Conflicting operation executed while current thread is delayed
                        // NOTE: For NULL order violation bugs, a NullRef exception has already been triggered,
                        //       which was either intercepted/handled or already forced the program to exit.
                        if (CheckForDelayPropagation(targetTp, currentThread) == true)
                        {
                            string staticLoc = targetTp.GetStaticCodeLoc();
                            if (delayInjectionPlan.ContainsKey(staticLoc))
                            {
                                // The two conflicting operations are likely happens-before relationshp,
                                // so the target torch point gets a lower delay priority in the future
                                DelayInjectionPlan plan = delayInjectionPlan[staticLoc];
                                // plan.DelayProbability = Constants.ProbabilityDecay(plan.DelayProbability);
                                plan.DelayProbability = (plan.DelayProbability > Constants.ZeroProbability) ? Math.Max(plan.DelayProbability - Constants.ProbDecayStep, Constants.ZeroProbability) : 1.0;
#if TraceGen                    
                                Logger.Log($"ProbabilityDecay\t{plan.DelayProbability}\t{targetTp.Serialze()}", /* bool isBufferingActive = */ true);
#endif
                            }
                        }

                        // Remove TorchPoint from active delay list
                        perThreadActiveDelays[currentThread].Remove(uniqueFieldId);
                    }
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        /// <param name="targetThread"></param>
        private bool CheckForDelayPropagation(TorchPoint targetTp, int targetThread)
        {
            lock (perThreadLastTP)
            {
                foreach (int tid in perThreadLastTP.Keys)
                {
                    TorchPoint lastTp = perThreadLastTP[tid];
                    if (CheckConflictingPair(targetTp, lastTp) == true) // Found an order violation bug
                    {
                        /*
                        var perThreadDelaySet = perThreadActiveDelays[targetThread];
                        long delayStart = perThreadDelaySet[ targetTp.GetUniqueFieldId() ].Timestamp;
                        long delayValue = perThreadDelaySet[ targetTp.GetUniqueFieldId() ].CurrentDelayVal;
                        
                        if ((delayStart + delayValue - lastTp.Timestamp) < targetPlan.DelayMs * Constants.NearMissWindowPercentage)
                        {
                            return false;
                        }
                        */
#if TraceGen
                        Logger.LogVerbose($"OrderViolation\t{targetTp.Serialze()}\t{lastTp.Serialze()}");
#endif

                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        private bool IsInterference(TorchPoint targetTp)
        {
            lock (activeDelaysLock)
            {
                foreach (var tid in perThreadActiveDelays.Keys)
                {
                    foreach (var objectId in perThreadActiveDelays[tid].Keys)
                    {
         
                        TorchPoint delayedTp = perThreadActiveDelays[tid][objectId];
                        if (interferenceSet.ContainsKey(delayedTp.GetStaticCodeLoc()) && interferenceSet[delayedTp.GetStaticCodeLoc()].Contains(targetTp.GetStaticCodeLoc()))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        /// <param name="conflictTp"></param>
        /// <returns>bool.</returns>
        private bool CheckConflictingPair(TorchPoint targetTp, TorchPoint conflictTp)
        {
            string targetId = targetTp.GetUniqueFieldId();
            string conflictId = conflictTp.GetUniqueFieldId();
            string racePairKey = GetRacePairKey(targetTp, conflictTp);

            if (targetId.Equals(conflictId) && // same object
                targetTp.ThreadId != conflictTp.ThreadId && // different threads
                this.candidateDataRaceList.ContainsKey(racePairKey)) // part of the current delay injection plan
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        public void CheckOrderViolation(TorchPoint conflictTp)
        {
            lock (this.lastDelayQueue)
            {
                foreach (var targetTp in this.lastDelayQueue)
                {
                    if (CheckConflictingPair(targetTp, conflictTp) == true)
                    {
#if TraceGen
                        Logger.LogVerbose($"OrderViolation\t{targetTp.Serialze()}\t{conflictTp.Serialze()}");
#endif
                    }
                }
            }
        }
        
        /// <summary>
        /// </summary>
        /// <param name="conflict"></param>
        /// <param name="e"></param>
        /// <returns>bool.</returns>
        public bool CheckOrderViolation(string callStack)
        {
            lock (this.lastDelayQueue)
            {
                foreach (var source in this.lastDelayQueue)
                {
                    foreach (var key in this.candidateDataRaceList.Keys)
                    {
                        var race = this.candidateDataRaceList[key];
                        string methodCall = TorchPoint.ExtractMethodName(TorchPoint.ExtractMethodName(race.Read.CallerMethod));
                        if (callStack.Contains(methodCall))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// </summary>
        string GetRacePairKey(TorchPoint write, TorchPoint read)
        {
            string racePairKey = write.GetStaticCodeLoc() + "!" + read.GetStaticCodeLoc();
            return racePairKey;
        }

        /// <summary>
        /// </summary>
        string GetRacePairKey(MemoryAccess write, MemoryAccess read)
        {
            string racePairKey = write.GetStaticLoc() + "!" + read.GetStaticLoc();
            return racePairKey;
        }

        /// <summary>
        /// </summary>
        private void LoadRaces()
        {
            if (File.Exists(Constants.RaceFileName))
            {
                // Constants.DisableLogging = true;

                List<RacyAccess> raceList = ConflictsAnalyzer.DeserializeRaces(Constants.RaceFileName);
                var prevDelayPr = PersistentConfig.ReadInProbs(Constants.DelayProbsFileName);

                foreach (var race in raceList)
                {
                    string staticLoc = null;
                    TorchPoint tp;
                    DelayInjectionPlan plan = new DelayInjectionPlan();

                    switch (race.WriteType)
                    {
                        case WriteType.NullToNonNull:
                            // inject delay before fieldWrite
                            tp = new TorchPoint(
                                        0,
                                        MemoryAccessType.Write,
                                        null,
                                        race.Write.MemoryId,
                                        null,
                                        race.Write.CallerMethod,
                                        int.Parse(race.Write.CallerILOffset),
                                        Guid.Empty,
                                        Guid.Empty);

                            plan.DelayInjectionEvent = DelayInjectionEvent.BeforeWrite;
                            plan.targetStaticTp = tp;
                            plan.DelayMs = Math.Min((int)((race.AccessGapMs + 1) * Constants.DelayFactor), Constants.MaxDelayValueMs);
                            plan.DelayProbability = prevDelayPr.ContainsKey(staticLoc = tp.GetStaticCodeLoc()) ? (prevDelayPr[staticLoc] > Constants.ZeroProbability ? prevDelayPr[staticLoc] : 1.0) : 1.0;

                            planForBeforeWrite.Add(plan.targetStaticTp.GetStaticCodeLoc());

                            break;

                        case WriteType.NonNullToNull:
                            // insert delay before field use
                            tp = new TorchPoint(
                                        0,
                                        MemoryAccessType.Use,
                                        null,
                                        race.Write.MemoryId,
                                        null,
                                        race.Read.CallerMethod,
                                        int.Parse(race.Read.CallerILOffset),
                                        Guid.Empty,
                                        Guid.Empty);

                            plan.DelayInjectionEvent = DelayInjectionEvent.BeforeUse;
                            plan.targetStaticTp = tp;
                            plan.DelayMs = Math.Min((int)((race.AccessGapMs + 1) * Constants.DelayFactor), Constants.MaxDelayValueMs);
                            plan.DelayProbability = prevDelayPr.ContainsKey(staticLoc = tp.GetStaticCodeLoc()) ? (prevDelayPr[staticLoc] > Constants.ZeroProbability ? prevDelayPr[staticLoc] : 1.0) : 1.0;

                            planForBeforeUse.Add(plan.targetStaticTp.GetStaticCodeLoc());

                            break;

                        case WriteType.Dispose:
                            // insert delay before field use
                            tp = new TorchPoint(
                                        0,
                                        MemoryAccessType.Dispose,
                                        null,
                                        race.Write.MemoryId,
                                        null,
                                        race.Read.CallerMethod,
                                        int.Parse(race.Read.CallerILOffset),
                                        Guid.Empty,
                                        Guid.Empty);

                            plan.DelayInjectionEvent = DelayInjectionEvent.BeforeUse;
                            plan.targetStaticTp = tp;
                            plan.DelayMs = Math.Min((int)((race.AccessGapMs + 1) * Constants.DelayFactor), Constants.MaxDelayValueMs);
                            plan.DelayProbability = prevDelayPr.ContainsKey(staticLoc = tp.GetStaticCodeLoc()) ? (prevDelayPr[staticLoc] > Constants.ZeroProbability ? prevDelayPr[staticLoc] : 1.0) : 1.0;

                            planForBeforeUse.Add(plan.targetStaticTp.GetStaticCodeLoc());

                            break;
                    }

                    if (!delayInjectionPlan.ContainsKey(staticLoc))
                    {
                        delayInjectionPlan[staticLoc] = plan;
                    }

                    // Take the maximum time gap over all dynamic instances of the same static data race
                    delayInjectionPlan[staticLoc].DelayMs = Math.Max(delayInjectionPlan[staticLoc].DelayMs, plan.DelayMs);
                    // TODO: merge this with TorchPoint.GetStaticCodeLoc
                    delayInjectionPlan[staticLoc].ConflictOps.Add($"{race.Read.CallerMethod}|{race.Read.CallerILOffset}");
                }

                foreach (var race in raceList)
                {
                    string racePairKey = string.Empty;
                    switch (race.WriteType)
                    {
                        case WriteType.NullToNonNull:
                            racePairKey = GetRacePairKey(race.Write, race.Read);
                            break;

                        case WriteType.NonNullToNull:
                        case WriteType.Dispose:
                            racePairKey = GetRacePairKey(race.Read, race.Write);
                            break;
                    }

                    this.candidateDataRaceList[racePairKey] = race;
                }
            }
        }

        /// <summary>
        /// </summary>
        private void LoadOverlaps()
        {
            if (File.Exists(Constants.OverlapFileName))
            {
                var overlaps = ConflictsAnalyzer.DeserializeOverlaps(Constants.OverlapFileName);

                foreach (var pair in overlaps)
                {
                    string interceptUniqeId = pair.Intercept.GetStaticLoc();
                    string overlapUniqueId = pair.Overlap.GetStaticLoc();

                    if (!interferenceSet.ContainsKey(interceptUniqeId))
                    {
                        interferenceSet[interceptUniqeId] = new HashSet<string>();
                    }

                    interferenceSet[interceptUniqeId].Add(overlapUniqueId);
                }
            }
        }

        private void HookShutdown()
        {
            AppDomain.CurrentDomain.DomainUnload += (s, e) => this.Shutdown();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => this.Shutdown();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => this.Shutdown();
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                this.Shutdown();
                return null;
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (s, e) =>
            {
                this.Shutdown();
                return null;
            };
            AppDomain.CurrentDomain.ResourceResolve += (s, e) =>
            {
                this.Shutdown();
                return null;
            };
        }

        private void Shutdown()
        {
            PersistentConfig.WriteOutProbs(delayInjectionPlan, Constants.DelayProbsFileName);
            PersistentConfig.WriteOutStats($"{Bookkeeping.totalDelayMs}\t{Bookkeeping.totalDelayCount}", Constants.StatsFileName);
            Logger.Shutdown();
        }
    }
}

#endif