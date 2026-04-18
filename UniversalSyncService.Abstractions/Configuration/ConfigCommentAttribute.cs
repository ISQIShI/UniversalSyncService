namespace UniversalSyncService.Abstractions.Configuration;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ConfigCommentAttribute : Attribute
{
    public ConfigCommentAttribute(string comment)
    {
        Comment = comment;
    }

    public string Comment { get; }
}
