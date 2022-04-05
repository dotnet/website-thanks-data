using dotnetthanks;
using Microsoft.Extensions.Configuration;
using Octokit;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace dotnetthanks_loader
{
    class Program
    {
        private static HttpClient _client;
        private static readonly string[] exclusions = new string[] { "dependabot[bot]", "github-actions[bot]", "msftbot[bot]", "github-actions[bot]", "dotnet-bot", "dotnet bot", "nuget team bot" };
        private static string _token;
        private static bool InDocker { get { return Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"; } }

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
            IEnumerable<dotnetthanks.Release> allReleases = await LoadReleasesAsync(owner, repo);

            // Sort releases from the yongest to the oldest by version
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
            List<dotnetthanks.Release> sortedReleases = allReleases
                .OrderByDescending(o => o.Version).ThenByDescending(o => o.Id)
                .ToList();

            // If arg 1 is "diff" calculate the diff and append it to current core.js file
            if (args != null && args.Length > 0 && args[0] == "diff")
            {
                // load current core.json file
                IEnumerable<dotnetthanks.Release> corejson = await LoadCurrentCoreJsonAsync();
                var diff = sortedReleases.Except(corejson);

                if (diff.Count() > 0)
                {
                    Console.WriteLine($"Processing diffs in releases...\n{repo} - {diff.Count()}");

                    // For each new release, find its prior release and add it into a new list for commit comparison
                    var sortedNewReleases = new List<dotnetthanks.Release>();
                    var newReleases = diff
                        .OrderByDescending(o => o.Version)
                        .ThenByDescending(o => o.Id)
                        .ToList();

                    newReleases.ForEach(r => {
                        if (sortedReleases.IndexOf(r) > -1)
                        {
                            sortedNewReleases.Add(r);
                            sortedNewReleases.Add(sortedReleases[sortedReleases.IndexOf(r) + 1]);
                        }
                    });
                    
                    // Process new list and trim the releases used for comparison
                    await ProcessReleases(sortedNewReleases, repo);
                    corejson = corejson.ToList().Concat(newReleases).OrderByDescending(o => o.Version).ThenByDescending(o => o.Id).ToList();

                    System.IO.File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(corejson));
                }
                else
                {
                    Console.WriteLine("The current releases list is up to date with core.js\nExiting...");
                }
            }
            else
            {
                Console.WriteLine($"Processing all releases...\n{repo} - {sortedReleases.Count}");

                await ProcessReleases(sortedReleases, repo);

                if (InDocker)
                {
                    var myRepo = Environment.GetEnvironmentVariable("source");
                    var root = $"/app/{Environment.GetEnvironmentVariable("dir")}";
                    var branch = $"thanks-data{Guid.NewGuid().ToString()}";
                    var output = new StringBuilder();

                    if (Debugger.IsAttached)
                    {
                        root = Environment.GetEnvironmentVariable("dir");
                    }

                    if (!Directory.Exists(root))
                    {
                        Directory.CreateDirectory(root);
                    }

                    System.IO.File.WriteAllText($"/{root}/{repo}.json", JsonSerializer.Serialize(sortedReleases));

                    // clone the repo
                    output.AppendLine(Bash($"git -C {myRepo} pull"));
                    // create branch 
                    output.AppendLine(Bash($"git checkout -b {branch}"));

                    output.AppendLine(Bash($"git add /{root}/{repo}.json"));
                    output.AppendLine(Bash($"git commit -m '{repo}.json added'"));
                    output.AppendLine(Bash($"git push --set-upstream origin {branch}"));

                    Console.WriteLine(output.ToString());

                    var pr = await CreatePullRequestFromFork("spboyer/website-resources", branch);

                    Console.WriteLine(pr.HtmlUrl);
                }
                else
                {
                    System.IO.File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedReleases));
                }
            }
        }

        private static async Task ProcessReleases(List<dotnetthanks.Release> releases, string repo)
        {
            // dotnet/core
            dotnetthanks.Release currentRelease;
            dotnetthanks.Release previousRelease;
            for (int i = 0; i < releases.Count - 1; i++)
            {
                currentRelease = releases[i];
                previousRelease = GetPreviousRelease(releases, currentRelease, i + 1);
                if (previousRelease is null)
                {
                    // Is this the first release?
                    Console.WriteLine($"[INFO]: {currentRelease.Tag} is the first release in the series.");
                    Debugger.Break();
                    continue;
                }

                Console.WriteLine($"Processing:[{i}] {repo} {previousRelease.Tag}..{currentRelease.Tag}");

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

                            currentRelease.Contributions += releaseDiff.Count;
                            TallyCommits(currentRelease, repoCurrentRelease.Repository, releaseDiff);
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

        private static async Task<PullRequest> CreatePullRequestFromFork(string forkname, string branch)
        {
            var basic = new Credentials(_token);
            var client = new GitHubClient(new ProductHeaderValue("dotnet-thanks"));
            client.Credentials = basic;

            NewPullRequest newPr = new NewPullRequest("Update thanks data file", $"spboyer:{branch}", "master");

            try
            {
                var pullRequest = await client.PullRequest.Create("dotnet", "website-resources", newPr);

                return pullRequest;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        /// <summary>
        /// Find the previous release for the current release in the sorted collection of all releases.
        /// Take the immediate previous release it it has the same major.minor version (e.g. 5.0.0-RC1 for 5.0.0-RC2),
        /// or take the previous GA release (e.g. 3.0.0 for 5.0.0-preview2 not 3.1.10).
        /// </summary>
        /// <param name="index">The index of the <paramref name="currentRelease"/> in the <paramref name="sortedReleases"/> list.</param>
        /// <returns>The previous release, if found; otherwise <see cref="null"/>, if the current release if the first release.</returns>
        private static dotnetthanks.Release GetPreviousRelease(List<dotnetthanks.Release> sortedReleases, dotnetthanks.Release currentRelease, int index)
        {
            if (currentRelease.Version.Major == sortedReleases[index].Version.Major &&
                currentRelease.Version.Minor == sortedReleases[index].Version.Minor)
                return sortedReleases[index];

            return sortedReleases.Skip(index).FirstOrDefault(r => currentRelease.Version > r.Version && r.IsGA);
        }

        private static void TallyCommits(dotnetthanks.Release core, string repoName, List<MergeBaseCommit> commits)
        {
            // these the commits within the release
            foreach (var item in commits)
            {
                if (item.author != null)
                {
                    var author = item.author;
                    author.name = item.commit.author.name;

                    if (String.IsNullOrEmpty(author.name))
                        author.name = "Unknown";

                    if (!exclusions.Contains(author.name.ToLower()))
                    {
                        // find if the author has been counted
                        var person = core.Contributors.Find(p => p.Name == author.name);
                        if (person == null)
                        {
                            person = new dotnetthanks.Contributor()
                            {
                                Name = author.name,
                                Link = author.html_url,
                                Avatar = author.avatar_url,
                                Count = 1
                            };
                            person.Repos.Add(new RepoItem() { Name = repoName, Count = 1 });

                            core.Contributors.Add(person);
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

        private static async Task<IEnumerable<dotnetthanks.Release>> LoadReleasesAsync(string owner, string repo)
        {
            var results = await _ghclient.Repository.Release.GetAll(owner, repo);

            return results.Select(release => new dotnetthanks.Release
            {
                Name = release.Name,
                Tag = release.TagName,
                Id = release.Id,
                ChildRepos = ParseReleaseBody(release.Body),
                Contributors = new List<dotnetthanks.Contributor>()
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
            _client = new HttpClient();
            _client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("dotnet-thanks", "1.0"));
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", _token);

            try
            {
                string url = $"https://api.github.com/repos/{owner}/{repo}/compare/{fromRelease}...{toRelease}";
                var compare = await ExecuteWithRateLimitHandling(() => _client.GetStringAsync(url));

                var compareDetails = JsonSerializer.Deserialize<Root>(compare);

                var remainingCommits = compareDetails.ahead_by;
                var page = 0;
                var releaseCommits = new List<MergeBaseCommit>(remainingCommits);
                while (remainingCommits > 0)
                {
                    url = $"https://api.github.com/repos/{owner}/{repo}/commits?sha={toRelease}&page={page}";
                    var commits = await ExecuteWithRateLimitHandling(() => _client.GetStringAsync(url));
                    var pageDetails = JsonSerializer.Deserialize<List<MergeBaseCommit>>(commits);
                    releaseCommits.AddRange(remainingCommits >= pageDetails.Count ? pageDetails : pageDetails.Take(remainingCommits));
                    remainingCommits -= pageDetails.Count;
                    page++;
                }

                return releaseCommits;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return null;
        }

        private static async Task<List<dotnetthanks.Release>> LoadCurrentCoreJsonAsync()
        {
            _client = new HttpClient();
            var url = "https://dotnetwebsitestorage.blob.core.windows.net/blob-assets/json/thanks/core.json";

            try
            {
                var response = await _client.GetFromJsonAsync<List<dotnetthanks.Release>>(url);
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
                    var delay = new TimeSpan(response.rate.reset)
                                  .Add(TimeSpan.FromMinutes(10)); // Add some buffer
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

        private static string Bash(string cmd)
        {
            var escapedArgs = cmd.Replace("\"", "\\\"");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return result;
        }

    }

}
