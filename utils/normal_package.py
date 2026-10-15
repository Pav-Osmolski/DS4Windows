"""Read-only checks for normal portable/MSI inputs; never starts an app or driver."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

LAB_NAMES = frozenset(name.casefold() for name in (
    'Run-Preview.ps1', 'Run-Lab.ps1', 'Run-MultiControllerLab.ps1',
    'START-MULTI.cmd', 'GenericJoystickLabClient.exe',
    'viiper-directinput-lab.exe', 'GenericJoystickProbe-x64.exe',
    'GenericJoystickProbe-x86.exe', 'lab-data',
))
PORTABLE_ONLY = frozenset(('viiper.exe', 'viiper.exe.sha256', 'ds4windows.portable'))


def reject_lab_payloads(root: Path) -> None:
    for path in (root, *root.rglob('*')):
        if path.is_symlink() or getattr(path.lstat(), 'st_file_attributes', 0) & getattr(stat, 'FILE_ATTRIBUTE_REPARSE_POINT', 0):
            raise ValueError('Normal package input contains a reparse point: ' + str(path))
        if path != root and any(part.casefold() in LAB_NAMES for part in path.relative_to(root).parts):
            raise ValueError('Normal package input contains preview-only content: ' + str(path.relative_to(root)))


def release_pin(repo: Path) -> tuple[str, str]:
    source = (repo / 'DS4Windows/DS4Control/Viiper/ViiperSetupManager.cs').read_text(encoding='utf-8-sig')
    def unique(pattern: str) -> str:
        values = re.findall(pattern, source)
        if len(values) != 1:
            raise ValueError('Could not uniquely read the compiled VIIPER release pin.')
        return values[0]
    name = unique(r'const string BundledViiperName\s*=\s*"(VIIPER-[A-Za-z0-9.-]+-x64\.exe)"')
    digest = unique(r'const string SupportedViiperSha256\s*=\s*"([0-9A-F]{64})"')
    return name, digest


def stream_hash(stream) -> str:
    hasher = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b''):
        hasher.update(chunk)
    return hasher.hexdigest().upper()


def check_sidecar(text: str, name: str, digest: str) -> None:
    if text.strip().upper() != (digest + ' *' + name).upper():
        raise ValueError('Stale or malformed VIIPER hash sidecar: ' + name)


def validate_publish(root: Path, name: str, digest: str) -> str:
    reject_lab_payloads(root)
    if not (root / 'DS4Windows.exe').is_file():
        raise ValueError('Normal publish input is missing DS4Windows.exe.')
    collisions = [path.name for path in root.iterdir() if path.name.casefold() in PORTABLE_ONLY]
    if collisions:
        raise ValueError('MSI/publish input contains portable-only paths: ' + ', '.join(collisions))
    with (root / 'extras' / name).open('rb') as stream:
        if stream_hash(stream) != digest:
            raise ValueError('Bundled VIIPER differs from the compiled release pin.')
    check_sidecar((root / 'extras' / (name + '.sha256')).read_text(encoding='ascii'), name, digest)
    release = (root / 'DS4Windows.release').read_text(encoding='utf-8-sig').strip()
    if not release:
        raise ValueError('Normal publish release marker is empty.')
    return release


def validate_portable(archive_path: Path, name: str, digest: str, release: str) -> None:
    with zipfile.ZipFile(archive_path) as archive:
        entries = {}
        for entry in archive.infolist():
            normalized = entry.filename.replace('\\', '/')
            parts = PurePosixPath(normalized).parts
            if not parts or parts[0] != 'DS4Windows' or any(part in ('.', '..') or ':' in part for part in parts):
                raise ValueError('Invalid portable archive path: ' + entry.filename)
            if any(part.casefold() in LAB_NAMES for part in parts[1:]):
                raise ValueError('Portable archive contains preview-only content: ' + entry.filename)
            key = normalized.casefold()
            if key in entries:
                raise ValueError('Duplicate portable archive path: ' + entry.filename)
            entries[key] = entry
        def data(relative: str) -> bytes:
            entry = entries['ds4windows/' + relative.casefold()]
            if entry.file_size > 1024 * 1024:
                raise ValueError('Oversized package metadata: ' + relative)
            return archive.read(entry)
        for relative in ('viiper.exe', 'extras/' + name):
            with archive.open(entries['ds4windows/' + relative.casefold()]) as stream:
                if stream_hash(stream) != digest:
                    raise ValueError('Portable VIIPER differs from compiled release pin: ' + relative)
        check_sidecar(data('viiper.exe.sha256').decode('ascii'), 'viiper.exe', digest)
        check_sidecar(data('extras/' + name + '.sha256').decode('ascii'), name, digest)
        if data('DS4Windows.portable') != b'DS4Windows portable package v1\n':
            raise ValueError('Invalid portable marker.')
        if data('DS4Windows.release').decode('utf-8-sig').strip() != release:
            raise ValueError('Portable and MSI/publish release identities differ.')
        owned = set(data('.ds4windows-managed-files.txt').decode('utf-8-sig').splitlines())
        if not {'viiper.exe', 'viiper.exe.sha256', 'DS4Windows.portable'}.issubset(owned):
            raise ValueError('Portable upgrade ownership is missing broker aliases/marker.')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish-root', type=Path, required=True)
    parser.add_argument('--portable-zip', type=Path)
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    name, digest = release_pin(args.repo_root)
    release = validate_publish(args.publish_root, name, digest)
    if args.portable_zip:
        validate_portable(args.portable_zip, name, digest, release)
    print(json.dumps(dict(normalPackageIntegrity='passed', release=release,
                          backend=name, backendSha256=digest,
                          portableChecked=args.portable_zip is not None,
                          hardwareOrInstallationTested=False)))


if __name__ == '__main__':
    main()
