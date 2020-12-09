using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using dotnetthanks;
using Microsoft.Extensions.Configuration;
using Octokit;

namespace dotnetthanks_loader
{
    class Program
    {
        private static HttpClient _client;
        private static readonly string[] exclusions = new string[] { "dependabot[bot]", "github-actions[bot]", "msftbot[bot]", "github-actions[bot]" };
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
            var basic = new Credentials(_token);
            _ghclient.Credentials = basic;

            var repo = "core";

            // load all releases for dotnet/core
            IEnumerable<dotnetthanks.Release> allReleases = await LoadReleasesAsync(repo);

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
            var sortedReleases = allReleases
                .OrderByDescending(o => o.Version).ThenByDescending(o => o.Id)
                .ToList();

            Console.WriteLine($"{repo} - {sortedReleases.Count}");

            // dotnet/core
            dotnetthanks.Release currentRelease;
            dotnetthanks.Release previousRelease;
            for (int i = 0; i < sortedReleases.Count - 1; i++)
            {
                currentRelease = sortedReleases[i];
                previousRelease = GetPreviousRelease(sortedReleases, currentRelease, i + 1);
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
                        var releaseDiff = await LoadCommitsForReleasesAsync(repoPrevRelease.Tag,
                                                                            repoCurrentRelease.Tag,
                                                                            repoCurrentRelease.Owner,
                                                                            repoCurrentRelease.Repository);
                        if (releaseDiff is null || releaseDiff.total_commits < 1)
                        {
                            Debugger.Break();
                            continue;
                        }

                        currentRelease.Contributions += releaseDiff.total_commits;
                        TallyCommits(currentRelease, repoCurrentRelease.Repository, releaseDiff);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

            }

            System.IO.File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedReleases));
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

        private static void TallyCommits(dotnetthanks.Release core, string repoName, Root root)
        {
            // these the commits within the release
            foreach (var item in root.commits)
            {

                if (item.author != null)
                {
                    var author = item.author;
                    author.name = item.commit.author.name;

                    if (String.IsNullOrEmpty(author.name))
                        author.name = "Unknown";

                    if (!exclusions.Contains(author.name))
                    {
                        // find if the author has been counted
                        var person = core.Contributors.Find(p => p.Name == author.name);
                        if (person == null)
                        {
                            person = new dotnetthanks.Contributor()
                            {
                                Name = author.name,
                                Link = author.html_url,
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

        private static async Task<IEnumerable<dotnetthanks.Release>> LoadReleasesAsync(string repo)
        {
            var results = await _ghclient.Repository.Release.GetAll("dotnet", repo);

            return results.Select(release => new dotnetthanks.Release
            {
                Name = release.Name,
                Tag = release.TagName,
                Id = release.Id,
                // This field points to the default branch, which is useless
                // TargetCommit = release.TargetCommitish,
                ChildRepos = ParseReleaseBody(release.Body)
            });
        }

        private static List<ChildRepo> ParseReleaseBody(string body)
        {
            var results = new List<ChildRepo>();

            var items = body.Split("\r\n");
            Array.ForEach(items, r =>
            {
                if (r.StartsWith("*"))
                {
                    var parts = r.Split("*[]())".ToCharArray());
                    results.Add(new ChildRepo() { Name = parts[2], Url = parts[4] });
                }
            });

            return results;
        }

        private static async Task<Root> LoadCommitsForReleasesAsync(string fromRelease, string toRelease, string owner, string repo)
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("authorization", $"Basic {_token}");
            _client.DefaultRequestHeaders.Add("cache-control", "no-cache");
            _client.DefaultRequestHeaders.Add("User-Agent", "dotnet-thanks");

            try
            {
                string url = $"https://api.github.com/repos/{owner}/{repo}/compare/{fromRelease}...{toRelease}";
                var commits = await _client.GetStringAsync(url);

                var releaseCommits = JsonSerializer.Deserialize<Root>(commits);

                return releaseCommits;
            }
            catch (System.Exception ex)
            {

                Console.WriteLine(ex.Message);
            }

            return null;

        }
    }
}
