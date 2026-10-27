# Actions artifact retention

Workflow runs and their ordinary logs are retained. This policy only removes
recognized temporary Actions artifacts; published release downloads, source
history, caches and repository files are never deleted by the cleanup tool.

The daily workflow runs from `main` with repository-scoped Actions permission.
A manual run defaults to an audit without deletion. Each run saves its selection
and completed deletions in a small 30-day audit artifact. Unknown artifact names,
missing run metadata and in-progress runs are always preserved.

Protection rules:

- Keep all artifacts of the latest two successful package-producing main runs.
- Keep all artifacts of the latest successful package run for every live branch,
  including paused `feature/directinput-output` work.
- Keep release workflow evidence, draft-release inputs and release candidates
  throughout their existing 30-day promotion window. Candidate expiry still
  requires an explicit fresh main build before promotion; no branch substitution.
- Keep successful test/lifecycle evidence for seven days and failed/cancelled
  evidence for fourteen days. New uploads use those expiry periods.
- Remove superseded ordinary packages after seven days. Packages retain a
  90-day maximum upload expiry so a branch's latest package does not disappear
  after only seven days while awaiting external testing or PID approval.
- Remove obsolete Pages staging artifacts after one day. Unknown/custom
  investigation artifacts remain untouched.

The cleanup never guarantees permanent storage of Actions artifacts: GitHub's
configured expiry still applies. Published GitHub release assets remain the
permanent distribution record. Keep needed long-term external test previews as
explicit prereleases rather than relying on Actions artifacts indefinitely.

Run locally to review the current plan:

```powershell
python utils/actions-artifact-retention.py --repo Pav-Osmolski/DS4Windows --output artifacts/retention-review
```

`--apply` performs the selected artifact deletions. `--superseded-packages-now`
is a one-time backlog option for already superseded successful package copies;
it does not bypass release, latest-branch, unknown-artifact or failure protections.
A new output directory is required for each audit. Deletions are paced, logged
individually, and followed by checks of protected artifacts and release asset IDs.
The scheduled run deletes at most 200 artifacts at a time.

Deleting current artifacts stops their future storage accumulation; it does not
remove usage already accrued in the billing cycle. See
[GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).
