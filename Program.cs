using Microsoft.Extensions.Configuration;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace dotnetthanks_loader
{
    class Program
    {
        private static readonly ILoggingService _logger = Logger.Instance;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>()
                .Build();

            // Initialize services
            var httpClient = new HttpClient();
            var gitHubService = new GitHubService(config, httpClient, _logger);
            var contributorService = new ContributorService(gitHubService, _logger);

            var repo = "core";
            var owner = "dotnet";

            // Load all releases using GitHubService
            _logger.Info("Loading releases...");
            var allReleases = await gitHubService.GetReleasesAsync(owner, repo);

            _logger.LogReleaseLoading("Aspire");
            var aspireReleases = await gitHubService.GetReleasesAsync("dotnet", "aspire");
            _logger.LogReleaseLoading("Aspire", aspireReleases.Count());

            _logger.LogReleaseLoading("MAUI");
            var mauiReleases = await gitHubService.GetReleasesAsync("dotnet", "maui");
            _logger.LogReleaseLoading("MAUI", mauiReleases.Count());

            // Sort releases from newest to oldest
            List<Release> sortedReleases = [..allReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            List<Release> sortedAspireReleases = [..aspireReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            List<Release> sortedMauiReleases = [..mauiReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            Dictionary<string, MajorRelease> majorReleasesDictionary = [];

            bool isDiffMode = args != null && args.Length > 0 && args[0] == "diff";

            if (isDiffMode)
            {
                await ProcessDiffModeAsync(
                    gitHubService,
                    contributorService,
                    sortedReleases,
                    sortedAspireReleases,
                    sortedMauiReleases,
                    majorReleasesDictionary,
                    repo);
            }
            else
            {
                await ProcessFullModeAsync(
                    gitHubService,
                    contributorService,
                    sortedReleases,
                    sortedAspireReleases,
                    sortedMauiReleases,
                    majorReleasesDictionary,
                    repo);
            }
        }

        /// <summary>
        /// Process all releases from scratch (full mode).
        /// </summary>
        private static async Task ProcessFullModeAsync(
            IGitHubService gitHubService,
            IContributorService contributorService,
            List<Release> sortedReleases,
            List<Release> sortedAspireReleases,
            List<Release> sortedMauiReleases,
            Dictionary<string, MajorRelease> majorReleasesDictionary,
            string repo)
        {
            // Initialize dictionary for major versions
            InitializeMajorReleasesDictionary(sortedReleases, majorReleasesDictionary);

            _logger.Info($"Processing all releases...\n{repo} - {sortedReleases.Count}");

            // Process core releases using ContributorService
            await contributorService.ProcessReleasesAsync(sortedReleases, majorReleasesDictionary, repo);

            // Process Aspire releases
            _logger.Info($"\nProcessing Aspire releases... - {sortedAspireReleases.Count}");
            await ProcessExternalReleasesAsync(
                gitHubService, sortedAspireReleases, majorReleasesDictionary, 
                "aspire", VersionMapper.MapAspireVersionToDotNet);

            // Process MAUI releases
            _logger.Info($"\nProcessing MAUI releases... - {sortedMauiReleases.Count}");
            await ProcessExternalReleasesAsync(
                gitHubService, sortedMauiReleases, majorReleasesDictionary, 
                "maui", VersionMapper.MapMauiVersionToDotNet);

            // Write results
            var sortedList = majorReleasesDictionary.Values.ToList();
            File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedList, _jsonOptions));
            _logger.Info($"\nResults written to ./{repo}.json");
        }

        /// <summary>
        /// Process only new releases since the last run (diff mode).
        /// </summary>
        private static async Task ProcessDiffModeAsync(
            IGitHubService gitHubService,
            IContributorService contributorService,
            List<Release> sortedReleases,
            List<Release> sortedAspireReleases,
            List<Release> sortedMauiReleases,
            Dictionary<string, MajorRelease> majorReleasesDictionary,
            string repo)
        {
            // Load current core.json using GitHubService
            var coreJson = await gitHubService.LoadCoreJsonAsync();
            if (coreJson == null || !coreJson.Any())
            {
                _logger.Error("Failed to load core.json. Cannot run in diff mode.");
                return;
            }

            // Initialize dictionary with existing data
            foreach (var release in coreJson)
            {
                majorReleasesDictionary[$"{release.Version.Major}.{release.Version.Minor}"] = release;
            }

            // Collect processed releases
            var processedReleases = coreJson.SelectMany(o => o.ProcessedReleases).ToList();

            // Find new releases
            var coreDiff = sortedReleases.Where(o => !processedReleases.Contains(o.Tag)).ToList();
            var aspireDiff = sortedAspireReleases.Where(r => !processedReleases.Contains($"aspire-{r.Tag}")).ToList();
            var mauiDiff = sortedMauiReleases.Where(r => !processedReleases.Contains($"maui-{r.Tag}")).ToList();

            bool hasChanges = false;

            // Process new core releases
            if (coreDiff.Count > 0)
            {
                hasChanges = true;
                _logger.Info($"Processing diffs in releases...\n{repo} - {coreDiff.Count}");

                // Add missing major versions
                foreach (var release in coreDiff)
                {
                    var key = $"{release.Version.Major}.{release.Version.Minor}";
                    if (!majorReleasesDictionary.ContainsKey(key))
                    {
                        majorReleasesDictionary.Add(key, CreateMajorRelease(release));
                    }
                }

                // Build release list with previous versions for comparison
                var sortedNewReleases = BuildDiffReleaseList(coreDiff, sortedReleases);
                await contributorService.ProcessReleasesAsync(sortedNewReleases, majorReleasesDictionary, repo, isDiff: true);
            }

            // Process new Aspire releases
            if (aspireDiff.Count > 0)
            {
                hasChanges = true;
                _logger.Info($"\nProcessing Aspire diffs... - {aspireDiff.Count}");
                await ProcessExternalReleasesAsync(
                    gitHubService, aspireDiff, majorReleasesDictionary, 
                    "aspire", VersionMapper.MapAspireVersionToDotNet);
            }

            // Process new MAUI releases
            if (mauiDiff.Count > 0)
            {
                hasChanges = true;
                _logger.Info($"\nProcessing MAUI diffs... - {mauiDiff.Count}");
                await ProcessExternalReleasesAsync(
                    gitHubService, mauiDiff, majorReleasesDictionary, 
                    "maui", VersionMapper.MapMauiVersionToDotNet);
            }

            if (hasChanges)
            {
                var sortedList = majorReleasesDictionary.Values.OrderByDescending(o => o.Version).ToList();
                File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedList, _jsonOptions));
                _logger.Info($"\nResults written to ./{repo}.json");
            }
            else
            {
                _logger.Info("The current releases list is up to date with core.json\nExiting...");
            }
        }

        /// <summary>
        /// Initialize major releases dictionary from sorted releases.
        /// </summary>
        private static void InitializeMajorReleasesDictionary(
            List<Release> releases, 
            Dictionary<string, MajorRelease> dictionary)
        {
            foreach (var release in releases)
            {
                var key = $"{release.Version.Major}.{release.Version.Minor}";
                if (!dictionary.ContainsKey(key))
                {
                    dictionary.Add(key, CreateMajorRelease(release));
                }
            }
        }

        /// <summary>
        /// Create a new MajorRelease from a Release.
        /// </summary>
        private static MajorRelease CreateMajorRelease(Release release) => new()
        {
            Contributions = 0,
            Contributors = [],
            Product = release.Product,
            Name = $"{release.Product} {release.Version.Major}.{release.Version.Minor}",
            Version = release.Version,
            Tag = $"v{release.Version.Major}.{release.Version.Minor}",
            ProcessedReleases = []
        };

        /// <summary>
        /// Build a list of releases including previous versions needed for diff comparison.
        /// </summary>
        private static List<Release> BuildDiffReleaseList(List<Release> newReleases, List<Release> allReleases)
        {
            var result = new List<Release>();
            var latestGARelease = allReleases.Find(r => r.IsGA);

            foreach (var release in newReleases)
            {
                var idx = allReleases.IndexOf(release);

                if (idx == allReleases.Count - 1)
                {
                    result.Add(release);
                    continue;
                }

                var previousVersion = allReleases[idx + 1];
                result.Add(release);

                if (!newReleases.Contains(previousVersion))
                {
                    if (release.Version.Major != previousVersion.Version.Major)
                    {
                        if (latestGARelease != null && !result.Contains(latestGARelease))
                            result.Add(latestGARelease);
                    }
                    else if (!result.Contains(previousVersion))
                    {
                        result.Add(previousVersion);
                    }
                }
                else if (release.Version.Major != previousVersion.Version.Major && latestGARelease != null)
                {
                    if (!result.Contains(latestGARelease))
                        result.Add(latestGARelease);
                }
            }

            return result
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)
                .ToList();
        }

        /// <summary>
        /// Process external product releases (Aspire, MAUI) using GitHubService.
        /// </summary>
        private static async Task ProcessExternalReleasesAsync(
            IGitHubService gitHubService,
            List<Release> releases,
            Dictionary<string, MajorRelease> majorReleasesDict,
            string repoName,
            Func<string, int> versionMapper)
        {
            for (int i = 0; i < releases.Count; i++)
            {
                var currentRelease = releases[i];

                int dotnetMajor = versionMapper(currentRelease.Tag);
                if (dotnetMajor == -1)
                {
                    _logger.LogSkip($"{repoName} {currentRelease.Tag}", "does not map to a supported .NET version");
                    continue;
                }

                majorReleasesDict.TryGetValue($"{dotnetMajor}.0", out var majorRelease);
                if (majorRelease == null)
                {
                    _logger.Error($".NET {dotnetMajor}.0 not found in dictionary for {repoName} {currentRelease.Tag}");
                    continue;
                }

                if (i == releases.Count - 1)
                {
                    majorRelease.ProcessedReleases.Add($"{repoName.ToLower()}-{currentRelease.Tag}");
                    break;
                }

                var previousRelease = releases[i + 1];
                majorRelease.ProcessedReleases.Add($"{repoName.ToLower()}-{currentRelease.Tag}");

                _logger.LogVersionProcessing($"{repoName.ToLower()} -> .NET {dotnetMajor}.0", previousRelease.Tag, currentRelease.Tag);

                try
                {
                    var commits = await gitHubService.CompareCommitsAsync(
                        "dotnet", repoName.ToLower(), previousRelease.Tag, currentRelease.Tag);

                    if (commits == null || commits.Count < 1)
                        continue;

                    majorRelease.Contributions += commits.Count;
                    TallyCommits(majorRelease, repoName.ToLower(), commits);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

        /// <summary>
        /// Tally commits for contributors (used for external releases).
        /// </summary>
        private static void TallyCommits(MajorRelease majorRelease, string repoName, List<MergeBaseCommit> commits)
        {
            foreach (var item in commits)
            {
                if (item.author == null) continue;

                var author = item.author;
                author.name = item.commit.author.name;

                if (string.IsNullOrEmpty(author.name))
                    author.name = "Unknown";

                if (BotExclusionConstants.IsBot(author.name))
                    continue;

                // Find by Link first
                var person = majorRelease.Contributors.Find(p => p.Link == author.html_url);
                
                // If not found by Link and we have a valid Link, try to find by Name (to merge with null-Link entries)
                if (person == null && !string.IsNullOrEmpty(author.html_url))
                {
                    person = majorRelease.Contributors.Find(p => p.Link == null && p.Name == author.name);
                    if (person != null)
                    {
                        // Backfill the missing GitHub info
                        person.Link = author.html_url;
                        person.Avatar = author.avatar_url;
                    }
                }
                
                if (person == null)
                {
                    person = new Contributor
                    {
                        Name = author.name,
                        Link = author.html_url,
                        Avatar = author.avatar_url,
                        Count = 1
                    };
                    person.Repos.Add(new RepoItem { Name = repoName, Count = 1 });
                    majorRelease.Contributors.Add(person);
                }
                else
                {
                    person.Count += 1;
                    var repoItem = person.Repos.Find(r => r.Name == repoName);
                    if (repoItem == null)
                        person.Repos.Add(new RepoItem { Name = repoName, Count = 1 });
                    else
                        repoItem.Count += 1;
                }
            }
        }
    }
}
