# {{testing.title}}

{{testing.description}}

## {{testing.levels.title}}

{{testing.levels.description}}

- **{{testing.levels.unit.name}}**: {{testing.levels.unit.description}}
- **{{testing.levels.integration.name}}**: {{testing.levels.integration.description}}
- **{{testing.levels.e2e.name}}**: {{testing.levels.e2e.description}}

## {{testing.entrypoints.title}}

{{testing.entrypoints.description}}

| {{testing.entrypoints.table.suite}} | {{testing.entrypoints.table.description}} |
|---|---|
| `UniversalSyncService.Host.IntegrationTests` | {{testing.entrypoints.table.hostDesc}} |
| `UniversalSyncService.IntegrationTests` | {{testing.entrypoints.table.syncDesc}} |

## {{testing.config.title}}

{{testing.config.description}}

- **{{testing.config.templates.name}}**: `tests/Config/Templates/`
- **{{testing.config.local.name}}**: `tests/Config/Local/` ({{testing.config.local.gitignored}})

## {{testing.onedrive.title}}

{{testing.onedrive.description}}

- **{{testing.onedrive.offline.name}}**: {{testing.onedrive.offline.description}}
- **{{testing.onedrive.warmAuth.name}}**: {{testing.onedrive.warmAuth.description}}
- **{{testing.onedrive.coldAuth.name}}**: {{testing.onedrive.coldAuth.description}}
- **{{testing.onedrive.negative.name}}**: {{testing.onedrive.negative.description}}

## {{testing.playwright.title}}

{{testing.playwright.description}}

```powershell
# {{testing.playwright.commandDesc}}
npm --prefix "UniversalSyncService.Web" run test:e2e
```

{{testing.playwright.isolationDesc}}

## {{testing.whenToRun.title}}

{{testing.whenToRun.description}}

- **{{testing.whenToRun.ci.name}}**: {{testing.whenToRun.ci.description}}
- **{{testing.whenToRun.local.name}}**: {{testing.whenToRun.local.description}}
- **{{testing.whenToRun.release.name}}**: {{testing.whenToRun.release.description}}

## {{testing.notes.title}}

{{testing.notes.generated}}
