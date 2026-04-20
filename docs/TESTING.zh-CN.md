> Languages: [English](TESTING.md) | **简体中文**

# 测试指南

Universal Sync Service 采用多层级测试策略，确保核心逻辑、宿主集成以及浏览器管理控制台的可靠性。

## 测试层级

我们根据测试范围和依赖关系将测试分为三个层级。

- **单元测试**: 专注于 Abstractions 或 Core 内部的隔离逻辑。我们尽量减少 Mock，优先使用真实数据结构。
- **集成测试**: 验证组件间的交互，包括文件系统 IO、SQLite 持久化以及插件加载。这些测试使用真实的 Host 实例。
- **端到端 (E2E) 测试**: 通过 Playwright 驱动 Web 控制台，从用户视角验证整个系统。

## 测试入口

`tests/` 目录下有两个主要的 C# 测试项目。

| 测试套件 | 说明 |
|---|---|
| `UniversalSyncService.Host.IntegrationTests` | 验证 HTTP API、gRPC 端点和 Web 控制台资源映射。使用 WebApplicationFactory。 |
| `UniversalSyncService.IntegrationTests` | 专注于同步引擎、统一节点一致性矩阵以及删除策略守卫。 |

## 统一一致性矩阵 (Conformance Matrix)

所有节点提供者（Local、OneDrive 等）都必须通过统一的一致性矩阵测试。这确保了能力驱动操作（如基于身份的删除和范围边界解析）在不同提供者间的一致行为。

## 测试配置

测试使用与生产服务相同的 YAML 配置文件，但在隔离的临时根目录下运行。

- **配置模板**: `tests/Config/Templates/`
- **本地覆盖**: `tests/Config/Local/` (已忽略，用于存放个人凭据)

## OneDrive 测试车道

OneDrive 测试分为多个车道，以平衡覆盖范围和便利性。

- **离线 (Offline)**: 模拟 OneDrive 行为，无需网络调用。始终在 CI 中运行。
- **在线 (Warm Auth)**: 使用 `tests/Config/Local/` 中现有的刷新令牌。如果凭据存在则运行。
- **在线 (Cold Auth)**: 需要交互式登录。用于初始设置或刷新凭据。
- **鉴权负面 (Auth Negative)**: 验证身份验证失败或令牌过期时的系统行为。

## Playwright 浏览器测试

E2E 测试确保 Web 控制台与后端协同工作正常。

```powershell
# 运行所有浏览器测试
npm --prefix "UniversalSyncService.Web" run test:e2e
```

测试运行在由 `run-playwright-host.ps1` 管理的隔离 Host 环境中，确保不会干扰你的开发环境。

## 何时运行哪些测试

遵循这些指南以保持高代码质量，同时不影响开发效率。

- **PR / CI**: 运行所有集成测试（离线车道）。
- **本地开发**: 运行与你正在开发的功能相关的集成测试。
- **发布前 (Pre-release)**: 运行完整套件，包括在线 Warm Auth 和 Playwright E2E。

## 说明

该文档由 docs/i18n/targets/testing/template.md 与语言资源生成。
