#nullable enable

namespace dotnetthanks_loader
{
    /// <summary>
    /// Interface for centralized logging service.
    /// Provides structured logging for GitHub API calls, version processing, and data validation.
    /// </summary>
    public interface ILoggingService
    {
        /// <summary>
        /// Logs an informational message.
        /// </summary>
        void Info(string message);

        /// <summary>
        /// Logs a debug message (only output in DEBUG builds).
        /// </summary>
        void Debug(string message);

        /// <summary>
        /// Logs an error message.
        /// </summary>
        void Error(string message);

        /// <summary>
        /// Logs an exception with optional context message.
        /// </summary>
        void Error(Exception ex, string? context = null);

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        void Warning(string message);

        /// <summary>
        /// Logs a GitHub API call (debug-only).
        /// </summary>
        void LogApiCall(string endpoint, string? details = null);

        /// <summary>
        /// Logs version processing activity (debug-only).
        /// </summary>
        void LogVersionProcessing(string repo, string fromTag, string toTag);

        /// <summary>
        /// Logs a data validation error.
        /// </summary>
        void LogValidationError(string entity, string message);

        /// <summary>
        /// Logs rate limit information.
        /// </summary>
        void LogRateLimit(double waitMinutes, DateTime resumeTime);

        /// <summary>
        /// Logs release loading activity.
        /// </summary>
        void LogReleaseLoading(string product, int? count = null);

        /// <summary>
        /// Logs a skip operation with reason.
        /// </summary>
        void LogSkip(string entity, string reason);
    }
}
