"""Behavior checks isolated from live assignments and Orca. Run: python -m unittest discover -s tests -p test_team_bus.py"""
import importlib.util,json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('team_bus',Path(__file__).resolve().parents[1]/'scripts/team_bus.py')
bus=importlib.util.module_from_spec(spec);spec.loader.exec_module(bus)

class TeamBusTests(unittest.TestCase):
 def setUp(self):
  parent=Path(__file__).resolve().parents[1]/'artifacts/team-bus-tests';parent.mkdir(parents=True,exist_ok=True)
  self.tmp=tempfile.TemporaryDirectory(dir=parent);self.addCleanup(self.tmp.cleanup)
  self.root=Path(self.tmp.name);self.state=self.root/'state'
  self.p1=patch.object(bus,'ROOT',self.root);self.p2=patch.object(bus,'STATE',self.state)
  self.p1.start();self.p2.start();self.addCleanup(self.p1.stop);self.addCleanup(self.p2.stop)
  (self.root/'source.txt').write_text('actual change');self.card=self.make_card('a')
 def make_card(self,task,role='assistant',files=None):
  return {'task':task,'role':role,'goal':'small correction','approval':'explicit user approval',
   'coordinator':'coordinator','terminal':'worker','owned_files':files or ['source.txt'],
   'acceptance':'source contains actual change','next_owner':'secretary'}
 def report(self,status='completed_pending_review',delta=''):
  bus.assign_card('assistant',self.card)
  return bus.report('assistant','a',status,'actual outcome',['source.txt'],['read actual source'],delta,notify_enabled=False)['event']
 def ack(self,event,action='complete',outcome='accepted',**kw):
  return bus.acknowledge(event,'checked actual evidence',outcome,action,**kw)
 def follow(self,event):
  card=self.make_card('b');path=self.root/'next.json';path.write_text(json.dumps(card));self.ack(event,'dispatch',next_card=path)
 def cli(self,args):
  if args[:2]==['terminal','list']:return {'ok':True,'result':{'terminals':[{'handle':'worker','agentIdentity':'opencode'}]}}
  return {'ok':True,'result':{'send':{'accepted':True,'prompt':{'requestId':'request-1','stages':['input_accepted']}}}}
 def test_small_change_uses_source_artifact_no_extra_report_or_wiki(self):
  event=self.report();self.ack(event)
  self.assertTrue(bus.finish()['can_finish']);self.assertEqual(bus.rows('wiki-inbox'),[])
 def test_requires_structured_next_action(self):
  event=self.report()
  with self.assertRaises(ValueError):bus.acknowledge(event,'checked',None,None)
  self.assertFalse(bus.finish()['can_finish'])
 def test_release_before_same_role_followup_and_deduplicated_dispatch(self):
  event=self.report();self.follow(event)
  self.assertEqual(bus.read(self.state/'assignments/assistant.json')['state'],'acked')
  self.assertFalse(bus.finish()['can_finish'])
  with patch.object(bus,'run_cli',side_effect=self.cli) as call:
   bus.dispatch(event);self.assertTrue(bus.dispatch(event)['duplicate']);self.assertEqual(call.call_count,2)
  self.assertEqual(bus.read(self.state/'assignments/assistant.json')['task'],'b')
 def test_timeout_retains_ambiguous_attempt_and_no_blind_resend(self):
  event=self.report();self.follow(event)
  def fail(args):
   if args[:2]==['terminal','list']:return self.cli(args)
   raise TimeoutError('unknown transport outcome')
  with patch.object(bus,'run_cli',side_effect=fail):
   with self.assertRaises(TimeoutError):bus.dispatch(event)
  with self.assertRaises(ValueError):bus.dispatch(event)
  self.assertFalse(bus.finish()['can_finish'])
 def test_interrupted_ack_repairs_assignment_release(self):
  event=self.report()
  with patch.object(bus,'reconcile_ack',side_effect=RuntimeError('interruption')):
   with self.assertRaises(RuntimeError):self.ack(event)
  self.assertEqual(bus.read(self.state/'assignments/assistant.json')['state'],'active')
  self.assertEqual(bus.finish()['recovery_required'],[event])
  bus.repair_acks();self.assertEqual(bus.read(self.state/'assignments/assistant.json')['state'],'acked')
 def test_duplicate_ack_does_not_change_decision(self):
  event=self.report();self.ack(event);before=bus.read(self.state/f'acks/{event}.json')
  self.ack(event,'wait',gate='human',reason='different');self.assertEqual(before,bus.read(self.state/f'acks/{event}.json'))
 def test_changed_artifact_not_accepted(self):
  event=self.report();(self.root/'source.txt').write_text('changed after report')
  with self.assertRaises(ValueError):self.ack(event)
 def test_missing_report_is_reconciliation_not_success(self):
  bus.assign_card('assistant',self.card)
  result=bus.hook('assistant',{},False);event=result['event']
  self.assertEqual(bus.read(self.state/f'outbox/{event}.json')['status'],'needs_reconciliation')
  with self.assertRaises(ValueError):self.ack(event)
  self.assertIn('skipped',bus.hook('assistant',{},False))
 def test_wiki_candidates_are_deduplicated_and_curated(self):
  event=self.report(delta='verified reusable fix');self.ack(event)
  self.assertEqual(bus.read(self.state/f'wiki-inbox/{event}.json')['status'],'candidate')
  bus.wiki_decide(event,'excluded','already covered',[]);self.ack(event)
  self.assertEqual(bus.read(self.state/f'wiki-inbox/{event}.json')['status'],'excluded')
 def test_promotion_requires_compiled_page(self):
  event=self.report(delta='approved contract');self.ack(event)
  with self.assertRaises(ValueError):bus.wiki_decide(event,'promoted','new useful fact',[])
  p=self.root/'docs/wiki/topics/fact.md';p.parent.mkdir(parents=True);p.write_text('verified source')
  bus.wiki_decide(event,'promoted','new useful fact',['docs/wiki/topics/fact.md'])
 def test_legacy_ack_unchanged(self):
  bus.write(self.state/'acks/old.json',{'at':'old','resolution':'historical'})
  bus.repair_acks();self.assertEqual(bus.read(self.state/'acks/old.json'),{'at':'old','resolution':'historical'})
 def test_ready_queue_prevents_finish(self):
  card=self.make_card('queued');card['queued']=True
  bus.write(self.state/'cards/queued.json',card)
  self.assertEqual(bus.finish()['ready_tasks'],['queued'])
 def test_local_wait_returns_event_immediately(self):
  self.report();self.assertTrue(bus.wait_for_event(0)['event_available'])
 def test_wait_is_bounded(self):
  with self.assertRaises(ValueError):bus.wait_for_event(61)
 def test_secretary_does_not_nudge_itself_for_running_dependency(self):
  bus.assign_card('assistant',self.card)
  self.assertFalse(bus.finish()['can_finish'])
  self.assertIn('awaiting assigned dependency',bus.hook('secretary',{},False)['skipped'])
 def test_overlapping_writers_rejected(self):
  bus.assign_card('assistant',self.card)
  with self.assertRaises(ValueError):bus.assign_card('tl',self.make_card('other','tl'))
 def test_disjoint_similarly_named_files_parallelize(self):
  bus.assign_card('assistant',self.make_card('one',files=['src/a.cs']))
  bus.assign_card('tl',self.make_card('two','tl',['src/ab.cs']))
  self.assertEqual(len(bus.finish()['active_tasks']),2)
 def test_wait_requires_real_gate_and_reason(self):
  event=self.report('blocked')
  with self.assertRaises(ValueError):self.ack(event,'wait','blocked')
  self.ack(event,'wait','blocked',gate='external',reason='provider 503, see error evidence')
 def test_secretary_idle_guard_notifies_once_no_event_echo(self):
  self.report();first=bus.hook('secretary',{},False);second=bus.hook('secretary',{},False)
  self.assertIn('finish',first);self.assertIn('skipped',second);self.assertEqual(len(bus.rows('outbox')),1)

if __name__=='__main__':unittest.main()
