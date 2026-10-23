"""Offline promotion regressions: source identity, required gates and exact bytes."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

spec=importlib.util.spec_from_file_location('candidate',Path(__file__).with_name('release-candidate.py'))
c=importlib.util.module_from_spec(spec);spec.loader.exec_module(c)
COMMIT='a'*40


class CandidateTests(unittest.TestCase):
    def test_workflow_promotion_keeps_existing_guards_and_cannot_compile(self):
        validator_spec=importlib.util.spec_from_file_location('validator',Path(__file__).with_name('validate-installer.py'))
        validator=importlib.util.module_from_spec(validator_spec);validator_spec.loader.exec_module(validator)
        workflow=(Path(__file__).resolve().parent.parent/'.github/workflows/release.yml').read_text(encoding='utf-8-sig')
        validator.validate_release_workflow(workflow)
        start=workflow.index('  promote:');before=workflow[:start];promotion=workflow[start:]
        for changed in (promotion.replace('Hash -cne $record.sha256','Hash -eq $record.sha256'),
                        promotion.replace('gh release upload','dotnet publish\n        gh release upload',1),
                        promotion.replace("inputs.mode == 'promote' && needs.identity.outputs.unsigned_release == 'true'", "inputs.mode == 'promote'")):
            with self.assertRaises(SystemExit):validator.validate_release_workflow(before+changed)

    def run_and_jobs(self):
        run={'repository':{'full_name':c.REPOSITORY},'head_repository':{'full_name':c.REPOSITORY},'path':c.WORKFLOW,
             'head_branch':'main','head_sha':COMMIT,'event':'push','status':'completed','conclusion':'success'}
        jobs=[{'name':name,'conclusion':'success','steps':[{'name':s,'conclusion':'success'} for s in steps]} for name,steps in c.REQUIRED_JOBS.items()]
        return run,jobs

    def test_pr_foreign_failed_stale_and_wrong_source_runs_cannot_promote(self):
        run,jobs=self.run_and_jobs();c.validate_run(run,jobs,COMMIT)
        for key,value in [('head_branch','feature/directinput-output'),('head_sha','b'*40),('event','pull_request'),
                          ('status','in_progress'),('conclusion','cancelled'),('path','.github/workflows/other.yml'),
                          ('head_repository',{'full_name':'other/repo'}),('repository',{'full_name':'other/repo'})]:
            with self.subTest(key=key):
                changed=copy.deepcopy(run);changed[key]=value
                with self.assertRaises(ValueError):c.validate_run(changed,jobs,COMMIT)

    def test_skipped_missing_duplicate_or_failed_required_steps_cannot_promote(self):
        run,jobs=self.run_and_jobs()
        for index in range(len(jobs)):
            changed=copy.deepcopy(jobs);changed[index]['conclusion']='skipped'
            with self.assertRaises(ValueError):c.validate_run(run,changed,COMMIT)
            with self.assertRaises(ValueError):c.validate_run(run,jobs[:index]+jobs[index+1:],COMMIT)
        for index,job in enumerate(jobs):
            for step in range(len(job['steps'])):
                changed=copy.deepcopy(jobs);changed[index]['steps'][step]['conclusion']='failure'
                with self.assertRaises(ValueError):c.validate_run(run,changed,COMMIT)
        with self.assertRaises(ValueError):c.validate_run(run,jobs+[copy.deepcopy(jobs[0])],COMMIT)

    def fixtures(self,root):
        version='5.0.45';binary='5.0.45.0';backend=b'synthetic-backend'
        portable=root/f'DS4Windows_{version}_x64.zip';installer=root/f'DS4Windows_{binary}_Setup_x64.exe'
        with zipfile.ZipFile(portable,'w') as z:
            z.writestr('DS4Windows/DS4Windows.release',version+'\n')
            z.writestr('DS4Windows/DS4Windows.portable',b'DS4Windows portable package v1\n')
            z.writestr('DS4Windows/viiper.exe',backend);z.writestr('DS4Windows/extras/VIIPER-0.1.12-x64.exe',backend)
            z.writestr('DS4Windows/viiper.exe.sha256',hashlib.sha256(backend).hexdigest()+' *viiper.exe\n')
        installer.write_bytes(b'synthetic-installer')
        record={'schema':1,'kind':'main-release-candidate','repository':c.REPOSITORY,'sourceCommit':COMMIT,
                'runId':'123','runAttempt':'2','version':version,'binaryVersion':binary,'unsignedRelease':True,'prerelease':False,
                'backendSha256':hashlib.sha256(backend).hexdigest().upper(),
                'assets':[{'name':p.name,'sha256':c.digest(p),'size':p.stat().st_size} for p in (portable,installer)]}
        return record

    def test_candidate_requires_final_versions_and_exact_run_attempt(self):
        with tempfile.TemporaryDirectory() as t:
            record=self.fixtures(Path(t));c.validate_record(record,COMMIT,'5.0.45','123','2')
            for key,value in [('version','5.0.44'),('binaryVersion','5.0.45.1'),('repository','outsider/repo'),
                              ('sourceCommit','b'*40),('runId','124'),('runAttempt','1'),('unsignedRelease',False),('prerelease',True)]:
                changed=copy.deepcopy(record);changed[key]=value
                with self.subTest(key=key),self.assertRaises(ValueError):c.validate_record(changed,COMMIT,'5.0.45','123','2')
            changed=copy.deepcopy(record);changed['assets'][0]['name']='../other.zip'
            with self.assertRaises(ValueError):c.validate_record(changed,COMMIT,'5.0.45','123','2')
            for version in ('5.0.45-ci','v5.0.45','05.0.45','5.0.13','5.0.45\n'):
                with self.assertRaises(ValueError):c.version_pair(version,version+'.0')

    def test_envelope_and_each_payload_are_verified_without_repacking(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);record=self.fixtures(root);archive=root/'artifact.zip';dest=root/'download';dest.mkdir()
            with zipfile.ZipFile(archive,'w') as z:
                z.writestr(c.RECEIPT,json.dumps(record))
                for a in record['assets']:z.write(root/a['name'],a['name'])
            before={a['name']:c.digest(root/a['name']) for a in record['assets']}
            c.extract_candidate(archive,dest,record,'sha256:'+c.digest(archive).lower())
            self.assertEqual(before,{a['name']:c.digest(dest/a['name']) for a in record['assets']})
            with self.assertRaises(ValueError):c.extract_candidate(archive,dest,record,'sha256:'+'0'*64)
            installer=dest/record['assets'][1]['name'];installer.write_bytes(b'changed')
            with self.assertRaises(ValueError):c.validate_payload(dest,record)

    def test_extra_or_escaping_archive_entries_are_not_extracted(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);record=self.fixtures(root);archive=root/'artifact.zip';dest=root/'download';dest.mkdir()
            with zipfile.ZipFile(archive,'w') as z:
                z.writestr(c.RECEIPT,json.dumps(record));z.writestr('../escape.txt','bad')
                for a in record['assets']:z.write(root/a['name'],a['name'])
            with self.assertRaises(ValueError):c.extract_candidate(archive,dest,record,'sha256:'+c.digest(archive).lower())
            self.assertEqual([],list(dest.iterdir()));self.assertFalse((root/'escape.txt').exists())

    def test_full_read_only_retrieval_binds_real_api_shapes_and_original_bytes(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t);record=self.fixtures(root);archive=root/'original-artifact.zip'
            with zipfile.ZipFile(archive,'w') as z:
                z.writestr(c.RECEIPT,json.dumps(record))
                for asset in record['assets']:z.write(root/asset['name'],asset['name'])
            run,jobs=self.run_and_jobs();run['run_attempt']=2
            artifact={'id':987,'name':f'DS4Windows_Release_Candidate_{COMMIT}_2','expired':False,
                      'size_in_bytes':archive.stat().st_size,'digest':'sha256:'+c.digest(archive).lower()}
            def github(path):
                if path.endswith('/123'):return run
                if '/jobs?' in path:return {'jobs':jobs}
                if '/artifacts?' in path:return {'artifacts':[artifact]}
                self.fail('Unexpected API path: '+path)
            def download(command,**kwargs):
                self.assertEqual(['gh','api',f'repos/{c.REPOSITORY}/actions/artifacts/987/zip'],command)
                kwargs['stdout'].write(archive.read_bytes())
            args=SimpleNamespace(run_id='123',output=root/'retrieved')
            with patch.object(c,'api',side_effect=github),patch.object(c.subprocess,'run',side_effect=download):
                actual=c.obtain(args,COMMIT,'5.0.45')
            self.assertEqual(987,actual['artifactId'])
            for asset in record['assets']:
                self.assertEqual((root/asset['name']).read_bytes(),(args.output/asset['name']).read_bytes())
            artifact['expired']=True;args.output=root/'expired'
            with patch.object(c,'api',side_effect=github),patch.object(c.subprocess,'run') as network:
                with self.assertRaises(ValueError):c.obtain(args,COMMIT,'5.0.45')
                network.assert_not_called();self.assertFalse(args.output.exists())


if __name__=='__main__':unittest.main()
