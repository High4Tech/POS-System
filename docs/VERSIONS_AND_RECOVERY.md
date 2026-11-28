# Source versions and recovery

Main source: [High4Tech/POS-System](https://github.com/High4Tech/POS-System). Standalone Retail distribution: [High4Tech/pos_backup](https://github.com/High4Tech/pos_backup), using the shared source with the documented standalone build flag.

## Source history

- `source-baseline`: oldest source tree available locally.
- `checkpoint-before-ui-restoration-20261121` and `checkpoint-before-business-modules-20261125`: recovered source checkpoints. Files absent from a captured checkpoint were carried forward from beta.5; these are not claimed to reconstruct every earlier binary exactly.
- `v1.1.0-beta.5`, `v1.1.0-beta.6`: earlier Windows source milestones.
- `v1.2.0-beta.1`, `.2`, `.3`, `.6`: maintained retail/digital-service and Wijgandu milestones.
- `v1.2.0-beta.7`: source used for the verified direct-digital checkout and recipient history release; build source commit `d378eb8ee5be51fb25cd452c8f0b80b477f1cb6c`.

Use Git commits/tags for source versions, rather than copying whole app/runtime folders into the repository. Keep the working tree clean before switching versions.

## Early compiled versions

Many November 17–21 copies have no exact source snapshot and reused an upstream version string despite local modifications. Their original compiled bytes must not be described as exact-source tags. A dedicated GitHub release archive preserves those app versions with content hashes and a per-version file manifest. Repeated runtime files are stored once; business databases and protected customer/store data are excluded.

`engineering/Archive-DesktopVersions.ps1` creates and verifies this archive. `engineering/Restore-DesktopVersion.ps1` restores an exact version ID into a new directory and verifies every restored file. It never launches an old app or writes into the live store directory. The archive manifest lists all available version IDs.

The [historical version catalog](LEGACY_DESKTOP_VERSIONS.md) lists all 47 preserved copies and their restore IDs. Its 229,372,017-byte archive replaces 12,588,719,082 bytes of repeated local application files. The seven historical data-only backup folders are retained separately.

Download the archive from the repository's versioned releases, then restore a selected copy:

```powershell
./engineering/Restore-DesktopVersion.ps1 -Archive '<downloaded archive.zip>' -VersionId 'App-before-20261117-135516' -Destination '<new empty directory>'
```

An old app must use a matching database backup; do not launch an obsolete executable against the current live database. Business data backups stay local and are not pushed to either repository. Keep the latest verified data/application recovery pair beside the live app.

Local cleanup is permitted only after the remote archive digest matches its local SHA256, restore checks pass, and each removal target is verified to remain inside the intended POS directory. Live `App`, database, managed media, protected drafts, scanner data and business backup files are retained. In mixed historical backup folders, only manifest-listed application files are removed; their business data remains local.

For future updates, commit/tag the source before publishing and use the existing desktop update script. Do not create new full `App-before-*` runtime folders. Keep one verified local application/data recovery pair; preserve older code as Git versions and archive any binary-only history before pruning redundant app copies. Business-data backup retention is separate from application-version cleanup.
