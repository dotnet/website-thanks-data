#nullable enable

using Microsoft.Extensions.Configuration;
using Octokit;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace dotnetthanks_loader
{
    /// <summary>
    /// Production implementation of <see cref="IGitHubService"/> using Octokit.
    /// </summary>
    public class GitHubService : IGitHubService
    {
        private readonly GitHubClient _ghclient;
        private readonly HttpClient _httpClient;
        private readonly ILoggingService _logger;
        private readonly bool _useLocalCoreJson;

        public GitHubService(IConfiguration config, ILoggingService? logger = null, bool useLocalCoreJson = false)
        {
            _logger = logger ?? Logger.Instance;
            _useLocalCoreJson = useLocalCoreJson;

            _ghclient = new GitHubClient(new ProductHeaderValue("dotnet-thanks"));
            
            var clientId = config.GetSection("GITHUB_CLIENTID").Value;
            var clientSecret = config.GetSection("GITHUB_CLIENTSECRET").Value;
            
            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                var basic = new Credentials(clientId, clientSecret);
                _ghclient.Credentials = basic;
            }

            _httpClient = new HttpClient();
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<Release>> GetReleasesAsync(string owner, string repo)
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

        /// <inheritdoc/>
        public async Task<List<MergeBaseCommit>?> CompareCommitsAsync(string owner, string repo, string fromRef, string toRef)
        {
            try
            {
                // First call to check total commits
                var initialComparison = await _ghclient.Repository.Commit.Compare(owner, repo, fromRef, toRef);

                var allCommits = new List<GitHubCommit>();

                if (initialComparison.TotalCommits > 300)
                {
                    _logger.Debug($"{fromRef}..{toRef} has {initialComparison.TotalCommits} total commits, paging through all commits...");

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

                        var pagedComparison = await _ghclient.Repository.Commit.Compare(owner, repo, fromRef, toRef, options);
                        allCommits.AddRange(pagedComparison.Commits);

                        _logger.Debug($"Loaded page {page}/{totalPages} ({pagedComparison.Commits.Count()} commits)");
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
                    _logger.Debug($"Filtered {mergeCommits} merge commits, counting {filteredCommits} individual commits");
                }

                // Convert filtered commits to MergeBaseCommit format
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
                    .Where(c => !string.IsNullOrEmpty(c.author.name) && 
                               !BotExclusionConstants.IsBot(c.author.name) && 
                               !c.author.name.ToLower().Contains("[bot]"))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Compare {fromRef}...{toRef}");
                return null;
            }
        }

        /// <inheritdoc/>
        public async Task<List<MajorRelease>?> LoadCoreJsonAsync()
        {
            if (_useLocalCoreJson)
            {
                return LoadLocalCoreJson();
            }

            var url = "https://dotnet.microsoft.com/blob-assets/json/thanks/core.json";

            try
            {
                var response = await _httpClient.GetFromJsonAsync<List<MajorRelease>>(url);
                return response;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load core.json from URL");
            }

            return null;
        }

        private List<MajorRelease>? LoadLocalCoreJson()
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
                _logger.Error(ex, "Failed to load core.json");
            }

            return null;
        }

        /// <inheritdoc/>
        public List<ChildRepo> ParseReleaseBody(string body)
        {
            var results = new List<ChildRepo>();

            if (string.IsNullOrEmpty(body))
                return results;

            var pattern = "\\[(.+)\\]\\(([^ ]+?)( \"(.+)\")?\\)";
            var rg = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var match = rg.Match(body);

            while (match.Success)
            {
                var name = match.Groups[1]?.Value.Trim();
                var url = match.Groups[2]?.Value.Trim();
                if (url != null && url.Contains("/tag/"))
                {
                    results.Add(new ChildRepo() { Name = name, Url = url });
                }

                match = match.NextMatch();
            }

            return results;
        }
    }
}
