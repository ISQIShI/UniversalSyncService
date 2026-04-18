namespace UniversalSyncService.Host.Configuration;

public sealed class YamlConfigurationProvider : FileConfigurationProvider
{
    public YamlConfigurationProvider(YamlConfigurationSource source)
        : base(source)
    {
    }

    public override void Load(Stream stream)
    {
        // 将 YAML 文档解析为扁平化键值结构，供 IConfiguration 消费。
        Data = YamlConfigurationStreamParser.Parse(stream);
    }
}
