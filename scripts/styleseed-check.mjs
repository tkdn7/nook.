#!/usr/bin/env node
// Deterministic setup/baseline inspection. Read-only; does not certify native UI quality.
import { readFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, resolve, relative } from 'node:path';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const read=p=>readFileSync(resolve(root,p));
const json=p=>JSON.parse(read(p));
const hash=b=>createHash('sha256').update(b).digest('hex');
const failures=[]; const checks=[];
function check(name, ok, detail) { checks.push({name,status:ok?'pass':'fail',detail}); if(!ok)failures.push(name); }
const pin=json('.styleseed/install-lock.json');
for(const dir of pin.paths) {
  const actual=[];
  function walk(p){ for(const n of readdirSync(p)){const q=resolve(p,n);if(statSync(q).isDirectory())walk(q);else actual.push(q);} }
  walk(resolve(root,dir));
  const mismatches=actual.filter(p=>{const r=relative(resolve(root,dir),p).replaceAll('\\','/');return !pin.files[r] || hash(readFileSync(p))!==pin.files[r];});
  check('pinned skill '+dir,actual.length===Object.keys(pin.files).length&&!mismatches.length,`${actual.length} files; mismatches=${mismatches.length}`);
}
const tokens=json('.styleseed/nook/native-tokens.json');
check('native token source freshness',hash(read(tokens.authority))===tokens.sourceSha256,tokens.authority);
const theme=read(tokens.authority).toString('utf8');
const arrays=[...theme.matchAll(/new\[\]\s*\{([^}]+)\}/g)].map(m=>[...m[1].matchAll(/"(#[0-9A-Fa-f]{6})"/g)].map(x=>x[1]));
const names=[...theme.match(/string\[\] names = \[([\s\S]*?)\];/)[1].matchAll(/"([A-Za-z]+Brush)"/g)].map(m=>m[1]);
check('all light/dark semantic values',names.length===12&&['dark','light'].every((mode,i)=>names.every((n,j)=>tokens[mode][n]===arrays[i][j])),`${names.length} native roles, both themes`);
const compiler=resolve(root,'.agents/skills/styleseed/workflows/ss-resolve/scripts/resolve-context.mjs');
const briefIndex=json('.styleseed/briefs/index.json');
for(const {id} of json('.styleseed/artifacts/index.json').artifacts){
  const result=spawnSync(process.execPath,[compiler,'--project-root',root,'--agent','codex','--artifact',id,'--check'],{encoding:'utf8',timeout:30000});
  check('official compiler freshness '+id,result.status===0,(result.stderr||result.stdout||'compiled manifest matches').trim());
  const briefPath=`.styleseed/briefs/${id}.md`;
  const manifest=json(`.styleseed/manifests/${id}.json`);
  check('short production brief '+id,existsSync(resolve(root,briefPath))&&briefIndex[id].briefSha256===hash(read(briefPath))&&briefIndex[id].bundleSha256===manifest.bundle.sha256,`${read(briefPath).length} bytes; brief hash bound to current compiled bundle; selected artifact only`);
}
const folder='docs/design/2026-10-15-production/b0-media-reference/';
const png=read(folder+'media-mid-light.png');
check('Media approved PNG hash',hash(png)==='62c4ba0e4c4e3e9f0bdd299fa217b3a5ec8d441fd6eafac2a36f17fa9cb27a74',hash(png));
check('Media PNG dimensions',png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10]))&&png.readUInt32BE(16)===320&&png.readUInt32BE(20)===200,`${png.readUInt32BE(16)}x${png.readUInt32BE(20)}`);
const canvas=json(folder+'canvas.pen'); const frame=canvas.children.find(n=>n.id==='cUmvy');
check('saved Media canvas frame',!!frame&&frame.width===320&&frame.height===200&&frame.cornerRadius===16&&frame.padding===8&&frame.strokeAlignment==='inner','Stored file inspection only; no live Pencil reopen/export in setup');
const nodes=[];function visit(n){nodes.push(n);for(const c of n.children??[])visit(c);}visit(frame??{});
check('saved Media cover',nodes.some(n=>n.name==='Cover'&&n.width===76&&n.height===76&&n.cornerRadius===11),'saved source geometry');
check('saved Media time colors',['Elapsed','Remaining'].every(name=>nodes.some(n=>n.name===name&&n.fill===tokens.light.MutedBrush)),'both times use native Muted');
function luminance(hex){const a=hex.slice(1).match(/../g).map(v=>parseInt(v,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return a[0]*.2126+a[1]*.7152+a[2]*.0722;}
const l=[luminance(tokens.light.MutedBrush),luminance(tokens.light.SurfaceBrush)].sort((a,b)=>a-b);
const contrast=(l[1]+.05)/(l[0]+.05);
const report={setupStatus:failures.length?'fail':'pass',checks,openQualityIssues:[`Media muted/surface contrast ${contrast.toFixed(3)}:1; below 4.5:1 small-text target, unresolved`, 'Media baseline times imply 35%, visual fill 130/304=42.8%; functional correction pending','CPU/Clock visual transfer and WPF font/DPI/keyboard/state behavior unverified'],limitations:['Setup integrity/baseline checks only. No new design, human acceptance, StyleSeed score or native runtime pass claimed.','Generated StyleSeed palettes are advisory only; native ThemeService tokens remain authoritative.','Skill files/inventory do not alone prove running agent-host discovery.']};
console.log(JSON.stringify(report,null,2));process.exit(failures.length?1:0);
