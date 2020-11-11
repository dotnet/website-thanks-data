using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using dotnetthanks;
using Octokit;
using Microsoft.Extensions.Configuration;

namespace dotnetthanks_loader
{
    class Program
    {
        private static HttpClient _client;
        private static string[] exclusions = new string[]{"dependabot[bot]", "github-actions[bot]", "msftbot[bot]", "github-actions[bot]"};

        private static string token;

        private static GitHubClient _ghclient;

        static async Task Main(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddEnvironmentVariables()
                .AddUserSecrets<Program>()
                .Build();

            token = config.GetSection("GITHUB_TOKEN").Value;

            _ghclient = new GitHubClient(new ProductHeaderValue("dotnet-thanks"));
            var basic = new Credentials(token);
            _ghclient.Credentials = basic;

            var repo = "core";

            // load all releases for dotnet/core
            var allReleases = await LoadReleases(repo);

            var sortedReleases = allReleases.OrderBy(o => o.Id).ToList();

            dotnetthanks.Release toRelease;


            Console.WriteLine($"{repo} - {sortedReleases.Count.ToString()}");

            // dotnet/core
            for (int i = sortedReleases.Count - 1; i >= 1; i--)
            {
                toRelease = sortedReleases[i];
                Console.WriteLine($"Processing: {repo} - {i.ToString()} : {toRelease.Tag}");
                
                // for each child repo get commits and count contribs
                foreach (var child in toRelease.ChildRepos)
                {
                    try
                    {
                        Console.WriteLine($"Processing Child : {child.Name}:{child.Tag}");
                        var childRelease = await LoadRelease(child.Owner, child.Repository, child.Tag);
                        var childRoot = await LoadCommitsForReleases(childRelease.Tag, childRelease.TargetCommit, child.Owner, child.Repository);

                        if (childRoot != null && childRoot.total_commits > 0)
                        {
                            toRelease.Contributions += childRoot.total_commits;
                            TallyCommits(toRelease, childRelease, childRoot);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }

            }

            System.IO.File.WriteAllText($"./{repo}.json", JsonSerializer.Serialize(sortedReleases));

        }

        private static void TallyCommits(dotnetthanks.Release core, dotnetthanks.Release release, Root root)
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
                            person.Repos.Add(new RepoItem() { Name = release.Name, Count = 1 });

                            core.Contributors.Add(person);
                        }
                        else
                        {
                            // found the author, does the repo exist as well?
                            person.Count += 1;

                            var repoItem = person.Repos.Find(r => r.Name == release.Name);
                            if (repoItem == null)
                            {
                                person.Repos.Add(new RepoItem() { Name = release.Name, Count = 1 });
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

        private static async Task<List<dotnetthanks.Release>> LoadReleases(string repo)
        {

            var list = new List<dotnetthanks.Release>();

            var results = await _ghclient.Repository.Release.GetAll("dotnet", repo);
            foreach (var release in results)
            {
                list.Add(new dotnetthanks.Release()
                {
                    Name = release.Name,
                    Tag = release.TagName,
                    Id = release.Id,
                    TargetCommit = release.TargetCommitish,
                    ChildRepos = ParseReleaseBody(release.Body)

                });
            }

            return list;

        }

        private static async Task<dotnetthanks.Release> LoadRelease(string owner, string repo, string tag)
        {

            var result = await _ghclient.Repository.Release.Get(owner, repo, tag);

            return new dotnetthanks.Release()
            {
                Name = result.Name,
                Tag = result.TagName,
                Id = result.Id,
                TargetCommit = result.TargetCommitish,
                ChildRepos = ParseReleaseBody(result.Body)

            };
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

        private static async Task<Root> LoadCommitsForReleases(string fromRelease, string toRelease, string owner, string repo)
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("authorization", $"Basic {token}");
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
