namespace UniversalSyncService.Testing;

/// <summary>
/// 统一解析测试配置模板与本地覆盖文件路径。
/// </summary>
public static class TestConfigPaths
{
    private static readonly Lazy<string> RepositoryRoot = new(ResolveRepositoryRoot);

    public static string GetTemplatePath(string templateFileName)
    {
        return Path.Combine(RepositoryRoot.Value, "tests", "Config", "Templates", templateFileName);
    }

    public static string GetLocalOverridePath(string templateFileName)
    {
        return Path.Combine(RepositoryRoot.Value, "tests", "Config", "Local", templateFileName);
    }

    private static string ResolveRepositoryRoot()
    {
        var startPaths = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(TestConfigPaths).Assembly.Location) ?? string.Empty
        }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var startPath in startPaths)
        {
            var directoryInfo = new DirectoryInfo(startPath);
            while (directoryInfo is not null)
            {
                var templateDirectory = Path.Combine(directoryInfo.FullName, "tests", "Config", "Templates");
                if (Directory.Exists(templateDirectory))
                {
                    return directoryInfo.FullName;
                }

                directoryInfo = directoryInfo.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"无法定位仓库根目录（期望存在 tests/Config/Templates）。起始路径：{string.Join("; ", startPaths)}");
    }
}
