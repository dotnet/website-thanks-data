#nullable enable

namespace dotnetthanks_loader
{
    /// <summary>
    /// Interface for contributor aggregation and processing operations.
    /// </summary>
    public interface IContributorService
    {
        /// <summary>
        /// Processes releases and aggregates contributor data.
        /// </summary>
        Task<Dictionary<string, MajorRelease>> ProcessReleasesAsync(
            List<Release> releases,
            Dictionary<string, MajorRelease> majorReleasesDict,
            string repo,
            bool isDiff = false,
            bool isLatestOnly = false);

        /// <summary>
        /// Tallies commits for a release into the contributor data.
        /// </summary>
        void TallyCommits(MajorRelease majorRelease, string repoName, List<MergeBaseCommit> commits);

        /// <summary>
        /// Gets the previous release for comparison.
        /// </summary>
        Release? GetPreviousRelease(List<Release> sortedReleases, Release currentRelease, int index);
    }
}
