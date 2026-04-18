using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

return await new AllContributorsApp().RunAsync(args);

sealed class AllContributorsApp
{
    private static readonly Regex ContributorCommandRegex = new(
        @"^\s*/contributor\s+add\s+@?(?<login>[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)\s+(?<type>[A-Za-z][A-Za-z0-9]*)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var options = CliOptions.Parse(args.Skip(1).ToArray());

            return command switch
            {
                "sync" => await SyncAsync(options),
                "add" => await AddAsync(options),
                "apply-commands" => await ApplyCommandsAsync(options),
                _ => throw new InvalidOperationException($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "help";

    private static async Task<int> SyncAsync(CliOptions options)
    {
        var repository = AllContributorsRepository.Load(options.GetRequiredPath("repo-root"));
        repository.Save();
        Console.WriteLine($"Synchronized {repository.ReadmeFiles.Count} contributor file(s).");
        await Task.CompletedTask;
        return 0;
    }

    private static async Task<int> AddAsync(CliOptions options)
    {
        var repository = AllContributorsRepository.Load(options.GetRequiredPath("repo-root"));
        var login = options.GetRequiredValue("login");
        var types = options.GetValues("type");

        if (types.Count == 0)
        {
            throw new InvalidOperationException("At least one --type value is required.");
        }

        await ApplyUpdatesAsync(repository, [new ContributorUpdate(login, CanonicalizeTypes(repository.Config, types))], options.GetGitHubToken());
        repository.Save();

        Console.WriteLine($"Updated contributor @{login}.");
        return 0;
    }

    private static async Task<int> ApplyCommandsAsync(CliOptions options)
    {
        var repository = AllContributorsRepository.Load(options.GetRequiredPath("repo-root"));
        var commandsFile = options.GetRequiredPath("commands-file");
        var updates = ParseContributorUpdates(File.ReadAllText(commandsFile), repository.Config);

        if (updates.Count == 0)
        {
            throw new InvalidOperationException("No valid /contributor add commands were found.");
        }

        await ApplyUpdatesAsync(repository, updates, options.GetGitHubToken());
        repository.Save();

        Console.WriteLine($"Applied {updates.Count} contributor update(s).");
        return 0;
    }

    private static IReadOnlyList<ContributorUpdate> ParseContributorUpdates(string content, ContributorConfig config)
    {
        var byLogin = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = ContributorCommandRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var login = match.Groups["login"].Value;
            var contributionType = config.CanonicalizeContributionType(match.Groups["type"].Value);

            if (!byLogin.TryGetValue(login, out var types))
            {
                types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byLogin.Add(login, types);
            }

            types.Add(contributionType);
        }

        return byLogin
            .Select(pair => new ContributorUpdate(pair.Key, CanonicalizeTypes(config, pair.Value)))
            .ToArray();
    }

    private static IReadOnlyList<string> CanonicalizeTypes(ContributorConfig config, IEnumerable<string> types)
        => types
            .Select(config.CanonicalizeContributionType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(config.GetContributionTypeOrder)
            .ThenBy(static type => type, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static async Task ApplyUpdatesAsync(AllContributorsRepository repository, IReadOnlyList<ContributorUpdate> updates, string? gitHubToken)
    {
        foreach (var update in updates)
        {
            await repository.AddOrUpdateContributorAsync(update.Login, update.ContributionTypes, gitHubToken);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run build/AllContributors.cs -- sync --repo-root <path>
              dotnet run build/AllContributors.cs -- add --repo-root <path> --login <user> --type <type> [--type <type> ...]
              dotnet run build/AllContributors.cs -- apply-commands --repo-root <path> --commands-file <path>

            Supported command syntax:
              /contributor add @user code
              /contributor add @user doc
            """);
    }
}

readonly record struct ContributorUpdate(string Login, IReadOnlyList<string> ContributionTypes);

sealed class CliOptions
{
    private readonly Dictionary<string, List<string>> values;

    private CliOptions(Dictionary<string, List<string>> values)
    {
        this.values = values;
    }

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument '{argument}'.");
            }

            var name = argument[2..];
            string value;

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }
            else
            {
                value = "true";
            }

            if (!values.TryGetValue(name, out var existing))
            {
                existing = [];
                values.Add(name, existing);
            }

            existing.Add(value);
        }

        return new CliOptions(values);
    }

    public string GetRequiredValue(string name)
    {
        if (!values.TryGetValue(name, out var optionValues) || optionValues.Count == 0 || string.IsNullOrWhiteSpace(optionValues[0]))
        {
            throw new InvalidOperationException($"Missing required option --{name}.");
        }

        return optionValues[0];
    }

    public string GetRequiredPath(string name)
        => Path.GetFullPath(GetRequiredValue(name));

    public IReadOnlyList<string> GetValues(string name)
        => values.TryGetValue(name, out var optionValues) ? optionValues : [];

    public string? GetGitHubToken()
    {
        if (values.TryGetValue("github-token", out var optionValues) && optionValues.Count > 0 && !string.IsNullOrWhiteSpace(optionValues[0]))
        {
            return optionValues[0];
        }

        return Environment.GetEnvironmentVariable("ALL_CONTRIBUTORS_GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("PRIVATE_TOKEN")
            ?? Environment.GetEnvironmentVariable("ALL_CONTRIBUTORS_PRIVATE_TOKEN");
    }
}

sealed class AllContributorsRepository
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonObject rootNode;
    private readonly JsonArray contributorsNode;
    private readonly Dictionary<string, ContributorEntry> contributorsByLogin;

    private AllContributorsRepository(string repositoryRoot, string configPath, JsonObject rootNode, JsonArray contributorsNode, ContributorConfig config, List<string> readmeFiles)
    {
        RepositoryRoot = repositoryRoot;
        ConfigPath = configPath;
        this.rootNode = rootNode;
        this.contributorsNode = contributorsNode;
        Config = config;
        ReadmeFiles = readmeFiles;
        contributorsByLogin = LoadContributors(contributorsNode);
    }

    public string RepositoryRoot { get; }

    public string ConfigPath { get; }

    public ContributorConfig Config { get; }

    public IReadOnlyList<string> ReadmeFiles { get; }

    public static AllContributorsRepository Load(string repositoryRoot)
    {
        var configPath = Path.Combine(repositoryRoot, ".all-contributorsrc");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("Unable to find .all-contributorsrc.", configPath);
        }

        var rootNode = JsonNode.Parse(File.ReadAllText(configPath))?.AsObject()
            ?? throw new InvalidOperationException("The .all-contributorsrc file is empty.");
        var contributorsNode = rootNode["contributors"]?.AsArray()
            ?? throw new InvalidOperationException("The .all-contributorsrc file does not contain a contributors array.");
        var readmeFiles = rootNode["files"] is JsonArray filesNode
            ? filesNode.Select(node => node?.GetValue<string>() ?? string.Empty).Where(static path => !string.IsNullOrWhiteSpace(path)).ToList()
            : ["README.md"];

        return new AllContributorsRepository(repositoryRoot, configPath, rootNode, contributorsNode, ContributorConfig.Load(rootNode), readmeFiles);
    }

    public async Task AddOrUpdateContributorAsync(string login, IReadOnlyList<string> contributionTypes, string? gitHubToken)
    {
        if (!contributorsByLogin.TryGetValue(login, out var contributor))
        {
            var user = await GitHubUserClient.GetUserAsync(login, Config.RepoHost, gitHubToken);
            var newContributorNode = new JsonObject
            {
                ["login"] = user.Login,
                ["name"] = user.Name,
                ["avatar_url"] = user.AvatarUrl,
                ["profile"] = user.Profile,
            };

#pragma warning disable IL2026, IL3050
            contributorsNode.Add(newContributorNode);
#pragma warning restore IL2026, IL3050
            contributor = ContributorEntry.FromJson(newContributorNode);
            contributorsByLogin.Add(contributor.Login, contributor);
        }

        contributor.MergeContributionTypes(contributionTypes, Config);
    }

    public void Save()
    {
        rootNode["contributors"] = RebuildContributorsNode();
        File.WriteAllText(ConfigPath, rootNode.ToJsonString(JsonWriteOptions) + Environment.NewLine, Utf8NoBom);

        foreach (var relativeReadmePath in ReadmeFiles)
        {
            var readmePath = Path.Combine(RepositoryRoot, relativeReadmePath);
            var readmeContent = File.ReadAllText(readmePath);
            var updatedContent = ReadmeFormatter.Update(readmeContent, Config, GetOrderedContributors());
            File.WriteAllText(readmePath, updatedContent, Utf8NoBom);
        }
    }

    private JsonArray RebuildContributorsNode()
    {
        var array = new JsonArray();
        foreach (var contributor in GetOrderedContributors())
        {
#pragma warning disable IL2026, IL3050
            array.Add(contributor.ToJson(Config));
#pragma warning restore IL2026, IL3050
        }

        return array;
    }

    private IReadOnlyList<ContributorEntry> GetOrderedContributors()
    {
        IEnumerable<ContributorEntry> contributors = contributorsByLogin.Values;
        if (Config.ContributorsSortAlphabetically)
        {
            contributors = contributors.OrderBy(static contributor => contributor.SortName, StringComparer.OrdinalIgnoreCase);
        }

        return contributors.ToArray();
    }

    private static Dictionary<string, ContributorEntry> LoadContributors(JsonArray contributorsNode)
    {
        var contributors = new Dictionary<string, ContributorEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in contributorsNode)
        {
            if (node is not JsonObject contributorObject)
            {
                continue;
            }

            var contributor = ContributorEntry.FromJson(contributorObject);
            contributors[contributor.Login] = contributor;
        }

        return contributors;
    }
}

sealed class ContributorEntry
{
    private readonly List<ContributionDescriptor> contributions;

    private ContributorEntry(string login, string name, string avatarUrl, string? profile, IEnumerable<ContributionDescriptor> contributions)
    {
        Login = login;
        Name = name;
        AvatarUrl = avatarUrl;
        Profile = profile;
        this.contributions = contributions.ToList();
    }

    public string Login { get; }

    public string Name { get; private set; }

    public string AvatarUrl { get; private set; }

    public string? Profile { get; private set; }

    public string SortName => string.IsNullOrWhiteSpace(Name) ? Login : Name;

    public IReadOnlyList<ContributionDescriptor> Contributions => contributions;

    public static ContributorEntry FromJson(JsonObject node)
    {
        var login = node["login"]?.GetValue<string>() ?? throw new InvalidOperationException("Contributor entry is missing a login.");
        var name = node["name"]?.GetValue<string>() ?? login;
        var avatarUrl = node["avatar_url"]?.GetValue<string>() ?? throw new InvalidOperationException($"Contributor '{login}' is missing avatar_url.");
        var profile = node["profile"]?.GetValue<string>();
        var contributionsNode = node["contributions"]?.AsArray() ?? [];
        var contributions = contributionsNode
            .Select(static node => ContributionDescriptor.FromJson(node))
            .Where(static contribution => contribution is not null)
            .Cast<ContributionDescriptor>()
            .ToArray();

        return new ContributorEntry(login, name, avatarUrl, profile, contributions);
    }

    public void MergeContributionTypes(IEnumerable<string> newContributionTypes, ContributorConfig config)
    {
        foreach (var contributionType in newContributionTypes)
        {
            if (contributions.Any(existing => string.Equals(existing.Type, contributionType, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            contributions.Add(ContributionDescriptor.FromType(contributionType));
        }

        var ordered = contributions
            .Select((contribution, index) => (contribution, index))
            .OrderBy(item => config.GetContributionTypeOrder(item.contribution.Type))
            .ThenBy(item => item.index)
            .Select(item => item.contribution)
            .DistinctBy(static contribution => contribution.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        contributions.Clear();
        contributions.AddRange(ordered);
    }

    public JsonObject ToJson(ContributorConfig config)
    {
        var node = new JsonObject
        {
            ["login"] = Login,
            ["name"] = Name,
            ["avatar_url"] = AvatarUrl,
            ["profile"] = Profile,
        };

        var contributionsNode = new JsonArray();
        foreach (var contribution in contributions)
        {
            contributionsNode.Add(contribution.ToJson());
        }

        node["contributions"] = contributionsNode;
        return node;
    }
}

sealed class ContributionDescriptor
{
    private ContributionDescriptor(string type, string? url)
    {
        Type = type;
        Url = url;
    }

    public string Type { get; }

    public string? Url { get; }

    public static ContributionDescriptor? FromJson(JsonNode? node)
    {
        return node switch
        {
            JsonValue value => new ContributionDescriptor(value.GetValue<string>(), null),
            JsonObject obj when obj["type"]?.GetValue<string>() is { Length: > 0 } type => new ContributionDescriptor(type, obj["url"]?.GetValue<string>()),
            _ => null,
        };
    }

    public static ContributionDescriptor FromType(string type)
        => new(type, null);

    public JsonNode ToJson()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return JsonValue.Create(Type)!;
        }

        return new JsonObject
        {
            ["type"] = Type,
            ["url"] = Url,
        };
    }
}

sealed class ContributorConfig
{
    private static readonly (string Key, string Symbol, string Description, string? LinkTemplate)[] DefaultContributionTypes =
    [
        ("a11y", "️️️️♿️", "Accessibility", null),
        ("audio", "🔊", "Audio", null),
        ("blog", "📝", "Blogposts", null),
        ("bug", "🐛", "Bug reports", "<%= options.repoHost || \"https://github.com\" %>/<%= options.projectOwner %>/<%= options.projectName %>/issues?q=author%3A<%= contributor.login %>"),
        ("business", "💼", "Business development", null),
        ("code", "💻", "Code", "<%= options.repoHost || \"https://github.com\" %>/<%= options.projectOwner %>/<%= options.projectName %>/commits?author=<%= contributor.login %>"),
        ("content", "🖋", "Content", null),
        ("data", "🔣", "Data", null),
        ("design", "🎨", "Design", null),
        ("doc", "📖", "Documentation", "<%= options.repoHost || \"https://github.com\" %>/<%= options.projectOwner %>/<%= options.projectName %>/commits?author=<%= contributor.login %>"),
        ("eventOrganizing", "📋", "Event Organizing", null),
        ("example", "💡", "Examples", null),
        ("financial", "💵", "Financial", null),
        ("fundingFinding", "🔍", "Funding Finding", null),
        ("ideas", "🤔", "Ideas, Planning, & Feedback", null),
        ("infra", "🚇", "Infrastructure (Hosting, Build-Tools, etc)", null),
        ("maintenance", "🚧", "Maintenance", null),
        ("mentoring", "🧑‍🏫", "Mentoring", null),
        ("platform", "📦", "Packaging/porting to new platform", null),
        ("plugin", "🔌", "Plugin/utility libraries", null),
        ("projectManagement", "📆", "Project Management", null),
        ("promotion", "📣", "Promotion", null),
        ("question", "💬", "Answering Questions", null),
        ("research", "🔬", "Research", null),
        ("review", "👀", "Reviewed Pull Requests", "<%= options.repoHost || \"https://github.com\" %>/<%= options.projectOwner %>/<%= options.projectName %>/pulls?q=is%3Apr+reviewed-by%3A<%= contributor.login %>"),
        ("security", "🛡️", "Security", null),
        ("talk", "📢", "Talks", null),
        ("test", "⚠️", "Tests", "<%= options.repoHost || \"https://github.com\" %>/<%= options.projectOwner %>/<%= options.projectName %>/commits?author=<%= contributor.login %>"),
        ("tool", "🔧", "Tools", null),
        ("translation", "🌍", "Translation", null),
        ("tutorial", "✅", "Tutorials", null),
        ("userTesting", "📓", "User Testing", null),
        ("video", "📹", "Videos", null),
    ];

    private readonly Dictionary<string, ContributionTypeDefinition> contributionTypes;
    private readonly Dictionary<string, int> contributionTypeOrder;

    private ContributorConfig(
        string projectOwner,
        string projectName,
        string repoType,
        string? repoHost,
        int contributorsPerLine,
        int imageSize,
        bool contributorsSortAlphabetically,
        Dictionary<string, ContributionTypeDefinition> contributionTypes,
        Dictionary<string, int> contributionTypeOrder)
    {
        ProjectOwner = projectOwner;
        ProjectName = projectName;
        RepoType = repoType;
        RepoHost = repoHost;
        ContributorsPerLine = contributorsPerLine;
        ImageSize = imageSize;
        ContributorsSortAlphabetically = contributorsSortAlphabetically;
        this.contributionTypes = contributionTypes;
        this.contributionTypeOrder = contributionTypeOrder;
    }

    public string ProjectOwner { get; }

    public string ProjectName { get; }

    public string RepoType { get; }

    public string? RepoHost { get; }

    public int ContributorsPerLine { get; }

    public int ImageSize { get; }

    public bool ContributorsSortAlphabetically { get; }

    public static ContributorConfig Load(JsonObject rootNode)
    {
        var contributionTypes = new Dictionary<string, ContributionTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        var contributionTypeOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var (key, symbol, description, linkTemplate) in DefaultContributionTypes)
        {
            contributionTypes[key] = new ContributionTypeDefinition(key, symbol, description, linkTemplate);
            contributionTypeOrder[key] = order++;
        }

        if (rootNode["types"] is JsonObject customTypes)
        {
            foreach (var (key, value) in customTypes)
            {
                if (value is not JsonObject customTypeNode)
                {
                    continue;
                }

                var symbol = customTypeNode["symbol"]?.GetValue<string>() ?? throw new InvalidOperationException($"Custom contribution type '{key}' is missing a symbol.");
                var description = customTypeNode["description"]?.GetValue<string>() ?? throw new InvalidOperationException($"Custom contribution type '{key}' is missing a description.");
                var link = customTypeNode["link"]?.GetValue<string>();

                contributionTypes[key] = new ContributionTypeDefinition(key, symbol, description, link);
                contributionTypeOrder[key] = order++;
            }
        }

        return new ContributorConfig(
            projectOwner: rootNode["projectOwner"]?.GetValue<string>() ?? throw new InvalidOperationException("The .all-contributorsrc file is missing projectOwner."),
            projectName: rootNode["projectName"]?.GetValue<string>() ?? throw new InvalidOperationException("The .all-contributorsrc file is missing projectName."),
            repoType: rootNode["repoType"]?.GetValue<string>() ?? "github",
            repoHost: rootNode["repoHost"]?.GetValue<string>(),
            contributorsPerLine: rootNode["contributorsPerLine"]?.GetValue<int>() ?? 7,
            imageSize: rootNode["imageSize"]?.GetValue<int>() ?? 100,
            contributorsSortAlphabetically: rootNode["contributorsSortAlphabetically"]?.GetValue<bool>() ?? false,
            contributionTypes: contributionTypes,
            contributionTypeOrder: contributionTypeOrder);
    }

    public string CanonicalizeContributionType(string requestedType)
    {
        if (!contributionTypes.TryGetValue(requestedType, out var type))
        {
            throw new InvalidOperationException(
                $"Unsupported contribution type '{requestedType}'. Valid types: {string.Join(", ", contributionTypes.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}");
        }

        return type.Key;
    }

    public int GetContributionTypeOrder(string contributionType)
        => contributionTypeOrder.TryGetValue(contributionType, out var order) ? order : int.MaxValue;

    public ContributionTypeDefinition GetContributionType(string contributionType)
        => contributionTypes[CanonicalizeContributionType(contributionType)];
}

sealed record ContributionTypeDefinition(string Key, string Symbol, string Description, string? LinkTemplate);

static class ReadmeFormatter
{
    private const string ListTagPrefix = "<!-- ALL-CONTRIBUTORS-LIST:";

    public static string Update(string content, ContributorConfig config, IReadOnlyList<ContributorEntry> contributors)
    {
        var openingTagIndex = content.IndexOf($"{ListTagPrefix}START", StringComparison.Ordinal);
        var openingTagEndIndex = content.IndexOf("-->", openingTagIndex, StringComparison.Ordinal);
        var closingTagIndex = content.IndexOf($"{ListTagPrefix}END", StringComparison.Ordinal);

        if (openingTagIndex < 0 || openingTagEndIndex < 0 || closingTagIndex < 0)
        {
            throw new InvalidOperationException("README.md does not contain the all-contributors markers.");
        }

        var generatedTable = GenerateTable(config, contributors);
        var replacement = new StringBuilder()
            .AppendLine("<!-- prettier-ignore-start -->")
            .AppendLine("<!-- markdownlint-disable -->")
            .Append(generatedTable)
            .AppendLine("<!-- markdownlint-restore -->")
            .AppendLine("<!-- prettier-ignore-end -->")
            .AppendLine()
            .ToString();

        return string.Concat(
            content.AsSpan(0, openingTagEndIndex + 3),
            Environment.NewLine,
            replacement,
            content.AsSpan(closingTagIndex));
    }

    private static string GenerateTable(ContributorConfig config, IReadOnlyList<ContributorEntry> contributors)
    {
        if (contributors.Count == 0)
        {
            return Environment.NewLine;
        }

        var lineWidth = Math.Floor(10000d / config.ContributorsPerLine) / 100d;
        var width = lineWidth.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var attributes = $"align=\"center\" valign=\"top\" width=\"{width}%\"";
        var rows = contributors
            .Chunk(config.ContributorsPerLine)
            .Select(chunk =>
                $"    <tr>{Environment.NewLine}      <td {attributes}>{string.Join($"</td>{Environment.NewLine}      <td {attributes}>", chunk.Select(contributor => FormatContributor(config, contributor)))}</td>{Environment.NewLine}    </tr>")
            .ToArray();

        return $"<table>{Environment.NewLine}  <tbody>{Environment.NewLine}{string.Join(Environment.NewLine, rows)}{Environment.NewLine}  </tbody>{Environment.NewLine}</table>{Environment.NewLine}{Environment.NewLine}";
    }

    private static string FormatContributor(ContributorConfig config, ContributorEntry contributor)
    {
        var name = EscapeName(string.IsNullOrWhiteSpace(contributor.Name) ? contributor.Login : contributor.Name);
        var avatar = $"<img src=\"{contributor.AvatarUrl}?s={config.ImageSize}\" width=\"{config.ImageSize}px;\" alt=\"{name}\"/>";
        var profileBlock = string.IsNullOrWhiteSpace(contributor.Profile)
            ? $"{avatar}<br /><sub><b>{name}</b></sub>"
            : $"<a href=\"{contributor.Profile}\">{avatar}<br /><sub><b>{name}</b></sub></a>";
        var contributions = string.Join(" ", contributor.Contributions.Select(contribution => FormatContribution(config, contributor, contribution)));

        return $"{profileBlock}<br />{contributions}";
    }

    private static string FormatContribution(ContributorConfig config, ContributorEntry contributor, ContributionDescriptor contribution)
    {
        var type = config.GetContributionType(contribution.Type);
        var url = !string.IsNullOrWhiteSpace(contribution.Url)
            ? contribution.Url!
            : !string.IsNullOrWhiteSpace(type.LinkTemplate)
                ? ExpandTemplate(type.LinkTemplate!, config, contributor)
                : $"#{contribution.Type}-{contributor.Login}";

        return $"<a href=\"{url}\" title=\"{type.Description}\">{type.Symbol}</a>";
    }

    private static string ExpandTemplate(string template, ContributorConfig config, ContributorEntry contributor)
        => Regex.Replace(
            template,
            @"<%=\s*(.+?)\s*%>",
            match => ResolveExpression(match.Groups[1].Value, config, contributor),
            RegexOptions.CultureInvariant);

    private static string ResolveExpression(string expression, ContributorConfig config, ContributorEntry contributor)
        => expression switch
        {
            "contributor.login" => contributor.Login,
            "options.projectOwner" => config.ProjectOwner,
            "options.projectName" => config.ProjectName,
            "options.repoHost" => config.RepoHost ?? GetDefaultRepoHost(config.RepoType),
            "options.repoHost || \"https://github.com\"" => config.RepoHost ?? GetDefaultRepoHost(config.RepoType),
            _ => throw new InvalidOperationException($"Unsupported contribution link template expression '{expression}'."),
        };

    private static string GetDefaultRepoHost(string repoType)
        => string.Equals(repoType, "gitlab", StringComparison.OrdinalIgnoreCase) ? "https://gitlab.com" : "https://github.com";

    private static string EscapeName(string name)
        => name.Replace("|", "&#124;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}

sealed class GitHubUserClient
{
    private static readonly HttpClient HttpClient = CreateClient();

    private GitHubUserClient()
    {
    }

    public static async Task<GitHubUser> GetUserAsync(string login, string? repoHost, string? gitHubToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GetApiBaseAddress(repoHost)}/users/{login}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(gitHubToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", gitHubToken);
        }

        using var response = await HttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var body = TryParseJsonObject(responseBody);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"The username {login} does not exist on GitHub.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = body?["message"]?.GetValue<string>()
                ?? responseBody.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = $"GitHub API request failed with status {(int)response.StatusCode}.";
            }

            throw new InvalidOperationException(message);
        }

        if (body is null)
        {
            throw new InvalidOperationException("GitHub API returned an unexpected non-JSON response.");
        }

        var resolvedLogin = body["login"]?.GetValue<string>() ?? login;
        var name = body["name"]?.GetValue<string>() ?? resolvedLogin;
        var avatarUrl = body["avatar_url"]?.GetValue<string>() ?? throw new InvalidOperationException($"GitHub user '{login}' is missing avatar_url.");
        var blog = body["blog"]?.GetValue<string>();
        var profile = NormalizeProfile(blog) ?? body["html_url"]?.GetValue<string>() ?? $"https://github.com/{resolvedLogin}";

        return new GitHubUser(resolvedLogin, name, avatarUrl, profile);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("stride-all-contributors/1.0");
        return client;
    }

    private static string GetApiBaseAddress(string? repoHost)
    {
        if (string.IsNullOrWhiteSpace(repoHost) || string.Equals(repoHost, "https://github.com", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.github.com";
        }

        return $"{repoHost.TrimEnd('/')}/api/v3";
    }

    private static string? NormalizeProfile(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return null;
        }

        return Uri.TryCreate(profile, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : null;
    }

    private static JsonObject? TryParseJsonObject(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(responseBody) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

sealed record GitHubUser(string Login, string Name, string AvatarUrl, string Profile);
