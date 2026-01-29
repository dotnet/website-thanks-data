using Microsoft.Extensions.Configuration;
using Octokit;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace dotnetthanks_loader
{
    class Program
    {
        private static HttpClient _client;
        private static string _token;

        private static GitHubClient _ghclient;

        static async Task Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>()
                .Build();

            _token = config.GetSection("GITHUB_TOKEN").Value;

            _ghclient = new GitHubClient(new ProductHeaderValue("dotnet-thanks"));
            var basic = new Credentials(config.GetSection("GITHUB_CLIENTID").Value, config.GetSection("GITHUB_CLIENTSECRET").Value);
            _ghclient.Credentials = basic;

            var repo = "core";
            var owner = "dotnet";

            // load all releases for dotnet/core
            IEnumerable<Release> allReleases = await LoadReleasesAsync(owner, repo);

            // Load Aspire and MAUI releases
            Console.WriteLine("Loading Aspire releases...");
            var aspireReleases = await LoadReleasesAsync("dotnet", "aspire");
            Console.WriteLine($"Loaded {aspireReleases.Count()} Aspire releases");

            Console.WriteLine("Loading MAUI releases...");
            var mauiReleases = await LoadReleasesAsync("dotnet", "maui");
            Console.WriteLine($"Loaded {mauiReleases.Count()} MAUI releases");

            // Sort releases from the youngest to the oldest by version
            // E.g.
            //      5.0.1
            //      5.0.0       // GA
            //      5.0.0-RC2
            //      5.0.0-RC1
            //      5.0.0-preview9
            //      ...
            //      5.0.0-preview2
            //      3.1.10
            //      3.1.9
            //      3.1.8
            //      ...
            //      3.1.0       // GA
            //      ...
            //
            List<Release> sortedReleases = [..allReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            // Sort Aspire releases
            List<Release> sortedAspireReleases = [..aspireReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            // Sort MAUI releases
            List<Release> sortedMauiReleases = [..mauiReleases
                .OrderByDescending(o => o.Version)
                .ThenByDescending(o => o.Id)];

            Dictionary<string, MajorRelease> sortedMajorReleasesDictionary = [];
            List<MajorRelease> sortedMajorReleasesList = [];

            bool isDiffMode = args != null && args.Length > 0 && args[0] == "diff";
            bool isLatestOnlyMode = args != null && args.Length > 1 && args[1] == "latest";

            // If arg 1 is "diff" calculate the diff and append it to current core.js file
            if (isDiffMode)
            {
                // load current core.json file
#if DEBUG
                IEnumerable<MajorRelease> corejson = LoadCurrentCoreJson();
#else
                IEnumerable<MajorRelease> corejson = await LoadCurrentCoreJsonAsync();
#endif
                // create a dictionary with preprocessed data
                foreach (var release in corejson)
                {
                    sortedMajorReleasesDictionary.Add($"{release.Version.Major}.{release.Version.Minor}", release);
                }

                List<string> processedReleases = [];

                foreach (var o in corejson)
                {
                    processedReleases.AddRange(o.ProcessedReleases);
                }

                List<Release> diff = [.. sortedReleases.Where(o => !processedReleases.Contains(o.Tag))];

                // Find new Aspire releases by checking for aspire- prefix in processedReleases
                List<Release> aspireDiff = [..sortedAspireReleases.Where(r =>
                    !processedReleases.Contains($"aspire-{r.Tag}"))];

                // Find new MAUI releases by checking for maui- prefix in processedReleases
                List<Release> mauiDiff = [..sortedMauiReleases.Where(r =>
                    !processedReleases.Contains($"maui-{r.Tag}"))];

                // Check if releases in diff are not in dictionary
                foreach (var release in diff)
                {
                    if (!sortedMajorReleasesDictionary.ContainsKey($"{release.Version.Major}.{release.Version.Minor}"))
                    {
                        sortedMajorReleasesDictionary.Add($"{release.Version.Major}.{release.Version.Minor}", new MajorRelease
                        {
                            Contributions = 0,
                            Contributors = [],
                            Product = release.Product,
                            Name = $"{release.Product} {release.Version.Major}.{release.Version.Minor}",
                            Version = release.Version,
                            Tag = $"v{release.Version.Major}.{release.Version.Minor}",
                            ProcessedReleases = []
                        });
                    }
                }

                if (diff.Count != 0)
                {
                    Console.WriteLine($"Processing diffs in releases...\n{repo} - {diff.Count}");

                    // For each new release, find its prior release and add it into a new list for commit comparison
                    List<Release> sortedNewReleases = [];
                    List<Release> majorReleasesList = [];
                    var latestGARelease = sortedReleases.ToList().Find(r => r.IsGA);

                    foreach (var r in diff)
                    {
                        var idx = sortedReleases.IndexOf(r);

                        // Skip if this is the last (oldest) release - no previous version to compare against
                        if (idx == sortedReleases.Count - 1)
                        {
                            sortedNewReleases.Add(r);
                            continue;
                        }

                        var previousVersion = sortedReleases[idx + 1];

                        // Add new release
                        sortedNewReleases.Add(r);

                        // If previous version isn't in newReleases already - add it
                        if (!diff.Contains(previousVersion))
                        {
                            // Append the latest processed release or last GA if first release
                            if (r.Version.Major != previousVersion.Version.Major)
                            {
                                sortedNewReleases.Add(latestGARelease);
                            }
                            else if (r.Version.Major == previousVersion.Version.Major)
                            {
                                sortedNewReleases.Add(previousVersion);
                            }
                        }
                        else if (diff.Contains(previousVersion) && r.Version.Major != previousVersion.Version.Major)
                        {
                            sortedNewReleases.Add(latestGARelease);
                        }
                    }

                    sortedNewReleases = [..sortedNewReleases
                        .OrderByDescending(o => o.Version)
                        .ThenByDescending(o => o.Id)];

                    // Process new list and trim the releases used for comparison
                    await ProcessReleases(sortedNewReleases, sortedMajorReleasesDictionary, repo, true);
                }

                // Process new Aspire releases in diff mode
                if (aspireDiff.Count != 0)
                {
                    Console.WriteLine($"\nProcessing Aspire diffs... - {aspireDiff.Count}");
                    await ProcessAspireReleases(aspireDiff, sortedMajorReleasesDictionary);
                }

                // Process new MAUI releases in diff mode
                if (mauiDiff.Count != 0)
                {
                    Console.WriteLine($"\nProcessing MAUI diffs... - {mauiDiff.Count}");
                    await ProcessMauiReleases(mauiDiff, sortedMajorReleasesDictionary);
                }

                // Write updated file if any changes were made
                if (diff.Count != 0 || aspireDiff.Count != 0 || mauiDiff.Count != 0)
                {
                    sortedMajorReleasesList = [.. sortedMajorReleasesDictionary.Values.OrderByDescending(o => o.Version)];

                    File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedMajorReleasesList));
                }
                else
                {
                    Console.WriteLine("The current releases list is up to date with core.js\nExiting...");
                }
            }
            else
            {
                // create a dictionary for major versions
                foreach (var release in sortedReleases)
                {
                    if (!sortedMajorReleasesDictionary.ContainsKey($"{release.Version.Major}.{release.Version.Minor}"))
                    {
                        var majorRelease = new MajorRelease
                        {
                            Contributions = 0,
                            Contributors = [],
                            Product = release.Product,
                            Name = $"{release.Product} {release.Version.Major}.{release.Version.Minor}",
                            Version = release.Version,
                            Tag = $"v{release.Version.Major}.{release.Version.Minor}",
                            ProcessedReleases = []
                        };
                        sortedMajorReleasesDictionary.Add($"{release.Version.Major}.{release.Version.Minor}", majorRelease);
                    }
                }

                Console.WriteLine($"Processing all releases...\n{repo} - {sortedReleases.Count}");

                // If latest-only mode, filter to only newest releases per major version
                if (isLatestOnlyMode)
                {
                    Console.WriteLine("Latest-only mode: Processing only newest contributors per .NET version");

                    // Group releases by major.minor and take only the newest ones
                    var latestReleases = sortedReleases
                        .GroupBy(r => $"{r.Version.Major}.{r.Version.Minor}")
                        .Select(g => g.First()) // First is already the newest due to OrderByDescending
                        .ToList();

                    await ProcessReleases(latestReleases, sortedMajorReleasesDictionary, repo, false, true);

                    // Get latest Aspire and MAUI releases per .NET version
                    var latestAspireReleases = GetLatestExternalReleases(sortedAspireReleases, MapAspireVersionToDotNet);
                    var latestMauiReleases = GetLatestExternalReleases(sortedMauiReleases, MapMauiVersionToDotNet);

                    Console.WriteLine($"\nProcessing latest Aspire releases... - {latestAspireReleases.Count}");
                    await ProcessAspireReleases(latestAspireReleases, sortedMajorReleasesDictionary, true);

                    Console.WriteLine($"\nProcessing latest MAUI releases... - {latestMauiReleases.Count}");
                    await ProcessMauiReleases(latestMauiReleases, sortedMajorReleasesDictionary, true);
                }
                else
                {
                    await ProcessReleases(sortedReleases, sortedMajorReleasesDictionary, repo);

                    // Process Aspire releases
                    Console.WriteLine($"\nProcessing Aspire releases... - {sortedAspireReleases.Count}");
                    await ProcessAspireReleases(sortedAspireReleases, sortedMajorReleasesDictionary);

                    // Process MAUI releases
                    Console.WriteLine($"\nProcessing MAUI releases... - {sortedMauiReleases.Count}");
                    await ProcessMauiReleases(sortedMauiReleases, sortedMajorReleasesDictionary);
                }

                sortedMajorReleasesList = [.. sortedMajorReleasesDictionary.Values];

                File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedMajorReleasesList));
            }
        }

        private static List<Release> GetLatestExternalReleases(List<Release> releases, Func<string, int> versionMapper)
        {
            // Group by .NET version mapping and take the first (newest) release for each
            var latestReleases = new List<Release>();
            var processedVersions = new HashSet<int>();

            foreach (var release in releases)
            {
                int dotnetVersion = versionMapper(release.Tag);
                if (dotnetVersion == -1)
                    continue;

                if (!processedVersions.Contains(dotnetVersion))
                {
                    latestReleases.Add(release);
                    processedVersions.Add(dotnetVersion);
                }
            }

            return latestReleases;
        }

#nullable enable
        private static async Task ProcessReleases(List<Release> releases, Dictionary<string, MajorRelease> majorReleasesDict, string repo, bool isDiff = false, bool isLatestOnly = false)
        {
            // dotnet/core
            Release currentRelease;
            Release previousRelease;
            for (int i = 0; i < releases.Count; i++)
            {
                currentRelease = releases[i];
                majorReleasesDict.TryGetValue($"{currentRelease.Version.Major}.{currentRelease.Version.Minor}", out var majorRelease);

                // In latest-only mode, process only the first release without comparison
                if (isLatestOnly)
                {
                    majorRelease?.ProcessedReleases.Add(currentRelease.Tag);
                    Console.WriteLine($"Processing (latest-only): {repo} {currentRelease.Tag}");

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
                        Console.WriteLine($"[INFO]: {currentRelease.Tag} is the first release in the series.");
                        //Debugger.Break();
                        continue;
                    }

                    majorRelease?.ProcessedReleases.Add(currentRelease.Tag);

                    Console.WriteLine($"Processing:[{i}] {repo} {previousRelease.Tag}..{currentRelease.Tag}");
                }

                // for each child repo get commits and count contribs
                foreach (var repoCurrentRelease in currentRelease.ChildRepos)
                {
                    var repoPrevRelease = previousRelease.ChildRepos.FirstOrDefault(r => r.Owner == repoCurrentRelease.Owner &&
                                                                                         r.Repository == repoCurrentRelease.Repository);
                    if (repoPrevRelease is null)
                    {
                        // This may happen
                        Console.WriteLine($"[ERROR]: {repoCurrentRelease.Url} doesn't exist in {previousRelease.Tag}!");
                        continue;
                    }

                    Debug.WriteLine($"{repoCurrentRelease.Tag} : {repoCurrentRelease.Name}");

                    try
                    {
                        Console.WriteLine($"\tProcessing: {repoCurrentRelease.Name}: {repoPrevRelease.Tag}..{repoCurrentRelease.Tag}");

                        if (repoPrevRelease.Tag != repoCurrentRelease.Tag)
                        {
                            var releaseDiff = await LoadCommitsForReleasesAsync(repoPrevRelease.Tag,
                                                                                repoCurrentRelease.Tag,
                                                                                repoCurrentRelease.Owner,
                                                                                repoCurrentRelease.Repository);
                            if (releaseDiff is null || releaseDiff.Count < 1)
                            {
                                //Debugger.Break();
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
                        Console.WriteLine(ex.Message);
                    }
                }

                if (Environment.GetEnvironmentVariable("TEST") == "1")
                    break;
            }
        }

        private static async Task ProcessExternalReleases(
            List<Release> releases,
            Dictionary<string, MajorRelease> majorReleasesDict,
            string repoName,
            Func<string, int> versionMapper,
            bool isLatestOnly = false)
        {
            Release currentRelease;
            Release previousRelease;

            for (int i = 0; i < releases.Count; i++)
            {
                currentRelease = releases[i];

                // Map version to .NET version
                int dotnetMajor = versionMapper(currentRelease.Tag);
                if (dotnetMajor == -1)
                {
                    Console.WriteLine($"[SKIP]: {repoName} {currentRelease.Tag} does not map to .NET 8, 9, or 10");
                    continue;
                }

                // Get the .NET major release to add contributors to
                majorReleasesDict.TryGetValue($"{dotnetMajor}.{0}", out var majorRelease);
                if (majorRelease == null)
                {
                    Console.WriteLine($"[ERROR]: .NET {dotnetMajor}.0 not found in dictionary for {repoName} {currentRelease.Tag}");
                    continue;
                }

                // In latest-only mode, only process the first release for each .NET version
                if (isLatestOnly)
                {
                    // Since GetLatestExternalReleases already filtered to one per .NET version,
                    // we just need to get commits from this release vs its previous release
                    if (i == releases.Count - 1)
                    {
                        string releaseTag = $"{repoName.ToLower()}-{currentRelease.Tag}";
                        majorRelease.ProcessedReleases.Add(releaseTag);
                        break;
                    }
                    previousRelease = releases[i + 1];
                }
                else
                {
                    // Skip if this is the last release (no previous to compare against)
                    if (i == releases.Count - 1)
                    {
                        // Add to processed releases for the last release
                        string releaseTag = $"{repoName.ToLower()}-{currentRelease.Tag}";
                        majorRelease.ProcessedReleases.Add(releaseTag);
                        break;
                    }

                    previousRelease = releases[i + 1];
                }

                // Add to processed releases
                string processedReleaseTag = $"{repoName.ToLower()}-{currentRelease.Tag}";
                majorRelease.ProcessedReleases.Add(processedReleaseTag);

                Console.WriteLine($"Processing: {repoName.ToLower()} {previousRelease.Tag}..{currentRelease.Tag} -> .NET {dotnetMajor}.0");

                try
                {
                    var releaseDiff = await LoadCommitsForReleasesAsync(previousRelease.Tag,
                                                                        currentRelease.Tag,
                                                                        "dotnet",
                                                                        repoName.ToLower());
                    if (releaseDiff is null || releaseDiff.Count < 1)
                    {
                        continue;
                    }

                    majorRelease.Contributions += releaseDiff.Count;
                    TallyCommits(majorRelease, repoName.ToLower(), releaseDiff);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]: {ex.Message}");
                }
            }
        }

        private static async Task ProcessAspireReleases(List<Release> aspireReleases, Dictionary<string, MajorRelease> majorReleasesDict, bool isLatestOnly = false)
        {
            await ProcessExternalReleases(aspireReleases, majorReleasesDict, "aspire", MapAspireVersionToDotNet, isLatestOnly);
        }

        private static async Task ProcessMauiReleases(List<Release> mauiReleases, Dictionary<string, MajorRelease> majorReleasesDict, bool isLatestOnly = false)
        {
            await ProcessExternalReleases(mauiReleases, majorReleasesDict, "maui", MapMauiVersionToDotNet, isLatestOnly);
        }

        private static int MapAspireVersionToDotNet(string aspireTag)
        {
            // Extract version from tag (v13.0.0 or just 13.0.0)
            var versionStr = aspireTag.TrimStart('v');
            var match = Regex.Match(versionStr, @"^(\d+)\.(\d+)");
            if (!match.Success) return -1;

            var major = int.Parse(match.Groups[1].Value);

            // Aspire 10-13 -> .NET 10
            if (major >= 10 && major <= 13) return 10;
            // Aspire 9.x -> .NET 9
            if (major == 9) return 9;
            // Aspire 8.x -> .NET 8
            if (major == 8) return 8;

            return -1;
        }

        private static int MapMauiVersionToDotNet(string mauiTag)
        {
            // Extract version from tag (v10.0.0 or just 10.0.0)
            var versionStr = mauiTag.TrimStart('v');
            var match = Regex.Match(versionStr, @"^(\d+)\.(\d+)");
            if (!match.Success) return -1;

            var major = int.Parse(match.Groups[1].Value);

            // MAUI follows .NET versioning: MAUI 10.x -> .NET 10, etc.
            if (major >= 6 && major <= 10) return major;

            return -1;
        }
#nullable disable

        /// <summary>
        /// Find the previous release for the current release in the sorted collection of all releases.
        /// Take the immediate previous release it it has the same major.minor version (e.g. 5.0.0-RC1 for 5.0.0-RC2),
        /// or take the previous GA release (e.g. 3.0.0 for 5.0.0-preview2 not 3.1.10).
        /// </summary>
        /// <param name="index">The index of the <paramref name="currentRelease"/> in the <paramref name="sortedReleases"/> list.</param>
        /// <returns>The previous release, if found; otherwise <see cref="null"/>, if the current release if the first release.</returns>
        private static Release GetPreviousRelease(List<Release> sortedReleases, Release currentRelease, int index)
        {
            if (currentRelease.Version.Major == sortedReleases[index].Version.Major &&
                currentRelease.Version.Minor == sortedReleases[index].Version.Minor)
                return sortedReleases[index];

            return sortedReleases.Skip(index).FirstOrDefault(r => currentRelease.Version > r.Version && r.IsGA);
        }

        private static void TallyCommits(MajorRelease majorRelease, string repoName, List<MergeBaseCommit> commits)
        {
            // these the commits within the release
            foreach (var item in commits)
            {
                if (item.author != null)
                {
                    var author = item.author;
                    author.name = item.commit.author.name;

                    if (string.IsNullOrEmpty(author.name))
                        author.name = "Unknown";

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

        private static async Task<IEnumerable<Release>> LoadReleasesAsync(string owner, string repo)
        {
            var results = await _ghclient.Repository.Release.GetAll(owner, repo);

            return results.Select(release => new Release
            {
                Name = release.Name,
                Tag = release.TagName,
                Id = release.Id,
                ChildRepos = ParseReleaseBody(release.Body),
                Contributors = []
            });
        }

        private static List<ChildRepo> ParseReleaseBody(string body)
        {
            var results = new List<ChildRepo>();

            var pattern = "\\[(.+)\\]\\(([^ ]+?)( \"(.+)\")?\\)";
            var rg = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var match = rg.Match(body);

            while (match.Success)
            {
                var name = match.Groups[1]?.Value.Trim();
                var url = match.Groups[2]?.Value.Trim();
                if (url.Contains("/tag/"))
                {
                    results.Add(new ChildRepo() { Name = name, Url = url });
                }

                match = match.NextMatch();
            }

            return results;
        }

        private static async Task<List<MergeBaseCommit>> LoadCommitsForReleasesAsync(string fromRelease, string toRelease, string owner, string repo)
        {
            try
            {
                // First call to check total commits
                var initialComparison = await _ghclient.Repository.Commit.Compare(owner, repo, fromRelease, toRelease);

                var allCommits = new List<GitHubCommit>();

                if (initialComparison.TotalCommits > 300)
                {
                    Console.WriteLine($"\t\t[INFO] {fromRelease}..{toRelease} has {initialComparison.TotalCommits} total commits, paging through all commits...");

                    // Calculate number of pages needed (300 commits per page)
                    var totalPages = (int)Math.Ceiling((double)initialComparison.TotalCommits / 300);

                    // Get all commits by paging through
                    for (int page = 1; page <= totalPages; page++)
                    {
                        var options = new ApiOptions
                        {
                            PageSize = 300,
                            PageCount = 1,
                            StartPage = page
                        };

                        var pagedComparison = await _ghclient.Repository.Commit.Compare(owner, repo, fromRelease, toRelease, options);
                        allCommits.AddRange(pagedComparison.Commits);

                        Console.WriteLine($"\t\t\tLoaded page {page}/{totalPages} ({pagedComparison.Commits.Count()} commits)");
                    }
                }
                else
                {
                    // Use commits from initial comparison if under 300
                    allCommits.AddRange(initialComparison.Commits);
                }

                // Filter out merge commits (commits with more than 1 parent)
                var individualCommits = allCommits.Where(c => c.Parents.Count() <= 1);

                // Log filtering results
                var totalCommits = allCommits.Count();
                var filteredCommits = individualCommits.Count();
                var mergeCommits = totalCommits - filteredCommits;

                if (mergeCommits > 0)
                {
                    Console.WriteLine($"\t\tFiltered {mergeCommits} merge commits, counting {filteredCommits} individual commits");
                }

                // Convert filtered commits to your MergeBaseCommit format
                return individualCommits
                .Select(c => new MergeBaseCommit
                {
                    sha = c.Sha,
                    author = new Author
                    {
                        name = c.Commit.Author?.Name,
                        html_url = c.Author?.HtmlUrl,
                        avatar_url = c.Author?.AvatarUrl
                    },
                    commit = new Commit
                    {
                        author = new Author { name = c.Commit.Author?.Name }
                    }
                })
                .Where(c => !string.IsNullOrEmpty(c.author.name) && !BotExclusionConstants.IsBot(c.author.name) && !c.author.name.ToLower().Contains("[bot]"))
                .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Compare {fromRelease}...{toRelease}: {ex.Message}");
                return null;
            }
        }

        private static List<MajorRelease> LoadCurrentCoreJson()
        {
            try
            {
                string fileName = "core.json";
                string jsonString = File.ReadAllText(fileName);
                var corejson = JsonSerializer.Deserialize<List<MajorRelease>>(jsonString);
                return corejson;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private static async Task<List<MajorRelease>> LoadCurrentCoreJsonAsync()
        {
            var url = "https://dotnet.microsoft.com/blob-assets/json/thanks/core.json";

            try
            {
                var response = await _client.GetFromJsonAsync<List<MajorRelease>>(url);
                return response;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private static async Task<T> ExecuteWithRateLimitHandling<T>(Func<Task<T>> operation)
        {
            var remainingRetries = 3;
        Retry:
            try
            {
                return await operation();
            }
            catch (Exception ex) when (remainingRetries > 0)
            {
                if (ex.Message.Contains("403"))
                {
                    string url = "https://api.github.com/rate_limit";
                    var limit = await _client.GetStringAsync(url);
                    var response = JsonSerializer.Deserialize<RateLimit>(limit);
                    var resetTime = DateTimeOffset.FromUnixTimeSeconds(response.rate.reset);
                    var delay = resetTime - DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1);
                    var until = DateTime.Now.Add(delay);
                    Console.WriteLine($"Rate limit exceeded. Waiting for {delay.TotalMinutes:N1} mins until {until}.");
                    await Task.Delay(delay);
                    remainingRetries--;
                    goto Retry;
                }
                else
                {
                    return await Task.FromException<T>(ex);
                }

            }
        }
    }
}
