using Microsoft.Extensions.Configuration;

namespace UniversalSyncService.Host.Configuration;

public static class YamlConfigurationExtensions
{
    public static IConfigurationBuilder AddYamlFile(
        this IConfigurationBuilder builder,
        string path,
        bool optional,
        bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 与 AddJsonFile 行为保持一致，接入自定义 YAML Source。
        return builder.Add(new YamlConfigurationSource
        {
            FileProvider = null,
            Path = path,
            Optional = optional,
            ReloadOnChange = reloadOnChange
        });
    }
}
