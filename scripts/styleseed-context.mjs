#!/usr/bin/env node
// Project wrapper: unchanged pinned official compiler, consistent execution context.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
if (args.some((arg, i) => !['--artifact','--all','--check','--stdout'].includes(arg) && !(i > 0 && args[i-1] === '--artifact' && ['media','cpu','clock'].includes(arg)))) {
  console.error('Use --artifact media|cpu|clock or --all; optional --check / --stdout.'); process.exit(2);
}
const result = spawnSync(process.execPath, [resolve(root,'.agents/skills/styleseed/workflows/ss-resolve/scripts/resolve-context.mjs'),'--project-root',root,'--agent','codex',...args], {stdio:'inherit'});
if (result.error) console.error(result.error.message);
process.exit(result.status ?? 2);
