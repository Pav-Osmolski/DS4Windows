# Update sources for this fork

DS4Windows now discovers releases and changelogs from
`Pav-Osmolski/DS4Windows`, and prepares updater downloads from
`Pav-Osmolski/DS4Updater`. Both portable and installed update handoffs require
DS4Updater 2.0.9 or newer; older workers still use upstream update sources.

The companion DS4Updater change validates package URLs and release receipts
against `Pav-Osmolski/DS4Windows`. Existing hash, size, release-channel,
transaction, and installation ownership checks are retained. Tests reject
upstream URLs and receipts and exercise the real RC4.6.7 portable package.
Historical upstream archives in updater CI remain pinned offline fixtures.

Publish updater `v2.0.9` with verified x64 and x86 assets before releasing a
DS4Windows build with this change. RC4.6.7's published binaries remain
unchanged and still check upstream. Users need to install the first build
containing this change manually; subsequent update checks target this fork.

This change does not alter the pinned VIIPER backend or USB/IP packages.

## RC versioning

Each new DS4Windows RC release must increment the numeric assembly, file,
and installer product/bundle version, even though its public tag uses the
VIIPERRC label. RC4.6.8 uses 5.0.13.0, after RC4.6.7's 5.0.12.0.
Keep the project package/informational versions, installer defaults, build
receipt, and setup filename in agreement. The next setup is
`DS4Windows_5.0.13.0_Setup_x64.exe`.
