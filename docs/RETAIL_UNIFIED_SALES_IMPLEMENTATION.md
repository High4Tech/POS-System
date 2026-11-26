# Unified retail Sales, service workspace and test edition

Extend the existing checkout, accounts, scanners, receipts and business profiles. Keep the production data and other business modules intact.

## Implementation checklist

- [x] Bundle supplied finance v3 and hardware v6 databases, CSV and available logo assets as reference templates. Preserve source status, snapshot prices, categories, brand/model/variant and eligibility data. Do not treat directory prices as shop prices or create physical stock from references.
- [x] Add additive product-kind and service/catalog mappings. Keep digital financial principal out of physical sales revenue and stock.
- [x] Make Digital Services a standalone navigation workspace for catalog, provider/network accounts, packages, history and reports. Support user-added networks and bank/wallet transfer, cash-in and cash-out accounts. Use a network dropdown and grouped Prepaid/Postpaid radio buttons.
- [x] Simplify Sales to search, categories and the shared card/list catalog. Remove Loose Goods/Digital Services header CTAs and embedded Close sections. Add unmatched names directly to the cart, choose labeled/loose/digital there, enter essential details, and remember products for future sales.
- [x] Reuse the existing sale and digital ledger transactions for atomic mixed checkout, confirmation checks, rollback and retries. Preserve generic digital service revenue, financial fees versus principal, receipts and pending/failed service records.
- [x] Add source template selection and deferred product details to Products & Stock and Digital Services. Images remain opt-in. Available asset manifests identify placeholders and missing official logos.
- [x] Add an isolated test edition with local store/username/password setup, no cloud signup or activation, separate data/installation identity and Tech Retail/digital/loose modules enabled. Build its requested installable package without generating the final production distribution.
- [x] Run focused financial and keyboard/UI tests, build the required Release payload, update the existing desktop after confirming it is closed, verify backups/hashes/data preservation, and push code and verification to the existing GitHub branch.

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

## Verified release — 1.2.0-beta.1

Source revision: 02f75684df63124833e2ba4490e988afc3e28faa. The source is versioned as normal commits; installers and intermediate payloads are outside the repository.

- 213 unit tests passed, including mixed cash-out settlement, transfer income, rollback, pending/failed records, reference coverage/idempotency, atomic configuration and configured packages following catalog changes.
- Release Windows UI build: zero warnings/errors. 174 retail keyboard/UI assertions passed; 12 isolated local-setup assertions passed. English/Urdu and 100%/200% layouts were captured and inspected. Small windows at large display sizes scroll the register workspace so neither product browsing nor cart editing collapses away.
- Bundled finance DB, hardware DB and CSV hashes match the supplied files. The CSV contains 650 models plus its header.
- Migration of an isolated production backup preserved original columns/rows in all 32 existing tables, matched the EF snapshot and passed SQLite integrity checks.
- The installed live database then passed integrity and history comparisons for all 31 tables outside business configuration. Only the requested module/template settings and configuration revision changed. The schema is 20261126180000_RetailQuickCatalog.
- Existing saved cart draft SHA-256 remained 7430CB1C420754D55A3F9FD32053D5B6605A03B1BA54161C7970CFEB7F6C2240.
- The required updater installed version 1.2.0-beta.1+g02f7568.6d3210347ec2 into D:/SupermarketPos/App and launched process 14556 at the exact target of the existing Tech Store POS desktop shortcut.
- Installed EXE, managed assembly and runtime configuration hashes match the verified desktop payload. Data backup: D:/SupermarketPos/Backups/Before-update-20261126-172527609.zip (29 entries). Previous binary backup: D:/SupermarketPos/Backups/Previous-application-20261126-172527609.zip (427 entries). Both ZIPs were opened and verified.

| Artifact | SHA-256 |
| --- | --- |
| Desktop SupermarketPos.exe | 0571C77A36EC0A313543A515BD0D63506B4C268398582E08ABC0010CC9013D1D |
| Desktop SupermarketPos.dll | CC8EF7267B44F25C2D20088F977DEB030CB19C79DA7046D3985B52841C2C2581 |
| Desktop runtime configuration | 6A0B658F4CD7EE506FEFBF79408BB5EFE2934BED86CC3E3754285780B9B6853E |
| High4Tech-POS-Test-1.2.0-beta.1-Setup.exe | 5183C17B77DE0796456573FED58EE520490987408E18BF169E9AD9EA7292D2EB |

## Separate testing PC installer

Download file: C:/Users/nas/Downloads/High4Tech-POS-Test-1.2.0-beta.1-Setup.exe (107,449,247 bytes).

The test app uses pack ID High4TechPosTestApp, executable High4TechPosTest.exe, a separate mutex/bootstrap pointer, and default data folder %LOCALAPPDATA%/High4TechPosTest/Data. It does not embed the live D: data path or publisher activation credentials. Its exact published executable opened the local setup screen against a fresh QA folder; the resulting database contained zero users and zero shop products. The local-owner setup fixture verifies required credentials, password confirmation, recovery code, language direction and enabled Tech Retail/loose/digital features. The test installer contains no shop database.

On the testing PC, install it and choose the store name, local username/password and language. Use Products & Stock → More for hardware/category templates. Use Digital Services for provider/network setup, packages, service catalog, history and reports. Enter real opening balances, shop prices and provider costs. Configured packages also appear in the shared Sales catalog; package details/price are filled automatically. Unmatched Sales names offer Add Product, then labeled/loose/digital selection and essential cart fields. Complete other inventory details later.

The package was created with the existing Velopack 1.2.161 integration and verified against its full package contents. See the [official packaging reference](https://docs.velopack.io/packaging/overview). This requested local test installer is unsigned; the signed commercial release script was not weakened and no final production distribution was generated.

## Practical verification limits

The installer itself has not been installed on the second PC; its exact payload startup and packaged binary hashes were checked here. No real provider activation/transfer, physical scanner/camera/Bluetooth/Wi-Fi session, or physical printer transaction was performed. Digital services remain manual records and require the authorized provider's actual result/reference before posting a confirmed transaction. Failed/pending records remain separate from confirmed money movements.

The supplied package prices are dated references, all source offers require verification, and directory-only entries cannot be sold as packages. The supplied logo files are explicitly non-official placeholders; genuine provider/hardware logos were not supplied. Catalog images remain opt-in. No reference template fabricates inventory or silently overwrites existing shop records.
