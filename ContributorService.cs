#nullable enable

using System.Diagnostics;

namespace dotnetthanks_loader
{
    /// <summary>
    /// Service for processing contributor data from GitHub releases.
    /// </summary>
    public class ContributorService : IContributorService
    {
        private readonly IGitHubService _gitHubService;
        private readonly ILoggingService _logger;

        public ContributorService(IGitHubService gitHubService, ILoggingService? logger = null)
        {
            _gitHubService = gitHubService;
            _logger = logger ?? Logger.Instance;
        }


        public async Task<Dictionary<string, MajorRelease>> ProcessReleasesAsync(
            List<Release> releases,
            Dictionary<string, MajorRelease> majorReleasesDict,
            string repo,
            bool isDiff = false,
            bool isLatestOnly = false)
        {
            Release currentRelease;
            Release? previousRelease;

            for (int i = 0; i < releases.Count; i++)
            {
                currentRelease = releases[i];
                majorReleasesDict.TryGetValue($"{currentRelease.Version.Major}.{currentRelease.Version.Minor}", out var majorRelease);

                // In latest-only mode, process only the first release without comparison
                if (isLatestOnly)
                {
                    majorRelease?.ProcessedReleases.Add(currentRelease.Tag);
                    _logger.Info($"Processing (latest-only): {repo} {currentRelease.Tag}");

                    // For latest-only, we want contributors from this single release
                    // We'll use the previous release in the full list for comparison
                    var allReleasesList = releases.ToList();
                    if (i < allReleasesList.Count - 1)
                    {
                        previousRelease = allReleasesList[i + 1];
                    }
                    else
                    {
                        // This is the oldest release, skip it
                        break;
                    }
                }
                else
                {
                    // Add the first release to the list of processed releases so diff does not pick it up
                    if (i == releases.Count - 1)
                    {
                        if (!isDiff)
                        {
                            majorRelease?.ProcessedReleases.Add(currentRelease.Tag);
                        }
                        break;
                    }

                    previousRelease = GetPreviousRelease(releases, currentRelease, i + 1);

                    if (previousRelease is null)
                    {
                        // Is this the first release?
                        _logger.Info($"{currentRelease.Tag} is the first release in the series.");
                        continue;
                    }

                    majorRelease?.ProcessedReleases.Add(currentRelease.Tag);

                    _logger.LogVersionProcessing(repo, previousRelease.Tag, currentRelease.Tag);
                }

                // for each child repo get commits and count contribs
                foreach (var repoCurrentRelease in currentRelease.ChildRepos)
                {
                    var repoPrevRelease = previousRelease.ChildRepos.FirstOrDefault(r => r.Owner == repoCurrentRelease.Owner &&
                                                                                         r.Repository == repoCurrentRelease.Repository);
                    if (repoPrevRelease is null)
                    {
                        // This may happen
                        _logger.LogValidationError(repoCurrentRelease.Url, $"doesn't exist in {previousRelease.Tag}");
                        continue;
                    }

                    Debug.WriteLine($"{repoCurrentRelease.Tag} : {repoCurrentRelease.Name}");

                    try
                    {
                        _logger.Debug($"Processing: {repoCurrentRelease.Name}: {repoPrevRelease.Tag}..{repoCurrentRelease.Tag}");

                        if (repoPrevRelease.Tag != repoCurrentRelease.Tag)
                        {
                            var releaseDiff = await _gitHubService.CompareCommitsAsync(
                                repoCurrentRelease.Owner,
                                repoCurrentRelease.Repository,
                                repoPrevRelease.Tag,
                                repoCurrentRelease.Tag);

                            if (releaseDiff is null || releaseDiff.Count < 1)
                            {
                                continue;
                            }

                            if (majorRelease is not null)
                            {
                                majorRelease.Contributions += releaseDiff.Count;
                                TallyCommits(majorRelease, repoCurrentRelease.Repository, releaseDiff);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex);
                    }
                }

                if (Environment.GetEnvironmentVariable("TEST") == "1")
                    break;
            }

            return majorReleasesDict;
        }


        public void TallyCommits(MajorRelease majorRelease, string repoName, List<MergeBaseCommit> commits)
        {
            // these the commits within the release
            foreach (var item in commits)
            {
                if (item.author != null)
                {
                    var author = item.author;
                    author.name = item.commit.author.name;

                    if (string.IsNullOrEmpty(author.name))
                    {
                        author.name = "Unknown";
                    }

                    if (!BotExclusionConstants.IsBot(author.name))
                    {
                        // find if the author has been counted
                        var person = majorRelease.Contributors.Find(p => p.Link == author.html_url);
                        if (person == null)
                        {
                            person = new Contributor()
                            {
                                Name = author.name,
                                Link = author.html_url,
                                Avatar = author.avatar_url,
                                Count = 1
                            };
                            person.Repos.Add(new RepoItem() { Name = repoName, Count = 1 });

                            majorRelease.Contributors.Add(person);
                        }
                        else
                        {
                            // found the author, does the repo exist as well?
                            person.Count += 1;

                            var repoItem = person.Repos.Find(r => r.Name == repoName);
                            if (repoItem == null)
                            {
                                person.Repos.Add(new RepoItem() { Name = repoName, Count = 1 });
                            }
                            else
                            {
                                repoItem.Count += 1;
                            }
                        }
                    }
                }
            }
        }


        public Release? GetPreviousRelease(List<Release> sortedReleases, Release currentRelease, int index)
        {
            if (index >= sortedReleases.Count)
            {
                return null;
            }

            if (currentRelease.Version.Major == sortedReleases[index].Version.Major &&
                currentRelease.Version.Minor == sortedReleases[index].Version.Minor)
            {
                return sortedReleases[index];
            }

            return sortedReleases.Skip(index).FirstOrDefault(r => currentRelease.Version > r.Version && r.IsGA);
        }
    }
}
