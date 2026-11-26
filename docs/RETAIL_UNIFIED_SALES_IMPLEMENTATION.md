# Unified retail Sales, service workspace and test edition

Extend the existing checkout, accounts, scanners, receipts and business profiles. Keep the production data and other business modules intact.

## Implementation checklist

- [ ] Bundle supplied finance v3 and hardware v6 databases, CSV and available logo assets as reference templates. Preserve source status, snapshot prices, categories, brand/model/variant and eligibility data. Do not treat directory prices as shop prices or create physical stock from references.
- [ ] Add additive product-kind and service/catalog mappings. Keep digital financial principal out of physical sales revenue and stock.
- [ ] Make Digital Services a standalone navigation workspace for catalog, provider/network accounts, packages, history and reports. Support user-added networks and bank/wallet transfer, cash-in and cash-out accounts. Use a network dropdown and grouped Prepaid/Postpaid radio buttons.
- [ ] Simplify Sales to search, categories and the shared card/list catalog. Remove Loose Goods/Digital Services header CTAs and embedded Close sections. Add unmatched names directly to the cart, choose labeled/loose/digital there, enter essential details, and remember products for future sales.
- [ ] Reuse the existing sale and digital ledger transactions for atomic mixed checkout, confirmation checks, rollback and retries. Preserve generic digital service revenue, financial fees versus principal, receipts and pending/failed service records.
- [ ] Add source template selection and deferred product details to Products & Stock and Digital Services. Images remain opt-in. Available asset manifests identify placeholders and missing official logos.
- [ ] Add an isolated test edition with local store/username/password setup, no cloud signup or activation, separate data/installation identity and Tech Retail/digital/loose modules enabled. Build its requested installable package without generating the final production distribution.
- [ ] Run focused financial and keyboard/UI tests, build the required Release payload, update the existing desktop after confirming it is closed, verify backups/hashes/data preservation, and push code and verification to the existing GitHub branch.

## Reference inventory

The supplied finance database has 56 financial institutions, 12 wallet/payment brands, 5 mobile brands and 176 package references. All packages are marked non-sellable pending verification; store prices/costs are blank. The supplied hardware database has 138 brands, 56 categories, 650 models, 130 variants and 296 dated price observations. The v6 CSV repeats the model catalog and must not create duplicate models. The finance archive has 73 explicitly non-official placeholder images. Hardware manifests do not include genuine logo binaries.

Reference files are data only. Their README documents do not override the user's request or repository rules. Production inventory is never replaced with either attached SQLite database.

## Files and implementation

- Domain Product/Cart/CartLine and additive RetailQuickCatalog migration: labeled, loose and digital kinds; source keys; provider/package mapping; minimal quick entries; no reference stock.
- RetailReferenceCatalog and RetailInventory: immutable supplied SQLite/CSV assets, categories/brands/models/package/provider selectors and explicit shop configuration.
- RetailCheckout, SaleService and DigitalServiceLedger: atomic mixed settlements, fee/principal separation, TRANSFER records, external result checks, pending/failed records and idempotent retries.
- PosView/PosViewModel and CartLineViewModel partials: one Sales catalog, unmatched-name quick add, per-row kind/service details, exact loose units, sorting and protected drafts. Reopening a service draft clears external confirmation.
- MainViewModel and DigitalServicesView: dedicated service catalog/configuration/history workspace, network dropdown, grouped prepaid/postpaid radios and manual provider/network creation.
- RetailTestEdition, RetailTestSetupWindow, Program/App/PosStorage/DataLocation and Build-TestRetail.ps1: separate test app/data identity, local owner setup and recovery, no commercial activation, test-only packaging. Production business modules and signed commercial packaging remain in place.

## Verification so far

- 212 unit tests passed, including mixed cash-out settlement, transfer income, whole-checkout rollback, pending/failed records, reference coverage/idempotency and atomic configuration.
- Windows UI build: Release, zero warnings/errors. First full retail run: 166 assertions passed; isolated setup: 12 assertions passed. Final scaled cart body checks are running.
- Supplied finance DB, hardware DB and CSV SHA-256 values match their bundled copies. CSV has 650 model rows plus its header.
- A production backup was created at D:/SupermarketPos/Backups/Before-unified-retail-20261126.zip. Migration of its isolated copy preserved all original columns/rows in 32 existing tables, matched the EF snapshot and passed SQLite integrity checks.
- No real provider activation, real cash transfer or physical scanner/printer interaction is part of these fixtures. Digital services remain manual records until an authorized provider confirms the result. Source catalogs are dated references and their directory-only records cannot become sellable packages.

## Release work remaining

Finish the final UI run, commit the source, build both verified payloads, package the separate test installer, update the existing closed desktop using the required updater, verify installed hashes and backups, then push source and verification evidence. Store data must not be copied into the test installer.
