namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.ExceptionServices;
    using System.Threading;
    using TraceAnalysis;

    /// <summary>
    /// Torchlite callbacks.
    /// </summary>
    public class Callbacks
    {
        private static readonly HighResolutionDateTime highResClock = new HighResolutionDateTime();

        private static readonly FileLogger Logger = new FileLogger(Constants.LogFileName);

        private static readonly Dictionary<Guid, string> UniqueFieldNameTable = new Dictionary<Guid, string>();

        private static DelayInjectionPolicy delayPolicy;// = new DelayInjectionPolicy(Logger, highResClock);

        static Callbacks()
        {
            // System.Diagnostics.Debugger.Launch();

            delayPolicy = new DelayInjectionPolicy(Logger, highResClock);
            AppDomain.CurrentDomain.FirstChanceException += FirstChanceHandler;
        }

        /// <summary>
        /// </summary>
        /// <param name="parentObject">Object owning the field.</param>
        static void FirstChanceHandler(object source, FirstChanceExceptionEventArgs e)
        {
            switch (e.Exception.GetType().FullName)
            {
                case "System.NullReferenceException":
                case "System.ObjectDisposedException":
                case "System.AccessViolationException":
                case "System.ArgumentNullException":
                case "System.FieldAccessException":
                case "System.IndexOutOfRangeException":
                    if (delayPolicy.CheckOrderViolation(Environment.StackTrace))
                    {
#if TraceGen
                        Logger.LogVerbose($"[EXCEPTION]:\tError\t : {e.Exception.Message}");
                        Logger.LogVerbose($"[EXCEPTION]:\tObject Name : {e.Exception.Source}");
                        Logger.LogVerbose($"[EXCEPTION]:\tTarget Site : {e.Exception.TargetSite}");
                        Logger.LogVerbose($"[EXCEPTION]:\tStack Trace :\n{e.Exception.StackTrace}");
                        Logger.LogVerbose($"[EXCEPTION]:\t------Stack--Trace------");
                        Logger.LogVerbose($"[EXCEPTION]:\t{Environment.StackTrace}");
                        Logger.LogVerbose($"[EXCEPTION]:\t------------------------");
#endif
                    }
                    break;

                default:
                    // Do nothing
                    break;
            }
        }

        /// <summary>
        /// Callback invoked before a field is written.
        /// </summary>
        /// <param name="parentObject">Object owning the field.</param>
        /// <param name="fieldName">Field name.</param>
        /// <param name="currentValue">Field value before the write.</param>
        /// <param name="newValue">Field value after the write.</param>
        /// <param name="caller">Method that writes the field.</param>
        /// <param name="ilOffset">ILOffset where the write happens.</param>
        public static void BeforeFieldWrite(object parentObject, string fieldName, object currentValue, object newValue, string caller, int ilOffset)
        {
            string uniqueFieldName = GetUniqueFieldId(parentObject, fieldName);
            Guid currValueId = ObjectId.GetRefId(currentValue);
            Guid newValueId = ObjectId.GetRefId(newValue);

            lock (UniqueFieldNameTable)
            {
                UniqueFieldNameTable[newValueId] = uniqueFieldName;
            }

            long currentTimestamp = highResClock.GetElapsedTicks();
            string parentObjectId = ExcludeFieldName(uniqueFieldName);

            // Check if this write is part of a potential race; attempt to trigger by injecting delay.
            TorchPoint tp = new TorchPoint(
                                    currentTimestamp,
                                    MemoryAccessType.Write,
                                    parentObjectId,
                                    fieldName,
                                    null,
                                    caller,
                                    ilOffset,
                                    currValueId,
                                    newValueId);
            delayPolicy.BeforeFieldWrite(tp);

#if TraceGen
            Logger.Log($"BeforeFieldWrite\t{uniqueFieldName}\t{currValueId}\t{newValueId}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif
        }

        /// <summary>
        /// Callback invoked after a field is written.
        /// </summary>
        /// <param name="parentObject">Object owning the field.</param>
        /// <param name="fieldName">Field name.</param>
        /// <param name="fieldValue">Field value after the write.</param>
        /// <param name="caller">Method that writes the field.</param>
        /// <param name="ilOffset">ILOffset where the write happens.</param>
        public static void AfterFieldWrite(object parentObject, string fieldName, object fieldValue, string caller, int ilOffset)
        {
            string uniqueFieldName = GetUniqueFieldId(parentObject, fieldName);
            Guid currValueId = ObjectId.GetRefId(fieldValue);

            long currentTimestamp = highResClock.GetElapsedTicks();
            string parentObjectId = ExcludeFieldName(uniqueFieldName);
            TorchPoint tp = new TorchPoint(
                                    currentTimestamp,
                                    MemoryAccessType.Write,
                                    parentObjectId,
                                    fieldName,
                                    null,
                                    caller,
                                    ilOffset,
                                    currValueId,
                                    Constants.NullGuid);
            delayPolicy.AfterFieldWrite(tp);

#if TraceGen
            Logger.Log($"AfterFieldWrite\t{uniqueFieldName}\t{currValueId}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif
        }

        /// <summary>
        /// Callback invoked before a field is read.
        /// </summary>
        /// <param name="parentObject">Object owning the field.</param>
        /// <param name="fieldName">Field name.</param>
        /// <param name="fieldValue">Field value.</param>
        /// <param name="caller">Method that writes the field.</param>
        /// <param name="ilOffset">ILOffset where the write happens.</param>
        public static void BeforeFieldRead(object parentObject, string fieldName, object fieldValue, string caller, int ilOffset)
        {
            string uniqueFieldName = GetUniqueFieldId(parentObject, fieldName);
            Guid currentVal = ObjectId.GetRefId(fieldValue);

            long currentTimestamp = highResClock.GetElapsedTicks();
            string parentObjectId = ExcludeFieldName(uniqueFieldName);

            // Record the read for bookkeeping.
            TorchPoint tp = new TorchPoint(
                                    currentTimestamp,
                                    MemoryAccessType.Read,
                                    parentObjectId,
                                    fieldName,
                                    null,
                                    caller,
                                    ilOffset,
                                    currentVal,
                                    Guid.Empty);
            delayPolicy.BeforeFieldRead(tp);

#if TraceGen
            Logger.Log($"BeforeFieldRead\t{uniqueFieldName}\t{currentVal}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif
        }

        /// <summary>
        /// Callback invoked before a method is called.
        /// </summary>
        /// <param name="instance">Instance object on which the method is called. Null if the method is static.</param>
        /// <param name="caller">Parent method that calls the method.</param>
        /// <param name="ilOffset">ILOffset where the method is invoked.</param>
        /// <param name="callee">Name of the called method.</param>
        /// <returns>A context given to AfterMethodCall callback.</returns>
        public static MethodCallbackContext BeforeMethodCall(object instance, string caller, int ilOffset, string callee)
        {
#if VCLOCKS_ENABLED
            VectorClock.InitThreadIfNeeded();
#endif
            long currentTimestamp = highResClock.GetElapsedTicks();

            // We are interested only for method calls that are invoked on fields.
            // We filter such a method by checking that (1) the method is an instance method,
            // and (2) the instance is a field (i.e., it appears in UniqueFieldNameTable).
            if (instance != null)
            {
                Guid objId = ObjectId.GetRefId(instance);

                string uniqueFieldName = String.Empty;
                string fieldName = String.Empty;
                string parentObjectId = String.Empty;

                lock (UniqueFieldNameTable)
                {
                    if (!UniqueFieldNameTable.ContainsKey(objId))
                    {
                        return null; // instance is not a field
                    }

                    uniqueFieldName = UniqueFieldNameTable[objId];
                    fieldName = ExcludeParentObjectId(UniqueFieldNameTable[objId]);
                    parentObjectId = ExcludeFieldName(UniqueFieldNameTable[objId]);
                }

                // Check if this method is part of a potential race and we need to verify the race by injecting delay.
                TorchPoint tp = new TorchPoint(
                                        currentTimestamp,
                                        callee.EndsWith("::Dispose") ? MemoryAccessType.Dispose : MemoryAccessType.Use,
                                        parentObjectId,
                                        fieldName,
                                        null,
                                        caller,
                                        ilOffset,
                                        Guid.Empty,
                                        Guid.Empty);
                delayPolicy.BeforeMethodCall(tp);

#if TraceGen
                Logger.Log($"BeforeMethodCall\t{uniqueFieldName}\t{callee}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif

                return new MethodCallbackContext() { Instance = instance, FieldId = uniqueFieldName, Caller = caller, ILOffset = ilOffset, Callee = callee };
            }
            else if (callee.StartsWith("System.Threading.Monitor::"))
            {
#if TraceGen
                Logger.Log($"BeforeMethodCall\tLockMethod\t{callee}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif
                return null;
            }
            else
            {
                TorchPoint tp = new TorchPoint(
                                        currentTimestamp,
                                        callee.EndsWith("::Dispose") ? MemoryAccessType.Dispose : MemoryAccessType.Use,
                                        String.Empty,
                                        String.Empty,
                                        null,
                                        caller,
                                        ilOffset,
                                        Guid.Empty,
                                        Guid.Empty);
                delayPolicy.CheckOrderViolation(tp);
#if TraceGen
                // Logger.Log($"BeforeMethodCall\tOtherMethod\t{callee}\t{caller}\t{ilOffset}", /* bool isBufferingActive = */ true);
#endif
                return null;
            }
        }

        /// <summary>
        /// Callback invoked after a method call.
        /// </summary>
        /// <param name="methodCallContext">Call context returned by BeforeMethodCall callback.</param>
        public static void AfterMethodCall(object methodCallContext)
        {
            var context = methodCallContext as MethodCallbackContext;
            if (context != null)
            {
                var instanceId = ObjectId.GetRefId(context.Instance);

#if TraceGen
                Logger.Log($"AfterMethodCall\t{context.FieldId}\t{context.Callee}\t{context.Caller}\t{context.ILOffset}\t{instanceId}", /* bool isBufferingActive = */ true);
#endif
            }
        }

        private static string GetUniqueFieldId(object parentObject, string fieldName)
        {
            Guid parentObjId = ObjectId.GetRefId(parentObject);
            return $"{parentObjId}{Constants.ObjectIdSeparator}{fieldName}";
        }

        private static string ExcludeParentObjectId(string uniqueFieldName)
        {
            int sepIndex = uniqueFieldName.IndexOf(Constants.ObjectIdSeparator);
            return uniqueFieldName.Substring(sepIndex + 1);
        }

        private static string ExcludeFieldName(string uniqueFieldName)
        {
            int sepIndex = uniqueFieldName.IndexOf(Constants.ObjectIdSeparator);
            return uniqueFieldName.Substring(0, sepIndex);
        }
    }
}
