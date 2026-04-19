> Languages: **English** | [简体中文](docs/README.zh-CN.md)

# Universal Sync Service

A headless file synchronization service with plugin-based node providers and Web management console.

## Features

- Create, edit, and execute synchronization plans
- Support Local Filesystem and OneDrive node providers
- Manage service status, plans, nodes, and system configuration in Web UI

## Setup

### Prerequisites

- Install .NET SDK 9.0 or newer
- Install Node.js 20 or newer

### Install dependencies

```powershell
dotnet build "UniversalSyncService.slnx"
npm --prefix "UniversalSyncService.Web" install
```

### Run host service

```powershell
dotnet run --project "UniversalSyncService.Host/UniversalSyncService.Host.csproj"
```

### Run Web console

```powershell
npm --prefix "UniversalSyncService.Web" run dev
```

### Build Web assets

```powershell
npm --prefix "UniversalSyncService.Web" run build
```

## Testing

See [Testing Guide](docs/TESTING.md) for detailed test instructions.

## Notes

This README is generated from template and locale resources.
