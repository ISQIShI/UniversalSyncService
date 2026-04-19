#!/usr/bin/env node

import { access, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
export const repoRoot = path.resolve(__dirname, '..');
export const targetsRoot = path.join(repoRoot, 'docs', 'i18n', 'targets');

const placeholderRegex = /{{\s*([a-zA-Z0-9._-]+)\s*}}/g;
const targetConfigFileName = 'target.config.json';
const fallbackLocaleCode = 'en';
export const docsI18nFallbackReportRelativePath = 'docs/i18n/reports/docs-i18n-fallback-report.json';

function toPosixPath(value) {
  return value.split(path.sep).join('/');
}

async function exists(filePath) {
  try {
    await access(filePath);
    return true;
  } catch {
    return false;
  }
}

function ensureObject(name, value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${name} 必须是 JSON 对象`);
  }
}

function parseStringOption(rawValue, fallbackValue) {
  if (typeof rawValue !== 'string') {
    return fallbackValue;
  }

  const trimmed = rawValue.trim();
  return trimmed.length > 0 ? trimmed : fallbackValue;
}

function getMainModulePath() {
  if (!process.argv[1]) {
    return null;
  }

  return pathToFileURL(path.resolve(process.argv[1])).href;
}

export function parseArgs(argv = process.argv.slice(2)) {
  const result = {
    check: false,
    target: null,
    locale: null,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];

    if (arg === '--check') {
      result.check = true;
      continue;
    }

    if (arg === '--target' && i + 1 < argv.length) {
      result.target = argv[i + 1];
      i += 1;
      continue;
    }

    if (arg.startsWith('--target=')) {
      result.target = arg.slice('--target='.length);
      continue;
    }

    if (arg === '--locale' && i + 1 < argv.length) {
      result.locale = argv[i + 1];
      i += 1;
      continue;
    }

    if (arg.startsWith('--locale=')) {
      result.locale = arg.slice('--locale='.length);
      continue;
    }

    throw new Error(`未知参数: ${arg}`);
  }

  return result;
}

function normalizeTargetConfig(targetDir, rawConfig) {
  ensureObject(`target config (${targetDir})`, rawConfig);

  const directoryName = path.basename(targetDir);
  const name = parseStringOption(rawConfig.target, directoryName);
  const templateRelativePath = parseStringOption(rawConfig.template, 'template.md');
  const localeDirRelativePath = parseStringOption(rawConfig.localeDir, 'locales');

  ensureObject(`${name}.outputs`, rawConfig.outputs);

  const outputs = Object.entries(rawConfig.outputs)
    .map(([locale, outputPath]) => {
      if (typeof outputPath !== 'string' || outputPath.trim().length === 0) {
        throw new Error(`target ${name} 的 locale 输出路径无效: ${locale}`);
      }

      return [locale, outputPath.trim()];
    })
    .sort(([leftLocale], [rightLocale]) => leftLocale.localeCompare(rightLocale, 'en'));

  if (outputs.length === 0) {
    throw new Error(`target ${name} 未定义任何 locale 输出映射`);
  }

  let languageLabels = {};
  if (rawConfig.languageLabels !== undefined) {
    ensureObject(`${name}.languageLabels`, rawConfig.languageLabels);
    languageLabels = { ...rawConfig.languageLabels };
  }

  return {
    name,
    directoryName,
    targetDir,
    templatePath: path.resolve(targetDir, templateRelativePath),
    localeDirPath: path.resolve(targetDir, localeDirRelativePath),
    outputs,
    languageLabels,
    topLanguageLinks: rawConfig.topLanguageLinks !== false,
  };
}

export async function loadTargets(baseDir = repoRoot) {
  const targetsDirectory = path.join(baseDir, 'docs', 'i18n', 'targets');
  if (!(await exists(targetsDirectory))) {
    return [];
  }

  const entries = await readdir(targetsDirectory, { withFileTypes: true });
  const targetDirs = entries
    .filter((entry) => entry.isDirectory())
    .map((entry) => path.join(targetsDirectory, entry.name))
    .sort((left, right) => left.localeCompare(right, 'en'));

  const targets = [];
  for (const targetDir of targetDirs) {
    const configPath = path.join(targetDir, targetConfigFileName);
    if (!(await exists(configPath))) {
      continue;
    }

    const configRaw = await readFile(configPath, 'utf8');
    const parsedConfig = JSON.parse(configRaw);

    const hasOutputs =
      parsedConfig
      && typeof parsedConfig === 'object'
      && !Array.isArray(parsedConfig)
      && parsedConfig.outputs
      && typeof parsedConfig.outputs === 'object'
      && !Array.isArray(parsedConfig.outputs);

    if (!hasOutputs) {
      const isNonDocTarget =
        parsedConfig
        && typeof parsedConfig === 'object'
        && !Array.isArray(parsedConfig)
        && typeof parsedConfig.output === 'string';

      if (isNonDocTarget) {
        continue;
      }

      throw new Error(`target config 缺少 outputs: ${normalizeOutputRelativePath(configPath)}`);
    }

    targets.push(normalizeTargetConfig(targetDir, parsedConfig));
  }

  targets.sort((left, right) => left.name.localeCompare(right.name, 'en'));
  return targets;
}

function normalizeOutputRelativePath(outputAbsolutePath) {
  return toPosixPath(path.relative(repoRoot, outputAbsolutePath));
}

function normalizeRenderableValue(value) {
  return typeof value === 'string' ? value : String(value);
}

function toFallbackHitKey(fallbackHit) {
  return `${fallbackHit.target}\u0000${fallbackHit.locale}\u0000${fallbackHit.key}`;
}

function sortFallbackHits(fallbackHits) {
  return [...fallbackHits].sort((left, right) => {
    const byTarget = left.target.localeCompare(right.target, 'en');
    if (byTarget !== 0) {
      return byTarget;
    }

    const byLocale = left.locale.localeCompare(right.locale, 'en');
    if (byLocale !== 0) {
      return byLocale;
    }

    return left.key.localeCompare(right.key, 'en');
  });
}

function deduplicateFallbackHits(fallbackHits) {
  const deduplicated = new Map();
  for (const fallbackHit of fallbackHits) {
    deduplicated.set(toFallbackHitKey(fallbackHit), fallbackHit);
  }

  return sortFallbackHits(Array.from(deduplicated.values()));
}

export async function writeFallbackReport({
  source,
  target,
  locale,
  fallbackHits,
  reportRelativePath = docsI18nFallbackReportRelativePath,
} = {}) {
  const normalizedFallbackHits = deduplicateFallbackHits(fallbackHits ?? []);
  const reportPath = path.resolve(repoRoot, reportRelativePath);
  await mkdir(path.dirname(reportPath), { recursive: true });

  const reportPayload = {
    generatedAt: new Date().toISOString(),
    source,
    filters: {
      target: target ?? null,
      locale: locale ?? null,
    },
    fallbackLocale: fallbackLocaleCode,
    fallbackHitCount: normalizedFallbackHits.length,
    fallbackHits: normalizedFallbackHits,
  };

  await writeFile(reportPath, `${JSON.stringify(reportPayload, null, 2)}\n`, 'utf8');

  return {
    reportPath,
    reportRelativePath: normalizeOutputRelativePath(reportPath),
    fallbackHits: normalizedFallbackHits,
  };
}

export async function buildPlan({ target = null } = {}) {
  const targets = await loadTargets(repoRoot);

  if (targets.length === 0) {
    throw new Error('未发现任何文档 target 配置，请检查 docs/i18n/targets/*/target.config.json');
  }

  const filteredTargets = target
    ? targets.filter((item) => item.name === target)
    : targets;

  if (filteredTargets.length === 0) {
    const available = targets.map((item) => item.name).join(', ');
    throw new Error(`未找到 target: ${target}。可用 target: ${available}`);
  }

  const entries = [];

  for (const targetConfig of filteredTargets) {
    const templateExists = await exists(targetConfig.templatePath);
    const localeDirExists = await exists(targetConfig.localeDirPath);

    for (const [locale, outputRelativePath] of targetConfig.outputs) {
      const localePath = path.join(targetConfig.localeDirPath, `${locale}.json`);
      const localeExists = localeDirExists && (await exists(localePath));
      const outputPath = path.resolve(repoRoot, outputRelativePath);

      entries.push({
        target: targetConfig,
        locale,
        templateExists,
        localeDirExists,
        localeExists,
        localePath,
        outputPath,
        outputRelativePath: normalizeOutputRelativePath(outputPath),
        canGenerate: templateExists && localeExists,
      });
    }
  }

  entries.sort((left, right) => {
    const byTarget = left.target.name.localeCompare(right.target.name, 'en');
    if (byTarget !== 0) {
      return byTarget;
    }

    return left.locale.localeCompare(right.locale, 'en');
  });

  return { targets: filteredTargets, entries };
}

function buildLanguageLinksLine(entry, siblingEntries) {
  const parts = siblingEntries.map((siblingEntry) => {
    const label = entry.target.languageLabels[siblingEntry.locale] ?? siblingEntry.locale;
    if (siblingEntry.locale === entry.locale) {
      return `**${label}**`;
    }

    const relativePath = path.relative(path.dirname(entry.outputPath), siblingEntry.outputPath);
    const href = toPosixPath(relativePath.length > 0 ? relativePath : path.basename(siblingEntry.outputPath));
    return `[${label}](${href})`;
  });

  return `> Languages: ${parts.join(' | ')}`;
}

export async function renderEntry(entry, siblingEntries) {
  const [templateRaw, localeRaw] = await Promise.all([
    readFile(entry.target.templatePath, 'utf8'),
    readFile(entry.localePath, 'utf8'),
  ]);

  const localeData = JSON.parse(localeRaw);
  ensureObject(`${entry.target.name}/${entry.locale}.json`, localeData);

  let fallbackData = localeData;
  if (entry.locale !== fallbackLocaleCode) {
    const fallbackLocalePath = path.join(entry.target.localeDirPath, `${fallbackLocaleCode}.json`);
    if (!(await exists(fallbackLocalePath))) {
      throw new Error(`target=${entry.target.name}, locale=${entry.locale} 缺少 fallback locale 文件: ${normalizeOutputRelativePath(fallbackLocalePath)}`);
    }

    const fallbackRaw = await readFile(fallbackLocalePath, 'utf8');
    fallbackData = JSON.parse(fallbackRaw);
    ensureObject(`${entry.target.name}/${fallbackLocaleCode}.json`, fallbackData);
  }

  const fallbackHits = [];
  const hardMissingKeys = new Set();
  const renderedBody = templateRaw.replace(placeholderRegex, (fullMatch, key) => {
    if (Object.prototype.hasOwnProperty.call(localeData, key)) {
      return normalizeRenderableValue(localeData[key]);
    }

    if (Object.prototype.hasOwnProperty.call(fallbackData, key)) {
      fallbackHits.push({
        target: entry.target.name,
        locale: entry.locale,
        key,
      });

      return normalizeRenderableValue(fallbackData[key]);
    }

    hardMissingKeys.add(key);
    return fullMatch;
  });

  if (hardMissingKeys.size > 0) {
    throw new Error(
      `target=${entry.target.name}, locale=${entry.locale} 缺少翻译键（locale 与 target-${fallbackLocaleCode} 均不存在）: ${Array.from(hardMissingKeys)
        .sort()
        .join(', ')}`,
    );
  }

  const renderedContent = entry.target.topLanguageLinks
    ? `${buildLanguageLinksLine(entry, siblingEntries)}\n\n${renderedBody}`
    : renderedBody;

  return {
    content: renderedContent,
    fallbackHits: deduplicateFallbackHits(fallbackHits),
  };
}

export async function runGenerateDocs({
  check = false,
  target = null,
  locale = null,
  logPrefix = 'generate-docs',
} = {}) {
  const { entries } = await buildPlan({ target });

  const activeEntries = locale
    ? entries.filter((entry) => entry.locale === locale)
    : entries;

  if (activeEntries.length === 0) {
    throw new Error(`未匹配到可处理的 target/locale 组合。target=${target ?? '(all)'} locale=${locale ?? '(all)'}`);
  }

  const generatedEntriesByTarget = new Map();
  for (const entry of entries) {
    if (!entry.canGenerate) {
      continue;
    }

    const bucket = generatedEntriesByTarget.get(entry.target.name);
    if (bucket) {
      bucket.push(entry);
    } else {
      generatedEntriesByTarget.set(entry.target.name, [entry]);
    }
  }

  for (const siblingEntries of generatedEntriesByTarget.values()) {
    siblingEntries.sort((left, right) => left.locale.localeCompare(right.locale, 'en'));
  }

  let hasDrift = false;
  const fallbackHits = [];

  for (const entry of activeEntries) {
    if (!entry.templateExists || !entry.localeDirExists || !entry.localeExists) {
      process.stdout.write(
        `[${logPrefix}] SKIP target=${entry.target.name} locale=${entry.locale} (template=${entry.templateExists ? 'ok' : 'missing'}, localeDir=${entry.localeDirExists ? 'ok' : 'missing'}, locale=${entry.localeExists ? 'ok' : 'missing'})\n`,
      );
      continue;
    }

    const siblings = generatedEntriesByTarget.get(entry.target.name) ?? [entry];
    const renderResult = await renderEntry(entry, siblings);
    const rendered = renderResult.content;
    fallbackHits.push(...renderResult.fallbackHits);

    if (check) {
      let existingContent = null;
      try {
        existingContent = await readFile(entry.outputPath, 'utf8');
      } catch {
        existingContent = null;
      }

      if (existingContent === null) {
        hasDrift = true;
        process.stderr.write(`[${logPrefix}] DRIFT: 文件不存在: ${entry.outputRelativePath} (target=${entry.target.name}, locale=${entry.locale})\n`);
        continue;
      }

      if (existingContent !== rendered) {
        hasDrift = true;
        process.stderr.write(`[${logPrefix}] DRIFT: 内容不匹配: ${entry.outputRelativePath} (target=${entry.target.name}, locale=${entry.locale})\n`);
        continue;
      }

      process.stdout.write(`[${logPrefix}] up-to-date: ${entry.outputRelativePath} (target=${entry.target.name}, locale=${entry.locale})\n`);
      continue;
    }

    await mkdir(path.dirname(entry.outputPath), { recursive: true });
    await writeFile(entry.outputPath, rendered, 'utf8');
    process.stdout.write(`[${logPrefix}] Generated: ${entry.outputRelativePath} (target=${entry.target.name}, locale=${entry.locale})\n`);
  }

  const fallbackReport = await writeFallbackReport({
    source: 'generate-docs',
    target,
    locale,
    fallbackHits,
  });

  if (fallbackReport.fallbackHits.length > 0) {
    process.stderr.write(
      `[${logPrefix}] 检测到 fallback 命中 ${fallbackReport.fallbackHits.length} 项，详见报告: ${fallbackReport.reportRelativePath}\n`,
    );

    for (const fallbackHit of fallbackReport.fallbackHits) {
      process.stderr.write(
        `[${logPrefix}] fallback hit: target=${fallbackHit.target} locale=${fallbackHit.locale} key=${fallbackHit.key}\n`,
      );
    }
  } else {
    process.stdout.write(`[${logPrefix}] fallback report: ${fallbackReport.reportRelativePath} (0 hit)\n`);
  }

  return {
    hasDrift,
    hasFallbackHits: fallbackReport.fallbackHits.length > 0,
    fallbackHits: fallbackReport.fallbackHits,
    reportRelativePath: fallbackReport.reportRelativePath,
  };
}

async function main() {
  const args = parseArgs();
  const result = await runGenerateDocs(args);
  if (args.check) {
    process.exit(result.hasDrift || result.hasFallbackHits ? 1 : 0);
  }
}

if (getMainModulePath() === import.meta.url) {
  main().catch((error) => {
    const message = error instanceof Error ? error.stack ?? error.message : String(error);
    process.stderr.write(`[generate-docs] Failed: ${message}\n`);
    process.exit(1);
  });
}
