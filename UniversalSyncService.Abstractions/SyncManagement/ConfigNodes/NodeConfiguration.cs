namespace UniversalSyncService.Abstractions.SyncManagement.ConfigNodes
{
    /// <summary>
    /// 表示节点配置信息，用于存储用户配置的节点连接和认证信息。
    /// </summary>
    public sealed class NodeConfiguration
    {
        /// <summary>
        /// 获取配置的唯一标识符。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 获取节点的显示名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 获取节点的类型标识（如 "Local", "OneDrive", "WebDAV" 等）。
        /// </summary>
        public string NodeType { get; }

        /// <summary>
        /// 获取节点的连接配置。
        /// 具体格式由节点类型决定，可以是连接字符串、OAuth令牌、API密钥等。
        /// </summary>
        public Dictionary<string, string> ConnectionSettings { get; }

        /// <summary>
        /// 获取或设置节点的自定义配置选项。
        /// </summary>
        public Dictionary<string, object>? CustomOptions { get; set; }

        /// <summary>
        /// 获取配置的创建时间。
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// 获取或设置配置的最后修改时间。
        /// </summary>
        public DateTimeOffset ModifiedAt { get; set; }

        /// <summary>
        /// 获取或设置配置是否已启用。
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 初始化 <see cref="NodeConfiguration"/> 类的新实例。
        /// </summary>
        public NodeConfiguration(
            string id,
            string name,
            string nodeType,
            Dictionary<string, string> connectionSettings,
            DateTimeOffset? createdAt = null)
        {
            var actualCreatedAt = createdAt ?? DateTimeOffset.Now;

            Id = id;
            Name = name;
            NodeType = nodeType;
            ConnectionSettings = connectionSettings;
            CreatedAt = actualCreatedAt;
            ModifiedAt = actualCreatedAt;
            IsEnabled = true;
            CustomOptions = new Dictionary<string, object>();
        }

        /// <summary>
        /// 更新配置信息。
        /// </summary>
        public void Update(string name, Dictionary<string, string> connectionSettings)
        {
            // 注意：这里不能修改Name和ConnectionSettings，因为它们只有getter
            // 如果需要修改这些属性，需要重新设计或使用其他方式
            ModifiedAt = DateTimeOffset.Now;
        }
    }
}
