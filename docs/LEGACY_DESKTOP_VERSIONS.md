# Historical desktop version catalog

[Download the versioned archive](https://github.com/High4Tech/POS-System/releases/tag/legacy-desktop-20261128). Source tags and restoration instructions are in [Versions and recovery](VERSIONS_AND_RECOVERY.md).

Archive: `legacy-desktop-20261128.zip`, 229,372,017 bytes, SHA256 `9619562E63CFB634FEE7A25AC0A03098B000A081749963CC2F68FB76449C2916`. All 839 unique file blobs were verified after writing the archive.

These 47 entries preserve the compiled application bytes. Some early builds reused an upstream version number without capturing their exact modified source; the archive does not turn those into exact-source Git checkpoints. Business databases and protected store data are excluded.

Restoration checks passed for `App-before-20261117-135516` (334 files), `Backups/App-before-retail-extensions-20261119-190109` (426 files), and `Backups/Previous-application-20261127-165413138.zip` (427 files). Every restored file matched its archived SHA256; no obsolete executable was launched. Temporary QA restores were removed after checking them.

## Cleanup verified on 28 November 2026

GitHub confirmed the archive's uploaded state, exact byte length and matching SHA256 before deletion. All local application files were rehashed before removal. Removed 36 obsolete application/staging directories and 11 application ZIPs from `D:/SupermarketPos`, reclaiming 12,588,719,082 bytes (12.59 GB).

Post-cleanup checks found zero obsolete application directories. The installed executable/DLL, app icon, desktop shortcut and latest paired recovery ZIPs remained unchanged. All 15 files in the seven historical business-backup folders retained their original hashes; the live database remained present. No new diagnostic reports were found. Business data, protected drafts, managed media, scanner files and data-backup retention were outside the removal plan.

The retained application recovery is `Backups/Previous-application-20261128-081441393.zip`; its matching pre-update data backup is `Backups/Before-update-20261128-081441393.zip`. Their hashes and the installed beta.7 payload are recorded in [release verification](../engineering/Direct-Digital-20261128.md).

| Exact restore version ID | Origin | Files |
| --- | --- | ---: |
| `.update-20261127-163335244` | directory | 428 |
| `App-before-20261117-135516` | directory | 334 |
| `App-before-accounts-devices-20261119-125121` | directory | 425 |
| `App-before-controls-20261117-152637` | directory | 318 |
| `App-before-dashboard-20261118-192024` | directory | 425 |
| `App-before-dashboard-range-20261118-204428-220` | directory | 425 |
| `App-before-display-20261118-201123-589` | directory | 425 |
| `App-before-expiry-20261118-142234` | directory | 425 |
| `App-before-fit-columns-20261118-132128` | directory | 425 |
| `App-before-media-20261118-210946-007` | directory | 425 |
| `App-before-mobile-20261117-172619` | directory | 318 |
| `App-before-modular-beta4-20261125-140056` | directory | 427 |
| `App-before-navigation-20261118-120822` | directory | 425 |
| `App-before-pharmacy-beta-20261121` | directory | 426 |
| `App-before-popups-20261118-212707-217` | directory | 425 |
| `App-before-pos-scannar-20261117-191202` | directory | 425 |
| `App-before-register-20261118-124523` | directory | 425 |
| `App-before-register-restoration-20261121` | directory | 427 |
| `App-before-restock-20261118-111146` | directory | 425 |
| `App-before-retail-beta6-20261125-205752` | directory | 427 |
| `App-before-retail-beta7-20261126-001731` | directory | 427 |
| `App-before-sales-20261118-014328` | directory | 425 |
| `App-before-scan-audio-20261118-115327` | directory | 425 |
| `App-before-scanner-1.1-20261117-180145` | directory | 425 |
| `App-before-scanner-1.4-20261117-233406` | directory | 425 |
| `App-before-scanner-1.5-20261118-004839` | directory | 425 |
| `App-before-search-panels-20261121` | directory | 427 |
| `App-before-shortcuts-20261125-192616` | directory | 427 |
| `App-before-tables-20261118-194239-078` | directory | 425 |
| `App-before-tech-20261117-142237` | directory | 317 |
| `App-before-ui-20261117-145218` | directory | 318 |
| `Backups/App-before-commercial-20261119` | directory | 425 |
| `Backups/App-before-onboarding-fixes-20261119-171714` | directory | 426 |
| `Backups/App-before-receipt-close-20261120` | directory | 426 |
| `Backups/App-before-register-ux-20261120` | directory | 426 |
| `Backups/App-before-retail-extensions-20261119-190109` | directory | 426 |
| `Backups/Previous-application-20261125-201656740.zip` | zip | 427 |
| `Backups/Previous-application-20261125-203642466.zip` | zip | 427 |
| `Backups/Previous-application-20261126-060451593.zip` | zip | 427 |
| `Backups/Previous-application-20261126-082552338.zip` | zip | 427 |
| `Backups/Previous-application-20261126-112941332.zip` | zip | 427 |
| `Backups/Previous-application-20261126-141904334.zip` | zip | 427 |
| `Backups/Previous-application-20261126-172527609.zip` | zip | 427 |
| `Backups/Previous-application-20261127-054821588.zip` | zip | 427 |
| `Backups/Previous-application-20261127-083311949.zip` | zip | 427 |
| `Backups/Previous-application-20261127-163335244.zip` | zip | 427 |
| `Backups/Previous-application-20261127-165413138.zip` | zip | 427 |
