# thanks.dot.net - Data Loader

This is the loader application that creates the data file "core.json" for http://thanks.dot.net

## Process

Each release of .NET is a collection of repositories. The list for each release is managed through the [dotnet/core](https://github.com/core) repository and for each see the [/releases](https://github.com/core/releses) section.

1. The loader application first loads all of the releases for /dotnet/core ordered by newest -> oldest.
1. For each release the **tag** of the current release and the **tag** of the previous release are used to retrieve the commits using the GitHub API : `https://api.github.com/repos/{owner}/{repo}/compare/{fromRelease}...{toRelease}`
1. Each commit is inspected to create the data model using the `TallyCommits` method.
    1. A `Contributor` object is created if needed
    1. The commits for the given repo are added

Each child repo, other repositories in the release, are processed in the same manner.

## Local Development

1. Create a GitHub ClientID and Secret in your settings under the [OAuth Apps section](https://github.com/settings/developers).
1. Fork the dotnetthanks-loader repo and cd into it.
1. Create user-secrets using the following commands
    1. `dotnet user-secrets init`
    1. `dotnet user-secrets set GITHUB_CLIENTID <your-value>`
    1. `dotnet user-secrets set GITHUB_CLIENTSECRET <your-value>`
