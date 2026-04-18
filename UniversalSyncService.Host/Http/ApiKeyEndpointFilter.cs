using Microsoft.AspNetCore.Http;
using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Http;

/// <summary>
/// 为浏览器/插件 HTTP 接口提供最小 API Key 保护。
/// 当前版本按单管理员、可信网络部署场景设计，后续可再升级到更完整的认证方案。
/// </summary>
public sealed class ApiKeyEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configurationManagementService = context.HttpContext.RequestServices.GetRequiredService<IConfigurationManagementService>();
        var interfaceOptions = configurationManagementService.GetInterfaceOptions();
        var configuredApiKey = interfaceOptions.ManagementApiKey;

        if (!interfaceOptions.RequireManagementApiKey)
        {
            return await next(context);
        }

        if (interfaceOptions.AllowAnonymousLoopback && IsLoopbackRequest(context.HttpContext))
        {
            return await next(context);
        }

        var authorizationHeader = context.HttpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.Unauthorized();
        }

        var providedApiKey = authorizationHeader["Bearer ".Length..].Trim();
        if (!string.Equals(providedApiKey, configuredApiKey, StringComparison.Ordinal))
        {
            return TypedResults.Unauthorized();
        }

        return await next(context);
    }

    private static bool IsLoopbackRequest(HttpContext httpContext)
    {
        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        return remoteIpAddress is not null && System.Net.IPAddress.IsLoopback(remoteIpAddress);
    }
}
