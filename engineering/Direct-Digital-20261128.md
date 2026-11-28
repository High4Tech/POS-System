# Direct digital transactions and customer history

Scope: both editions, as explicitly confirmed by the user on 28 November 2026. Preserve full-edition licensing, business profile/onboarding, physical sales and inventory.

1. Fix Common/WindowsNavigation.cs: ignore non-hotkey messages before inspecting pointer-sized parameters; add a regression check for large native parameters.
2. Update Views/DigitalServicesView.Cart.cs, .Packages.cs, .Checkout.cs and .Drafts.cs: normal flow is one editable transaction followed directly by existing cash/card payment and receipt. Remove the cart table and Add-to-cart step. Keep explicit recovery for old saved batches so no existing draft disappears.
3. Extend Domain/Entities/DigitalServices.cs, PosDbContext, an additive migration and DigitalServiceLedger: store a normalized recipient identity and receipt name snapshot; keep optional customer names separately. Phone/Raast identities group across load, packages and wallet services; IBANs are canonical; plain bank accounts are scoped to the provider to avoid merging unrelated accounts.
4. Extend CustomersViewModel/CustomersView with a separate digital tab, identifier search, optional name editing and transaction/receipt history. Extend the existing digital Reports tab with identifier/name search and customer columns; keep physical customer accounting separate.
5. Reuse English/Urdu resources and receipts; preserve idempotency, validation, fees, pending/failed/reversed entries, and manual-provider semantics. Verify the domain/migration, direct UI and native hook, then build both editions, test real executables, back up/update only after pending work is handled and the app is closed, verify hashes/shortcuts and push a versioned source checkpoint.

## Implementation and checks

- Normal load, package and transfer/cash-out use one transaction form, shared cash/card payment and a service receipt. Physical goods retain their cart. Only explicit recovery of an older multi-entry draft retains batch settlement.
- Optional names are separate digital contact aliases. New receipts store the name used at posting time; later name edits do not rewrite old receipts. Phone searches accept local/international forms and partial prefixes. Plain account numbers remain provider-scoped.
- Customer history uses the recipient/date index and scoped posting reads. Digital report history supports recipient/name and inclusive date filters.
- The reported desktop fault was OverflowException in WindowsNavigation.Message: it converted every native wParam to Int32. The hook now ignores unrelated messages first and checks the pointer-sized ID before narrowing it.
- UI checks exposed and fixed an empty form being marked pending by initial dropdown events. Draft resume now remains available after clearing. The package editor scrolls its selected recipient into view; the duplicate page heading was removed to preserve space at larger UI sizes.
- Serial regression suite: 243 passed, 0 failed/skipped (tests/SupermarketPos.Tests/TestResults/direct-digital-20261128.trx). This includes the additive migration, canonical phone grouping, optional aliases, immutable name snapshots, retry protection and reversals.
- Final published backend: 11 focused identity, name snapshot, migration, rollback and scoped-posting checks passed against the exact Release assemblies. Both published editions passed 268 UI/keyboard assertions each; standalone Retail setup passed 23 assertions. Real bank/telecom operations and physical printing are outside these isolated checks.

## Verified release and installation

Source: `d378eb8ee5be51fb25cd452c8f0b80b477f1cb6c`, version `1.2.0-beta.7+gd378eb8.55f78e2d98c2`, source fingerprint `55F78E2D98C2A0B66F537FFE179A7C7B8769296EB21EBA9E9330B9D5133B7BC2`. The same source produced both editions. Full-edition business onboarding, profiles and licensing screens are retained; standalone Retail continues to use simple local setup and separate storage.

The real full executable opened first-run Business setup and an existing store; the real Retail executable opened fresh and completed-store setup. All four owned-process entry checks generated zero new diagnostic reports. Tests inspect only their own QA windows and do not inspect browser tabs or inject Windows-key combinations. The OS reserved all requested Windows combinations on this PC; existing Alt navigation passed.

Visual checks cover English/Urdu at 100%/200%. Selected package inputs scroll into view after layout. Digital customer tables use a finite, scrollable workspace instead of collapsing under large headers. Row checks allow two pixels for WPF border/layout rounding. Recipient history and posting query plans use `IX_ServiceTransactions_RecipientKey_OccurredAt` and `IX_ServicePostings_TransactionId`.

The closed store was backed up and restore-tested before installation; trial migration preserved all original columns and rows in 33 existing tables, with a current EF snapshot and SQLite integrity. `Update-LocalDesktop.ps1` then installed the Release payload in `D:/SupermarketPos/App`. All 428 installed files matched the published hashes. Automatically launched process 17440 matched the existing Wijgandu desktop shortcut, ran at Normal priority and generated zero new diagnostic reports.

- Installed EXE SHA256: `8098ECAF2F79F732EE741ACDECB9E3B4EA40DFA44ED09D6F0BC20C59233EC05C`.
- Installed WPF DLL SHA256: `E19D5151A1C25E9A2A052F7C7DDFE538FFD1BA730B3EE973DD8BD412372EB192`.
- Paired data backup: `Before-update-20261128-081441393.zip`, SHA256 `84DCEC322331D64ED2EB9335B07C92E1305B0A247703B7B12E43C991CA2FF94F`; successfully restored using the application's restore service.
- Paired beta.6 binary backup: `Previous-application-20261128-081441393.zip`, SHA256 `DE26FD0B1E807B70EA0D104ADB3E710B8A4781DF85118A202F9C749652E109D3`.
- Separate Retail installer: `D:/Retail POS/Wijgandu-Retail-1.2.0-beta.7-Setup.exe`, 111,421,192 bytes, SHA256 `78E00E57941F6AB7A86FB7A819DD54262EAE32BF32FB8084A06D0473DFE83455`. Package EXE/DLL/runtime-config hashes match the Retail payload. No store database or Protected data is packaged. This testing installer is unsigned.
