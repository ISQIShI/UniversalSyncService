using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Configuration;

internal static class ConfigurationYamlCommentSerializer
{
    public static string Serialize<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder();
        WriteObject(builder, value.GetType(), value, indent: 0);
        return builder.ToString();
    }

    private static void WriteObject(StringBuilder builder, Type type, object instance, int indent)
    {
        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .OrderBy(property => property.MetadataToken)
            .ToList();

        foreach (var property in properties)
        {
            var propertyName = property.Name;
            var propertyValue = property.GetValue(instance);
            var propertyType = property.PropertyType;

            WriteComment(builder, property.GetCustomAttribute<ConfigCommentAttribute>()?.Comment, indent);
            WriteProperty(builder, propertyName, propertyType, propertyValue, indent);
        }
    }

    private static void WriteProperty(StringBuilder builder, string key, Type type, object? value, int indent)
    {
        if (value is null)
        {
            return;
        }

        if (IsSimpleType(type))
        {
            WriteLine(builder, indent, $"{key}: {FormatScalar(value)}");
            return;
        }

        if (TryAsDictionary(type, value, out var dictionary))
        {
            if (dictionary.Count == 0)
            {
                WriteLine(builder, indent, $"{key}: {{}}");
                return;
            }

            WriteLine(builder, indent, $"{key}:");
            foreach (DictionaryEntry entry in dictionary)
            {
                var dictionaryKey = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                WriteNode(builder, dictionaryKey, entry.Value, indent + 2);
            }

            return;
        }

        if (TryAsEnumerable(type, value, out var enumerable))
        {
            var items = enumerable.Cast<object?>().ToList();
            if (items.Count == 0)
            {
                WriteLine(builder, indent, $"{key}: []");
                return;
            }

            WriteLine(builder, indent, $"{key}:");
            foreach (var item in items)
            {
                WriteListItem(builder, item, indent + 2);
            }

            return;
        }

        WriteLine(builder, indent, $"{key}:");
        WriteObject(builder, type, value, indent + 2);
    }

    private static void WriteNode(StringBuilder builder, string key, object? value, int indent)
    {
        if (value is null)
        {
            return;
        }

        var type = value.GetType();
        if (IsSimpleType(type))
        {
            WriteLine(builder, indent, $"{key}: {FormatScalar(value)}");
            return;
        }

        if (TryAsDictionary(type, value, out var dictionary))
        {
            if (dictionary.Count == 0)
            {
                WriteLine(builder, indent, $"{key}: {{}}");
                return;
            }

            WriteLine(builder, indent, $"{key}:");
            foreach (DictionaryEntry entry in dictionary)
            {
                var nestedKey = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                WriteNode(builder, nestedKey, entry.Value, indent + 2);
            }

            return;
        }

        if (TryAsEnumerable(type, value, out var enumerable))
        {
            var items = enumerable.Cast<object?>().ToList();
            if (items.Count == 0)
            {
                WriteLine(builder, indent, $"{key}: []");
                return;
            }

            WriteLine(builder, indent, $"{key}:");
            foreach (var item in items)
            {
                WriteListItem(builder, item, indent + 2);
            }

            return;
        }

        WriteLine(builder, indent, $"{key}:");
        WriteObject(builder, type, value, indent + 2);
    }

    private static void WriteListItem(StringBuilder builder, object? item, int indent)
    {
        if (item is null)
        {
            WriteLine(builder, indent, "- null");
            return;
        }

        var itemType = item.GetType();
        if (IsSimpleType(itemType))
        {
            WriteLine(builder, indent, $"- {FormatScalar(item)}");
            return;
        }

        if (TryAsDictionary(itemType, item, out var dictionary))
        {
            if (dictionary.Count == 0)
            {
                WriteLine(builder, indent, "- {}");
                return;
            }

            WriteLine(builder, indent, "-");
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                WriteNode(builder, key, entry.Value, indent + 2);
            }

            return;
        }

        if (TryAsEnumerable(itemType, item, out var enumerable))
        {
            var items = enumerable.Cast<object?>().ToList();
            if (items.Count == 0)
            {
                WriteLine(builder, indent, "- []");
                return;
            }

            WriteLine(builder, indent, "-");
            foreach (var nestedItem in items)
            {
                WriteListItem(builder, nestedItem, indent + 2);
            }

            return;
        }

        WriteLine(builder, indent, "-");
        WriteObject(builder, itemType, item, indent + 2);
    }

    private static bool IsSimpleType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;

        return actualType.IsPrimitive
               || actualType.IsEnum
               || actualType == typeof(string)
               || actualType == typeof(decimal)
               || actualType == typeof(DateTime)
               || actualType == typeof(DateTimeOffset)
               || actualType == typeof(Guid)
               || actualType == typeof(TimeSpan);
    }

    private static bool TryAsDictionary(Type type, object value, out IDictionary dictionary)
    {
        if (typeof(IDictionary).IsAssignableFrom(type) && value is IDictionary castedDictionary)
        {
            dictionary = castedDictionary;
            return true;
        }

        dictionary = null!;
        return false;
    }

    private static bool TryAsEnumerable(Type type, object value, out IEnumerable enumerable)
    {
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && value is IEnumerable castedEnumerable)
        {
            enumerable = castedEnumerable;
            return true;
        }

        enumerable = null!;
        return false;
    }

    private static string FormatScalar(object value)
    {
        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is DateTime dateTime)
        {
            return Quote(dateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return Quote(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
        }

        if (value is TimeSpan timeSpan)
        {
            return Quote(timeSpan.ToString("c", CultureInfo.InvariantCulture));
        }

        if (value is Enum)
        {
            return Quote(value.ToString() ?? string.Empty);
        }

        if (value is string text)
        {
            return Quote(text);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return Quote(value.ToString() ?? string.Empty);
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void WriteComment(StringBuilder builder, string? comment, int indent)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var lines = comment
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            WriteLine(builder, indent, $"# {line.Trim()}");
        }
    }

    private static void WriteLine(StringBuilder builder, int indent, string line)
    {
        builder.Append(' ', indent);
        builder.AppendLine(line);
    }
}
