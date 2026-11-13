"""Verify VIIPER release inputs and write a review-only pin promotion plan."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

from normal_package import release_pin

NOTICES = frozenset(('licenses.txt', 'LICENSE.txt', 'VIIPER-SYSTRAY-NOTICE.md',
                     'VIIPER-SYSTRAY-LICENSE.txt', 'VIIPER-WDL-NOTICE.txt'))
PIN_TARGETS = (
    'DS4Windows/DS4Control/Viiper/ViiperSetupManager.cs',
    'DS4Windows/DS4WinWPF.csproj',
    'installer/DS4Windows.Bootstrapper/InfrastructureProbe.cs',
    'installer/DS4Windows.SetupActions/Program.cs',
    'installer/DS4Windows.Bundle/Bundle.wxs',
    'installer/build-installer.ps1', 'extras/install-viiper-backend.ps1',
    'utils/post-build.py', 'utils/validate-installer.py',
    '.github/workflows/release.yml',
)


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper() if hasattr(hashlib, 'file_digest') else _digest(stream)


def _digest(stream) -> str:
    result = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        result.update(chunk)
    return result.hexdigest().upper()


def unique_json(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate metadata key: ' + key)
        result[key] = value
    return result


def regular(path: Path) -> None:
    import stat
    if not path.is_file() or any(p.is_symlink() or getattr(p.lstat(), 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0) for p in (path, *path.parents)):
        raise ValueError('Missing or redirected release input: ' + str(path))


def verify_inputs(root: Path, source: Path, tag: str, commit: str, embedded: dict) -> dict:
    if not re.fullmatch(r'v\d+\.\d+\.\d+(?:-(?:rc|beta|alpha)\.\d+)?', tag):
        raise ValueError('Expected tag must be an explicit stable or prerelease version.')
    if not re.fullmatch(r'[0-9a-f]{40}', commit):
        raise ValueError('Expected source commit must be 40 lowercase hexadecimal digits.')
    for path in (root / 'BUILD-INFO.json', root / 'viiper.exe', source):
        regular(path)
    info = json.loads((root / 'BUILD-INFO.json').read_text(encoding='utf-8-sig'), object_pairs_hook=unique_json)
    if info['SchemaVersion'] != 1 or info['Version'] != tag or info['SourceCommit'] != commit:
        raise ValueError('Build metadata does not match the expected version/source commit.')
    binary = root / 'viiper.exe'
    if info['Binary']['Name'] != 'viiper.exe' or info['Binary']['Size'] != binary.stat().st_size or info['Binary']['Sha256'].upper() != digest(binary):
        raise ValueError('Binary size/hash differs from build metadata.')
    if info['ProductVersion'] != tag[1:] or info['FileVersion'] != '.'.join(tag[1:].split('-')[0].split('.') + ['0']):
        raise ValueError('Recorded PE versions differ from the expected tag.')
    # Read Go metadata from the binary independently of its BUILD-INFO receipt.
    # PE versions above are receipt consistency only; production promotion must
    # still pass the existing Windows PE/signing and full installer validators.
    if embedded != info['GoBuildInfo']:
        raise ValueError('Embedded Go metadata differs from the build receipt.')
    settings = unique_json((entry['Key'], entry['Value']) for entry in embedded['Settings'])
    for key, value in {'vcs.revision': commit, 'vcs.modified': 'false', '-tags': 'release',
                       '-trimpath': 'true', 'CGO_ENABLED': '0', 'GOOS': 'windows', 'GOARCH': 'amd64'}.items():
        if settings.get(key) != value:
            raise ValueError('Embedded build setting mismatch: ' + key)
    if embedded['GoVersion'] != 'go1.27.2' or embedded.get('Path') != 'github.com/Alia5/VIIPER/cmd/viiper':
        raise ValueError('Unexpected release toolchain or command package.')
    notices = info['LicenseFiles']
    names = [entry['Name'] for entry in notices]
    if len(names) != len(set(names)) or set(names) != NOTICES:
        raise ValueError('Missing, duplicate or unexpected licence records.')
    for entry in notices:
        path = root / entry['Name']
        regular(path)
        if digest(path) != entry['Sha256'].upper():
            raise ValueError('Licence/notice hash mismatch: ' + entry['Name'])
    with zipfile.ZipFile(source) as archive:
        if archive.comment.decode('ascii') != commit or not archive.namelist():
            raise ValueError('Corresponding-source archive does not identify the expected commit.')
    return dict(tag=tag, sourceCommit=commit, binaryName=f'VIIPER-{tag[1:]}-x64.exe',
                binarySha256=digest(binary), sourceArchiveSha256=digest(source),
                buildInfoSha256=digest(root / 'BUILD-INFO.json'),
                notices=[dict(name=item['Name'], sha256=item['Sha256'].upper()) for item in notices])


def make_plan(repo: Path, candidate: dict) -> dict:
    name, checksum = release_pin(repo)
    targets = []
    for relative in PIN_TARGETS:
        path = repo / relative
        regular(path)
        targets.append(dict(path=relative, currentSha256=digest(path)))
    return dict(schema=1, reviewOnly=True, releaseReady=False,
                current=dict(binaryName=name, binarySha256=checksum), candidate=candidate,
                coordinatedPinTargets=targets,
                outstandingChecks=['Verify selected GitHub release tag resolves to expected commit',
                    'Verify approved USB identity and generic joystick capability',
                    'Review multi-controller compatibility acceptance',
                    'Preserve exact PE/signing/provenance/source/licence pins',
                    'Validate normal installer, upgrades and built-in HidHide'],
                appliedRepositoryChanges=False)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release-directory', type=Path, required=True)
    parser.add_argument('--source-archive', type=Path, required=True)
    parser.add_argument('--expected-tag', required=True)
    parser.add_argument('--expected-commit', required=True)
    parser.add_argument('--go', required=True, help='Go tool executable; only version -m is invoked')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError('Use a new output file; existing promotion plans are not overwritten.')
    root = args.release_directory.absolute()
    regular(root / 'viiper.exe')
    result = subprocess.run([args.go, 'version', '-m', '-json', str(root / 'viiper.exe')],
                            check=True, capture_output=True, text=True, timeout=30)
    embedded = json.loads(result.stdout, object_pairs_hook=unique_json)
    candidate = verify_inputs(root, args.source_archive, args.expected_tag, args.expected_commit, embedded)
    plan = make_plan(args.repo_root, candidate)
    with args.output.open('x', encoding='utf-8') as stream:
        json.dump(plan, stream, indent=2)
        stream.write('\n')
    print('Verified release inputs; wrote review-only promotion plan. No repository pins were changed.')


if __name__ == '__main__':
    main()
