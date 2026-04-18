# UniversalSyncService.Web

这是为 `UniversalSyncService.Host` 提供浏览器前端页面的 TypeScript 项目。

## 技术栈

- Vite
- React
- TypeScript

## 本地开发

```powershell
npm install
npm run dev
```

也可以先复制一份环境变量模板：

```powershell
Copy-Item .env.example .env.local
```

默认会通过 Vite 代理把 `/api` 与 `/health` 转发到：

- `https://localhost:7188`

因此本地开发时通常需要同时启动：

```powershell
dotnet run --project "../UniversalSyncService.Host/UniversalSyncService.Host.csproj"
```

## 构建

```powershell
npm run build
```

构建产物会直接输出到：

- `../UniversalSyncService.Host/wwwroot`

因此 Host 启动后会直接把这些静态文件作为 Web 控制台页面对外提供。

## 部署注意事项

1. 先在 Host 配置中设置安全的 `ManagementApiKey`
2. 修改前端后务必重新执行 `npm run build`
3. 生产环境推荐通过 HTTPS 暴露 Host

## Playwright 验收

```powershell
npx playwright test
```

当前默认会：

- 使用 Edge (`channel: msedge`)
- 启动一个独立的临时 Host 环境
- 不污染正常开发使用的 `appsettings.yaml`

你也可以通过环境变量覆盖：

- `PLAYWRIGHT_BASE_URL`
- `PLAYWRIGHT_API_KEY`
- `PLAYWRIGHT_TEST_TIMEOUT_MS`
- `PLAYWRIGHT_WEBSERVER_TIMEOUT_MS`
