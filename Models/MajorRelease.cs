namespace dotnetthanks_loader
{
    public class MajorRelease
    {
        public List<Contributor> Contributors { get; set; }
        public int Contributions { get; set; }
        public string Name { get; set; }
        public string Product { get; set; }
        public Version Version { get; set; }
        public string Tag { get; set; }
        public List<string> ProcessedReleases { get; set; } = [];
    }
}