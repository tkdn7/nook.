"""Cheap local Markdown link/source integrity check; no model or network call."""
from pathlib import Path
import re,json,hashlib,sys
R=Path(__file__).resolve().parent.parent
vault=R/'docs/wiki';errors=[]
for p in vault.rglob('*.md'):
    if 'raw' in p.relative_to(vault).parts:continue
    content=p.read_text(encoding='utf-8-sig')
    links=re.findall(r'(?<!!)\[[^\]]*\]\(([^)]+)\)',content)
    if content.startswith('---\n'):
        front=content.split('---',2)[1]
        block=re.search(r'^sources:\s*\n((?:[ \t]+-.*\n?)*)',front,re.M)
        if block:
            links += [line.strip().removeprefix('-').strip().strip('\"\'') for line in block[1].splitlines()]
    for link in links:
        link=link.split('#')[0].strip('<>')
        if not link or re.match(r'[a-z]+://',link):continue
        if not (p.parent/link).exists():errors.append({'page':str(p.relative_to(R)),'missing':link})
manifest=json.loads((R/'docs/ai/migration/legacy-wiki-manifest.json').read_text(encoding='utf-8'))
external=R/'docs/ai/migration/external-source-manifest.json'
extra=json.loads(external.read_text(encoding='utf-8')) if external.exists() else []
history=vault/'raw/retired-operations/manifest.json'
local_history=json.loads(history.read_text(encoding='utf-8')) if history.exists() else []
for m in manifest+extra+local_history:
    p=R/m['snapshot']
    if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=m['sha256']:errors.append({'raw_integrity':m['snapshot']})
print(json.dumps({'raw_sources':len(manifest),'retired_operating_sources':len(local_history),'errors':errors,'ok':not errors},ensure_ascii=False,indent=2))
sys.exit(bool(errors))
