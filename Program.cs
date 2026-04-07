using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace dotnetthanks_loader
{
    class Program
    {
        private static ILoggingService _logger = Logger.Instance;
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

            // Configure DI container
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<HttpClient>();
            services.AddSingleton<ILoggingService, ConsoleLoggingService>();
            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<IContributorService, ContributorService>();

            var serviceProvider = services.BuildServiceProvider();

            // Resolve services
            _logger = serviceProvider.GetRequiredService<ILoggingService>();
            Logger.Instance = _logger;
            var gitHubService = serviceProvider.GetRequiredService<IGitHubService>();
            var contributorService = serviceProvider.GetRequiredService<IContributorService>();

            var repo = "core";
            var owner = "dotnet";

            // Load all releases using GitHubService in parallel
            _logger.Info("Loading releases...");
            var allReleasesTask = gitHubService.GetReleasesAsync(owner, repo);
            var aspireReleasesTask = gitHubService.GetReleasesAsync("dotnet", "aspire");
            var mauiReleasesTask = gitHubService.GetReleasesAsync("dotnet", "maui");

            await Task.WhenAll(allReleasesTask, aspireReleasesTask, mauiReleasesTask);

            var allReleases = await allReleasesTask;
            var aspireReleases = await aspireReleasesTask;
            var mauiReleases = await mauiReleasesTask;

            _logger.LogReleaseLoading("Aspire", aspireReleases.Count());
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
                gitHubService, contributorService, sortedAspireReleases, majorReleasesDictionary,
                "aspire", VersionMapper.MapAspireVersionToDotNet);

            // Process MAUI releases
            _logger.Info($"\nProcessing MAUI releases... - {sortedMauiReleases.Count}");
            await ProcessExternalReleasesAsync(
                gitHubService, contributorService, sortedMauiReleases, majorReleasesDictionary,
                "maui", VersionMapper.MapMauiVersionToDotNet);

            // Process dotnet-docker contributions for all .NET versions
            _logger.Info("\nProcessing dotnet-docker contributions for all .NET versions...");

            // Process dotnet-docker contributions and collect contributors per version
            var dotnetDockerContributors = await ProcessDotnetDockerContributionsWithResultAsync(gitHubService, majorReleasesDictionary);

            // Write dotnet-docker contributors to a separate JSON file for historical tracking
            File.WriteAllText("./dotnetdocker-contributors.json", JsonSerializer.Serialize(dotnetDockerContributors, _jsonOptions));
            _logger.Info("\nDotnet-docker contributors written to ./dotnetdocker-contributors.json");

            // (Legacy call for compatibility, can be removed if not needed)
            // await ProcessDotnetDockerContributionsAsync(gitHubService, majorReleasesDictionary);

            // Write results
            var sortedList = majorReleasesDictionary.Values.ToList();
            File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedList, _jsonOptions));
            _logger.Info($"\nResults written to ./{repo}.json");

        }

        /// <summary>
        /// Process dotnet-docker repo contributions for all .NET versions and return a dictionary for historical tracking.
        /// </summary>
        private static async Task<Dictionary<string, List<Contributor>>> ProcessDotnetDockerContributionsWithResultAsync(
            IGitHubService gitHubService,
            Dictionary<string, MajorRelease> majorReleasesDictionary)
        {
            var versionContributors = new Dictionary<string, List<Contributor>>();
            // For each .NET version in majorReleasesDictionary, enumerate src/*/<version>/ folders and aggregate contributors
            foreach (var kvp in majorReleasesDictionary)
            {
                var versionKey = kvp.Key; // e.g., "10.0"
                var majorRelease = kvp.Value;
                _logger.Info($"Processing dotnet-docker for .NET {versionKey}...");

                // List all src/*/<version>/ folders
                var versionFolders = await gitHubService.ListDotnetDockerVersionFoldersAsync(versionKey);
                _logger.Info($"Found {versionFolders.Count} dotnet-docker releases for .NET {versionKey}");

                var allContributors = new Dictionary<string, Contributor>();
                int totalCommits = 0;

                foreach (var folder in versionFolders)
                {
                    var commits = await gitHubService.GetCommitsForPathAsync(folder);
                    totalCommits += commits.Count;
                    foreach (var commit in commits)
                    {
                        var author = commit?.Author;
                        if (author == null || string.IsNullOrEmpty(author.Login)) continue;
                        if (BotExclusionConstants.IsBot(author.Login)) continue;

                        if (!allContributors.TryGetValue(author.Login, out var contributor))
                        {
                            contributor = new Contributor
                            {
                                Name = author.Login,
                                Link = author.HtmlUrl,
                                Avatar = author.AvatarUrl,
                                Count = 1,
                                Repos = new List<RepoItem> { new RepoItem { Name = "dotnet-docker", Count = 1 } }
                            };
                            allContributors[author.Login] = contributor;
                        }
                        else
                        {
                            contributor.Count += 1;
                            var repoItem = contributor.Repos.Find(r => r.Name == "dotnet-docker");
                            if (repoItem == null)
                                contributor.Repos.Add(new RepoItem { Name = "dotnet-docker", Count = 1 });
                            else
                                repoItem.Count += 1;
                        }
                    }
                }


                // Add or update contributors in MajorRelease
                foreach (var contributor in allContributors.Values)
                {
                    var existing = majorRelease.Contributors.FirstOrDefault(c => c.Link == contributor.Link);
                    if (existing == null)
                    {
                        majorRelease.Contributors.Add(contributor);
                    }
                    else
                    {
                        // Update commit count
                        existing.Count += contributor.Count;
                        // Merge/update repo list
                        foreach (var repoItem in contributor.Repos)
                        {
                            var existingRepo = existing.Repos.FirstOrDefault(r => r.Name == repoItem.Name);
                            if (existingRepo == null)
                                existing.Repos.Add(new RepoItem { Name = repoItem.Name, Count = repoItem.Count });
                            else
                                existingRepo.Count += repoItem.Count;
                        }
                    }
                }
                majorRelease.Contributions += totalCommits;

                // Mark as processed
                var processedKey = $"dotnet-docker-{versionKey}";
                if (!majorRelease.ProcessedReleases.Contains(processedKey))
                    majorRelease.ProcessedReleases.Add(processedKey);

                // Save contributors for this version for historical tracking
                versionContributors[versionKey] = allContributors.Values.ToList();
            }
            return versionContributors;
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
            var processedReleases = coreJson.SelectMany(o => o.ProcessedReleases).ToHashSet();

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
                    gitHubService, contributorService, aspireDiff, majorReleasesDictionary,
                    "aspire", VersionMapper.MapAspireVersionToDotNet);
            }

            // Process new MAUI releases
            if (mauiDiff.Count > 0)
            {
                hasChanges = true;
                _logger.Info($"\nProcessing MAUI diffs... - {mauiDiff.Count}");
                await ProcessExternalReleasesAsync(
                    gitHubService, contributorService, mauiDiff, majorReleasesDictionary,
                    "maui", VersionMapper.MapMauiVersionToDotNet);
            }

            if (hasChanges)
            {
                // Also process dotnet-docker contributions for updated releases
                _logger.Info("\nProcessing dotnet-docker contributions for all .NET versions (diff mode)...");
                var dotnetDockerContributors = await ProcessDotnetDockerContributionsWithResultAsync(gitHubService, majorReleasesDictionary);
                File.WriteAllText("./dotnetdocker-contributors.json", JsonSerializer.Serialize(dotnetDockerContributors, _jsonOptions));
                _logger.Info("\nDotnet-docker contributors written to ./dotnetdocker-contributors.json");

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
            IContributorService contributorService,
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
                    majorRelease.ProcessedReleases.Add($"{repoName}-{currentRelease.Tag}");
                    break;
                }

                var previousRelease = releases[i + 1];
                majorRelease.ProcessedReleases.Add($"{repoName}-{currentRelease.Tag}");

                _logger.LogVersionProcessing($"{repoName} -> .NET {dotnetMajor}.0", previousRelease.Tag, currentRelease.Tag);

                try
                {
                    var commits = await gitHubService.CompareCommitsAsync(
                        "dotnet", repoName, previousRelease.Tag, currentRelease.Tag);

                    if (commits == null || commits.Count < 1)
                        continue;

                    majorRelease.Contributions += commits.Count;
                    contributorService.TallyCommits(majorRelease, repoName, commits);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }
    }
}
