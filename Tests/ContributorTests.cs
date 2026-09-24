using dotnetthanks_loader;
using Octokit;
using System.Text.Json;
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

        [Fact]
        public void MergeAndWriteDockerContributors_AccumulatesExistingSnapshot()
        {
            var outputPath = Path.Combine(Path.GetTempPath(), $"docker-contributors-{Guid.NewGuid()}.json");
            var existingSnapshots = new Dictionary<string, DockerVersionSnapshot>
            {
                ["10.0"] = new DockerVersionSnapshot
                {
                    LatestSha = "old-sha",
                    Contributors =
                    [
                        new Contributor
                        {
                            Name = "existing",
                            Link = "https://github.com/existing",
                            Count = 3,
                            Repos =
                            [
                                new RepoItem { Name = "dotnet-docker", Count = 3 },
                                new RepoItem { Name = "runtime", Count = 1 }
                            ]
                        },
                        new Contributor
                        {
                            Name = "untouched",
                            Link = "https://github.com/untouched",
                            Count = 2
                        }
                    ]
                },
                ["9.0"] = new DockerVersionSnapshot { LatestSha = "nine-sha" }
            };
            var currentSnapshots = new Dictionary<string, DockerVersionSnapshot>
            {
                ["10.0"] = new DockerVersionSnapshot
                {
                    LatestSha = "new-sha",
                    Contributors =
                    [
                        new Contributor
                        {
                            Name = "existing",
                            Link = "https://github.com/existing",
                            Count = 2,
                            Repos =
                            [
                                new RepoItem { Name = "dotnet-docker", Count = 2 },
                                new RepoItem { Name = "sdk", Count = 1 }
                            ]
                        },
                        new Contributor
                        {
                            Name = "new",
                            Link = "https://github.com/new",
                            Count = 1
                        }
                    ]
                }
            };

            try
            {
                File.WriteAllText(outputPath, JsonSerializer.Serialize(existingSnapshots));

                Program.MergeAndWriteDockerContributors(currentSnapshots, outputPath);

                var merged = JsonSerializer.Deserialize<Dictionary<string, DockerVersionSnapshot>>(
                    File.ReadAllText(outputPath));
                Assert.NotNull(merged);
                Assert.Equal("new-sha", merged["10.0"].LatestSha);
                Assert.Equal("nine-sha", merged["9.0"].LatestSha);
                Assert.Equal(3, merged["10.0"].Contributors.Count);

                var existing = merged["10.0"].Contributors.Single(
                    contributor => contributor.Link == "https://github.com/existing");
                Assert.Equal(5, existing.Count);
                Assert.Equal(5, existing.Repos.Single(repo => repo.Name == "dotnet-docker").Count);
                Assert.Equal(1, existing.Repos.Single(repo => repo.Name == "runtime").Count);
                Assert.Equal(1, existing.Repos.Single(repo => repo.Name == "sdk").Count);
                Assert.Contains(merged["10.0"].Contributors,
                    contributor => contributor.Link == "https://github.com/untouched");
                Assert.Contains(merged["10.0"].Contributors,
                    contributor => contributor.Link == "https://github.com/new");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [Fact]
        public async Task DockerCommitFetchFailure_DoesNotAdvanceLatestSha()
        {
            var snapshotPath = Path.Combine(Path.GetTempPath(), $"docker-contributors-{Guid.NewGuid()}.json");
            var majorRelease = new MajorRelease
            {
                Contributors = [],
                Contributions = 0,
                Name = ".NET 10.0",
                Product = ".NET",
                Version = Version.Parse("10.0.0"),
                Tag = "v10.0",
                ProcessedReleases = []
            };
            var existingSnapshots = new Dictionary<string, DockerVersionSnapshot>
            {
                ["10.0"] = new DockerVersionSnapshot { LatestSha = "old-sha" }
            };
            _mockGitHubService.FailedDockerCommitPaths.Add("src/runtime/10.0");

            try
            {
                File.WriteAllText(snapshotPath, JsonSerializer.Serialize(existingSnapshots));

                var snapshots = await Program.ProcessDotnetDockerContributionsWithResultAsync(
                    _mockGitHubService,
                    new Dictionary<string, MajorRelease> { ["10.0"] = majorRelease },
                    snapshotPath);

                Assert.Equal("old-sha", snapshots["10.0"].LatestSha);
                Assert.Empty(snapshots["10.0"].Contributors);
                Assert.Empty(majorRelease.Contributors);
                Assert.Equal(0, majorRelease.Contributions);
                Assert.DoesNotContain("dotnet-docker-10.0", majorRelease.ProcessedReleases);
            }
            finally
            {
                File.Delete(snapshotPath);
            }
        }

        [Fact]
        public async Task DockerContributors_AccumulateAcrossPipelineRuns()
        {
            var snapshotPath = Path.Combine(Path.GetTempPath(), $"docker-contributors-{Guid.NewGuid()}.json");

            try
            {
                _mockGitHubService.DockerCommitFixtureSet = "run-1";
                var firstRun = await Program.ProcessDotnetDockerContributionsWithResultAsync(
                    _mockGitHubService,
                    CreateDockerMajorReleases(),
                    snapshotPath);
                Program.MergeAndWriteDockerContributors(firstRun, snapshotPath);

                _mockGitHubService.DockerCommitFixtureSet = "run-2";
                var secondRun = await Program.ProcessDotnetDockerContributionsWithResultAsync(
                    _mockGitHubService,
                    CreateDockerMajorReleases(),
                    snapshotPath);
                Program.MergeAndWriteDockerContributors(secondRun, snapshotPath);

                var persisted = JsonSerializer.Deserialize<Dictionary<string, DockerVersionSnapshot>>(
                    File.ReadAllText(snapshotPath));
                Assert.NotNull(persisted);

                var snapshot = persisted["10.0"];
                Assert.Equal("run2-alice", snapshot.LatestSha);
                Assert.Equal(5, snapshot.Contributors.Sum(contributor => contributor.Count));
                Assert.Equal(3, snapshot.Contributors.Count);
                Assert.DoesNotContain(snapshot.Contributors,
                    contributor => BotExclusionConstants.IsBot(contributor.Name));

                var alice = snapshot.Contributors.Single(
                    contributor => contributor.Link == "https://github.com/alice");
                Assert.Equal(3, alice.Count);
                Assert.Equal(3, alice.Repos.Single(repo => repo.Name == "dotnet-docker").Count);
                Assert.Contains(snapshot.Contributors,
                    contributor => contributor.Link == "https://github.com/bob" && contributor.Count == 1);
                Assert.Contains(snapshot.Contributors,
                    contributor => contributor.Link == "https://github.com/charlie" && contributor.Count == 1);
            }
            finally
            {
                File.Delete(snapshotPath);
            }
        }

        [Fact]
        public void MissingLatestSha_ProcessesAllFetchedCommits()
        {
            var commits = new List<GitHubCommit>
            {
                CreateDockerCommit("new-sha", DateTimeOffset.Parse("2026-01-02T00:00:00Z")),
                CreateDockerCommit("older-sha", DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
            };

            var selected = Program.SelectCommitsAfterCutoff(commits, "missing-sha", out var cutoffFound);

            Assert.False(cutoffFound);
            Assert.Equal(["new-sha", "older-sha"], selected.Select(commit => commit.Sha));
        }

        [Fact]
        public void EqualCutoffTimestamps_DoNotTruncateAmbiguousCommit()
        {
            var cutoffDate = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var commits = new List<GitHubCommit>
            {
                CreateDockerCommit("new-sha", DateTimeOffset.Parse("2026-01-02T00:00:00Z")),
                CreateDockerCommit("cutoff-sha", cutoffDate),
                CreateDockerCommit("same-time-sha", cutoffDate),
                CreateDockerCommit("old-sha", DateTimeOffset.Parse("2025-12-31T00:00:00Z"))
            };

            var selected = Program.SelectCommitsAfterCutoff(commits, "cutoff-sha", out var cutoffFound);

            Assert.True(cutoffFound);
            Assert.Equal(["new-sha", "same-time-sha"], selected.Select(commit => commit.Sha));
        }

        private static GitHubCommit CreateDockerCommit(string sha, DateTimeOffset authorDate)
        {
            var author = new Octokit.Committer("test", "test@example.com", authorDate);
            var commit = new Octokit.Commit(
                null, null, null, null, sha, null, null, "test", author, author,
                null, [], 0, null);
            return new GitHubCommit(
                null, null, null, null, sha, null, null, null, null, commit,
                null, null, null, [], []);
        }

        private static Dictionary<string, MajorRelease> CreateDockerMajorReleases()
        {
            return new Dictionary<string, MajorRelease>
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
        }
    }
}
