"""Compose/verify immutable main CI candidates; never compile or change packages.

Only this fork's unsigned numeric channel is eligible. Signed releases retain
their existing build/signing path. GitHub download digests are enforced here,
not merely left as download-artifact warnings. No PR artifact is promotable.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zipfile

REPOSITORY = "Pav-Osmolski/DS4Windows"
RECEIPT = "CANDIDATE-BUILD.json"
WORKFLOW = ".github/workflows/ci-build.yml"
REQUIRED_JOBS = {
    "test": ("Run tests (x64)", "Run standalone hardware-verification unit tests"),
    "build (x64)": (
        "Build standard installer", "Validate offline installer layout plan",
        "Exercise MSI install, repair, and uninstall lifecycle",
        "Verify legacy installer upgrade and preserve profiles (RC4.5.5)",
        "Verify recent stable installer upgrade and preserve profiles (5.0.43)",
        "Compose immutable main release candidate", "Upload immutable main release candidate"),
    "package_plan": (), "checks": (),
}


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""): h.update(block)
    return h.hexdigest().upper()


def require(condition, message):
    if not condition: raise ValueError(message)


def version_pair(version, binary):
    require(isinstance(version, str) and re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", version), "Numeric stable version required")
    require(tuple(map(int, version.split('.'))) >= (5, 0, 14) and binary == version + '.0', "Version pair mismatch")


def expected_names(version, binary):
    return {f'DS4Windows_{version}_x64.zip', f'DS4Windows_{binary}_Setup_x64.exe'}


def validate_record(record, commit, version, run_id, attempt):
    version_pair(version, record.get('binaryVersion'))
    require(record.get('schema') == 1 and record.get('kind') == 'main-release-candidate', 'Unknown candidate schema')
    require(record.get('repository') == REPOSITORY and record.get('sourceCommit') == commit, 'Candidate source/repository mismatch')
    require(record.get('version') == version and record.get('unsignedRelease') is True and record.get('prerelease') is False, 'Candidate channel mismatch')
    require(str(record.get('runId')) == str(run_id) and str(record.get('runAttempt')) == str(attempt), 'Candidate run/attempt mismatch')
    assets = record.get('assets', [])
    require(len(assets) == 2 and {a.get('name') for a in assets} == expected_names(version, record['binaryVersion']), 'Wrong candidate asset set')
    for asset in assets:
        require(isinstance(asset.get('sha256'), str) and re.fullmatch('[A-F0-9]{64}', asset['sha256']), 'Invalid candidate digest')
        require(type(asset.get('size')) is int and 0 < asset['size'] <= 512 * 1024 * 1024, 'Invalid candidate size')


def validate_run(run, jobs, commit):
    require(run.get('repository', {}).get('full_name') == REPOSITORY and run.get('head_repository', {}).get('full_name') == REPOSITORY, 'Foreign candidate run')
    require(run.get('path') == WORKFLOW and run.get('head_branch') == 'main' and run.get('head_sha') == commit, 'Candidate is not the exact main CI commit')
    require(run.get('event') in ('push', 'workflow_dispatch') and run.get('status') == 'completed' and run.get('conclusion') == 'success', 'Candidate CI did not complete successfully')
    for name, steps in REQUIRED_JOBS.items():
        found = [j for j in jobs if j.get('name') == name]
        require(len(found) == 1 and found[0].get('conclusion') == 'success', 'Missing successful candidate gate: ' + name)
        for step in steps:
            matches = [s for s in found[0].get('steps', []) if s.get('name') == step]
            require(len(matches) == 1 and matches[0].get('conclusion') == 'success', 'Missing successful candidate step: ' + step)


def validate_payload(directory: Path, record):
    for asset in record['assets']:
        path = directory / asset['name']
        require(path.is_file() and not path.is_symlink() and path.stat().st_size == asset['size'] and digest(path) == asset['sha256'], 'Candidate bytes changed: ' + asset['name'])
    portable = directory / f"DS4Windows_{record['version']}_x64.zip"
    with zipfile.ZipFile(portable) as z:
        names = z.namelist()
        require(len(names) == len(set(n.casefold() for n in names)) and z.testzip() is None, 'Invalid portable archive')
        require(z.read('DS4Windows/DS4Windows.release').decode().strip() == record['version'], 'Portable identity mismatch')
        require(z.read('DS4Windows/DS4Windows.portable') == b'DS4Windows portable package v1\n', 'Portable deployment marker mismatch')
        backend = z.read('DS4Windows/viiper.exe')
        require(backend == z.read('DS4Windows/extras/VIIPER-0.1.17-x64.exe'), 'Portable backend aliases differ')
        backend_hash = hashlib.sha256(backend).hexdigest().upper()
        require(backend_hash == record.get('backendSha256') and z.read('DS4Windows/viiper.exe.sha256').decode().split()[0].upper() == backend_hash, 'Candidate backend pin mismatch')


def create(args):
    require(os.environ.get('GITHUB_REPOSITORY') == REPOSITORY and os.environ.get('GITHUB_REF') == 'refs/heads/main' and os.environ.get('GITHUB_EVENT_NAME') in ('push','workflow_dispatch'), 'Candidates may only be composed by same-repository main CI')
    version_pair(args.version, args.binary_version)
    require(re.fullmatch('[0-9a-f]{40}', os.environ.get('GITHUB_SHA', '')), 'Missing exact source commit')
    require(args.portable.name in expected_names(args.version, args.binary_version) and args.installer.name in expected_names(args.version, args.binary_version), 'Candidate filenames must already be final')
    args.output.mkdir(parents=True, exist_ok=False)
    for path in (args.portable, args.installer): shutil.copyfile(path, args.output / path.name)
    with zipfile.ZipFile(args.portable) as z:
        backend_hash = hashlib.sha256(z.read('DS4Windows/viiper.exe')).hexdigest().upper()
    record = dict(schema=1, kind='main-release-candidate', repository=REPOSITORY,
                  sourceCommit=os.environ['GITHUB_SHA'], runId=os.environ['GITHUB_RUN_ID'],
                  runAttempt=os.environ['GITHUB_RUN_ATTEMPT'], version=args.version,
                  binaryVersion=args.binary_version, unsignedRelease=True, prerelease=False,
                  backendSha256=backend_hash,
                  assets=[dict(name=p.name,size=p.stat().st_size,sha256=digest(p)) for p in (args.portable,args.installer)])
    validate_record(record, record['sourceCommit'], args.version, record['runId'], record['runAttempt'])
    validate_payload(args.output, record)
    (args.output / RECEIPT).write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')


def api(path):
    return json.loads(subprocess.check_output(['gh','api',path], text=True, encoding='utf-8'))


def pages(path, key):
    result=[]; page=1
    while True:
        data=api(path+('&' if '?' in path else '?')+f'per_page=100&page={page}')[key]
        result.extend(data)
        if len(data)<100: return result
        page+=1


def extract_candidate(archive: Path, directory: Path, record, expected_digest):
    require(re.fullmatch('sha256:[a-f0-9]{64}', expected_digest or '') and 'sha256:'+digest(archive).lower() == expected_digest, 'GitHub candidate artifact digest mismatch')
    with zipfile.ZipFile(archive) as z:
        require(len(z.namelist()) == 3 and set(z.namelist()) == expected_names(record['version'], record['binaryVersion']) | {RECEIPT}, 'Unexpected candidate archive entries')
        for asset in record['assets']:
            entry=z.getinfo(asset['name'])
            require(entry.file_size==asset['size'], 'Candidate ZIP entry size mismatch')
            with z.open(entry) as inp, (directory/asset['name']).open('xb') as out: shutil.copyfileobj(inp,out)
    validate_payload(directory, record)


def obtain(args, commit, version):
    require(re.fullmatch('[1-9][0-9]*', args.run_id), 'Select an explicit successful main CI run ID')
    require(re.fullmatch('[0-9a-f]{40}',commit), 'Exact candidate source SHA required')
    version_pair(version,version+'.0')
    run=api(f'repos/{REPOSITORY}/actions/runs/{args.run_id}')
    attempt=run['run_attempt']
    jobs=pages(f'repos/{REPOSITORY}/actions/runs/{args.run_id}/attempts/{attempt}/jobs','jobs')
    validate_run(run,jobs,commit)
    artifacts=pages(f'repos/{REPOSITORY}/actions/runs/{args.run_id}/artifacts','artifacts')
    wanted=f'DS4Windows_Release_Candidate_{commit}_{attempt}'
    matches=[a for a in artifacts if a['name']==wanted and not a['expired']]
    require(len(matches)==1, 'Candidate missing/expired: explicitly rerun main CI with installer packaging; never substitute a branch artifact')
    require(0 < matches[0]['size_in_bytes'] <= 1024*1024*1024, 'Invalid candidate artifact size')
    args.output.mkdir(parents=True,exist_ok=False)
    archive=args.output/'github-candidate.zip'
    with archive.open('xb') as out:
        subprocess.run(['gh','api',f"repos/{REPOSITORY}/actions/artifacts/{matches[0]['id']}/zip"],stdout=out,check=True,timeout=600)
    require('sha256:'+digest(archive).lower() == matches[0].get('digest'), 'GitHub candidate artifact digest mismatch')
    with zipfile.ZipFile(archive) as z:
        require(z.getinfo(RECEIPT).file_size<=32768, 'Oversized candidate receipt')
        record=json.loads(z.read(RECEIPT))
    validate_record(record,commit,version,args.run_id,attempt)
    extract_candidate(archive,args.output,record,matches[0].get('digest'))
    record['artifactId']=matches[0]['id']
    (args.output/RECEIPT).write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
    return record


def fetch(args):
    require(os.environ.get('GITHUB_REPOSITORY') == REPOSITORY and os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch', 'Promotion requires this repository manual release workflow')
    commit=os.environ['GITHUB_SHA']
    require(api(f'repos/{REPOSITORY}/commits/{args.tag}')['sha'] == commit, 'Release tag differs from selected workflow source')
    record=obtain(args,commit,args.tag)
    portable=args.output/f'DS4Windows_{args.tag}_x64.zip'
    # Only safe, identity-checked payload names are written to the publish tree.
    # post-build output is needed by the unchanged source/notices record builder.
    publish=args.output/'publish';publish.mkdir()
    with zipfile.ZipFile(portable) as z:
        seen=set()
        for entry in z.infolist():
            require(entry.filename.startswith('DS4Windows/'), 'Unexpected portable root')
            relative=entry.filename[len('DS4Windows/'):]
            require(relative and '\\' not in relative and ':' not in relative and all(p not in ('','..','.') for p in relative.split('/')), 'Unsafe portable path')
            target=publish/relative
            require(target.resolve().is_relative_to(publish.resolve()) and str(target).casefold() not in seen, 'Duplicate/escaping portable path')
            seen.add(str(target).casefold());target.parent.mkdir(parents=True,exist_ok=True)
            with z.open(entry) as inp,target.open('xb') as out:shutil.copyfileobj(inp,out)
    exports={'VERSION':args.tag,'BINARY_VERSION':record['binaryVersion'],
             'RELEASE_ASSET':str(portable.resolve()),'INSTALLER_ASSET':str((args.output/f"DS4Windows_{record['binaryVersion']}_Setup_x64.exe").resolve()),
             'CANDIDATE_PUBLISH_ROOT':str(publish.resolve()),'CANDIDATE_RUN_ID':args.run_id,
             'CANDIDATE_RUN_ATTEMPT':str(record['runAttempt']),'CANDIDATE_ARTIFACT_ID':str(record['artifactId'])}
    with Path(os.environ['GITHUB_ENV']).open('a',encoding='utf-8') as env:
        for key,value in exports.items():
            require('\n' not in value and '\r' not in value, 'Unsafe output metadata')
            env.write(f'{key}={value}\n')
    print('Verified exact main CI candidate; no application or installer rebuild performed.')


def main():
    parser=argparse.ArgumentParser(description=__doc__);commands=parser.add_subparsers(dest='command',required=True)
    compose=commands.add_parser('create');compose.add_argument('--portable',type=Path,required=True);compose.add_argument('--installer',type=Path,required=True)
    compose.add_argument('--version',required=True);compose.add_argument('--binary-version',required=True);compose.add_argument('--output',type=Path,required=True)
    promote=commands.add_parser('fetch');promote.add_argument('--run-id',required=True);promote.add_argument('--tag',required=True);promote.add_argument('--output',type=Path,required=True)
    verify=commands.add_parser('verify',help='Read-only download/verification before creating a release draft')
    verify.add_argument('--run-id',required=True);verify.add_argument('--commit',required=True);verify.add_argument('--version',required=True);verify.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    if args.command=='create':create(args)
    elif args.command=='fetch':fetch(args)
    else:
        record=obtain(args,args.commit,args.version)
        print(json.dumps({'verifiedMainCandidate':True,'sourceCommit':record['sourceCommit'],'assets':record['assets']},indent=2))


if __name__=='__main__':main()
