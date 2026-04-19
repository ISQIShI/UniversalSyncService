#!/usr/bin/env node

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  buildPlan,
  docsI18nFallbackReportRelativePath,
  parseArgs,
  renderEntry,
  writeFallbackReport,
} from './generate-docs.mjs';

function toPosixPath(value) {
  return value.split(path.sep).join('/');
}

function buildArgView(args) {
  return `target=${args.target ?? '(all)'} locale=${args.locale ?? '(all)'}`;
}

async function verifyDocsI18n(options = {}) {
  const args = {
    target: options.target ?? null,
    locale: options.locale ?? null,
  };

  const { entries } = await buildPlan({ target: args.target });
  const activeEntries = args.locale
    ? entries.filter((entry) => entry.locale === args.locale)
    : entries;

  if (activeEntries.length === 0) {
    throw new Error(`未匹配到需要验证的 target/locale 组合。${buildArgView(args)}`);
  }

  const issues = [];
  const fallbackHits = [];

  for (const entry of activeEntries) {
    if (!entry.templateExists) {
      issues.push(`缺少模板: target=${entry.target.name} path=${toPosixPath(path.relative(process.cwd(), entry.target.templatePath))}`);
    }

    if (!entry.localeDirExists) {
      issues.push(`缺少 locale 目录: target=${entry.target.name} path=${toPosixPath(path.relative(process.cwd(), entry.target.localeDirPath))}`);
    }

    if (!entry.localeExists) {
      issues.push(`缺少 locale 资源: target=${entry.target.name} locale=${entry.locale} path=${toPosixPath(path.relative(process.cwd(), entry.localePath))}`);
    }
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

  for (const entry of activeEntries) {
    if (!entry.canGenerate) {
      continue;
    }

    const siblings = generatedEntriesByTarget.get(entry.target.name) ?? [entry];
    const renderResult = await renderEntry(entry, siblings);
    const expected = renderResult.content;
    fallbackHits.push(...renderResult.fallbackHits);

    let actual = null;
    try {
      actual = await readFile(entry.outputPath, 'utf8');
    } catch {
      actual = null;
    }

    if (actual === null) {
      issues.push(`缺少生成文档: target=${entry.target.name} locale=${entry.locale} output=${entry.outputRelativePath}`);
      continue;
    }

    if (actual !== expected) {
      issues.push(`生成文档与源不一致: target=${entry.target.name} locale=${entry.locale} output=${entry.outputRelativePath}`);
    }
  }

  const fallbackReport = await writeFallbackReport({
    source: 'verify-docs-i18n',
    target: args.target,
    locale: args.locale,
    fallbackHits,
    reportRelativePath: docsI18nFallbackReportRelativePath,
  });

  if (fallbackReport.fallbackHits.length > 0) {
    issues.push(`检测到 fallback 命中 ${fallbackReport.fallbackHits.length} 项（报告: ${fallbackReport.reportRelativePath}）`);
    for (const fallbackHit of fallbackReport.fallbackHits) {
      issues.push(`fallback hit: target=${fallbackHit.target} locale=${fallbackHit.locale} key=${fallbackHit.key}`);
    }
  }

  if (issues.length > 0) {
    process.stderr.write('[verify-docs-i18n] Verification failed:\n');
    for (const issue of issues) {
      process.stderr.write(`- ${issue}\n`);
    }
    process.exit(1);
  }

  process.stdout.write(
    `[verify-docs-i18n] OK (${activeEntries.length} target+locale combinations, ${buildArgView(args)}, report=${fallbackReport.reportRelativePath})\n`,
  );
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.check) {
    process.stdout.write('[verify-docs-i18n] --check 参数由 generate-docs.mjs 处理，此处忽略。\n');
  }

  await verifyDocsI18n(args);
}

main().catch((error) => {
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  process.stderr.write(`[verify-docs-i18n] Failed: ${message}\n`);
  process.exit(1);
});
