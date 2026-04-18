# {{readme.title}}

{{readme.description}}

## {{readme.features.title}}

- {{readme.features.syncPlans}}
- {{readme.features.nodeProviders}}
- {{readme.features.webConsole}}

## {{readme.setup.title}}

### {{readme.setup.prerequisites}}

- {{readme.setup.dotnet}}
- {{readme.setup.node}}

### {{readme.setup.install}}

```powershell
dotnet build "UniversalSyncService.slnx"
npm --prefix "UniversalSyncService.Web" install
```

### {{readme.setup.runHost}}

```powershell
dotnet run --project "UniversalSyncService.Host/UniversalSyncService.Host.csproj"
```

### {{readme.setup.runWeb}}

```powershell
npm --prefix "UniversalSyncService.Web" run dev
```

### {{readme.setup.buildWeb}}

```powershell
npm --prefix "UniversalSyncService.Web" run build
```

### {{readme.setup.tests}}

```powershell
dotnet test "tests/UniversalSyncService.Host.IntegrationTests/UniversalSyncService.Host.IntegrationTests.csproj"
dotnet test "tests/UniversalSyncService.IntegrationTests/UniversalSyncService.IntegrationTests.csproj"
```

### {{readme.setup.playwright}}

```powershell
npm --prefix "UniversalSyncService.Web" run test:e2e
```

## {{readme.notes.title}}

{{readme.notes.generated}}
