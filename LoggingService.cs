#nullable enable

using System.Diagnostics;

namespace dotnetthanks_loader
{
    /// <summary>
    /// Console-based implementation of <see cref="ILoggingService"/>.
    /// Debug-level messages are only output when running in DEBUG configuration.
    /// </summary>
    public class ConsoleLoggingService : ILoggingService
    {
        /// <inheritdoc/>
        public void Info(string message)
        {
            Console.WriteLine(message);
        }

        /// <inheritdoc/>
        public void Debug(string message)
        {
#if DEBUG
            Console.WriteLine($"[DEBUG]: {message}");
#endif
        }

        /// <inheritdoc/>
        public void Error(string message)
        {
            Console.WriteLine($"[ERROR]: {message}");
        }

        /// <inheritdoc/>
        public void Error(Exception ex, string? context = null)
        {
            if (string.IsNullOrEmpty(context))
            {
                Console.WriteLine($"[ERROR]: {ex.Message}");
            }
            else
            {
                Console.WriteLine($"[ERROR]: {context} - {ex.Message}");
            }

#if DEBUG
            if (ex.StackTrace != null)
            {
                Console.WriteLine($"[DEBUG]: Stack trace: {ex.StackTrace}");
            }
#endif
        }

        /// <inheritdoc/>
        public void Warning(string message)
        {
            Console.WriteLine($"[WARNING]: {message}");
        }

        /// <inheritdoc/>
        public void LogApiCall(string endpoint, string? details = null)
        {
#if DEBUG
            var detailsSuffix = string.IsNullOrEmpty(details) ? "" : $" - {details}";
            Console.WriteLine($"[DEBUG]: API call: {endpoint}{detailsSuffix}");
#endif
        }

        /// <inheritdoc/>
        public void LogVersionProcessing(string repo, string fromTag, string toTag)
        {
#if DEBUG
            Console.WriteLine($"[DEBUG]: Processing version: {repo} {fromTag}..{toTag}");
#endif
        }

        /// <inheritdoc/>
        public void LogValidationError(string entity, string message)
        {
            Console.WriteLine($"[VALIDATION ERROR]: {entity} - {message}");
        }

        /// <inheritdoc/>
        public void LogRateLimit(double waitMinutes, DateTime resumeTime)
        {
            Console.WriteLine($"Rate limit exceeded. Waiting for {waitMinutes:N1} mins until {resumeTime}.");
        }

        /// <inheritdoc/>
        public void LogReleaseLoading(string product, int? count = null)
        {
            if (count.HasValue)
            {
                Console.WriteLine($"Loaded {count} {product} releases");
            }
            else
            {
                Console.WriteLine($"Loading {product} releases...");
            }
        }

        /// <inheritdoc/>
        public void LogSkip(string entity, string reason)
        {
            Console.WriteLine($"[SKIP]: {entity} - {reason}");
        }
    }

    /// <summary>
    /// Static accessor for the logging service.
    /// Provides a singleton instance for use throughout the application.
    /// </summary>
    public static class Logger
    {
        private static ILoggingService? _instance;

        /// <summary>
        /// Gets or sets the logging service instance.
        /// Defaults to <see cref="ConsoleLoggingService"/> if not explicitly set.
        /// </summary>
        public static ILoggingService Instance
        {
            get => _instance ??= new ConsoleLoggingService();
            set => _instance = value;
        }
    }
}
