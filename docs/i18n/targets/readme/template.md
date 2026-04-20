# {{readme.title}}

{{readme.description}}

## {{readme.features.title}}

- {{readme.features.syncPlans}}
- {{readme.features.nodeProviders}}
- {{readme.features.lifecycle}}
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

## {{readme.testing.title}}

{{readme.testing.description}}

## {{readme.notes.title}}

{{readme.notes.generated}}
