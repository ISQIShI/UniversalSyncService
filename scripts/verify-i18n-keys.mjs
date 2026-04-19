#!/usr/bin/env node

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');

const readmeEnPath = path.join(repoRoot, 'docs', 'i18n', 'targets', 'readme', 'locales', 'en.json');
const readmeZhCnPath = path.join(repoRoot, 'docs', 'i18n', 'targets', 'readme', 'locales', 'zh-CN.json');
const webEnPath = path.join(repoRoot, 'docs', 'i18n', 'targets', 'web', 'locales', 'en.json');
const webZhCnPath = path.join(repoRoot, 'docs', 'i18n', 'targets', 'web', 'locales', 'zh-CN.json');
const templatePath = path.join(repoRoot, 'docs', 'i18n', 'targets', 'readme', 'template.md');
const webI18nResourcesPath = path.join(repoRoot, 'UniversalSyncService.Web', 'src', 'i18n', 'generated', 'resources.ts');
const webI18nLocaleResolverPath = path.join(repoRoot, 'UniversalSyncService.Web', 'src', 'i18n', 'locale.ts');

const requiredLocales = ['en', 'zh-CN'];
const fallbackLocale = 'en';

const keyPattern = /^(web|readme)\.[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*$/;
const interpolationPattern = /\{([a-zA-Z0-9_]+)\}/g;
const templatePlaceholderPattern = /{{\s*([a-zA-Z0-9._-]+)\s*}}/g;

function collectPlaceholders(text) {
  interpolationPattern.lastIndex = 0;
  const values = [];
  while (true) {
    const match = interpolationPattern.exec(text);
    if (match === null) {
      break;
    }
    values.push(match[1]);
  }

  return values.sort();
}

function equalArray(a, b) {
  if (a.length !== b.length) {
    return false;
  }

  for (let i = 0; i < a.length; i += 1) {
    if (a[i] !== b[i]) {
      return false;
    }
  }

  return true;
}

function listDifference(left, rightSet) {
  return left.filter((item) => !rightSet.has(item));
}

function ensureObject(name, value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${name} 必须是 JSON 对象`);
  }
}

function collectQuotedValues(text) {
  const values = [];
  const quotePattern = /'([^']+)'/g;
  while (true) {
    const match = quotePattern.exec(text);
    if (match === null) {
      break;
    }
    values.push(match[1]);
  }

  return values;
}

function verifyLocaleParity(issues, scope, en, zhCN) {
  ensureObject(`${scope}/en.json`, en);
  ensureObject(`${scope}/zh-CN.json`, zhCN);

  const enKeys = Object.keys(en).sort();
  const zhKeys = Object.keys(zhCN).sort();
  const enSet = new Set(enKeys);
  const zhSet = new Set(zhKeys);

  const zhMissing = listDifference(enKeys, zhSet);
  const zhOrphan = listDifference(zhKeys, enSet);

  if (zhMissing.length > 0) {
    issues.push(`${scope} zh-CN 缺少键: ${zhMissing.join(', ')}`);
  }

  if (zhOrphan.length > 0) {
    issues.push(`${scope} zh-CN 存在孤儿键: ${zhOrphan.join(', ')}`);
  }

  for (const key of enKeys) {
    if (!keyPattern.test(key)) {
      issues.push(`${scope} 键命名不符合规范: ${key}`);
      continue;
    }

    const enValue = en[key];
    const zhValue = zhCN[key];

    if (typeof enValue !== 'string' || enValue.trim().length === 0) {
      issues.push(`${scope} en 值无效: ${key}`);
    }

    if (typeof zhValue !== 'string' || zhValue.trim().length === 0) {
      issues.push(`${scope} zh-CN 值无效: ${key}`);
    }

    if (typeof enValue === 'string' && typeof zhValue === 'string') {
      const enPlaceholders = collectPlaceholders(enValue);
      const zhPlaceholders = collectPlaceholders(zhValue);

      if (!equalArray(enPlaceholders, zhPlaceholders)) {
        issues.push(`${scope} 插值占位符不一致: ${key} | en={${enPlaceholders.join(',')}} zh-CN={${zhPlaceholders.join(',')}}`);
      }
    }
  }

  return { enKeys, enSet };
}

try {
  const [
    readmeEnRaw,
    readmeZhCnRaw,
    webEnRaw,
    webZhCnRaw,
    templateRaw,
    webI18nResourcesRaw,
    webI18nLocaleResolverRaw,
  ] = await Promise.all([
    readFile(readmeEnPath, 'utf8'),
    readFile(readmeZhCnPath, 'utf8'),
    readFile(webEnPath, 'utf8'),
    readFile(webZhCnPath, 'utf8'),
    readFile(templatePath, 'utf8'),
    readFile(webI18nResourcesPath, 'utf8'),
    readFile(webI18nLocaleResolverPath, 'utf8'),
  ]);

  const readmeEn = JSON.parse(readmeEnRaw);
  const readmeZhCN = JSON.parse(readmeZhCnRaw);
  const webEn = JSON.parse(webEnRaw);
  const webZhCN = JSON.parse(webZhCnRaw);

  const issues = [];

  const readmeVerified = verifyLocaleParity(issues, 'readme target', readmeEn, readmeZhCN);
  const webVerified = verifyLocaleParity(issues, 'web target', webEn, webZhCN);

  if (!readmeVerified.enSet.has('readme.title')) {
    issues.push('缺少回退结构键: readme.title');
  }

  if (!webVerified.enSet.has('web.nav.dashboard')) {
    issues.push('缺少回退结构键: web.nav.dashboard');
  }

  if (!webVerified.enSet.has('web.app.title')) {
    issues.push('缺少回退结构键: web.app.title');
  }

  for (const key of webVerified.enKeys) {
    if (key.startsWith('readme.')) {
      issues.push(`web target 不允许 readme 命名空间: ${key}`);
    }
  }

  // fallback 规则：fallback locale 必须在受支持 locale 集合中
  if (!requiredLocales.includes(fallbackLocale)) {
    issues.push(`fallback locale 必须出现在受支持 locale 列表中: ${fallbackLocale}`);
  }

  templatePlaceholderPattern.lastIndex = 0;
  const templatePlaceholders = new Set();
  while (true) {
    const templateMatch = templatePlaceholderPattern.exec(templateRaw);
    if (templateMatch === null) {
      break;
    }
    templatePlaceholders.add(templateMatch[1]);
  }

  for (const placeholder of templatePlaceholders) {
    if (!readmeVerified.enSet.has(placeholder)) {
      issues.push(`README 模板占位符未在 en.json 中定义: ${placeholder}`);
    }

    if (!placeholder.startsWith('readme.')) {
      issues.push(`README 模板占位符必须使用 readme.* 命名空间: ${placeholder}`);
    }
  }

  // fallback 规则：Web runtime 的 fallbackLocale 必须固定为 en
  const fallbackLocaleMatch = webI18nResourcesRaw.match(/export\s+const\s+fallbackLocale\s*:\s*LocaleCode\s*=\s*'([^']+)'\s*;/);
  if (!fallbackLocaleMatch) {
    issues.push('Web i18n resources.ts 中缺少 fallbackLocale 声明');
  } else if (fallbackLocaleMatch[1] !== fallbackLocale) {
    issues.push(`Web i18n fallbackLocale 必须为 ${fallbackLocale}，当前为 ${fallbackLocaleMatch[1]}`);
  }

  // fallback 规则：LocaleCode 至少包含 en 与 zh-CN
  const localeCodeMatch = webI18nResourcesRaw.match(/export\s+type\s+LocaleCode\s*=\s*([^;]+);/);
  if (!localeCodeMatch) {
    issues.push('Web i18n resources.ts 中缺少 LocaleCode 类型声明');
  } else {
    const localeCodes = new Set(collectQuotedValues(localeCodeMatch[1]));
    for (const locale of requiredLocales) {
      if (!localeCodes.has(locale)) {
        issues.push(`Web i18n LocaleCode 缺少 locale: ${locale}`);
      }
    }
  }

  // fallback 规则：locale resolver 必须采用 user override > browser locale > en fallback
  if (!webI18nLocaleResolverRaw.includes("localStorage.getItem('uss_locale')")) {
    issues.push('locale resolver 缺少 user override（uss_locale）读取逻辑');
  }

  if (!webI18nLocaleResolverRaw.includes('window.navigator.language')) {
    issues.push('locale resolver 缺少 browser locale 解析逻辑');
  }

  if (!webI18nLocaleResolverRaw.includes('return fallbackLocale;')) {
    issues.push('locale resolver 缺少 fallbackLocale 回退逻辑');
  }

  if (issues.length > 0) {
    process.stderr.write('[verify-i18n-keys] Verification failed:\n');
    for (const issue of issues) {
      process.stderr.write(`- ${issue}\n`);
    }
    process.exit(1);
  }

  process.stdout.write(`[verify-i18n-keys] OK (readme=${readmeVerified.enKeys.length} keys, web=${webVerified.enKeys.length} keys, parity aligned)\n`);
} catch (error) {
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  process.stderr.write(`[verify-i18n-keys] Failed: ${message}\n`);
  process.exit(1);
}
