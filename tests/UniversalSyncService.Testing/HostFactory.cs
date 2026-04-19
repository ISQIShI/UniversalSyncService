using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace UniversalSyncService.Testing;

/// <summary>
/// 测试宿主工厂辅助：统一 Host/HTTP/gRPC 启动方式。
/// </summary>
public static class HostFactory
{
    public static WebApplicationFactory<Program> CreateHost(string contentRoot, bool enableWebRoot)
    {
        return new TestWebApplicationFactory(contentRoot, enableWebRoot);
    }

    public static HttpClient CreateHttpClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient();
    }

    public static GrpcChannel CreateGrpcChannel(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });
    }

    private sealed class TestWebApplicationFactory(string contentRoot, bool enableWebRoot) : WebApplicationFactory<Program>
    {
        private readonly string _contentRoot = contentRoot;
        private readonly bool _enableWebRoot = enableWebRoot;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(_contentRoot);

            if (_enableWebRoot)
            {
                builder.UseWebRoot(Path.Combine(_contentRoot, "wwwroot"));
            }
        }
    }
}
