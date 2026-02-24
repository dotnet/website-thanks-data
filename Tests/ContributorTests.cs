using dotnetthanks_loader;
using Xunit;

namespace dotnetthanks_loader.Tests
{
    /// <summary>
    /// Unit tests for contributor processing between .NET 10.0 GA and 10.0.1.
    /// These tests validate core functionality using mock GitHub data.
    /// </summary>
    public class ContributorTests
    {
        private readonly string _fixturesPath;
        private readonly MockGitHubService _mockGitHubService;
        private readonly ContributorService _contributorService;

        public ContributorTests()
        {
            _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            _mockGitHubService = new MockGitHubService(_fixturesPath);
            _contributorService = new ContributorService(_mockGitHubService);
        }

        /// <summary>
        /// Test 1: Validates that "TestContributor" user has the correct total commit count
        /// and specific repo commit counts between .NET 10.0 GA and 10.0.1 releases.
        /// </summary>
        [Fact]
        public async Task TestContributor_HasCorrectCommitCounts()
        {
            // Arrange
            var releases = (await _mockGitHubService.GetReleasesAsync("dotnet", "core")).ToList();
            
            // Sort releases by version descending (like the real code does)
            releases = releases.OrderByDescending(o => o.Version).ThenByDescending(o => o.Id).ToList();
            
            // Both releases are in the 10.0 series (10.0.0 and 10.0.1)
            var majorReleasesDict = new Dictionary<string, MajorRelease>
            {
                ["10.0"] = new MajorRelease
                {
                    Contributors = [],
                    Contributions = 0,
                    Name = ".NET 10.0",
                    Product = ".NET",
                    Version = Version.Parse("10.0.0"),
                    Tag = "v10.0",
                    ProcessedReleases = []
                }
            };

            // Act
            await _contributorService.ProcessReleasesAsync(releases, majorReleasesDict, "core");

            // Assert - Contributors are added to the 10.0 major release
            var majorRelease = majorReleasesDict["10.0"];
            var testContributor = majorRelease.Contributors.Find(c => c.Name == "TestContributor");
            
            Assert.NotNull(testContributor);
            
            // TestContributor has: 3 commits in runtime, 1 in sdk, 1 in aspnetcore = 5 total
            Assert.Equal(5, testContributor.Count);
            
            // Verify specific repo counts
            var runtimeRepo = testContributor.Repos.Find(r => r.Name == "runtime");
            Assert.NotNull(runtimeRepo);
            Assert.Equal(3, runtimeRepo.Count);
            
            var sdkRepo = testContributor.Repos.Find(r => r.Name == "sdk");
            Assert.NotNull(sdkRepo);
            Assert.Equal(1, sdkRepo.Count);
            
            var aspnetcoreRepo = testContributor.Repos.Find(r => r.Name == "aspnetcore");
            Assert.NotNull(aspnetcoreRepo);
            Assert.Equal(1, aspnetcoreRepo.Count);
        }

        /// <summary>
        /// Test 2: Validates the total contributor count for the release.
        /// Ensures only unique contributors are counted (no duplicates).
        /// </summary>
        [Fact]
        public async Task TotalContributorCount_IsCorrect()
        {
            // Arrange
            var releases = (await _mockGitHubService.GetReleasesAsync("dotnet", "core")).ToList();
            
            // Sort releases by version descending (like the real code does)
            releases = releases.OrderByDescending(o => o.Version).ThenByDescending(o => o.Id).ToList();
            
            var majorReleasesDict = new Dictionary<string, MajorRelease>
            {
                ["10.0"] = new MajorRelease
                {
                    Contributors = [],
                    Contributions = 0,
                    Name = ".NET 10.0",
                    Product = ".NET",
                    Version = Version.Parse("10.0.0"),
                    Tag = "v10.0",
                    ProcessedReleases = []
                }
            };

            // Act
            await _contributorService.ProcessReleasesAsync(releases, majorReleasesDict, "core");

            // Assert
            var majorRelease = majorReleasesDict["10.0"];
            
            // Expected contributors (bots excluded):
            // - TestContributor (3 in runtime, 1 in sdk, 1 in aspnetcore)
            // - John Developer (1 in runtime)
            // - Jane Coder (1 in runtime)
            // - SDK Developer (1 in sdk)
            // - ASP.NET Developer (1 in aspnetcore)
            // Total: 5 unique contributors
            Assert.Equal(5, majorRelease.Contributors.Count);
            
            // Verify total contributions (non-bot commits)
            // runtime: 5 (3 TestContributor + 1 john + 1 jane, 2 bots excluded)
            // sdk: 2 (1 TestContributor + 1 sdk dev, 1 bot excluded)
            // aspnetcore: 2 (1 TestContributor + 1 asp dev)
            // Total: 9 contributions
            Assert.Equal(9, majorRelease.Contributions);
        }

        /// <summary>
        /// Test 3: Validates that bots are properly excluded from contributor counts.
        /// Verifies that BotExclusionConstants correctly identifies all bot accounts.
        /// </summary>
        [Fact]
        public async Task BotExclusion_WorksCorrectly()
        {
            // Arrange
            var releases = (await _mockGitHubService.GetReleasesAsync("dotnet", "core")).ToList();
            
            // Sort releases by version descending (like the real code does)
            releases = releases.OrderByDescending(o => o.Version).ThenByDescending(o => o.Id).ToList();
            
            var majorReleasesDict = new Dictionary<string, MajorRelease>
            {
                ["10.0"] = new MajorRelease
                {
                    Contributors = [],
                    Contributions = 0,
                    Name = ".NET 10.0",
                    Product = ".NET",
                    Version = Version.Parse("10.0.0"),
                    Tag = "v10.0",
                    ProcessedReleases = []
                }
            };

            // Act
            await _contributorService.ProcessReleasesAsync(releases, majorReleasesDict, "core");

            // Assert
            var majorRelease = majorReleasesDict["10.0"];
            
            // Verify that known bots are NOT in the contributors list
            var botNames = new[]
            {
                "dependabot[bot]",
                "github-actions[bot]",
                "dotnet-maestro[bot]"
            };

            foreach (var botName in botNames)
            {
                var botContributor = majorRelease.Contributors.Find(c => 
                    c.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
                Assert.Null(botContributor);
            }

            // Verify BotExclusionConstants correctly identifies bots
            foreach (var botName in BotExclusionConstants.BotUsernames)
            {
                Assert.True(BotExclusionConstants.IsBot(botName), 
                    $"BotExclusionConstants.IsBot should return true for '{botName}'");
            }

            // Verify non-bots are not excluded
            Assert.False(BotExclusionConstants.IsBot("TestContributor"));
            Assert.False(BotExclusionConstants.IsBot("John Developer"));
            Assert.False(BotExclusionConstants.IsBot("Jane Coder"));
        }
    }
}
