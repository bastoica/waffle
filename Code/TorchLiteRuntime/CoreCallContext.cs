namespace TorchLiteRuntime
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Runtime.Remoting.Messaging;
    using System.Threading;

    /// <summary>
    /// Provides a way to set contextual data that flows with the call and 
    /// async context of a test or invocation.
    /// This can be used as a replacement for .NET Core/Standard where CallContext is not available.
    /// </summary>
    public static class CoreCallContext
    {
        static ConcurrentDictionary<string, AsyncLocal<object>> alState = new ConcurrentDictionary<string, AsyncLocal<object>>();
        static ConcurrentDictionary<string, ThreadLocal<object>> tlState = new ConcurrentDictionary<string, ThreadLocal<object>>();

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void LogicalSetData(string name, object data) =>
            alState.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object LogicalGetData(string name) =>
            alState.TryGetValue(name, out AsyncLocal<object> data) ? data.Value : null;

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            tlState.GetOrAdd(name, _ => new ThreadLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name) =>
            tlState.TryGetValue(name, out ThreadLocal<object> data) ? data.Value : null;

    }

}
