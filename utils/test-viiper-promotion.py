import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from prepare_viiper_promotion import verify_inputs, make_plan, main

REPO = Path(__file__).resolve().parents[1]
COMMIT = 'a' * 40
TAG = 'v9.9.9'  # Synthetic fixture; not a proposed project version.


class PromotionPlanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='viiper-promotion-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'viiper.exe').write_bytes(b'synthetic image; never executed')
        self.embedded = dict(GoVersion='go1.27.2', Path='github.com/Alia5/VIIPER/cmd/viiper', Settings=[dict(Key=k, Value=v) for k,v in {
            'vcs.revision':COMMIT, 'vcs.modified':'false', '-tags':'release', '-trimpath':'true',
            'CGO_ENABLED':'0','GOOS':'windows','GOARCH':'amd64'}.items()])
        self.info = dict(SchemaVersion=1, Version=TAG, SourceCommit=COMMIT, ProductVersion='9.9.9', FileVersion='9.9.9.0',
            Binary=dict(Name='viiper.exe', Size=(self.root/'viiper.exe').stat().st_size,
                        Sha256=hashlib.sha256((self.root/'viiper.exe').read_bytes()).hexdigest()),
            LicenseFiles=[], GoBuildInfo=copy.deepcopy(self.embedded))
        for name in ('licenses.txt','LICENSE.txt','VIIPER-SYSTRAY-NOTICE.md','VIIPER-SYSTRAY-LICENSE.txt','VIIPER-WDL-NOTICE.txt'):
            data=('fixture:'+name).encode()
            (self.root/name).write_bytes(data)
            self.info['LicenseFiles'].append(dict(Name=name,Sha256=hashlib.sha256(data).hexdigest()))
        self.source = self.root/'source.zip'
        with zipfile.ZipFile(self.source,'w') as archive:
            archive.writestr('fixture/LICENSE.txt',b'source fixture')
            archive.comment=COMMIT.encode()
        self.write_info()

    def write_info(self):
        (self.root/'BUILD-INFO.json').write_text(json.dumps(self.info))

    def verify(self):
        return verify_inputs(self.root,self.source,TAG,COMMIT,self.embedded)

    def test_valid_inputs_produce_review_only_plan_without_changing_pins(self):
        before={p:(REPO/p).read_bytes() for p in ('DS4Windows/DS4Control/Viiper/ViiperSetupManager.cs','utils/validate-installer.py')}
        plan=make_plan(REPO,self.verify())
        self.assertFalse(plan['releaseReady'])
        self.assertTrue(plan['reviewOnly'])
        self.assertFalse(plan['appliedRepositoryChanges'])
        self.assertEqual('VIIPER-9.9.9-x64.exe',plan['candidate']['binaryName'])
        self.assertGreaterEqual(len(plan['coordinatedPinTargets']),10)
        for p, data in before.items():
            self.assertEqual(data,(REPO/p).read_bytes())

    def test_expected_version_and_commit_are_external_constraints(self):
        for key,value in [('Version','v9.9.8'),('SourceCommit','b'*40),('ProductVersion','other'),('FileVersion','9.9.8.0')]:
            with self.subTest(key=key):
                old=self.info[key];self.info[key]=value;self.write_info()
                with self.assertRaises(ValueError):self.verify()
                self.info[key]=old
        self.write_info()
        with self.assertRaises(ValueError):verify_inputs(self.root,self.source,'main',COMMIT,self.embedded)

    def test_tampered_binary_or_receipt_size_is_rejected(self):
        (self.root/'viiper.exe').write_bytes(b'tampered image')
        with self.assertRaisesRegex(ValueError,'Binary size/hash'):self.verify()

    def test_recorded_and_independently_read_go_metadata_must_agree(self):
        self.info['GoBuildInfo']['Settings'][0]['Value']='b'*40
        self.write_info()
        with self.assertRaisesRegex(ValueError,'Embedded Go metadata'):self.verify()

    def test_dirty_nonrelease_and_wrong_platform_builds_rejected(self):
        for key,value in [('vcs.modified','true'),('-tags','debug'),('-trimpath','false'),('GOARCH','arm64'),('GOOS','linux')]:
            with self.subTest(key=key):
                entry=next(x for x in self.embedded['Settings'] if x['Key']==key)
                old=entry['Value'];entry['Value']=value
                self.info['GoBuildInfo']=copy.deepcopy(self.embedded);self.write_info()
                with self.assertRaisesRegex(ValueError,'build setting'):self.verify()
                entry['Value']=old
        self.embedded['GoVersion']='go1.27.0'
        self.info['GoBuildInfo']=copy.deepcopy(self.embedded);self.write_info()
        with self.assertRaisesRegex(ValueError,'toolchain'):self.verify()

    def test_missing_changed_or_duplicate_notices_rejected(self):
        (self.root/'licenses.txt').write_bytes(b'truncated notice')
        with self.assertRaisesRegex(ValueError,'notice hash'):self.verify()
        self.info['LicenseFiles'].append(self.info['LicenseFiles'][0]);self.write_info()
        with self.assertRaisesRegex(ValueError,'licence records'):self.verify()

    def test_source_archive_must_bind_the_expected_commit(self):
        with zipfile.ZipFile(self.source,'a') as archive:archive.comment=b'b'*40
        with self.assertRaisesRegex(ValueError,'source archive'):self.verify()

    def test_duplicate_json_or_embedded_settings_rejected(self):
        path=self.root/'BUILD-INFO.json'
        path.write_text(path.read_text().replace('"SchemaVersion": 1','"SchemaVersion": 1, "SchemaVersion": 1'))
        with self.assertRaisesRegex(ValueError,'Duplicate metadata key'):self.verify()
        self.embedded['Settings'].append(self.embedded['Settings'][0])
        self.info['GoBuildInfo']=copy.deepcopy(self.embedded);self.write_info()
        with self.assertRaisesRegex(ValueError,'Duplicate metadata key'):self.verify()

    def test_existing_plan_is_preserved_before_running_any_tool(self):
        output = self.root / 'plan.json'
        output.write_text('existing review')
        args = ['prepare_viiper_promotion.py', '--release-directory', str(self.root),
                '--source-archive', str(self.source), '--expected-tag', TAG,
                '--expected-commit', COMMIT, '--go', 'unused-go.exe', '--output', str(output)]
        with patch('sys.argv', args), patch('prepare_viiper_promotion.subprocess.run') as run:
            with self.assertRaisesRegex(ValueError, 'not overwritten'):
                main()
            run.assert_not_called()
        self.assertEqual('existing review', output.read_text())


if __name__=='__main__':unittest.main()
