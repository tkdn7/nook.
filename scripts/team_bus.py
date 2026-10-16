"""Durable, event-driven team reporting; never settles native Orca work."""
from pathlib import Path
import argparse, datetime, hashlib, json, os, re, subprocess, sys, uuid, fnmatch, time
from contextlib import contextmanager

ROOT = Path(__file__).resolve().parent.parent
STATE = ROOT / 'docs/ai/runtime'
ORCA = Path.home() / 'AppData/Local/Programs/orca/resources/bin/orca.exe'
ROLES = ('pm', 'tl', 'secretary', 'designer', 'assistant', 'wiki')

def read(p, default=None):
    try: return json.loads(p.read_text(encoding='utf-8-sig'))
    except FileNotFoundError: return default

def write(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    tmp = p.with_name(p.name + '.' + uuid.uuid4().hex + '.tmp')
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    os.replace(tmp, p)

def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def safe_id(value):
    if not re.fullmatch(r'[a-zA-Z0-9_-]{1,200}', value): raise ValueError('invalid local task/event ID')
    return value

@contextmanager
def mutation_lock():
    STATE.mkdir(parents=True, exist_ok=True)
    path=STATE/'mutation.lock'
    deadline=time.monotonic()+5
    while True:
        try:
            fd=os.open(path, os.O_CREAT|os.O_EXCL|os.O_WRONLY);break
        except FileExistsError:
            if time.monotonic()>=deadline:raise ValueError('another local mutation or interrupted lock; inspect mutation.lock, do not bypass')
            time.sleep(.05)
    try:
        os.write(fd,json.dumps({'pid':os.getpid(),'at':now()}).encode());os.close(fd)
        yield
    finally: path.unlink(missing_ok=True)

def run_cli(args):
    p = subprocess.run([str(ORCA), *args, '--json'], capture_output=True, text=True,
                       encoding='utf-8', errors='replace', timeout=20)
    try: receipt=json.loads(p.stdout)
    except ValueError: receipt={'ok':False,'error':'unreadable receipt','exit':p.returncode}
    return receipt

def notify(event):
    team = read(STATE/'team.json', {})
    dest = event.get('coordinator') or team.get('secretary', {}).get('handle')
    if not dest: return {'ok':False,'error':'missing coordinator handle; event preserved'}
    # Validate live identity rather than injecting into a stale or recycled terminal.
    fleet = run_cli(['terminal','list','--worktree','path:'+str(ROOT)])
    live = {x['handle']:x for x in fleet.get('result',{}).get('terminals',[])}
    if dest not in live: return {'ok':False,'error':'coordinator handle not live; event preserved'}
    expected = team.get('secretary',{}).get('handle')
    if dest == expected and live[dest].get('agentIdentity') != 'opencode':
        return {'ok':False,'error':'coordinator agent identity mismatch'}
    message = (f"TEAM_EVENT {event['id']} {event['role']} {event['task']} {event['status']}. "
               f"Read docs/ai/runtime/outbox/{event['id']}.json; verify evidence, process owning task, "
               "record structured ack/release, then execute saved follow-up; run team_bus finish before ending. "
               "This is a notification, not proof of task success. Do not reply to this event with another event.")
    if event['role']=='secretary':
        message='CONTINUATION_CHECK: run python scripts/team_bus.py finish; process unresolved result/follow-up/ready work before ending. This is a deduplicated operational notice, not a task. Do not create a report in reply.'
    return run_cli(['terminal','send','--terminal',dest,'--text',message,'--enter','--wait-submit','2'])

def report(role, task, status, summary, artifacts, checks, wiki_delta='', next_owner='', notify_enabled=True, event_key=''):
    safe_id(task)
    card=read(STATE/'assignments'/f'{role}.json', {})
    if card.get('task') != task: raise ValueError('task not registered for this role')
    if card.get('state') == 'acked': return {'duplicate':True,'reason':'already acknowledged'}
    paths=[]
    for name in artifacts:
        p=(ROOT/name).resolve() if not Path(name).is_absolute() else Path(name).resolve()
        # Project and explicitly assigned external output paths only.
        allowed=[ROOT.resolve(),*[Path(x).resolve() for x in card.get('external_roots',[])]]
        if not any(p.is_relative_to(base) for base in allowed): raise ValueError('artifact outside assigned roots')
        if not p.is_file(): raise ValueError('missing artifact: '+str(p))
        paths.append({'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    if status=='completed_pending_review' and not paths: raise ValueError('completed result requires a real artifact')
    signature=hashlib.sha256(json.dumps([role,task,status,summary,paths,checks,wiki_delta,next_owner,event_key],sort_keys=True,ensure_ascii=False).encode()).hexdigest()[:24]
    eid=f'{task}-{signature}'
    path=STATE/'outbox'/f'{eid}.json'
    if path.exists(): return {'duplicate':True,'event':eid,'path':str(path)}
    event={'schema':2,'id':eid,'at':now(),'role':role,'task':task,'status':status,'summary':summary[:3000],
           'artifacts':paths,'checks':checks,'wiki_delta':wiki_delta[:3000],'next_owner':next_owner,
           'coordinator':card.get('coordinator'),'native':card.get('native'),
           'notice':'Agent-reported outcome; requires coordinator evidence review. No native settlement performed.'}
    write(path,event)
    receipt=notify(event) if notify_enabled else {'dry_run':True}
    write(STATE/'receipts'/f'{eid}.json',receipt)
    return {'event':eid,'path':str(path),'notification':receipt}

def hook(role,payload,notify_enabled=True):
    if role=='secretary':
        state=finish()
        if state['can_finish']: return {'skipped':'coordinator clear'}
        if not (state['pending_events'] or state['unresolved_followups'] or state['ready_tasks'] or state['recovery_required']):
            return {'skipped':'awaiting assigned dependency; no actionable idle nudge','finish':state}
        digest=hashlib.sha256(json.dumps(state,sort_keys=True,ensure_ascii=False).encode()).hexdigest()[:20]
        marker=STATE/'coordinator-notices'/f'{digest}.json'
        if marker.exists(): return {'skipped':'same unresolved state already notified','finish':state}
        write(marker,{'at':now(),'finish':state})
        team=read(STATE/'team.json',{})
        notice={'id':'coordinator-'+digest,'role':'secretary','task':'finish-check',
                'status':'continuation_required','coordinator':team.get('secretary',{}).get('handle')}
        receipt=notify(notice) if notify_enabled else {'dry_run':True}
        write(marker,{'at':now(),'finish':state,'receipt':receipt})
        return {'finish':state,'notification':receipt}
    card=read(STATE/'assignments'/f'{role}.json',{})
    if not card or card.get('state')!='active' or role=='secretary': return {'skipped':'no active assignment'}
    # An explicit result suppresses idle fallback. Never synthesize success from a stopped turn.
    for path in (STATE/'outbox').glob(card['task']+'-*.json'):
        ev=read(path,{})
        if ev.get('status') in ('completed_pending_review','blocked','failed','needs_reconciliation'):
            return {'skipped':'result already recorded'}
    if payload.get('stop_hook_active'): return {'skipped':'recursive stop'}
    summary='Assigned agent turn ended without a durable result; coordinator must inspect owner/artifacts before any settlement.'
    return report(role,card['task'],'needs_reconciliation',summary,[],[],notify_enabled=notify_enabled)

def rows(folder):
    return [read(p) for p in sorted((STATE/folder).glob('*.json'))]

def pending():
    return [e for e in rows('outbox') if not (STATE/'acks'/f"{e['id']}.json").exists()]

def check_card(card):
    safe_id(card['task'])
    if len(card['task'])>100: raise ValueError('local task IDs are limited to 100 characters')
    for key in ['goal','approval','coordinator','owned_files','acceptance','next_owner']:
        if not card.get(key): raise ValueError('missing/empty card field '+key)
    if not isinstance(card['owned_files'],list): raise ValueError('owned_files must be concrete path list')
    for path in card['owned_files']:
        if not isinstance(path,str) or '(' in path or '..' in Path(path).parts:
            raise ValueError('owned_files must be actual paths/globs, not prose')
    return card

def completed_tasks():
    result=set()
    for ack in rows('acks'):
        if ack.get('outcome')=='accepted':
            event=read(STATE/'outbox'/f"{ack['event']}.json",{})
            if event: result.add(event['task'])
    return result

def assign_card(role,card):
    check_card(card)
    old=read(STATE/'assignments'/f'{role}.json',{})
    if old.get('task')==card['task']:
        if old.get('state')=='active': return old
        raise ValueError('task ID already used; use a new task ID')
    if old.get('state')=='active': raise ValueError('role already active; review/ack first')
    if set(card.get('dependencies',[]))-completed_tasks(): raise ValueError('dependencies not accepted')
    def norm(p): return str((ROOT/p).resolve()).replace('\\','/').lower()
    for other in rows('assignments'):
        if other.get('state')!='active' or other.get('role')==role: continue
        for a in card['owned_files']:
            for b in other.get('owned_files',[]):
                a,b=norm(a),norm(b)
                wildcard_overlap=('*' in a and '*' in b and (a.split('*')[0].startswith(b.split('*')[0]) or b.split('*')[0].startswith(a.split('*')[0])))
                if fnmatch.fnmatchcase(a,b) or fnmatch.fnmatchcase(b,a) or wildcard_overlap:
                    raise ValueError('owned path overlaps active '+other['role'])
    card=dict(card,role=role,state='active',assigned_at=now())
    write(STATE/'assignments'/f'{role}.json',card)
    return card

def reconcile_ack(ack):
    event=read(STATE/'outbox'/f"{ack['event']}.json")
    path=STATE/'assignments'/f"{event['role']}.json";card=read(path,{})
    if card.get('task')==event['task'] and card.get('state')=='active':
        card.update(state='acked',acked_at=ack['at']);write(path,card)
    if event.get('wiki_delta'):
        dest=STATE/'wiki-inbox'/f"{event['id']}.json"
        if not dest.exists():
            write(dest,{'event':event['id'],'delta':event['wiki_delta'],'artifacts':event['artifacts'],
                        'coordinator_review':ack['resolution'],
                        'status':'already_compiled_by_wiki' if event['role']=='wiki' else 'candidate'})

def acknowledge(event_id,resolution,outcome,action,next_card=None,gate=None,reason=None):
    event=read(STATE/'outbox'/f'{safe_id(event_id)}.json')
    if not event: raise ValueError('event missing')
    dest=STATE/'acks'/f'{event_id}.json'
    prior=read(dest)
    if prior:
        if prior.get('schema')==2: reconcile_ack(prior)
        return {'duplicate':True,'acked':event_id,'record':prior}
    if event.get('schema')!=2:
        # New code can process an old unacked event, but never rewrites historical acks.
        pass
    if not outcome or not action: raise ValueError('new ack requires outcome and action')
    if not resolution.strip(): raise ValueError('evidence review resolution required')
    if outcome=='accepted' and event['status']!='completed_pending_review':
        raise ValueError('blocker/missing report is not a success')
    if action=='complete' and outcome!='accepted': raise ValueError('only accepted result can complete goal')
    for item in event.get('artifacts',[]):
        p=Path(item['path'])
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=item['sha256']:
            raise ValueError('artifact changed/missing; obtain fresh result before ack')
    follow={'kind':action,'state':'pending' if action in ('dispatch','rework') else 'resolved'}
    if action in ('dispatch','rework'):
        if not next_card: raise ValueError('dispatch/rework needs --next-card')
        card=check_card(read(Path(next_card)));role=card.get('role')
        if role not in ROLES or role=='secretary': raise ValueError('next card needs worker role')
        if card['task']==event['task']: raise ValueError('follow-up needs new task ID')
        if card.get('native'): raise ValueError('native follow-up requires native gate, not terminal injection')
        if not card.get('terminal'): raise ValueError('next card requires verified terminal handle')
        follow.update(card=card) # embed snapshot: no dependency on mutable card file after commit
    elif action=='wait':
        if gate not in ('dependency','human','external','pause') or not reason or not reason.strip():
            raise ValueError('wait needs named gate and concrete reason/evidence')
        if event['status']=='needs_reconciliation' and gate!='external':
            raise ValueError('missing report requires investigation, not routine waiting')
        follow.update(gate=gate,reason=reason)
    record={'schema':2,'event':event_id,'at':now(),'resolution':resolution,'outcome':outcome,
            'followup':follow,'note':'local evidence processing only; not native settlement'}
    write(dest,record) # write-ahead commit: repair release/staging on retry or next mutation
    reconcile_ack(record)
    return {'acked':event_id,'record':record}

def repair_acks():
    for a in rows('acks'):
        if a.get('schema')==2: reconcile_ack(a)

def receipt_accepted(receipt):
    result=receipt.get('result',{})
    return receipt.get('ok') is True and result.get('send',result).get('accepted') is True

def dispatch(event_id,retry_request=None):
    path=STATE/'acks'/f'{safe_id(event_id)}.json';ack=read(path)
    if not ack or ack.get('schema')!=2: raise ValueError('structured ack required')
    follow=ack['followup']
    if follow['kind'] not in ('dispatch','rework'): raise ValueError('no dispatch follow-up')
    if follow['state']=='submitted': return {'duplicate':True,'followup':follow}
    if follow['state']=='attempting' and not retry_request:
        raise ValueError('submission may have occurred; inspect receipt, never blindly resend')
    if retry_request:
        result=follow.get('receipt',{}).get('result',{})
        known=result.get('send',{}).get('prompt',{}).get('requestId') or result.get('mutation',{}).get('requestId') or result.get('requestId')
        if not known or known!=retry_request: raise ValueError('retry ID must match saved accepted request')
    card=follow['card'];role=card['role']
    fleet=run_cli(['terminal','list','--worktree','path:'+str(ROOT)])
    live={x['handle']:x for x in fleet.get('result',{}).get('terminals',[])}
    if card['terminal'] not in live: raise ValueError('target terminal not live; follow-up preserved')
    expected={'pm':'codex','tl':'claude','wiki':'opencode','designer':'opencode','assistant':'opencode'}[role]
    if live[card['terminal']].get('agentIdentity')!=expected: raise ValueError('terminal identity mismatch')
    if card.get('coordinator')!=read(STATE/'outbox'/f'{event_id}.json')['coordinator']:
        raise ValueError('cannot transfer coordinator authority')
    saved=assign_card(role,card)
    prompt=(f"Assigned local task {card['task']}; follow current docs/AI_COLLABORATION.md and COMMUNICATION.md. "
            f"Read docs/ai/runtime/assignments/{role}.json for goal, owned files, acceptance and report route. "
            "Only do this finite task, verify it and send team_bus report before idling. No native Dispatch was created. "
            "Do not create meetings, mandatory extra report documents or wiki deltas for routine work.")
    args=['terminal','send','--terminal',card['terminal'],'--text',prompt,'--enter','--wait-submit','3']
    if retry_request: args+=['--retry-request',retry_request]
    follow.update(state='attempting',attempted_at=now());write(path,ack)
    try: receipt=run_cli(args)
    except Exception as e:
        follow['error']=str(e);write(path,ack);raise
    follow['receipt']=receipt
    follow['state']='submitted' if receipt_accepted(receipt) else 'attempting'
    follow['submitted_at']=now() if follow['state']=='submitted' else None
    write(path,ack)
    return {'task':saved['task'],'followup':follow}

def ready_cards():
    done=completed_tasks();assigned={c.get('task') for c in rows('assignments')}
    processed={e['task'] for e in rows('outbox') if (STATE/'acks'/f"{e['id']}.json").exists()}
    active={c.get('role') for c in rows('assignments') if c.get('state')=='active'}
    return [c for c in rows('cards') if c.get('queued') is True and c.get('approval')
            and c.get('task') not in assigned|done|processed and c.get('role') not in active
            and not set(c.get('dependencies',[]))-done and not c.get('gate')]

def finish():
    unacked=[e['id'] for e in pending()]
    actions=[{'event':a['event'],'state':a['followup']['state'],'kind':a['followup']['kind']}
             for a in rows('acks') if a.get('schema')==2 and a['followup']['state'] not in ('resolved','submitted')]
    ready=[c['task'] for c in ready_cards()]
    active=[{'role':c.get('role'),'task':c['task'],'coordinator':c.get('coordinator')}
            for c in rows('assignments') if c.get('state')=='active']
    recovery=[]
    for a in rows('acks'):
        if a.get('schema')!=2:continue
        e=read(STATE/'outbox'/f"{a['event']}.json",{})
        card=read(STATE/'assignments'/f"{e.get('role')}.json",{})
        if (card.get('task')==e.get('task') and card.get('state')=='active') or (e.get('wiki_delta') and not (STATE/'wiki-inbox'/f"{a['event']}.json").exists()):
            recovery.append(a['event'])
    return {'can_finish':not (unacked or actions or ready or active or recovery),'pending_events':unacked,
            'unresolved_followups':actions,'ready_tasks':ready,'active_tasks':active,
            'recovery_required':recovery,
            'notice':'Active work is a dependency checkpoint, not proof of completion. Preserve native ownership.'}

def wait_for_event(timeout):
    if timeout<0 or timeout>60: raise ValueError('wait timeout must be between 0 and 60 seconds')
    deadline=time.monotonic()+timeout
    while True:
        state=finish()
        if state['pending_events'] or state['unresolved_followups'] or state['ready_tasks'] or state['recovery_required']:
            return {'event_available':True,'finish':state}
        if not state['active_tasks']:return {'event_available':False,'finish':state}
        if time.monotonic()>=deadline:return {'event_available':False,'checkpoint':'awaiting actual worker event; idle with named dependency instead of repeated unchanged waits','finish':state}
        time.sleep(min(.25,max(0,deadline-time.monotonic())))

def wiki_decide(event_id,decision,reason,pages):
    path=STATE/'wiki-inbox'/f'{safe_id(event_id)}.json';candidate=read(path)
    if not candidate: raise ValueError('candidate missing')
    if candidate.get('status') in ('promoted','excluded','already_compiled_by_wiki'):
        return {'duplicate':True,'candidate':candidate}
    if not reason or not reason.strip(): raise ValueError('curation reason required')
    checked=[]
    for name in pages:
        p=(ROOT/name).resolve()
        if not p.is_relative_to((ROOT/'docs/wiki').resolve()) or not p.is_file() or 'raw' in p.relative_to(ROOT/'docs/wiki').parts:
            raise ValueError('promoted page must be actual compiled wiki file')
        checked.append(str(p))
    if decision=='promoted' and not checked: raise ValueError('promotion needs actual --page')
    candidate.update(status=decision,reason=reason,pages=checked,curated_at=now());write(path,candidate)
    return candidate

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='command',required=True)
    assign=sub.add_parser('assign');assign.add_argument('--role',choices=ROLES,required=True);assign.add_argument('--card',required=True)
    rep=sub.add_parser('report');rep.add_argument('--role',choices=ROLES,required=True);rep.add_argument('--task',required=True)
    rep.add_argument('--status',choices=['completed_pending_review','blocked','failed'],required=True)
    rep.add_argument('--summary',required=True);rep.add_argument('--artifact',action='append',default=[]);rep.add_argument('--check',action='append',default=[])
    rep.add_argument('--wiki-delta',default='');rep.add_argument('--next-owner',default='');rep.add_argument('--dry-run',action='store_true')
    hk=sub.add_parser('hook');hk.add_argument('--role',choices=ROLES,required=True);hk.add_argument('--dry-run',action='store_true')
    sub.add_parser('pending');sub.add_parser('finish');sub.add_parser('recover')
    waiter=sub.add_parser('wait');waiter.add_argument('--timeout',type=int,default=45)
    ack=sub.add_parser('ack');ack.add_argument('--event',required=True);ack.add_argument('--resolution',required=True)
    ack.add_argument('--outcome',choices=['accepted','rejected','blocked'])
    ack.add_argument('--action',choices=['dispatch','rework','complete','wait'])
    ack.add_argument('--next-card');ack.add_argument('--gate',choices=['dependency','human','external','pause']);ack.add_argument('--reason')
    follow=sub.add_parser('dispatch');follow.add_argument('--event',required=True);follow.add_argument('--retry-request')
    wiki=sub.add_parser('wiki-decide');wiki.add_argument('--event',required=True)
    wiki.add_argument('--decision',choices=['promoted','excluded'],required=True);wiki.add_argument('--reason',required=True);wiki.add_argument('--page',action='append',default=[])
    retry=sub.add_parser('notify');retry.add_argument('--event',required=True)
    a=parser.parse_args()
    if a.command=='assign':result=assign_card(a.role,read(Path(a.card)))
    elif a.command=='report':result=report(a.role,a.task,a.status,a.summary,a.artifact,a.check,a.wiki_delta,a.next_owner,not a.dry_run)
    elif a.command=='hook':
        data=sys.stdin.read();result=hook(a.role,json.loads(data) if data.strip() else {},not a.dry_run)
    elif a.command=='pending':result=pending()
    elif a.command=='finish':result=finish()
    elif a.command=='recover':result=finish() # write-ahead ack side effects repaired under mutation lock before main
    elif a.command=='wait':result=wait_for_event(a.timeout)
    elif a.command=='dispatch':result=dispatch(a.event,a.retry_request)
    elif a.command=='wiki-decide':result=wiki_decide(a.event,a.decision,a.reason,a.page)
    elif a.command=='notify':
        event=read(STATE/'outbox'/f'{safe_id(a.event)}.json');result=notify(event);write(STATE/'receipts'/f"{event['id']}.json",result)
    else:result=acknowledge(a.event,a.resolution,a.outcome,a.action,a.next_card,a.gate,a.reason)
    print(json.dumps(result,ensure_ascii=False))

if __name__=='__main__':
    try:
        if len(sys.argv)>1 and sys.argv[1] not in ('pending','finish','wait','--help','-h'):
            with mutation_lock():
                repair_acks();main()
        else:main()
    except Exception as e:
        print(json.dumps({'ok':False,'error':str(e)},ensure_ascii=False));sys.exit(1)
