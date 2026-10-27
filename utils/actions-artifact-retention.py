"""Conservative Actions artifact retention; dry-run by default.

Never deletes workflow runs, logs, releases or caches. Only recognized disposable
artifact names are eligible. Unknown/incomplete run metadata always preserves data.
"""
import argparse
from collections import defaultdict
from datetime import datetime, timezone, timedelta
import json
from pathlib import Path
import re
import subprocess
import time
from urllib.request import Request, urlopen

ALLOWED = {'Pav-Osmolski/DS4Windows', 'Pav-Osmolski/VIIPER'}

def pages(repo, path, key=None):
    raw = subprocess.check_output(['gh', 'api', '--paginate', '--slurp', f'repos/{repo}/{path}'], encoding='utf-8-sig')
    return [item for page in json.loads(raw) for item in (page[key] if key else page)]

def kind(name):
    if name.startswith('DS4Windows_Release_Candidate_'): return 'candidate'
    if name in {'DS4Windows_Test_Results', 'DS4Windows_Release_Test_Results', 'DS4Windows_Installer_Lifecycle_Evidence'}: return 'evidence'
    if re.fullmatch(r'DS4Windows_.+_(?:Portable|Setup_x64|Setup_x86)', name): return 'package'
    if re.fullmatch(r'(?:VIIPER|libVIIPER|typescript-client-library|rust-client-library|csharp-client-library-nupkg|cpp-client-library-headers).*-Snapshot', name): return 'package'
    if name == 'github-pages': return 'pages'
    return 'unknown'

def stamp(value): return datetime.fromisoformat(value.replace('Z', '+00:00'))

def plan(artifacts, runs, branches, releases, now, superseded_now=False):
    runs = {r['id']: r for r in runs}
    package_runs = defaultdict(set)
    for a in artifacts:
        r = runs.get(a.get('workflow_run', {}).get('id'))
        if r and r['status'] == 'completed' and r['conclusion'] == 'success' and kind(a['name']) in {'package', 'candidate'}:
            package_runs[r['head_branch']].add(r['id'])
    protected = {}
    for branch, ids in package_runs.items():
        if branch not in branches: continue
        ordered = sorted(ids, key=lambda i: (runs[i]['created_at'], i), reverse=True)
        for i in ordered[:2 if branch == 'main' else 1]: protected[i] = 'latest successful package for live branch ' + branch
    # An open draft may point to a commit or a branch. Neither should lose its inputs.
    draft_targets = {r['target_commitish'] for r in releases if r['draft']}
    result = []
    for a in artifacts:
        r = runs.get(a.get('workflow_run', {}).get('id')); category = kind(a['name'])
        reason = None
        if a.get('expired'): reason = 'already expired'
        elif not r: reason = 'run metadata unavailable'
        elif r['status'] != 'completed': reason = 'run still active'
        elif r['id'] in protected: reason = protected[r['id']]
        elif category == 'candidate': reason = 'release candidate: preserve its 30-day promotion window'
        elif r.get('event') == 'release' or r.get('path', '').endswith('/release.yml'): reason = 'release workflow evidence'
        elif r['head_sha'] in draft_targets or r['head_branch'] in draft_targets: reason = 'draft release input'
        elif category == 'unknown': reason = 'unrecognized artifact: preserve'
        else:
            age = now - stamp(a['created_at'])
            days = 14 if r['conclusion'] != 'success' else 1 if category == 'pages' else 7
            immediate = superseded_now and category == 'package' and r['conclusion'] == 'success'
            if not immediate and age < timedelta(days=days): reason = f'within {days}-day retention'
        result.append({'id': a['id'], 'name': a['name'], 'bytes': a['size_in_bytes'], 'run': a.get('workflow_run', {}).get('id'),
                       'branch': r['head_branch'] if r else None, 'expiresAt': a.get('expires_at'), 'delete': reason is None, 'reason': reason or 'superseded disposable artifact'})
    return result

def delete_artifact(repo, identity, token):
    # Repository is allowlisted and the only permitted mutation is this exact endpoint.
    if repo not in ALLOWED or not isinstance(identity, int) or identity <= 0:
        raise ValueError('Deletion requires an allowlisted repository and a positive artifact ID')
    req = Request(f'https://api.github.com/repos/{repo}/actions/artifacts/{int(identity)}', method='DELETE',
                  headers={'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'})
    with urlopen(req, timeout=60) as response:
        if response.status != 204: raise RuntimeError(f'Unexpected delete status: {response.status}')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', required=True, choices=sorted(ALLOWED))
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--superseded-packages-now', action='store_true', help='One-time cleanup of successful superseded package copies, regardless of age')
    parser.add_argument('--max-delete', type=int, default=200)
    args = parser.parse_args()
    if not 0 <= args.max_delete <= 2000: parser.error('max-delete must be between 0 and 2000')
    if args.output.exists(): parser.error('Use a new audit output directory')
    artifacts = pages(args.repo, 'actions/artifacts?per_page=100', 'artifacts')
    runs = pages(args.repo, 'actions/runs?per_page=100', 'workflow_runs')
    branches = {b['name'] for b in pages(args.repo, 'branches?per_page=100')}
    releases = pages(args.repo, 'releases?per_page=100')
    now = datetime.now(timezone.utc)
    decisions = plan(artifacts, runs, branches, releases, now, args.superseded_packages_now)
    args.output.mkdir(parents=True)
    audit = {'repository': args.repo, 'createdAt': now.isoformat(), 'apply': args.apply, 'artifacts': decisions,
             'releaseAssets': {str(r['id']): [a['id'] for a in r['assets']] for r in releases}}
    (args.output/'plan.json').write_text(json.dumps(audit, indent=2)+'\n', encoding='utf-8')
    selected = [a for a in decisions if a['delete']][:args.max_delete]
    print(json.dumps({'repository': args.repo, 'artifacts': len(artifacts), 'selected': len(selected),
                      'reclaimGiB': round(sum(a['bytes'] for a in selected)/2**30, 3),
                      'preserved': len(decisions)-sum(a['delete'] for a in decisions)}, indent=2), flush=True)
    if not args.apply: return
    token = subprocess.check_output(['gh', 'auth', 'token'], encoding='utf-8').strip()
    deleted = 0; freed = 0
    with (args.output/'deleted.jsonl').open('x', encoding='utf-8') as log:
        for a in selected:
            started = time.monotonic()
            delete_artifact(args.repo, a['id'], token)
            log.write(json.dumps(a)+'\n'); log.flush()
            deleted += 1; freed += a['bytes']
            if deleted % 25 == 0: print(f'Deleted {deleted}/{len(selected)} artifacts; {freed/2**30:.2f} GiB', flush=True)
            # Stay below secondary REST mutation rate limits. No retries of uncertain deletes.
            time.sleep(max(0, .9 - (time.monotonic()-started)))
    after = pages(args.repo, 'actions/artifacts?per_page=100', 'artifacts')
    remaining = {a['id'] for a in after}
    protected = {a['id'] for a in decisions if not a['delete'] and a['reason'] != 'already expired'
                 and (not a.get('expiresAt') or stamp(a['expiresAt']) > datetime.now(timezone.utc))}
    # Natural expiry is not a deletion by this tool; only still-live protected IDs must remain.
    missing = protected - remaining
    if missing: raise RuntimeError(f'Protected artifacts missing: {sorted(missing)}')
    releases_after = pages(args.repo, 'releases?per_page=100')
    for r in releases_after:
        before = audit['releaseAssets'].get(str(r['id']))
        if before is not None and before != [a['id'] for a in r['assets']]: raise RuntimeError('Release asset list changed; inspect audit')
    summary = {'deleted': deleted, 'reclaimedBytes': freed, 'remainingArtifacts': len(after),
               'remainingBytes': sum(a['size_in_bytes'] for a in after if not a['expired']), 'protectedArtifactsVerified': len(protected), 'releaseAssetsUnchanged': True}
    (args.output/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary, indent=2), flush=True)

if __name__ == '__main__': main()
