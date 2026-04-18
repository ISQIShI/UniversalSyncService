using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Core.DependencyInjection;
using UniversalSyncService.Host.Grpc;
using UniversalSyncService.Host.Http;
using UniversalSyncService.Host;
using UniversalSyncService.Host.Configuration;
using UniversalSyncService.Host.Logging;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 统一使用 YAML 配置源，替换默认 JSON 配置链路。
    builder.ConfigureUniversalSyncConfiguration(args);

    // gRPC 依赖 HTTP/2；这里统一配置为 Kestrel 支持 HTTP/1.1 + HTTP/2，兼容未来扩展。
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureEndpointDefaults(listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        });
    });

    // 依次注册强类型配置、Serilog 和 Core 服务能力（含插件生命周期托管）。
    builder.Services
        .AddUniversalSyncOptions(builder.Configuration)
        .AddUniversalSyncSerilog()
        .AddUniversalSyncCore();
    builder.Services.AddSingleton<HostRuntimeState>();
    builder.Services.AddGrpc();

    // 后台工作器负责心跳和运行期状态输出。
    builder.Services.AddHostedService<Worker>();

    var app = builder.Build();

    var interfaceOptions = app.Services.GetRequiredService<IConfigurationManagementService>().GetInterfaceOptions();

    if (interfaceOptions.EnableWebConsole)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    if (interfaceOptions.EnableHttpApi)
    {
        app.MapUniversalSyncHttpApi();
    }

    if (interfaceOptions.EnableGrpc)
    {
        app.MapGrpcService<SyncApiService>();
    }

    if (interfaceOptions.EnableWebConsole)
    {
        app.MapFallbackToFile("index.html");
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "宿主服务发生未处理异常并终止。");
    throw;
}
finally
{
    // 确保 Serilog 在退出时刷新缓冲并释放资源。
    Log.CloseAndFlush();
}

public partial class Program;
