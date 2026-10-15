"""Exercise actual normal-package checks with synthetic bytes; never execute payloads."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

from normal_package import LAB_NAMES, release_pin, validate_publish, validate_portable

REPO = Path(__file__).resolve().parents[1]
NAME = 'VIIPER-9.9.9-x64.exe'
BROKER = b'synthetic pinned backend; never executed'
HASH = hashlib.sha256(BROKER).hexdigest().upper()
RELEASE = 'identity-preparation-test'


class NormalPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='ds4-normal-package-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.publish = self.root / 'publish'
        (self.publish / 'extras').mkdir(parents=True)
        (self.publish / 'DS4Windows.exe').write_bytes(b'never executed')
        (self.publish / 'extras' / NAME).write_bytes(BROKER)
        (self.publish / 'extras' / (NAME + '.sha256')).write_text(f'{HASH} *{NAME}\n', encoding='ascii')
        (self.publish / 'DS4Windows.release').write_text(RELEASE)
        self.files = {
            'DS4Windows.exe': b'never executed',
            'viiper.exe': BROKER,
            'extras/' + NAME: BROKER,
            'viiper.exe.sha256': f'{HASH} *viiper.exe\n'.encode(),
            'extras/' + NAME + '.sha256': f'{HASH} *{NAME}\n'.encode(),
            'DS4Windows.release': RELEASE.encode(),
            'DS4Windows.portable': b'DS4Windows portable package v1\n',
            '.ds4windows-managed-files.txt': b'viiper.exe\nviiper.exe.sha256\nDS4Windows.portable\n',
        }

    def archive(self, extra=()):
        path = self.root / 'portable.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            for name, content in self.files.items():
                archive.writestr('DS4Windows/' + name, content)
            for name, content in extra:
                archive.writestr(name, content)
        return path

    def test_matching_portable_and_msi_input_pass(self):
        self.assertEqual(RELEASE, validate_publish(self.publish, NAME, HASH))
        validate_portable(self.archive(), NAME, HASH, RELEASE)

    def test_self_consistent_feature_backend_cannot_replace_release_pin(self):
        replacement = b'feature backend with matching self-generated sidecar'
        (self.publish / 'extras' / NAME).write_bytes(replacement)
        new_hash = hashlib.sha256(replacement).hexdigest()
        (self.publish / 'extras' / (NAME + '.sha256')).write_text(f'{new_hash} *{NAME}\n')
        with self.assertRaisesRegex(ValueError, 'compiled release pin'):
            validate_publish(self.publish, NAME, HASH)

    def test_portable_alias_and_extras_are_each_pinned(self):
        for name in ('viiper.exe', 'extras/' + NAME):
            with self.subTest(name=name):
                previous = self.files[name]
                self.files[name] = b'wrong broker'
                with self.assertRaisesRegex(ValueError, 'compiled release pin'):
                    validate_portable(self.archive(), NAME, HASH, RELEASE)
                self.files[name] = previous

    def test_stale_sidecars_rejected_in_both_formats(self):
        sidecar = self.publish / 'extras' / (NAME + '.sha256')
        sidecar.write_text('0' * 64 + ' *' + NAME)
        with self.assertRaisesRegex(ValueError, 'sidecar'):
            validate_publish(self.publish, NAME, HASH)
        for name in ('viiper.exe.sha256', 'extras/' + NAME + '.sha256'):
            previous = self.files[name]
            self.files[name] = b'invalid sidecar'
            with self.assertRaisesRegex(ValueError, 'sidecar'):
                validate_portable(self.archive(), NAME, HASH, RELEASE)
            self.files[name] = previous

    def test_release_identity_and_upgrade_ownership_must_match(self):
        self.files['DS4Windows.release'] = b'other-release'
        with self.assertRaisesRegex(ValueError, 'release identities'):
            validate_portable(self.archive(), NAME, HASH, RELEASE)
        self.files['DS4Windows.release'] = RELEASE.encode()
        self.files['.ds4windows-managed-files.txt'] = b'DS4Windows.exe\n'
        with self.assertRaisesRegex(ValueError, 'upgrade ownership'):
            validate_portable(self.archive(), NAME, HASH, RELEASE)

    def test_missing_or_invalid_portable_marker_rejected(self):
        self.files['DS4Windows.portable'] = b'preview'
        with self.assertRaisesRegex(ValueError, 'portable marker'):
            validate_portable(self.archive(), NAME, HASH, RELEASE)
        del self.files['DS4Windows.portable']
        with self.assertRaises(KeyError):
            validate_portable(self.archive(), NAME, HASH, RELEASE)

    def test_portable_only_files_or_directories_never_enter_msi_input(self):
        for name in ('viiper.exe', 'viiper.exe.sha256', 'DS4Windows.portable'):
            path = self.publish / name.upper()
            path.mkdir()
            try:
                with self.assertRaisesRegex(ValueError, 'portable-only'):
                    validate_publish(self.publish, NAME, HASH)
            finally:
                path.rmdir()

    def test_preview_names_rejected_case_insensitively_in_both_formats(self):
        for name in LAB_NAMES:
            with self.subTest(name=name):
                path = self.publish / 'extras' / name.upper()
                path.write_bytes(b'preview')
                try:
                    with self.assertRaisesRegex(ValueError, 'preview-only'):
                        validate_publish(self.publish, NAME, HASH)
                finally:
                    path.unlink()
                with self.assertRaisesRegex(ValueError, 'preview-only'):
                    validate_portable(self.archive([('DS4Windows/extras/' + name.upper(), b'preview')]), NAME, HASH, RELEASE)

    def test_duplicate_or_escaping_archive_paths_are_rejected(self):
        for name in ('DS4Windows/VIIPER.EXE', '../viiper.exe', 'DS4Windows/../viiper.exe', 'DS4Windows/C:/viiper.exe'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                validate_portable(self.archive([(name, b'wrong')]), NAME, HASH, RELEASE)

    def test_real_composers_reject_preview_before_mutating_outputs(self):
        (self.publish / 'Run-Preview.ps1').write_bytes(b'preview launcher')
        old_output = self.root / 'DS4Windows'
        old_output.mkdir()
        sentinel = old_output / 'preserve.txt'
        sentinel.write_bytes(b'previous package')
        for command in (
            ['post-build.py', str(self.publish), str(REPO), RELEASE],
            ['generate-installer-files.py', str(self.publish), str(self.root / 'files.wxs'), str(self.root / 'manifest.json'), '--version', RELEASE],
        ):
            result = subprocess.run([sys.executable, str(REPO / 'utils' / command[0]), *command[1:]], capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('preview-only', result.stderr)
            self.assertEqual(b'previous package', sentinel.read_bytes())
            self.assertFalse((self.publish / 'DS4Windows.release.sha256').exists())
            self.assertFalse((self.root / 'manifest.json').exists())
            self.assertFalse((self.root / 'files.wxs').exists())

    def test_cli_uses_compiled_pin_without_payload_override(self):
        name, digest = release_pin(REPO)
        (self.publish / 'extras' / name).write_bytes(b'unmatched feature backend')
        result = subprocess.run([sys.executable, str(REPO / 'utils/normal_package.py'), '--publish-root', str(self.publish)], capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('compiled release pin', result.stderr)
        self.assertEqual(64, len(digest))


if __name__ == '__main__':
    unittest.main()
