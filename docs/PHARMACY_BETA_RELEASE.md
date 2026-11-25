# Pharmacy local beta — 21 November 2026

The existing modified POS was extended and installed as **1.1.0-beta.1** at `D:\SupermarketPos\App\SupermarketPos.exe`. It opened successfully at the existing store's sign-in screen. The installation keeps business data at `D:\SupermarketPos`; no live test sales, inventory reset, owner-password change or business-type change was performed.

## Using the pharmacy features

1. Sign in normally. In **Store profile → Business, licensing & support → Business features**, select **Pharmacy** and the supplied Pakistani medicine reference template, then save. Finish drafts before changing business type. The existing non-pharmacy store remains unchanged until the owner makes this selection.
2. The Pharmacy medicine-catalog section can load the built-in template or import a replacement CSV. It contains **10,068 source rows**, including distinct records sharing a registration number. Registration numbers are not assigned as GTINs. This is a reference catalog, not a statement of current regulatory approval or a stock/price list.
3. Search the Register by medicine name/composition. Choose a catalog record or enter an unlisted name, choose the selling unit, enter price/VAT/quantity and review dispensing requirements. Unknown physical stock remains **Stock Untracked**; its historical cost/profit stays unknown. Existing tracked shortages cannot be bypassed by switching to untracked stock.
4. Configure actual tablet/strip/pack conversions and independently verified selling prices in product details. Receive stock with the supplier, unit, batch/expiry and destination where required. Creating a product during receiving does not post stock: deliberately select/rescan it and confirm the receipt.
5. Manage racks and shelves from Inventory. Assign counted stock explicitly, generate Code 128 or QR labels, and scan a shelf to filter Inventory or choose a receiving destination. A shelf scan never moves stock. Multiple possible sale sources require a selection.
6. Inventory supports remembered table/card views, Needs Setup and stock status, combined category/batch/company/formulation/barcode/expiry/location filters, and quantities grouped by base unit. The filter section can collapse to leave room for the table at larger display sizes.
7. Connect POS Scannar once. Register, Inventory and receiving route scans automatically. A focused primary or outer-pack barcode field takes priority; unrelated modal fields pause background scans. Website-only QR codes are rejected. Uncompressed GS1 Digital Link is supported; compressed links are not.
8. F1 Cash, F2 Card, F3 Udhar and F4 Clear use the existing checkout. F4 asks before clearing. Credit requires a usable debtor identity, not a full customer profile; due date and note are optional. Receipts retain Close/outside/Esc dismissal, Print/WhatsApp/Download, actual cash/change/balance, two contact numbers, ordered enabled notes and the specified High4Tech footer.
9. Choose Urdu in the existing language settings. The supplied Noto Nastaliq font and its OFL license are bundled in the desktop app, PDFs and APK; no separate Windows font installation is needed. Numeric identifiers remain left to right.
10. Daily local backup runs while the POS is open, including startup catch-up. Settings show the folder, last success and any failure. It is not a Windows service or a cloud backup.

## Recovery and data preservation

- Full pre-update archive: `D:\SupermarketPos\Backups\Before-pharmacy-beta-20261121.zip`.
- Previous executable folder: `D:\SupermarketPos\App-before-pharmacy-beta-20261121`.
- Restored/upgraded verification copy: `D:\SupermarketPos\Verification\pharmacy-existing-upgrade-20261121`.
- Before and after the installed app started, all original columns/rows in **21 pre-existing tables** matched the recovery archive. **102 products, 17 sales and one purchase** remained; SQLite integrity was `ok`.
- Do not run the previous executable against the migrated database. Recovery requires the matching application and data archive after closing the POS. Keep the complete current data folder and diagnostics before restoring.

## Changed code and migrations

The change reuses the WPF/.NET/SQLite architecture and existing CameraX/zxing-cpp Android project. No original GitHub checkout was downloaded or substituted.

| Area | Main files |
| --- | --- |
| Business isolation | `Domain/Business/BusinessProfile.cs`, `BusinessSetup`, `DigitalServiceLedger`, `TradeLedger`, `FbrCompliance`, existing setup/settings fields |
| Catalog | `Domain/Entities/MedicineReference.cs`, `Infrastructure/Business/MedicineCatalogService.cs`, `Persistence/MedicineSearchSchema.cs`, supplied CSV asset |
| Stock, dispensing and locations | `Product`, `InventoryBatch`, `StockLocation`, `StockReconciliation`, `CartLine`, `SaleLine`, `PharmacyInventory`, `StockLocationService`, existing sale/purchase services |
| Screens and scanning | Existing POS/Products/Purchases view models and views; `MedicineSearchBox`, `PharmacySalePanel`, `PharmacyQuickAddPanel`, `StockLocationsPanel`, `ScannerController`, `PhysicalBarcodeInput` |
| Receipts and backup | `ReceiptDocument`, `ReceiptService`, `ReceiptFonts`, `LocationLabelDocument`, `BarcodeImage`, invoice-note settings, `BackupSchedule`, `DataBackup` |
| Android | Existing `mobile-scanner/app` GS1 parser/outcomes, Urdu font/theme/license resources; version 1.8 / code 10 |
| Verification | Pharmacy/catalog/location/backup regression tests, `engineering/UiChecks`, `engineering/ModelTools`, local publisher tests |

Paths in the table are within the existing `upstream/src` projects unless noted. Additive migrations are **MedicineCatalog, PharmacyDispensing, PharmacyLocations, InvoiceSnapshots, CreditTerms, DefaultStockLocation and MedicineDetails** (20–21 November 2026), with the current model snapshot and backup/update compatibility limit set to **schema 10**. No downgrade migration deletes business records.

## Executed checks and builds

| Check | Result |
| --- | --- |
| .NET data/domain/integration tests | **185 passed** |
| WPF checks | **978 passed**, including English/Urdu at 100%/200%, shared popup dismissal, actual active workflow routing, repeated scans, focused primary/pack capture, fast Enter search, combined inventory filters, receiving destination and F4 cancel/confirm |
| Thermal receipt render checks | English/Urdu × 58/80 mm; width, grayscale image/text and successful rendering verified; representative images inspected |
| Rack-label checks | Code 128/QR round-trip decoding, both PDF render formats; Code 128 uses a full-width label to avoid over-dense printing |
| Catalog | 10,068 rows imported in isolated real-file SQLite, duplicate identities preserved, bounded local search, no seeded inventory/prices |
| Backup/restore/migration | Real recovery archive staged/restored into a separate folder; schema/model match and all old values preserved; installed startup compared again |
| Publisher/licensing | **7 local tests passed**: authentication, seat limits/idempotency, offline certificate and transfer, release signatures/entitlement, consent and rate limits; .NET verifies Worker-signed test certificates |
| Android | **8 tests passed**, assembleDebug and lintDebug succeeded; lint: 0 errors, 8 warnings |
| Windows | Release win-x64 self-contained build; verified local development metadata with no production publisher endpoint or activation enforcement; installed and opened successfully |

Synthetic test data/screenshots are in `engineering/qa-data/pharmacy-final-20261121-h`. Actual business-data backup and verification files remain on D:. Local Windows build output is in `engineering/release-staging/pharmacy-beta1-20261121/app`; it is configured for this PC's D: folder and is **not a shareable installer**.

APK: `C:\Users\nas\Downloads\POS Scannar 1.8.apk`.

- SHA-256: `09936EE11C3FB0E8821EDB2BD1006172E9BC5622BC455FB620A6DB71AF318D4A`.
- Certificate matches POS Scannar 1.7, so it can update that APK. It remains a debug-signed test build. No physical phone installation or camera/network benchmark is claimed.

## Remaining release gates and three-pharmacy pilot

This is a **local functional beta**, not a completed commercial or regulatory certification.

- **Supported runtime:** the existing application targets .NET 8, whose support ended 10 November 2026. A verified upgrade to a supported runtime is required before commercial rollout. Windows 7 is not supported by this build; no insecure fallback was introduced. See [Microsoft's lifecycle policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) and [.NET 8 supported operating systems](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md).
- **Catalog redistribution:** the supplied spec requires verifying permission before putting the dataset into a commercially distributed installer. The source/license confirmation remains outstanding. Local embedding/testing is implemented; the commercial build omits the dataset unless approval is explicitly configured.
- **Commercial deployment:** publisher HTTPS endpoint, signing public key/date, actual Windows code-signing certificate and authorized cloud configuration must be supplied and tested. Existing signed-update checks were preserved. No final installer, Cloudflare deployment, permanent license keys or production FBR integration was created. No customer database is included in release output.
- **Physical acceptance:** for each of three pharmacies, use a separate chosen data folder and device-bound test activation. Verify offline restart, supplier receiving of 1 pack = 30 tablets, sales of 2 tablets then 1 strip leaving 18, batch/source selection, scanned rack labels, reconnect and duplicate events, cash/card/Udhar, 58/80 mm paper output and a restore drill. Exercise a low-tier phone, shadowed barcodes and the actual shop Wi-Fi. Record results before calling the three-site pilot passed.
- **Installer/update acceptance:** a signed two-version install/update/restart and interrupted-install recovery on a clean second PC still need execution. Automated local Velopack/feed checks do not establish these physical deployment results.

Optional encrypted cloud backup, detailed sealed/open-pack counting and a dedicated stock-count session workflow are deferred beyond Beta 1 as allowed by the spec. Owner verification of previously untracked opening stock is implemented. No substituted-medicine advice or automatic tax/legal determinations were added.

GS1 parser scope follows the [GS1 Digital Link URI syntax](https://ref.gs1.org/standards/digital-link/uri-syntax/1.7.0/). A medicine registration field is not treated as an official product barcode, current approval or permission to dispense.
