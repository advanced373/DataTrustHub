using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataTrustHub.Application.Abstractions.Behaviors
{
    public static class LoggerExtensions
    {
        static readonly Action<ILogger, Exception> failedToProcessTodoItem = LoggerMessage.Define(LogLevel.Critical, new EventId(13, nameof(FailedToProcessTodoItem)), "Epic failure processing item!");
        static readonly Func<ILogger, DateTime, IDisposable?> processingTodoScope = LoggerMessage.DefineScope<DateTime>("Processing work, started at: {DateTime}");
        static readonly Func<ILogger, DateTime, IDisposable?> webApiScope = LoggerMessage.DefineScope<DateTime>("WebApi work, started at: {DateTime}");
        static readonly Action<ILogger, Exception> unhandledException = LoggerMessage.Define(LogLevel.Critical, new EventId(13, nameof(UnhandledException)), "Unhandled exception occurred");
        /// <summary>Failed to process to do item.</summary>
        /// <param name="logger">Logger</param>
        /// <param name="ex">Exception.</param>
        public static void FailedToProcessTodoItem(this ILogger logger, Exception ex) => failedToProcessTodoItem(logger, ex);

        /// <summary>Processing work scope</summary>
        /// <param name="logger">Logger.</param>
        /// <param name="time">Date time.</param>
        /// <returns>Disposable scope.</returns>
        public static IDisposable? ProcessingTodoScope(this ILogger logger, DateTime time) => processingTodoScope(logger, time);

        /// <summary>Unhandled exception.</summary>
        /// <param name="logger">Logger</param>
        /// <param name="ex">Exception.</param>
        public static void UnhandledException(this ILogger logger, Exception ex) => unhandledException(logger, ex);

        /// <summary>Web Api scope.</summary>
        /// <param name="logger">Logger.</param>
        /// <param name="time">Date Time.</param>
        /// <returns>Disposable scope.</returns>
        public static IDisposable? WebApiScope(this ILogger logger, DateTime time) => webApiScope(logger, time);
    }
}
