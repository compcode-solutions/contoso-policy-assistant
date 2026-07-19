using System.Text.RegularExpressions;

namespace Contoso.PolicyAssistant.Api.Features.Policies;

/// <summary>
/// Loads markdown policies + YAML-ish frontmatter (title, allowedRoles).
/// ACL filter is applied before any future RAG retrieval (Day 3).
/// </summary>
public sealed class PolicyCatalog
{
    private static readonly Regex Frontmatter = new(
        @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n(.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleLine = new(
        @"^title:\s*(.+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex RolesLine = new(
        @"^allowedRoles:\s*\[([^\]]*)\]",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public IReadOnlyList<PolicyDocument> All { get; }

    public PolicyCatalog(IHostEnvironment env, IConfiguration config)
    {
        var relative = config["Policies:RootPath"] ?? "../../data/policies";
        var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, relative));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Policy folder not found: {root}. Set Policies:RootPath.");
        }

        All = Directory.GetFiles(root, "*.md")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(ParseFile)
            .ToList();
    }

    /// <summary>Test / tooling constructor.</summary>
    public PolicyCatalog(IEnumerable<PolicyDocument> documents)
    {
        All = documents.ToList();
    }

    public static PolicyCatalog LoadFromDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Policy folder not found: {root}");
        }

        var docs = Directory.GetFiles(root, "*.md")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(ParseFile)
            .ToList();
        return new PolicyCatalog(docs);
    }

    public IReadOnlyList<PolicyDocument> GetVisibleTo(IEnumerable<string> userRoles)
    {
        var roles = userRoles.ToArray();
        return All.Where(d => d.IsVisibleTo(roles)).ToList();
    }

    private static PolicyDocument ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        var fileName = Path.GetFileName(path);
        var id = Path.GetFileNameWithoutExtension(path);

        var match = Frontmatter.Match(text);
        if (!match.Success)
        {
            return new PolicyDocument
            {
                Id = id,
                Title = id,
                AllowedRoles = ["Employee", "Supervisor", "Admin"],
                FileName = fileName,
                BodyMarkdown = text
            };
        }

        var meta = match.Groups[1].Value;
        var body = match.Groups[2].Value.Trim();
        var title = TitleLine.Match(meta).Groups[1].Value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(title)) title = id;

        var rolesRaw = RolesLine.Match(meta).Groups[1].Value;
        var roles = rolesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Trim().Trim('"'))
            .Where(r => r.Length > 0)
            .ToArray();

        if (roles.Length == 0)
        {
            roles = ["Employee", "Supervisor", "Admin"];
        }

        return new PolicyDocument
        {
            Id = id,
            Title = title,
            AllowedRoles = roles,
            FileName = fileName,
            BodyMarkdown = body
        };
    }
}
