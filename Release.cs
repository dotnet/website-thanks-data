using System.Diagnostics;
using System.Text.RegularExpressions;

namespace dotnetthanks
{
    [DebuggerDisplay("Name = {Name}, GA = {IsGA}, Tag = {Tag}, Commit = {TargetCommit}")]
    public class Release: IEquatable<Release>
    {
        // The list of GA releases
        private static readonly HashSet<string> GaReleases = new()
        { 
            "1.0.0",
            "v1.1",
            "v2.0.0",
            "v2.1.0",
            "v2.2.0",
            "v3.0.0",
            "v3.1.0",
            "v5.0.0",
            "v6.0.0"
        };
        private string _tag;

        public List<ChildRepo> ChildRepos { get; set; } //= new List<ChildRepo>();
        public List<Contributor> Contributors { get; set; }
        public int Contributions { get; set; }
        public int Id { get; set; }
        public bool IsGA
        {
            get => GaReleases.Contains(Tag);
        }
        public string Name { get; set; }
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
            const string pattern = "(v)?(?<version>\\d+.\\d+(.\\d+)?)(-(?<label>.*))?";
            Match m = Regex.Match(_tag, pattern, RegexOptions.Compiled | RegexOptions.Singleline);
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
        public int Count { get; set; }

        public List<RepoItem> Repos { get; set; } = new List<RepoItem>();
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

        public string Tag { get => Url?.Substring(Url.LastIndexOf($"/") + 1).Trim(); }
    }
}