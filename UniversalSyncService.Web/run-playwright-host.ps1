$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$hostProjectPath = Join-Path $repoRoot 'UniversalSyncService.Host\UniversalSyncService.Host.csproj'
$hostWwwroot = Join-Path $repoRoot 'UniversalSyncService.Host\wwwroot'
$playwrightRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'UniversalSyncService-PlaywrightHost'
$playwrightApiKey = if ($env:PLAYWRIGHT_API_KEY) { $env:PLAYWRIGHT_API_KEY } else { 'playwright-key' }
$playwrightUrls = if ($env:PLAYWRIGHT_BASE_URL) { $env:PLAYWRIGHT_BASE_URL } else { 'http://127.0.0.1:7199' }

if (Test-Path $playwrightRoot) {
    Remove-Item $playwrightRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $playwrightRoot | Out-Null
Copy-Item $hostWwwroot -Destination (Join-Path $playwrightRoot 'wwwroot') -Recurse -Force

$masterRoot = Join-Path $playwrightRoot 'sync-test\master'
$slaveRoot = Join-Path $playwrightRoot 'sync-test\slave'
New-Item -ItemType Directory -Force -Path $masterRoot | Out-Null
New-Item -ItemType Directory -Force -Path $slaveRoot | Out-Null
Set-Content -Path (Join-Path $masterRoot 'playwright-seed.txt') -Value 'seed-from-playwright' -Encoding UTF8

$playwrightConfig = @'
UniversalSyncService:
  Service:
    ServiceName: "UniversalSyncService.Host.Playwright"
    HeartbeatIntervalSeconds: 60
  Interface:
    EnableGrpc: true
    EnableHttpApi: true
    EnableWebConsole: true
    ManagementApiKey: "__PLAYWRIGHT_API_KEY__"
  Logging:
    MinimumLevel: "Information"
    EnableConsoleSink: false
    EnableFileSink: false
    Overrides: {}
    File:
      Path: "logs/playwright-.log"
      RollingInterval: "Day"
      RetainedFileCountLimit: 2
      FileSizeLimitBytes: 1048576
      RollOnFileSizeLimit: true
      OutputTemplate: "{Message:lj}{NewLine}{Exception}"
  Plugins:
    EnablePluginSystem: false
    PluginDirectory: "plugins"
    Descriptors: []
  Sync:
    EnableSyncFramework: true
    SchedulerPollingIntervalSeconds: 60
    MaxConcurrentTasks: 1
    HistoryRetentionVersions: 20
    HistoryStorePath: "data/playwright-sync-history.db"
    Nodes:
      - Id: "local-master"
        Name: "本地主节点"
        NodeType: "Local"
        ConnectionSettings:
          RootPath: "sync-test/master"
        CustomOptions: {}
        CreatedAt: "2026-04-09T00:00:00+08:00"
        ModifiedAt: "2026-04-09T00:00:00+08:00"
        IsEnabled: true
      - Id: "local-slave"
        Name: "本地从节点"
        NodeType: "Local"
        ConnectionSettings:
          RootPath: "sync-test/slave"
        CustomOptions: {}
        CreatedAt: "2026-04-09T00:00:00+08:00"
        ModifiedAt: "2026-04-09T00:00:00+08:00"
        IsEnabled: true
    Plans:
      - Id: "local-filesystem-test"
        Name: "本地文件系统测试计划"
        Description: "供 Playwright 浏览器验收使用。"
        MasterNodeId: "local-master"
        SyncItemType: "FileSystem"
        SlaveConfigurations:
          - SlaveNodeId: "local-slave"
            SyncMode: "Bidirectional"
            SourcePath: "."
            TargetPath: "."
            EnableDeletionProtection: true
            Filters: []
            Exclusions: []
        Schedule:
          TriggerType: "Manual"
          EnableFileSystemWatcher: false
        IsEnabled: true
        CreatedAt: "2026-04-09T00:00:00+08:00"
        ModifiedAt: "2026-04-09T00:00:00+08:00"
        ExecutionCount: 0
'@

$playwrightConfig = $playwrightConfig.Replace('__PLAYWRIGHT_API_KEY__', $playwrightApiKey)

Set-Content -Path (Join-Path $playwrightRoot 'appsettings.yaml') -Value $playwrightConfig -Encoding UTF8

$env:ASPNETCORE_URLS = $playwrightUrls
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:DOTNET_ENVIRONMENT = 'Production'

dotnet run --project $hostProjectPath --no-build --no-launch-profile -- --contentRoot $playwrightRoot
