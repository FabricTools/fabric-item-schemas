var rootFolder = Environment.CurrentDirectory;
var subModuleFolder = Path.Combine(rootFolder, "ext");
var itemsSourceFolder = Path.Combine(rootFolder, "ext", "fabric", "item");

using var git = new Repository(rootFolder, new RepositoryOptions { });
var tags = git.Tags.Select(t => t.FriendlyName).ToArray();

Console.WriteLine("::group::Existing Tags");
Array.ForEach(tags, Console.WriteLine);
Console.WriteLine("::endgroup::");

// Create new local branch
var uniqueId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
var workingBranch = git.Branches.Add($"auto-updates/{uniqueId}", git.Head.Tip);
Commands.Checkout(git, workingBranch);

Signature GetSignature() => new Signature("mthierba", "3763662+mthierba@users.noreply.github.com", DateTimeOffset.UtcNow);

// Commit submodule changes
if (git.RetrieveStatus("ext") == FileStatus.ModifiedInWorkdir)
{
    Console.WriteLine("Submodule is not up to date, updating ...");
    Commands.Stage(git, "ext");
    var author = GetSignature();
    git.Commit("updated submodule", author, author);
}

var matcher = new Matcher(StringComparison.OrdinalIgnoreCase)
    .AddInclude("*/definition/**/schema.json");
var matchesOrdered = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(itemsSourceFolder)))
    .Files.Select(f =>
    {
        var segments = f.Path.Split(['/', '\\'])
            .Where(s => !s.Equals("definition", StringComparison.InvariantCultureIgnoreCase))
            .ToArray();
        var version = NuGet.Versioning.SemanticVersion.Parse(segments[segments.Length - 2]);
        return new
        {
            f.Path,
            /* graphInstance/definition/graphDefinition/1.0.0/schema.json */
            Version = version,
            /* 1.0.0 */
            Target = string.Join("/", segments.Take(segments.Length - 2)),
            /* graphInstance/dataSources */
            Tag = $"{string.Join("-", segments.Take(segments.Length - 2))}-{version}"
            /* graphInstance-dataSources-1.0.0 */
        };
    })
    .OrderBy(x => x.Target)
    .ThenBy(x => x.Version);

foreach (var match in matchesOrdered)
{
    var tag = match.Tag;
    if (tags.Contains(tag))
    {
        Console.WriteLine($"Tag {tag} already exists, skipping.");
        continue;
    }

    Console.WriteLine($"Adding new version for {match.Path} ...");

    var source = new FileInfo(Path.Combine(itemsSourceFolder, match.Path));
    var targetPath = $"item/{match.Target}/{Path.GetFileName(match.Path)}";
    var target = new FileInfo(Path.Combine(rootFolder, targetPath));
    target.Directory?.Create();

    source.CopyTo(target.FullName, true);

    // Commit the change
    // Create a tag
    if (git.RetrieveStatus(targetPath) is FileStatus.ModifiedInWorkdir or FileStatus.NewInWorkdir)
    {
        Commands.Stage(git, targetPath);
        var author = GetSignature();
        git.Commit($"Added: {match.Path}", author, author);
        git.ApplyTag(tag);
    }
}
