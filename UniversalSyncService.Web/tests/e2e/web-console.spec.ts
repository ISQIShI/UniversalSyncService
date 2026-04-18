import { expect, test } from '@playwright/test';

async function connectConsoleIfNeeded(page: import('@playwright/test').Page) {
  const connectButton = page.getByTestId('connect-button');
  if (await connectButton.count() === 0) {
    return;
  }

  try {
    await connectButton.waitFor({ state: 'visible', timeout: 1500 });
  } catch {
    // 配置切换为匿名访问时不会出现连接按钮，直接继续流程。
    return;
  }

  const apiKeyInput = page.getByTestId('api-key-input');
  try {
    await apiKeyInput.fill('playwright-key');
    await connectButton.click();
  } catch {
    // 页面可能在 profile 拉取后从 API Key 模式切换到匿名模式，属于预期竞态。
  }
}

async function runPlanFlow(page: import('@playwright/test').Page) {
  await expect(page.getByTestId('dashboard-plan-table')).toBeVisible();
  await page.getByTestId('execute-plan-local-filesystem-test').click();
  await expect(page.locator('.result-strip')).toBeVisible();
  await page.getByTestId('plan-link-local-filesystem-test').click();

  await expect(page).toHaveURL(/\/plans\//);
  await expect(page.getByTestId('plan-execution-count-card')).toBeVisible();
  await expect(page.getByTestId('plan-execution-count-value')).toBeVisible();

  const initialExecutionCount = Number(await page.getByTestId('plan-execution-count-value').innerText());
  await page.getByTestId('execute-selected-plan-button').click();
  await expect.poll(async () => {
    const text = await page.getByTestId('plan-execution-count-value').innerText();
    return Number(text);
  }, { timeout: 10000 }).toBe(initialExecutionCount + 1);
}

test('web console locale-safe plan flow works in en and zh-CN', async ({ page }) => {
  const pageErrors: Error[] = [];
  page.on('pageerror', (error) => pageErrors.push(error));

  await page.goto('/');
  await page.waitForTimeout(1000); // Give profile fetch a chance

  await connectConsoleIfNeeded(page);

  // en path: use locale-switcher and verify stable flow
  await page.getByTestId('locale-switcher').selectOption('en');
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await runPlanFlow(page);

  // zh-CN path: use locale-switcher and verify key content switched
  await page.getByTestId('locale-switcher').selectOption('zh-CN');
  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');

  await page.goto('/');
  await connectConsoleIfNeeded(page);
  await expect(page.getByTestId('locale-switcher')).toHaveValue('zh-CN');
  await runPlanFlow(page);

  await expect(page.locator('.message-banner.error')).toHaveCount(0);
  expect(pageErrors).toHaveLength(0);
});
