Wijgandu 1.2.0-beta.7 — full Windows application source checkpoint.

Direct digital transaction → payment → receipt, separate phone/account-based digital customer history and reports, optional names, indexed lookups and the native Windows message overflow fix. Existing physical sales, inventory, purchasing, scanner integration, business onboarding, profiles and licensing screens are retained.

Verified: 243 regression tests, 11 focused checks against exact published backend assemblies, 268 published full-edition UI/keyboard assertions, and real fresh/existing-store startup with zero new diagnostics. Trial migration preserved all original rows and columns in 33 tables. All 428 installed desktop files matched the published Release payload.

Build source: d378eb8ee5be51fb25cd452c8f0b80b477f1cb6c. Source fingerprint: 55F78E2D98C2A0B66F537FFE179A7C7B8769296EB21EBA9E9330B9D5133B7BC2.

Standalone Retail source and its separately packaged installer are in https://github.com/High4Tech/pos_backup. These local builds are development/testing builds; production signing, licensing and integrations need publisher configuration. Older apps require a matching data backup.
