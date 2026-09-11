#nullable enable

using Octokit;

namespace dotnetthanks_loader
{
    /// <summary>
    /// Interface for GitHub API operations.
    /// Abstracts GitHub API calls for testability with mock implementations.
    /// </summary>
    public interface IGitHubService
    {
        /// <summary>
        /// Gets all releases for a repository.
        /// </summary>
        /// <param name="owner">Repository owner (e.g., "dotnet")</param>
        /// <param name="repo">Repository name (e.g., "core")</param>
        /// <returns>Collection of releases</returns>
        Task<IEnumerable<Release>> GetReleasesAsync(string owner, string repo);

        /// <summary>
        /// Compares two commits/tags and returns the commits between them.
        /// </summary>
        /// <param name="owner">Repository owner</param>
        /// <param name="repo">Repository name</param>
        /// <param name="fromRef">Base reference (tag or commit)</param>
        /// <param name="toRef">Head reference (tag or commit)</param>
        /// <returns>List of commits between the two references</returns>
        Task<List<MergeBaseCommit>?> CompareCommitsAsync(string owner, string repo, string fromRef, string toRef);

        /// <summary>
        /// Loads the current core.json data.
        /// </summary>
        /// <returns>List of major releases from core.json</returns>
        Task<List<MajorRelease>?> LoadCoreJsonAsync();

        /// <summary>
        /// Parses release body to extract child repository information.
        /// </summary>
        /// <param name="body">The release body markdown content</param>
        /// <returns>List of child repositories with their tags</returns>
        List<ChildRepo> ParseReleaseBody(string body);

        /// <summary>
        /// Lists all subdirectories under src/*/* in dotnet-docker (all version folders for all products).
        /// </summary>
        Task<IReadOnlyList<string>> ListAllDotnetDockerVersionFoldersAsync();

        /// <summary>
        /// Lists all subdirectories under src/ in dotnet-docker for a given .NET version (e.g., 10.0).
        /// </summary>
        Task<IReadOnlyList<string>> ListDotnetDockerVersionFoldersAsync(string version);

        /// <summary>
        /// Gets commit history for a given path in dotnet-docker main branch.
        /// </summary>
        /// <remarks>
        /// On error, the exception is logged and swallowed; any commits already fetched are
        /// returned, or an empty list if none were fetched yet. Callers should not rely on a
        /// non-empty result as proof that no error occurred.
        /// </remarks>
        Task<IReadOnlyList<Octokit.GitHubCommit>> GetCommitsForPathAsync(string path);
    }
}
