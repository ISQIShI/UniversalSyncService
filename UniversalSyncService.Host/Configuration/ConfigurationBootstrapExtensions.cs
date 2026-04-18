using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;

namespace UniversalSyncService.Host.Configuration;

public static class ConfigurationBootstrapExtensions
{
    public static void ConfigureUniversalSyncConfiguration(this WebApplicationBuilder builder, string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var defaultConfigPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.yaml");

        // Web Host 与 Worker Host 共享同一套 YAML 启动规则，避免接口层与后台服务出现配置漂移。
        DefaultConfigurationYamlGenerator.EnsureDefaultConfigurationFile(defaultConfigPath);

        builder.Configuration.Sources.Clear();
        ConfigureYamlSources(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.EnvironmentName, args);
    }

    public static void ConfigureUniversalSyncConfiguration(this HostApplicationBuilder builder, string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var defaultConfigPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.yaml");

        // 当配置文件不存在时自动生成默认模板，避免首次启动失败。
        DefaultConfigurationYamlGenerator.EnsureDefaultConfigurationFile(defaultConfigPath);

        // 清空默认配置源，确保项目完全按 YAML 规则进行加载。
        builder.Configuration.Sources.Clear();

        // 配置优先级从上到下逐步覆盖：基础 -> 环境 -> 本地 -> 环境变量 -> 命令行。
        ConfigureYamlSources(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.EnvironmentName, args);
    }

    private static void ConfigureYamlSources(IConfigurationBuilder builder, string contentRootPath, string environmentName, string[] args)
    {
        builder
            .SetBasePath(contentRootPath)
            .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: true)
            .AddYamlFile($"appsettings.{environmentName}.yaml", optional: true, reloadOnChange: true)
            .AddYamlFile("appsettings.Local.yaml", optional: true, reloadOnChange: true)
            .AddYamlFile($"appsettings.{environmentName}.Local.yaml", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
    }
}
