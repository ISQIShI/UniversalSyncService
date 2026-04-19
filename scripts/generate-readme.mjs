#!/usr/bin/env node

import { parseArgs, runGenerateDocs } from './generate-docs.mjs';

function mapReadmeArgs(rawArgs) {
  const args = parseArgs(rawArgs);

  if (args.target && args.target !== 'readme') {
    throw new Error(`generate-readme 兼容入口不支持 target=${args.target}，仅支持 readme`);
  }

  return {
    check: args.check,
    target: 'readme',
    locale: args.locale,
    logPrefix: 'generate-readme',
  };
}

async function main() {
  const options = mapReadmeArgs(process.argv.slice(2));
  const result = await runGenerateDocs(options);
  if (options.check) {
    process.exit(result.hasDrift ? 1 : 0);
  }
}

main().catch((error) => {
  const message = error instanceof Error ? error.stack ?? error.message : String(error);
  process.stderr.write(`[generate-readme] Failed: ${message}\n`);
  process.exit(1);
});
