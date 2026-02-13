namespace dotnetthanks_loader
{
    /// <summary>
    /// Contains bot usernames to exclude from contributor counts.
    /// Usernames are stored in lowercase for case-insensitive matching.
    /// </summary>
    public static class BotExclusionConstants
    {
        /// <summary>
        /// List of bot usernames to exclude from contributor recognition.
        /// All entries should be lowercase for consistent matching.
        /// </summary>
        public static readonly string[] BotUsernames =
        [
            "dependabot[bot]",
            "github-actions[bot]",
            "msftbot[bot]",
            "dotnet bot",
            "dotnet-bot",
            "nuget team bot",
            "net source-build bot",
            "dotnet-maestro-bot",
            "dotnet-maestro[bot]",
            ".net source-build bot",
            "dotnet-policy-service[bot]",
            "dotnet-sb-bot",
            "dotnet-gitsync-bot",
            "dependabot-preview[bot]",
        ];

        /// <summary>
        /// Checks if the given username is a known bot.
        /// </summary>
        /// <param name="username">The username to check (case-insensitive).</param>
        /// <returns>True if the username is a known bot; otherwise, false.</returns>
        public static bool IsBot(string username)
        {
            if (string.IsNullOrEmpty(username))
                return false;

            return BotUsernames.Contains(username.ToLower());
        }
    }
}
