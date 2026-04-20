> Languages: **English** | [简体中文](TESTING.zh-CN.md)

# Testing Guide

Universal Sync Service uses a multi-layered testing strategy to ensure reliability across core logic, host integration, and browser management console.

## Test Levels

We categorize tests into three levels based on their scope and dependencies.

- **Unit Tests**: Focus on isolated logic within Abstractions or Core. We minimize mocks and prefer real data structures.
- **Integration Tests**: Verify the interaction between components, including filesystem IO, SQLite persistence, and plugin loading. These use real Host instances.
- **End-to-End (E2E) Tests**: Validate the entire system from the user's perspective using Playwright to drive the Web console.

## Test Entrypoints

There are two main C# test projects in the `tests/` directory.

| Test Suite | Description |
|---|---|
| `UniversalSyncService.Host.IntegrationTests` | Verifies HTTP API, gRPC endpoints, and Web console asset mapping. Uses WebApplicationFactory. |
| `UniversalSyncService.IntegrationTests` | Focuses on the sync engine, unified node conformance matrix, and deletion policy guards. |

## Unified Conformance Matrix

All node providers (Local, OneDrive, etc.) must pass a unified conformance matrix. This ensures consistent behavior for capability-driven operations like identity-based deletion and scope boundary resolution.

## Test Configuration

Tests use YAML-based configuration just like the production service, but with isolated temporary roots.

- **Config Templates**: `tests/Config/Templates/`
- **Local Overrides**: `tests/Config/Local/` (gitignored, used for personal credentials)

## OneDrive Testing Lane

OneDrive tests are split into lanes to balance coverage and convenience.

- **Offline**: Simulates OneDrive behavior without network calls. Always runs in CI.
- **Online (Warm Auth)**: Uses existing refresh tokens in `tests/Config/Local/`. Runs if credentials present.
- **Online (Cold Auth)**: Requires interactive login. Used for initial setup or credential refreshing.
- **Auth Negative**: Verifies system behavior when authentication fails or tokens expire.

## Playwright Browser Tests

E2E tests ensure the Web console works correctly with the backend.

```powershell
# Run all browser tests
npm --prefix "UniversalSyncService.Web" run test:e2e
```

Tests run against an isolated Host environment managed by `run-playwright-host.ps1`, ensuring no interference with your development setup.

## When to Run Which Tests

Follow these guidelines to maintain high code quality without slowing down development.

- **PR / CI**: Run all integration tests (offline lanes).
- **Local Development**: Run relevant integration tests for the feature you are working on.
- **Pre-release**: Run the full suite including Online Warm Auth and Playwright E2E.

## Notes

This document is generated from docs/i18n/targets/testing/template.md and locale resources.
