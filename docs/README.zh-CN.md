> Languages: [English](../README.md) | **简体中文**

# Universal Sync Service

一个无头文件同步服务，支持插件化节点提供者与 Web 管理控制台。

## 功能特性

- 创建、编辑并执行同步计划
- 支持本地文件系统和 OneDrive 节点提供者
- 在 Web 界面中管理服务状态、计划、节点和系统配置

## 快速开始

### 前置条件

- 安装 .NET SDK 9.0 或更高版本
- 安装 Node.js 20 或更高版本

### 安装依赖

```powershell
dotnet build "UniversalSyncService.slnx"
npm --prefix "UniversalSyncService.Web" install
```

### 运行 Host 服务

```powershell
dotnet run --project "UniversalSyncService.Host/UniversalSyncService.Host.csproj"
```

### 运行 Web 控制台

```powershell
npm --prefix "UniversalSyncService.Web" run dev
```

### 构建 Web 静态资源

```powershell
npm --prefix "UniversalSyncService.Web" run build
```

## 测试指南

详见 [测试指南](docs/TESTING.md) 获取详细的测试指令。

## 说明

该 README 是基于模板与语言资源生成的。
