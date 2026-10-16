import os from 'node:os';
import path from 'node:path';
import {pathToFileURL} from 'node:url';
const url=pathToFileURL(path.join(os.homedir(),'Documents/Codex/cli-tools/nook-codex-effort.mjs'));
try { const {main}=await import(url.href); main(process.argv.slice(2)); }
catch(e) { console.error(JSON.stringify({verified:false,error:e.message})); process.exitCode=1; }
