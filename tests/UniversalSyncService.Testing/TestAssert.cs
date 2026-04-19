using Xunit;

namespace UniversalSyncService.Testing;

/// <summary>
/// 常用测试断言与结果记录帮助器。
/// </summary>
public static class TestAssert
{
    public static void ContainsOnlyExpectedResults<T>(IReadOnlyDictionary<string, T> results, params T[] expected)
        where T : notnull
    {
        Assert.NotEmpty(results);
        Assert.All(results.Values, value => Assert.Contains(value, expected));
    }

    public static string ToResultSummary<T>(IReadOnlyDictionary<string, T> results)
        where T : notnull
    {
        return string.Join(", ",
            results
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
