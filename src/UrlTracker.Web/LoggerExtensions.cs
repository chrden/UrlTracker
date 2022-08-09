﻿using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using UrlTracker.Core.Models;

namespace UrlTracker.Web
{
    [ExcludeFromCodeCoverage]
    internal static class LoggerExtensions
    {
        public static void LogUrlTrackerDisabled(this ILogger logger)
            => logger.LogDebug(new EventId(2001), "Url tracker is disabled by config");

        public static void LogRequestDetected(this ILogger logger, string url)
            => logger.LogDebug(new EventId(2004), "Incoming url detected: '{url}'", url);

        public static void LogAbortHandling(this ILogger logger, string reason)
            => logger.LogDebug(new EventId(2005), "Handling aborted: {reason}", reason);

        public static void LogInterceptFound(this ILogger logger, Type interceptType)
            => logger.LogDebug(new EventId(2006), "Found an intercept of type '{interceptType}'", interceptType);

        public static void LogAbortClientErrorHandling(this ILogger logger, string reason)
            => logger.LogDebug(new EventId(2008), "Client error handling aborted: {reason}", reason);

        public static void LogInterceptCancelled(this ILogger logger, string reason, Redirect redirect)
            => logger.LogDebug(new EventId(2009), "Intercept cancelled: {reason}, {redirect}", reason, redirect.Id);

        public static void LogRequestRedirected(this ILogger logger, string targetUrl)
            => logger.LogInformation(new EventId(2010), "Redirect request to: {targetUrl}", targetUrl);

        public static void LogStart<T>(this ILogger logger)
            => logger.LogDebug(new EventId(2011), "Filter incoming url with {source}", typeof(T));

        public static void LogPathIsReserved(this ILogger logger)
            => logger.LogDebug(new EventId(2012), "Incoming url is reserved by umbraco settings.");

        public static void LogRequestConvertedToGone(this ILogger logger)
            => logger.LogInformation(new EventId(2013), "Response converted to 410");

        public static void LogNoHandlerFound(this ILogger logger)
            => logger.LogError(new EventId(2014), "No handler was found to handle the intercept.");

        public static void LogLastChance(this ILogger logger, Type interceptType)
            => logger.LogWarning(new EventId(2014), "Last chance handler invoked for intercept of type {interceptType}. Did you forget to register a handler?", interceptType);
    }
}
