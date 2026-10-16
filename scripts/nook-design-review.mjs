#!/usr/bin/env node
// Read-only provenance/input pack for the Nook visual-review adapter. No LLM, writes or engine.
import {readFileSync,readdirSync,statSync,realpathSync} from 'node:fs';
import {resolve,dirname,relative,isAbsolute,sep} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
const root=realpathSync(resolve(dirname(fileURLToPath(import.meta.url)),'..'));
const hash=b=>createHash('sha256').update(b).digest('hex');
function inside(value){
 if(!value||isAbsolute(value))throw Error('Use a project-relative target path');
 const p=realpathSync(resolve(root,value));const rel=relative(root,p);
 if(!rel||rel==='..'||rel.startsWith('..'+sep)||isAbsolute(rel)||!statSync(p).isFile())throw Error('Target must be a file inside this project');
 return p;
}
const read=p=>readFileSync(inside(p));const json=p=>JSON.parse(read(p));
try{
 const args=process.argv.slice(2);let options={};
 for(let i=0;i<args.length;i++){
  if(args[i]==='--verify'){options.verify=true;continue;}
  if(!['--artifact','--render','--spec'].includes(args[i])||!args[i+1]||args[i+1].startsWith('--'))throw Error('Use --verify or --artifact media|cpu|clock --render project.png --spec project.md');
  options[args[i].slice(2)]=args[++i];
 }
 const lock=json('.impeccable/install-lock.json');let count=0;
 for(const [p,expected] of Object.entries(lock.files)){
  const actual=hash(read(lock.vendorPath+'/'+p));if(actual!==expected)throw Error('Pinned Impeccable file drift: '+p);count++;
 }
 const registry=json('.styleseed/artifacts/index.json');const index=json('.styleseed/briefs/index.json');
 const artifacts={};
 for(const {id} of registry.artifacts){
  const briefPath=`.styleseed/briefs/${id}.md`,manifestPath=`.styleseed/manifests/${id}.json`;
  const manifest=json(manifestPath);const briefSha=hash(read(briefPath));
  if(index[id]?.briefSha256!==briefSha||index[id]?.bundleSha256!==manifest.bundle.sha256)throw Error('StyleSeed brief/manifest binding drift: '+id);
  if('sha256:'+hash(read(manifest.bundle.path))!==manifest.bundle.sha256)throw Error('StyleSeed bundle output drift: '+id);
  artifacts[id]={briefPath,briefSha256:briefSha,manifestPath,bundleSha256:manifest.bundle.sha256};
 }
 const verification={status:'pass',commit:lock.commit,vendorFiles:count,activeSkill:lock.activeSkill,artifacts,mode:'Nook adapter; read-only proposals; no official full critique/detector claim'};
 if(options.verify&&!options.artifact){console.log(JSON.stringify(verification,null,2));process.exit(0);}
 if(!options.artifact||!artifacts[options.artifact]||!options.render||!options.spec)throw Error('Choose one registered artifact, render PNG and actual spec');
 if(!options.render.toLowerCase().endsWith('.png')||!options.spec.toLowerCase().endsWith('.md'))throw Error('Expected PNG render and Markdown spec');
 const png=read(options.render),spec=read(options.spec);
 if(png.length<24||!png.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])))throw Error('Invalid PNG header');
 const width=png.readUInt32BE(16),height=png.readUInt32BE(20);if(!width||!height)throw Error('Invalid PNG dimensions');
 console.log(JSON.stringify({...verification,artifact:options.artifact,render:{path:options.render,sha256:hash(png),width,height},spec:{path:options.spec,sha256:hash(spec)},freshness:'NOT established by hashes: reviewer must match current assigned source/spec/theme/state and open the actual image.',limits:'Proposals only. No source/canvas/rule edits, extra reviewers, automatic polishing or native UI pass. Existing approval and QA gates remain.'},null,2));
}catch(error){console.error(error.message);process.exit(2);}
