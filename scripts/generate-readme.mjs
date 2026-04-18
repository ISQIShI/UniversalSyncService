#!/usr/bin/env node

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');

const supportedLocales = ['en', 'zh-CN'];
const templatePath = path.join(repoRoot, 'docs', 'i18n', 'README.template.md');

const placeholderRegex = /{{\s*([a-zA-Z0-9._-]+)\s*}}/g;

function parseArgs() {
  const args = process.argv.slice(2);
  const result = {
    locale: null,
    check: false,
  };

  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === '--check') {
      result.check = true;
    } else if (arg === '--locale' && i + 1 < args.length) {
      result.locale = args[i + 1];
      i++;
    } else if (arg.startsWith('--locale=')) {
      result.locale = arg.slice('--locale='.length);
    }
  }

  return result;
}

async function renderReadme(locale) {
  const localePath = path.join(repoRoot, 'docs', 'i18n', 'locales', `${locale}.json`);
  const [templateRaw, localeRaw] = await Promise.all([
    readFile(templatePath, 'utf8'),
    readFile(localePath, 'utf8'),
  ]);

  const localeData = JSON.parse(localeRaw);

  const missingKeys = new Set();
  const rendered = templateRaw.replace(placeholderRegex, (fullMatch, key) => {
    if (!Object.prototype.hasOwnProperty.call(localeData, key)) {
      missingKeys.add(key);
      return fullMatch;
    }

    const value = localeData[key];
    return typeof value === 'string' ? value : String(value);
  });

  if (missingKeys.size > 0) {
    throw new Error(`模板占位符缺少翻译键: ${Array.from(missingKeys).sort().join(', ')}`);
  }

  return rendered;
}

async function generateForLocale(locale, outputPath) {
  const rendered = await renderReadme(locale);
  await mkdir(path.dirname(outputPath), { recursive: true });
  await writeFile(outputPath, rendered, 'utf8');
  return outputPath;
}

async function checkDrift(locale, expectedPath) {
  const rendered = await renderReadme(locale);
  let existing;
  try {
    existing = await readFile(expectedPath, 'utf8');
  } catch {
    return { drift: true, message: `文件不存在: ${expectedPath}` };
  }

  if (existing !== rendered) {
    return { drift: true, message: `内容不匹配: ${expectedPath}` };
  }

  return { drift: false, message: `up-to-date: ${expectedPath}` };
}

async function main() {
  const args = parseArgs();

  if (args.locale && !supportedLocales.includes(args.locale)) {
    process.stderr.write(`[generate-readme] Error: unsupported locale "${args.locale}". Supported: ${supportedLocales.join(', ')}\n`);
    process.exit(1);
  }

  const targets = args.locale
    ? [{ locale: args.locale, path: path.join(repoRoot, 'README.md') }]
    : [
        { locale: 'en', path: path.join(repoRoot, 'README.md') },
        { locale: 'zh-CN', path: path.join(repoRoot, 'docs', 'README.zh-CN.md') },
      ];

  if (args.check) {
    let hasDrift = false;
    for (const target of targets) {
      const result = await checkDrift(target.locale, target.path);
      if (result.drift) {
        hasDrift = true;
        process.stderr.write(`[generate-readme] DRIFT: ${result.message}\n`);
      } else {
        process.stdout.write(`[generate-readme] ${result.message}\n`);
      }
    }
    process.exit(hasDrift ? 1 : 0);
  }

  for (const target of targets) {
    const outPath = await generateForLocale(target.locale, target.path);
    process.stdout.write(`[generate-readme] Generated: ${path.relative(repoRoot, outPath)} (${target.locale})\n`);
  }
}

main().catch((error) => {
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  process.stderr.write(`[generate-readme] Failed: ${message}\n`);
  process.exit(1);
});
