using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.Plugins;

namespace UniversalSyncService.Core.Plugins;

public sealed class PluginManager : IPluginManager
{
    // 使用宿主环境信息解析插件相对目录。
    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PluginManager> _logger;
    private readonly List<IPlugin> _loadedPluginInstances = [];

    public PluginManager(
        IConfigurationManagementService configurationManagementService,
        IHostEnvironment environment,
        ILogger<PluginManager> logger)
    {
        _configurationManagementService = configurationManagementService;
        _environment = environment;
        _logger = logger;
    }

    public IReadOnlyList<PluginMetadata> LoadedPlugins =>
        _loadedPluginInstances.Select(plugin => plugin.Metadata).ToList();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 统一通过配置管理器读取插件配置，避免与其他模块读取方式不一致。
        var options = _configurationManagementService.GetPluginSystemOptions();

        if (!options.EnablePluginSystem)
        {
            _logger.LogInformation("插件系统已在配置中禁用。");
            return;
        }

        if (options.Descriptors.Count == 0)
        {
            _logger.LogInformation("未配置任何插件描述符。");
            return;
        }

        // 启动前确保插件根目录存在，避免后续路径解析失败。
        var pluginRootDirectory = ResolvePluginRootDirectory(options);
        Directory.CreateDirectory(pluginRootDirectory);

        var descriptors = ResolveDescriptors(options);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryLoadPlugin(pluginRootDirectory, descriptor);
        }

        foreach (var plugin in _loadedPluginInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await plugin.InitializeAsync(cancellationToken);
            _logger.LogInformation("插件初始化完成：{PluginId}（版本：{Version}）", plugin.Metadata.Id, plugin.Metadata.Version);
        }

        _logger.LogInformation("插件初始化流程结束，已加载 {PluginCount} 个插件。", _loadedPluginInstances.Count);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in _loadedPluginInstances.AsEnumerable().Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await plugin.StopAsync(cancellationToken);
                _logger.LogInformation("插件已停止：{PluginId}", plugin.Metadata.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止插件时发生异常：{PluginId}", plugin.Metadata.Id);
            }
        }
    }

    private static IReadOnlyList<PluginDescriptor> ResolveDescriptors(PluginSystemOptions options)
    {
        return options.Descriptors
            .Where(descriptor => descriptor.Enabled)
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.Id))
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.AssemblyPath))
            .Where(descriptor => !string.IsNullOrWhiteSpace(descriptor.EntryType))
            .Select(descriptor => new PluginDescriptor(
                descriptor.Id,
                descriptor.AssemblyPath!,
                descriptor.EntryType!,
                descriptor.Enabled,
                descriptor.Description))
            .ToList();
    }

    private string ResolvePluginRootDirectory(PluginSystemOptions options)
    {
        // 相对路径按 ContentRoot 进行解析，保证在不同启动目录下行为一致。
        return Path.IsPathRooted(options.PluginDirectory)
            ? options.PluginDirectory
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, options.PluginDirectory));
    }

    private void TryLoadPlugin(string pluginRootDirectory, PluginDescriptor descriptor)
    {
        try
        {
            // 插件程序集路径支持绝对路径和相对插件目录路径。
            var assemblyFullPath = Path.IsPathRooted(descriptor.AssemblyPath)
                ? descriptor.AssemblyPath
                : Path.GetFullPath(Path.Combine(pluginRootDirectory, descriptor.AssemblyPath));

            if (!File.Exists(assemblyFullPath))
            {
                _logger.LogWarning("未找到插件程序集：{PluginId}，路径：{AssemblyPath}", descriptor.Id, assemblyFullPath);
                return;
            }

            var assembly = Assembly.LoadFrom(assemblyFullPath);
            var type = assembly.GetType(descriptor.EntryType, throwOnError: false, ignoreCase: false);
            if (type is null)
            {
                _logger.LogWarning("未找到插件入口类型：{PluginId}，类型：{EntryType}", descriptor.Id, descriptor.EntryType);
                return;
            }

            if (!typeof(IPlugin).IsAssignableFrom(type))
            {
                _logger.LogWarning("插件入口类型未实现 IPlugin：{PluginId}，类型：{EntryType}", descriptor.Id, descriptor.EntryType);
                return;
            }

            if (Activator.CreateInstance(type) is not IPlugin plugin)
            {
                _logger.LogWarning("无法实例化插件：{PluginId}，类型：{EntryType}", descriptor.Id, descriptor.EntryType);
                return;
            }

            _loadedPluginInstances.Add(plugin);
            _logger.LogInformation("插件加载成功：{PluginId}（版本：{Version}）", plugin.Metadata.Id, plugin.Metadata.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载插件失败：{PluginId}", descriptor.Id);
        }
    }
}
