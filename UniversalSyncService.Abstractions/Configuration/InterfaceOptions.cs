namespace UniversalSyncService.Abstractions.Configuration;

public sealed class InterfaceOptions
{
    public const string SectionName = "UniversalSyncService:Interface";

    [ConfigComment("是否启用 gRPC 接口。")]
    public bool EnableGrpc { get; set; } = true;

    [ConfigComment("是否启用浏览器友好 HTTP API。")]
    public bool EnableHttpApi { get; set; } = true;

    [ConfigComment("是否启用 Web 控制台静态页面。")]
    public bool EnableWebConsole { get; set; } = true;

    [ConfigComment("是否强制要求 Web 管理接口访问密钥。关闭后浏览器和插件可直接访问 HTTP API。")]
    public bool RequireManagementApiKey { get; set; } = false;

    [ConfigComment("当请求来自 127.0.0.1 / ::1 时，是否跳过 Web 管理接口密钥校验。")]
    public bool AllowAnonymousLoopback { get; set; } = true;

    [ConfigComment("Web 管理接口访问密钥。浏览器或插件调用 HTTP API 时需以 Bearer Token 方式携带。")]
    public string ManagementApiKey { get; set; } = "change-me";
}
