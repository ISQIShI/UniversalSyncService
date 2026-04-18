using Microsoft.Extensions.Configuration;
using YamlDotNet.RepresentationModel;

namespace UniversalSyncService.Host.Configuration;

internal static class YamlConfigurationStreamParser
{
    public static IDictionary<string, string?> Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // IConfiguration 采用扁平键值模型，因此需要把 YAML 树形结构展开为冒号分隔键。
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(stream);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
        {
            return data;
        }

        VisitNode(data, parentPath: null, yaml.Documents[0].RootNode);
        return data;
    }

    private static void VisitNode(IDictionary<string, string?> data, string? parentPath, YamlNode node)
    {
        // 递归访问三类节点：映射、序列、标量。
        switch (node)
        {
            case YamlMappingNode mappingNode:
                VisitMappingNode(data, parentPath, mappingNode);
                break;
            case YamlSequenceNode sequenceNode:
                VisitSequenceNode(data, parentPath, sequenceNode);
                break;
            case YamlScalarNode scalarNode:
                if (parentPath is not null)
                {
                    data[parentPath] = scalarNode.Value;
                }
                break;
            default:
                if (parentPath is not null)
                {
                    data[parentPath] = node.ToString();
                }
                break;
        }
    }

    private static void VisitMappingNode(
        IDictionary<string, string?> data,
        string? parentPath,
        YamlMappingNode mappingNode)
    {
        foreach (var (keyNode, valueNode) in mappingNode.Children)
        {
            var rawKey = (keyNode as YamlScalarNode)?.Value;
            if (string.IsNullOrWhiteSpace(rawKey))
            {
                continue;
            }

            var key = parentPath is null ? rawKey : ConfigurationPath.Combine(parentPath, rawKey);
            VisitNode(data, key, valueNode);
        }
    }

    private static void VisitSequenceNode(
        IDictionary<string, string?> data,
        string? parentPath,
        YamlSequenceNode sequenceNode)
    {
        // 数组节点按索引展开为 key:0 / key:1 的形式。
        for (var index = 0; index < sequenceNode.Children.Count; index++)
        {
            var key = parentPath is null
                ? index.ToString()
                : ConfigurationPath.Combine(parentPath, index.ToString());

            VisitNode(data, key, sequenceNode.Children[index]);
        }
    }
}
