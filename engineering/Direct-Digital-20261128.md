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
- Release payload, both published UI runs, actual entry executables, backup/upgrade proof and installation hashes will be recorded below once verified. Real bank/telecom operations and physical printing are outside these isolated checks.
