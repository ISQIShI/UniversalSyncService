import { expect, test } from '@playwright/test';

test('web console loads and can execute a plan', async ({ page }) => {
  const pageErrors: Error[] = [];
  page.on('pageerror', (error) => pageErrors.push(error));

  await page.goto('/');

  await page.waitForTimeout(1000); // Give profile fetch a chance

  const apiKeyInput = page.getByTestId('api-key-input');
  if (await apiKeyInput.count() > 0 && await apiKeyInput.isVisible()) {
    await apiKeyInput.fill('playwright-key');
    await page.getByTestId('connect-button').click();
  }

  await expect(page.getByTestId('dashboard-plan-table')).toBeVisible();
  await page.getByTestId('execute-plan-local-filesystem-test').click();
  await expect(page.locator('.result-strip')).toBeVisible();
  await page.getByTestId('plan-link-local-filesystem-test').click();

  await expect(page.getByText('同步计划', { exact: true })).toBeVisible();
  await expect(page.getByText('从节点配置')).toBeVisible();
  await expect(page.getByText('累计执行次数')).toBeVisible();
  await page.getByRole('button', { name: '立即执行同步' }).click();

  await expect(page.getByTestId('plan-execution-count-card')).toContainText('累计执行次数');
  await expect(page.getByTestId('plan-execution-count-value')).toHaveText('2', { timeout: 10000 });
  await expect(page.locator('.message-banner.error')).toHaveCount(0);
  expect(pageErrors).toHaveLength(0);
});
