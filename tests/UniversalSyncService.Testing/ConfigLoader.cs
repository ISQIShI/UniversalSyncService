using System.Text;
using System.Text.RegularExpressions;

namespace UniversalSyncService.Testing;

/// <summary>
/// 负责测试配置加载：模板 -> 本地覆盖 -> 占位符覆盖注入。
/// </summary>
public static class ConfigLoader
{
    private static readonly Regex PlaceholderRegex = new("\\{\\{([A-Za-z0-9_]+)\\}\\}", RegexOptions.Compiled);

    public static async Task<string> BuildYamlAsync(
        string templatePath,
        string? localOverridePath = null,
        IReadOnlyDictionary<string, string>? placeholders = null,
        string? inlineOverrideYaml = null,
        CancellationToken cancellationToken = default)
    {
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);
        var localOverride = string.IsNullOrWhiteSpace(localOverridePath) || !File.Exists(localOverridePath)
            ? string.Empty
            : await File.ReadAllTextAsync(localOverridePath, cancellationToken);

        var merged = MergeYamlByAppend(template, localOverride, inlineOverrideYaml);
        return InjectPlaceholders(merged, placeholders);
    }

    public static async Task<string> WriteYamlAsync(
        string outputDirectory,
        string templatePath,
        string? localOverridePath = null,
        IReadOnlyDictionary<string, string>? placeholders = null,
        string? inlineOverrideYaml = null,
        string fileName = "appsettings.yaml",
        CancellationToken cancellationToken = default)
    {
        var yaml = await BuildYamlAsync(templatePath, localOverridePath, placeholders, inlineOverrideYaml, cancellationToken);
        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(outputDirectory, fileName);
        await File.WriteAllTextAsync(outputPath, yaml, cancellationToken);
        return outputPath;
    }

    private static string MergeYamlByAppend(string template, string localOverride, string? inlineOverrideYaml)
    {
        var builder = new StringBuilder();
        builder.AppendLine(template.TrimEnd());

        if (!string.IsNullOrWhiteSpace(localOverride))
        {
            builder.AppendLine();
            builder.AppendLine(localOverride.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(inlineOverrideYaml))
        {
            builder.AppendLine();
            builder.AppendLine(inlineOverrideYaml.TrimEnd());
        }

        return builder.ToString();
    }

    private static string InjectPlaceholders(string yaml, IReadOnlyDictionary<string, string>? placeholders)
    {
        if (placeholders is null || placeholders.Count == 0)
        {
            return yaml;
        }

        return PlaceholderRegex.Replace(yaml, match =>
        {
            var key = match.Groups[1].Value;
            return placeholders.TryGetValue(key, out var value) ? value : match.Value;
        });
    }
}
