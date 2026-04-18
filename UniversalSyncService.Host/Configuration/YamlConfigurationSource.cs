using Microsoft.Extensions.Configuration;

namespace UniversalSyncService.Host.Configuration;

public sealed class YamlConfigurationSource : FileConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // 为 FileConfigurationSource 补齐默认文件提供器和路径解析逻辑。
        EnsureDefaults(builder);
        return new YamlConfigurationProvider(this);
    }
}
