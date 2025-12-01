using System.Diagnostics;
using System.Text.RegularExpressions;

namespace dotnetthanks_loader
{
    [DebuggerDisplay("Name = {Name}, GA = {IsGA}, Tag = {Tag}, Commit = {TargetCommit}")]
    public class Release : IEquatable<Release>
    {
        // The list of GA releases
        private static readonly HashSet<string> GaReleases =
        [
            "v1.0.0",
            "v1.1",
            "v2.0.0",
            "v2.1.0",
            "v2.2.0",
            "v3.0.0",
            "v3.1.0",
            "v5.0.0",
            "v6.0.0",
            "v7.0.0",
            "v8.0.0",
            "v9.0.0"
        ];
        private string _tag;
        
        // Special Aspire version mappings (only when version doesn't match .NET version)
        private static readonly Dictionary<int, int> AspireToNetVersionMap = new()
        {
            { 13, 10 }  // Aspire 13.x -> .NET 10
        };

        public string SourceRepository { get; set; }
        public List<ChildRepo> ChildRepos { get; set; }
        public List<Contributor> Contributors { get; set; }
        public int Contributions { get; set; }
        public long Id { get; set; }
        public bool IsGA
        {
            get
            {
                // For aspire and maui, GA releases don't have preview/rc/beta/alpha labels
                if (SourceRepository == "aspire" || SourceRepository == "maui")
                {
                    return string.IsNullOrEmpty(VersionLabel) || 
                           (!VersionLabel.Contains("preview", StringComparison.OrdinalIgnoreCase) &&
                            !VersionLabel.Contains("rc", StringComparison.OrdinalIgnoreCase) &&
                            !VersionLabel.Contains("beta", StringComparison.OrdinalIgnoreCase) &&
                            !VersionLabel.Contains("alpha", StringComparison.OrdinalIgnoreCase));
                }
                // For core, use the predefined list
                return GaReleases.Contains(Tag);
            }
        }
        public string Name { get; set; }
        public string Product
        {
            get
            {
                // Return product name based on source repository
                if (SourceRepository == "aspire")
                {
                    return ".NET Aspire";
                }
                else if (SourceRepository == "maui")
                {
                    return ".NET MAUI";
                }
                else if (Name != null && Name.IndexOf("Core") > 0)
                {
                    return ".NET Core";
                }
                else
                {
                    return ".NET";
                }
            }
        }
        
        // Get the mapped .NET version for this release (used for grouping aspire versions)
        public Version MappedVersion
        {
            get
            {
                if (SourceRepository == "aspire")
                {
                    // Check if there's a special mapping for this Aspire version
                    if (AspireToNetVersionMap.TryGetValue(Version.Major, out int mappedMajor))
                    {
                        return new Version(mappedMajor, 0);
                    }
                    // Otherwise, Aspire version maps directly to .NET version (Aspire 9 -> .NET 9, etc.)
                    return Version;
                }
                // For all other repos, use the actual version
                return Version;
            }
        }
        
        // Get the prefixed tag for ProcessedReleases tracking (e.g., "maui-8.0.100")
        public string PrefixedTag
        {
            get
            {
                // For core repository, use tag as-is for backward compatibility
                if (SourceRepository == "core")
                {
                    return Tag;
                }
                // For aspire and maui, prefix with repository name
                return $"{SourceRepository}-{Tag}";
            }
        }
        
        public string Tag
        {
            get => _tag;
            set
            {
                if (_tag == value)
                    return;

                _tag = value;
                ParseVersion();
            }
        }
        public string TargetCommit { get; set; }

        public Version Version { get; private set; }
        public string VersionLabel { get; private set; }
        public List<string> ProcessedReleases { get; set; } = [];
        public bool Equals(Release other)
        {
            if (other is null)
                return false;

            return this.Id == other.Id;
        }

        public override bool Equals(object obj) => Equals(obj as Release);
        public override int GetHashCode() => (Id).GetHashCode();

        private void ParseVersion()
        {
            Match m = RegexHelper.VersionRegex().Match(_tag);
            if (!m.Success)
                throw new ArgumentException($"Tag '{_tag}' has unexpected format");

            Version = Version.Parse(m.Groups["version"].Value);
            VersionLabel = m.Groups["label"].Value;
        }
    }

    [DebuggerDisplay("Name = {Name}, Count = {Count}, Link = {Link}")]
    public class Contributor
    {
        public string Name { get; set; }
        public string Link { get; set; }
        public string Avatar { get; set; }
        public int Count { get; set; }

        public List<RepoItem> Repos { get; set; } = [];
    }

    [DebuggerDisplay("Name = {Name}, Count = {Count}")]
    public class RepoItem
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    [DebuggerDisplay("Name = {Name}, Tag = {Tag}, Url = {Url}")]
    public class ChildRepo
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public string Repository { get => this.Url?.Split("/")[4]; }

        public string Owner
        {
            get => "dotnet"; // this.Url?.Split("/")[3];

        }
        public string Tag { get => Url?[(Url.LastIndexOf('/') + 1)..].Trim(); }
    }

    public partial class RegexHelper
    {
        [GeneratedRegex("(v)?(?<version>\\d+.\\d+(.\\d+)?)(-(?<label>.*))?", RegexOptions.Compiled | RegexOptions.Singleline)]
        public static partial Regex VersionRegex();
    }
}