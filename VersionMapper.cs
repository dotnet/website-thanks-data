using System.Text.RegularExpressions;

namespace dotnetthanks_loader
{
    public static class VersionMapper
    {
        public static int MapAspireVersionToDotNet(string aspireTag)
        {
            // Extract version from tag (v13.0.0 or just 13.0.0)
            var versionStr = aspireTag.TrimStart('v');
            var match = Regex.Match(versionStr, @"^(\d+)\.(\d+)");
            if (!match.Success) return -1;

            var major = int.Parse(match.Groups[1].Value);

            // Aspire 10-13 -> .NET 10
            if (major >= 10 && major <= 13) return 10;
            // Aspire 9.x -> .NET 9
            if (major == 9) return 9;
            // Aspire 8.x -> .NET 8
            if (major == 8) return 8;

            return -1;
        }

        public static int MapMauiVersionToDotNet(string mauiTag)
        {
            // Extract version from tag (v10.0.0 or just 10.0.0)
            var versionStr = mauiTag.TrimStart('v');
            var match = Regex.Match(versionStr, @"^(\d+)\.(\d+)");
            if (!match.Success) return -1;

            var major = int.Parse(match.Groups[1].Value);

            // MAUI follows .NET versioning: MAUI 10.x -> .NET 10, etc.
            if (major >= 6 && major <= 10) return major;

            return -1;
        }
    }
}
