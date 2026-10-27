import importlib.util
from pathlib import Path
import unittest
from datetime import datetime, timezone, timedelta
spec=importlib.util.spec_from_file_location('retention',Path(__file__).with_name('actions-artifact-retention.py'))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
NOW=datetime(2026,10,27,tzinfo=timezone.utc)
def run(i,branch='main',conclusion='success',age=20,**kw):
 return dict(id=i,head_branch=branch,head_sha=str(i)*40,status='completed',conclusion=conclusion,created_at=(NOW-timedelta(days=age)).isoformat(),event='push',path='.github/workflows/ci-build.yml',**kw)
def artifact(i,r,name='DS4Windows_ci_x64_Portable',age=20):
 return dict(id=i,name=name,workflow_run={'id':r},size_in_bytes=100,created_at=(NOW-timedelta(days=age)).isoformat(),expired=False)
class Retention(unittest.TestCase):
 def decide(self,items,runs,branches={'main'},releases=[],immediate=False):
  return {a['id']:a for a in m.plan(items,runs,branches,releases,NOW,immediate)}
 def test_latest_two_main_and_latest_live_feature_keep_all_evidence(self):
  rs=[run(i,age=10-i) for i in range(1,5)]+[run(5,'feature/directinput-output',age=2),run(6,'feature/directinput-output',age=1)]
  aa=[artifact(i,i) for i in range(1,7)]+[artifact(7,6,'DS4Windows_Test_Results')]
  d=self.decide(aa,rs,{'main','feature/directinput-output'},immediate=True)
  self.assertTrue(d[1]['delete']);self.assertTrue(d[2]['delete']);self.assertTrue(d[5]['delete'])
  for i in [3,4,6,7]:self.assertFalse(d[i]['delete'])
 def test_unknown_inflight_release_and_candidate_never_deleted(self):
  rs=[run(1),run(2),run(3),run(4)];rs[1]['status']='in_progress';rs[2]['event']='release'
  aa=[artifact(1,1,'custom-investigation'),artifact(2,2),artifact(3,3),artifact(4,4,'DS4Windows_Release_Candidate_hash_1'),artifact(5,999)]
  self.assertTrue(all(not x['delete'] for x in self.decide(aa,rs,set(),immediate=True).values()))
 def test_success_evidence_seven_days_failure_fourteen_days(self):
  rs=[run(1),run(2,conclusion='failure')]
  aa=[artifact(1,1,'DS4Windows_Test_Results',6),artifact(2,1,'DS4Windows_Test_Results',8),artifact(3,2,'DS4Windows_Test_Results',13),artifact(4,2,'DS4Windows_Test_Results',15)]
  d=self.decide(aa,rs,set());self.assertEqual([False,True,False,True],[d[i]['delete'] for i in range(1,5)])
 def test_superseded_now_does_not_bypass_failed_evidence_or_unknown(self):
  rs=[run(1),run(2,conclusion='failure')]
  aa=[artifact(1,1,age=1),artifact(2,2,age=1),artifact(3,1,'unrecognized',age=20)]
  d=self.decide(aa,rs,set(),immediate=True);self.assertEqual([True,False,False],[d[i]['delete'] for i in range(1,4)])
 def test_draft_source_commit_preserved(self):
  r=run(1);d=self.decide([artifact(1,1)],[r],set(),[{'draft':True,'target_commitish':r['head_sha']}],True)
  self.assertFalse(d[1]['delete'])
 def test_snapshot_and_package_classification(self):
  self.assertEqual('package',m.kind('VIIPER-windows-amd64-Snapshot'))
  self.assertEqual('unknown',m.kind('VIIPER-windows-amd64-Release'))
  self.assertEqual('package',m.kind('DS4Windows_5.0.53_Setup_x64'))
  self.assertEqual('evidence',m.kind('DS4Windows_Installer_Lifecycle_Evidence'))
 def test_missing_latest_package_does_not_keep_old_unrelated_run(self):
  d=self.decide([artifact(1,1)],[run(1,'deleted-branch')],{'main'},immediate=True);self.assertTrue(d[1]['delete'])
 def test_release_workflow_dispatch_is_preserved(self):
  r=run(1);r.update(event='workflow_dispatch',path='.github/workflows/release.yml')
  self.assertFalse(self.decide([artifact(1,1)],[r],set(),immediate=True)[1]['delete'])
if __name__=='__main__':unittest.main()
