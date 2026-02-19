#nullable enable

using System.Text.Json;
using System.Text.RegularExpressions;

namespace dotnetthanks_loader.Tests
{
    /// <summary>
    /// Mock implementation of <see cref="IGitHubService"/> that loads data from JSON fixture files.
    /// Used for unit testing without making actual GitHub API calls.
    /// </summary>
    public class MockGitHubService : IGitHubService
    {
        private readonly string _fixturesPath;
        private readonly ILoggingService _logger;

        public MockGitHubService(string fixturesPath, ILoggingService? logger = null)
        {
            _fixturesPath = fixturesPath;
            _logger = logger ?? Logger.Instance;
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<Release>> GetReleasesAsync(string owner, string repo)
        {
            var filePath = Path.Combine(_fixturesPath, $"releases-{owner}-{repo}.json");
            
            if (!File.Exists(filePath))
            {
                _logger.Warning($"Fixture file not found: {filePath}");
                return Enumerable.Empty<Release>();
            }

            var json = await File.ReadAllTextAsync(filePath);
            var releases = JsonSerializer.Deserialize<List<ReleaseFixture>>(json) ?? [];

            return releases.Select(r => new Release
            {
                Name = r.Name,
                Tag = r.Tag,
                Id = r.Id,
                ChildRepos = r.ChildRepos?.Select(c => new ChildRepo 
                { 
                    Name = c.Name, 
                    Url = c.Url 
                }).ToList() ?? [],
                Contributors = []
            });
        }

        /// <inheritdoc/>
        public async Task<List<MergeBaseCommit>?> CompareCommitsAsync(string owner, string repo, string fromRef, string toRef)
        {
            // Sanitize refs for filename (replace special chars)
            var safeFromRef = SanitizeRefForFilename(fromRef);
            var safeToRef = SanitizeRefForFilename(toRef);
            
            var filePath = Path.Combine(_fixturesPath, "commits", $"{owner}-{repo}-{safeFromRef}-{safeToRef}.json");
            
            if (!File.Exists(filePath))
            {
                _logger.Debug($"Commit fixture file not found: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var commits = JsonSerializer.Deserialize<List<CommitFixture>>(json) ?? [];

            return commits
                .Select(c => new MergeBaseCommit
                {
                    sha = c.Sha,
                    author = new Author
                    {
                        name = c.AuthorName,
                        html_url = c.AuthorUrl,
                        avatar_url = c.AvatarUrl
                    },
                    commit = new Commit
                    {
                        author = new Author { name = c.AuthorName }
                    }
                })
                .Where(c => !string.IsNullOrEmpty(c.author.name) && 
                           !BotExclusionConstants.IsBot(c.author.name) && 
                           !c.author.name.ToLower().Contains("[bot]"))
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<List<MajorRelease>?> LoadCoreJsonAsync()
        {
            var filePath = Path.Combine(_fixturesPath, "core.json");
            
            if (!File.Exists(filePath))
            {
                _logger.Warning($"Core.json fixture file not found: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<MajorRelease>>(json);
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

        private static string SanitizeRefForFilename(string refName)
        {
            // Replace characters that are invalid in filenames
            return refName.Replace("/", "-").Replace("\\", "-").Replace(":", "-");
        }
    }

    /// <summary>
    /// DTO for release fixture JSON files.
    /// </summary>
    public class ReleaseFixture
    {
        public string Name { get; set; } = "";
        public string Tag { get; set; } = "";
        public long Id { get; set; }
        public List<ChildRepoFixture>? ChildRepos { get; set; }
    }

    /// <summary>
    /// DTO for child repo in fixture JSON files.
    /// </summary>
    public class ChildRepoFixture
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// DTO for commit fixture JSON files.
    /// </summary>
    public class CommitFixture
    {
        public string Sha { get; set; } = "";
        public string AuthorName { get; set; } = "";
        public string AuthorUrl { get; set; } = "";
        public string AvatarUrl { get; set; } = "";
    }
}
