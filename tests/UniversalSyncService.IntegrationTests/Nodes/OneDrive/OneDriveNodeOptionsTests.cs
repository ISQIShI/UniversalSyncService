using UniversalSyncService.Core.Nodes.OneDrive;
using Xunit;

namespace UniversalSyncService.IntegrationTests.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点配置选项测试。
/// </summary>
public class OneDriveNodeOptionsTests
{
    [Fact]
    public void FromConnectionSettings_ShouldParseAllOptions()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["TenantId"] = "common",
            ["ClientId"] = "test-client-id",
            ["AuthMode"] = "InteractiveBrowser",
            ["UserId"] = "me",
            ["DriveSelector"] = "Me",
            ["RootPath"] = "Documents/UniversalSync",
            ["TokenCachePath"] = "data/auth/test",
            ["Scopes"] = "Files.ReadWrite offline_access",
            ["LargeFileThresholdBytes"] = "8388608",
            ["UploadSliceSizeBytes"] = "10485760",
            ["ConflictBehavior"] = "rename",
            ["ConnectTimeoutMs"] = "60000",
            ["MaxRetries"] = "5",
            ["BackoffMs"] = "2000",
            ["InteractiveBrowserRedirectPort"] = "8080"
        };

        // Act
        var options = OneDriveNodeOptions.FromConnectionSettings(settings);

        // Assert
        Assert.Equal("common", options.TenantId);
        Assert.Equal("test-client-id", options.ClientId);
        Assert.Equal("InteractiveBrowser", options.AuthMode);
        Assert.Equal("me", options.UserId);
        Assert.Equal("Me", options.DriveSelector);
        Assert.Equal("Documents/UniversalSync", options.RootPath);
        Assert.Equal("data/auth/test", options.TokenCachePath);
        Assert.Equal("Files.ReadWrite offline_access", options.Scopes);
        Assert.Equal(8388608, options.LargeFileThresholdBytes);
        Assert.Equal(10485760, options.UploadSliceSizeBytes);
        Assert.Equal("rename", options.ConflictBehavior);
        Assert.Equal(60000, options.ConnectTimeoutMs);
        Assert.Equal(5, options.MaxRetries);
        Assert.Equal(2000, options.BackoffMs);
        Assert.Equal(8080, options.InteractiveBrowserRedirectPort);
    }

    [Fact]
    public void FromConnectionSettings_ShouldUseDefaultsForMissingValues()
    {
        // Arrange
        var settings = new Dictionary<string, string>
        {
            ["ClientId"] = "test-client-id"
        };

        // Act
        var options = OneDriveNodeOptions.FromConnectionSettings(settings);

        // Assert
        Assert.Equal("common", options.TenantId); // 默认值
        Assert.Equal("test-client-id", options.ClientId);
        Assert.Equal("InteractiveBrowser", options.AuthMode); // 默认值（推荐浏览器认证）
        Assert.Equal("me", options.UserId); // 默认值
        Assert.Equal("Me", options.DriveSelector); // 默认值
        Assert.Equal("/", options.RootPath); // 默认值
        Assert.Equal("data/auth/onedrive", options.TokenCachePath); // 默认值
        Assert.Equal("Files.ReadWrite offline_access User.Read", options.Scopes); // 默认值
        Assert.Equal(4 * 1024 * 1024, options.LargeFileThresholdBytes); // 默认值 4MB
        Assert.Equal(5 * 1024 * 1024, options.UploadSliceSizeBytes); // 默认值 5MB
        Assert.Equal("replace", options.ConflictBehavior); // 默认值
        Assert.Equal(30000, options.ConnectTimeoutMs); // 默认值
        Assert.Equal(3, options.MaxRetries); // 默认值
        Assert.Equal(1000, options.BackoffMs); // 默认值
        Assert.Equal(8765, options.InteractiveBrowserRedirectPort); // 默认值
    }

    [Theory]
    [InlineData("", "ClientId 不能为空")]
    [InlineData("test-client-id", null)]
    public void Validate_ShouldCheckClientId(string clientId, string? expectedError)
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            RootPath = "/"
        };

        // Act
        var isValid = options.Validate(out var errorMessage);

        // Assert
        if (expectedError == null)
        {
            Assert.True(isValid);
            Assert.Null(errorMessage);
        }
        else
        {
            Assert.False(isValid);
            Assert.Equal(expectedError, errorMessage);
        }
    }

    [Theory]
    [InlineData("", "OneDrive RootPath 不能为空，请填写 / 或 /Documents/UniversalSync 这类以 / 开头的远端绝对路径。")]
    [InlineData("Documents/Sync", "OneDrive RootPath 必须以 / 开头，例如 / 或 /Documents/UniversalSync。")]
    [InlineData("/", null)]
    [InlineData("/Documents/Sync", null)]
    public void Validate_ShouldCheckAbsoluteRootPath(string rootPath, string? expectedError)
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id",
            RootPath = rootPath
        };

        // Act
        var isValid = options.Validate(out var errorMessage);

        // Assert
        if (expectedError == null)
        {
            Assert.True(isValid);
            Assert.Null(errorMessage);
        }
        else
        {
            Assert.False(isValid);
            Assert.Equal(expectedError, errorMessage);
        }
    }

    [Theory]
    [InlineData("DriveId", null, "DriveSelector 为 DriveId 时 DriveId 不能为空")]
    [InlineData("DriveId", "some-drive-id", null)]
    [InlineData("Me", null, null)]
    public void Validate_ShouldCheckDriveIdForDriveIdSelector(string driveSelector, string? driveId, string? expectedError)
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id",
            DriveSelector = driveSelector,
            DriveId = driveId
        };

        // Act
        var isValid = options.Validate(out var errorMessage);

        // Assert
        if (expectedError == null)
        {
            Assert.True(isValid);
            Assert.Null(errorMessage);
        }
        else
        {
            Assert.False(isValid);
            Assert.Equal(expectedError, errorMessage);
        }
    }
}
