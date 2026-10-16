"""Check captured resolved OpenCode rules; never executes sensitive operations."""
from pathlib import Path
import re,json,sys
R=Path(__file__).resolve().parents[1]
def match(pattern,value):
    return re.fullmatch(re.escape(pattern).replace(r'\*','.*').replace(r'\?','.'),value,re.S) is not None
def evaluate(rules,tool,value):
    action='ask'
    for r in rules:
        if match(r['permission'],tool) and match(r.get('pattern','*'),value):action=r['action']
    return action
def main():
    path=R/'docs/ai/migration/v5/resolved-opencode-permissions.json'
    roles=json.loads(path.read_text(encoding='utf-8'));results=[]
    cases=[('bash','Where-Object { $_.Name -like "*.md" }','allow'),
           ('bash','ForEach-Object { Get-Content $_.FullName -Raw }','allow'),
           ('bash','python scripts/team_bus.py finish','allow'),
           ('bash','Start-Sleep -Seconds 1','allow'),('bash','orca --help 2>&1','allow'),
           ('bash','node --version','allow'),('bash','pwsh -NoProfile -File scripts/render.ps1','allow'),
           ('bash','& "'+str(Path.home()/'AppData/Local/Programs/Python/Python313/python.exe')+'" -c "print(6382)"','allow'),
           ('bash','git push origin main','ask'),('bash','Remove-Item -Recurse src','ask'),
           ('bash','python -c "import os; os.remove(\'src/data\')"','ask'),
           ('bash','python scripts/opaque.py','allow'),
           ('bash','orca terminal close --terminal sample','ask'),
           ('bash','powershell -Command "Remove-Item src/data"','ask'),
           ('bash','git reset --hard HEAD','ask'),('bash','npm install package','ask'),
           ('bash','unrecognized-executable do-something','ask'),
           ('read','project.env','deny'),('edit','opencode.json','ask')]
    for role in roles:
        rules=role['permission']
        for tool,value,want in cases:
            got=evaluate(rules,tool,value)
            results.append({'role':role['name'],'tool':tool,'input':value,'expected':want,'actual':got,'ok':got==want})
        if role['name']=='nook-wiki':
            for value,want in [('docs/wiki/topics/test.md','allow'),('docs/wiki/raw/karpathy-llm-wiki.md','deny'),((R/'docs/wiki/raw/karpathy-llm-wiki.md').as_posix(),'deny')]:
                got=evaluate(rules,'edit',value);results.append({'role':role['name'],'input':value,'expected':want,'actual':got,'ok':got==want})
    output={'ok':all(r['ok'] for r in results),'cases':results,
            'limitations':'Static rule evaluation is not arbitrary-script side-effect analysis. Python/Node can perform destructive actions despite interpreter allow; user-approved scope and script review remain required. Never treat this as an OS sandbox.'}
    (R/'docs/ai/migration/v5/permission-evaluation.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'ok':output['ok'],'cases':len(results),'failures':[r for r in results if not r['ok']]},ensure_ascii=False))
    return not output['ok']
if __name__=='__main__':sys.exit(main())
