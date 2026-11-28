Historical Windows application versions from 17–28 November 2026.

Source versions are preserved as Git commits and tags. Several early local builds did not have an exact source snapshot; this release preserves their compiled application files without pretending that an upstream source tag reproduces them.

The archive stores repeated files once by SHA256. Its manifest lists each version and every original file hash. Business databases, protected credentials, store media and transaction backups are excluded. The live application and the latest paired application/data recovery backup remain local.

Use engineering/Restore-DesktopVersion.ps1 from this repository to restore an exact version ID from the manifest into a new directory. Restored files are checked against their original hashes. Do not run an old application against the current live store database; use a matching data backup.

See docs/VERSIONS_AND_RECOVERY.md for source tags, restoration and cleanup verification. This release is historical recovery material, not the current installer.
