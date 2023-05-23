#if WaflBasicPolicy

namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using global::TraceAnalysis;

    class TPAccessHistory
    {
        private List<TorchPoint> AccessList = new List<TorchPoint>();
        private Queue<TorchPoint> AccessWindow = new Queue<TorchPoint>();
        private List<TorchPoint> CtorList = new List<TorchPoint>();

        public void Add(TorchPoint tp)
        {
            AccessList.Add(tp);

            AccessWindow.Enqueue(tp);
            if (AccessWindow.Count > 1)
            {
                TorchPoint head = AccessWindow.Peek();
                double deltaMs = (int)Math.Ceiling(1.0 * (tp.Timestamp - head.Timestamp) / TimeSpan.TicksPerMillisecond);
                if (deltaMs > Constants.NearMissWindowMs || AccessWindow.Count > Constants.NearMissWindowCount)
                {
                    AccessWindow.Dequeue();
                }
            }

            // TODO: Make this a TorchPoint class method
            if (tp.OpType == MemoryAccessType.Write && tp.CurrentValue == Guid.Empty && tp.NewValue != Guid.Empty)
            {
                CtorList.Add(tp);
            }
        }

        public IEnumerable<RacyAccess> GetPotentialRaces(TorchPoint tp)
        {
            // TODO: Make this a TorchPoint class method
            if (tp.OpType.Equals(MemoryAccessType.Use) || tp.OpType.Equals(MemoryAccessType.Dispose))
            {
                for (var i = this.CtorList.Count - 1; i >= 0; --i)
                {
                    TorchPoint conflict = this.CtorList[i];
                    if (tp.ThreadId == conflict.ThreadId || !tp.FieldName.Equals(conflict.FieldName))
                    {
                        continue;
                    }

                    double deltaMs = (int)Math.Ceiling(1.0 * (tp.Timestamp - conflict.Timestamp) / TimeSpan.TicksPerMillisecond);
                    if (deltaMs > 0 && deltaMs > Constants.NearMissWindowMs)
                    {
                        continue;
                    }

                    MemoryAccess write = new MemoryAccess() { Timestamp = conflict.Timestamp, ThreadId = conflict.ThreadId, TaskId = conflict.TaskId, MemoryId = conflict.GetUniqueFieldId(), AccessType = conflict.OpType, CallerMethod = conflict.Caller, CallerILOffset = conflict.ILOffset.ToString() };
                    MemoryAccess read = new MemoryAccess() { Timestamp = tp.Timestamp, ThreadId = tp.ThreadId, TaskId = tp.TaskId, MemoryId = tp.GetUniqueFieldId(), AccessType = tp.OpType, CallerMethod = tp.Caller, CallerILOffset = tp.ILOffset.ToString() };

                    yield return new RacyAccess(read, read.AccessType.Equals(MemoryAccessType.Use) ? ReadType.Use : ReadType.Read, write, WriteType.NullToNonNull);
                }
            }

            if (tp.OpType.Equals(MemoryAccessType.Dispose) || (tp.OpType.Equals(MemoryAccessType.Write) && tp.CurrentValue != Guid.Empty && tp.NewValue == Guid.Empty))
            {
                foreach (var conflict in AccessWindow)
                {
                    if (tp.ThreadId == conflict.ThreadId || !tp.FieldName.Equals(conflict.FieldName))
                    {
                        continue;
                    }

                    double deltaMs = (int)Math.Ceiling(1.0 * (tp.Timestamp - conflict.Timestamp) / TimeSpan.TicksPerMillisecond);
                    if (deltaMs > 0 && deltaMs > Constants.NearMissWindowMs)
                    {
                        continue;
                    }

                    MemoryAccess write = new MemoryAccess() { Timestamp = tp.Timestamp, ThreadId = tp.ThreadId, TaskId = tp.TaskId, MemoryId = tp.GetUniqueFieldId(), AccessType = tp.OpType, CallerMethod = tp.Caller, CallerILOffset = tp.ILOffset.ToString() };
                    MemoryAccess read = new MemoryAccess() { Timestamp = conflict.Timestamp, ThreadId = conflict.ThreadId, TaskId = conflict.TaskId, MemoryId = conflict.GetUniqueFieldId(), AccessType = conflict.OpType, CallerMethod = conflict.Caller, CallerILOffset = conflict.ILOffset.ToString() };

                    yield return new RacyAccess(read, read.AccessType.Equals(MemoryAccessType.Use) ? ReadType.Use : ReadType.Read, write, write.AccessType.Equals(MemoryAccessType.Dispose) ? WriteType.Dispose : WriteType.NonNullToNull);
                }
            }
        }
    }


    class DelayInjectionPolicy
    {
        /// <summary>
        /// </summary>
        private FileLogger Logger;

        /// <summary>
        /// </summary>
        private HighResolutionDateTime HighResClock;

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
        private readonly Dictionary<string, TPAccessHistory> perObjectTP = new Dictionary<string, TPAccessHistory>();

        /// <summary>
        /// </summary>
        private readonly Dictionary<int, Dictionary<string, TorchPoint>> perThreadActiveDelays = new Dictionary<int, Dictionary<string, TorchPoint>>();

        /// <summary>
        /// </summary>
        private readonly Queue<TorchPoint> lastDelayQueue = new Queue<TorchPoint>();

        /// <summary>
        /// </summary>
        private readonly Dictionary<string, RacyAccess> candidateDataRaceList = new Dictionary<string, RacyAccess>();

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
            this.Logger = logEntity;
            this.HighResClock = clock;
            this.Bookkeeping.totalDelayMs = 0;
            this.Bookkeeping.totalDelayCount = 0;

            PersistentConfig.LoadConfigFile();
            this.LoadRaces();
            this.HookShutdown();
        }

        private void RecordTP(TorchPoint tp)
        {
            lock (this.perThreadLastTP)
            {
                this.perThreadLastTP[tp.ThreadId] = tp;
            }

            lock (this.perThreadLastTP)
            {
                if (!this.perObjectTP.ContainsKey(tp.GetUniqueFieldId()))
                {
                    this.perObjectTP[tp.GetUniqueFieldId()] = new TPAccessHistory();
                }

                this.perObjectTP[tp.GetUniqueFieldId()].Add(tp);

                // TODO: This is an ungraceful way to add potential racy pairs to the current delay plan!!!
                IEnumerable<RacyAccess> races = this.perObjectTP[tp.GetUniqueFieldId()].GetPotentialRaces(tp);
                foreach (var race in races)
                {
                    string racePairKey = GetRacePairKey(race.Write, race.Read);
                    if (this.candidateDataRaceList.ContainsKey(racePairKey))
                    {
                        continue;
                    }

                    this.candidateDataRaceList[racePairKey] = race;
                    AddToDelayPlan(race);
                }
            }
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

            RecordTP(tp);
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
            RecordTP(tp);
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

            RecordTP(tp);
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

#if TraceGen
                Logger.Log($"WBasicDelayInjection\t{targetPlan.DelayMs}\t{targetTp.OpType}\t{targetTp.GetUniqueFieldId()}\t{targetTp.Caller}\t{targetTp.ILOffset}", /* bool isBufferingActive = */ true);
#endif

                // Claim the current delay window, put the thread to sleep
                OnDelayStart(targetTp);

                // Delay the current thread
                Thread.Sleep(targetPlan.DelayMs);

                // Keep delay statistics
                Bookkeeping.totalDelayCount++;
                Bookkeeping.totalDelayMs += targetPlan.DelayMs;

                // Add to delay set
                lock (this.lastDelayQueue)
                {
                    this.lastDelayQueue.Enqueue(targetTp);
                    if (this.lastDelayQueue.Count > Constants.DelayHistoryCount)
                    {
                        this.lastDelayQueue.Dequeue();
                    }
                }

                // Release curent delay window and perform post-delay checks and cleanup
                OnDelayEnd(targetTp, targetPlan);

                Thread.EndCriticalRegion();
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="plan"></param>
        private void OnDelayStart(TorchPoint targetTp)
        {
            lock (perThreadActiveDelays)
            {
                int currentThread = targetTp.ThreadId;
                if (!perThreadActiveDelays.ContainsKey(currentThread))
                {
                    perThreadActiveDelays[currentThread] = new Dictionary<string, TorchPoint>();
                }

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
            lock (perThreadActiveDelays)
            {
                int currentThread = targetTp.ThreadId;
                if (perThreadActiveDelays.ContainsKey(currentThread))
                {
                    var perThreadDelaySet = perThreadActiveDelays[currentThread];
                    string uniqueFieldId = targetTp.GetUniqueFieldId();
                    if (perThreadDelaySet.ContainsKey(uniqueFieldId))
                    {
                        long delayStart = perThreadDelaySet[uniqueFieldId].Timestamp;
                        bool hasPropagated = true;

                        lock (perThreadLastTP)
                        {
                            foreach (int tid in perThreadLastTP.Keys)
                            {
                                TorchPoint lastTp = perThreadLastTP[tid];
                                if ((delayStart + targetPlan.DelayMs - lastTp.Timestamp) < targetPlan.DelayMs * Constants.NearMissWindowPercentage)
                                {
                                    // Conflicting operation executed while current thread is delayed
                                    // NOTE: For NULL order violation bugs, a NullRef exception has already been triggered,
                                    //       which was either intercepted/handled or already forced the program to exit.
                                    if (IsConflicting(targetTp, lastTp) == true)
                                    {
                                        hasPropagated = false;
#if TraceGen
                                        Logger.LogVerbose($"OrderViolation\t{targetTp.Serialze()}\t{lastTp.Serialze()}");
#endif
                                    }
                                }
                            }
                        }

                        if (hasPropagated == true)
                        {
                            lock (delayInjectionPlan)
                            {
                                string staticLoc = targetTp.GetStaticCodeLoc();
                                if (delayInjectionPlan.ContainsKey(staticLoc))
                                {
                                    // The two conflicting operations are likely happens-before relationshp,
                                    // so the target torch point gets a lower delay priority in the future
                                    DelayInjectionPlan plan = delayInjectionPlan[staticLoc];
                                    plan.DelayProbability = (plan.DelayProbability > Constants.ZeroProbability) ? Math.Max(plan.DelayProbability - Constants.ProbDecayStep, Constants.ZeroProbability) : 1.0;
#if TraceGen
                                    Logger.Log($"ProbabilityDecay\t{plan.DelayProbability}\t{targetTp.Serialze()}", /* bool isBufferingActive = */ true);
#endif
                                }
                            }
                        }
                    }

                    perThreadActiveDelays[currentThread].Remove(uniqueFieldId);
                }
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="targetTp"></param>
        /// <param name="conflictTp"></param>
        /// <returns>bool.</returns>
        private bool IsConflicting(TorchPoint targetTp, TorchPoint conflictTp)
        {
            string targetId = targetTp.GetUniqueFieldId();
            string conflictId = conflictTp.GetUniqueFieldId();
            DelayInjectionPlan plan = delayInjectionPlan[targetTp.GetStaticCodeLoc()];

            if (targetId.Equals(conflictId) && // same object
                targetTp.ThreadId != conflictTp.ThreadId && // different threads
                plan.ConflictOps.Contains(conflictTp.GetStaticCodeLoc())) // part of the current delay injection plan
            {
                return true;
            }

            return false;
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
        private void LoadRaces()
        {
            // Constants.DisableLogging = true;

            if (File.Exists(Constants.RaceFileName))
            {
                // Add races to current plan
                var races = ConflictsAnalyzer.DeserializeRaces(Constants.RaceFileName);
                foreach (var race in races)
                {
                    string racePairKey = GetRacePairKey(race.Write, race.Read);
                    if (this.candidateDataRaceList.ContainsKey(racePairKey))
                    {
                        continue;
                    }

                    this.candidateDataRaceList[racePairKey] = race;
                    AddToDelayPlan(race);
                }

                // Update delay injection probability
                // TODO: Merge delay probability within Candidates.wfl
                // TODO2: Reconcile PersistentConfig with TraceAnalysis and standardize writing out to disk
                var prevDelayPr = PersistentConfig.ReadInProbs(Constants.DelayProbsFileName);
                foreach (var id in delayInjectionPlan.Keys)
                {
                    var plan = delayInjectionPlan[id];
                    plan.DelayProbability = prevDelayPr.ContainsKey(plan.targetStaticTp.GetStaticCodeLoc()) ? prevDelayPr[plan.targetStaticTp.GetStaticCodeLoc()] : 1.0;
                }
            }
        }

        private void AddToDelayPlan(RacyAccess race)
        {
            if (race.AccessGapMs > Constants.NearMissWindowMs)
            {
                return; // ignore racy pairs with large gaps
            }

            TorchPoint tp = null;
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
                    plan.DelayMs = Constants.MaxDelayValueMs;
                    plan.DelayProbability = 1.0;

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
                    plan.DelayMs = Constants.MaxDelayValueMs;
                    plan.DelayProbability = 1.0;

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
                    plan.DelayMs = Constants.MaxDelayValueMs;
                    plan.DelayProbability = 1.0;

                    planForBeforeUse.Add(plan.targetStaticTp.GetStaticCodeLoc());

                    break;
            }

            string staticLoc = tp.GetStaticCodeLoc();
            if (!delayInjectionPlan.ContainsKey(staticLoc))
            {
                delayInjectionPlan[staticLoc] = plan;
            }

            // If already planned, incease only the delay value; leave other metrics (e.g., injection probability,
            // hit count threshold, etc.) unchanged
            delayInjectionPlan[staticLoc].DelayMs = Math.Max(delayInjectionPlan[staticLoc].DelayMs, plan.DelayMs);
            // TODO: merge this with TorchPoint.GetStaticCodeLoc
            delayInjectionPlan[staticLoc].ConflictOps.Add($"{race.Read.CallerMethod}|{race.Read.CallerILOffset}");
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
            // TODO: Merge delay probability within Candidates.wfl
            PersistentConfig.WriteOutProbs(delayInjectionPlan, Constants.DelayProbsFileName);
            // TODO: Move this if condition inside SerializeRaces() method
            if (candidateDataRaceList.Count > 0)
            {
                List<RacyAccess> raceList = new List<RacyAccess>();
                raceList.AddRange(this.candidateDataRaceList.Values);
                ConflictsAnalyzer.SerializeRaces(raceList, Constants.RaceFileName);
            }
            PersistentConfig.WriteOutStats($"{Bookkeeping.totalDelayMs}\t{Bookkeeping.totalDelayCount}", Constants.StatsFileName);
            Logger.Shutdown();
        }
    }
}

#endif