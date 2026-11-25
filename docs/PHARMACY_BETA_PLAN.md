# Pharmacy beta — implementation checklist

Extend the existing .NET 8 WPF/SQLite application and existing CameraX/zxing-cpp Android scanner. Preserve the current checkout, purchasing, pairing and non-pharmacy modules.

## Audit

- Working: language-first onboarding, registration/FBR sandbox configuration, immutable sale lines, cash/card/credit ledger, exact integer stock, outer-package conversion, expiry batches, supplier receipts, three invoice actions, popup dismissal, local SQLite backup, offline license verification and signed-update checks.
- Partial: two-level pharmacy units, medicine stock reconciliation, pharmacy-specific feature enforcement, context scanning, scheduled backup status, invoice contact/notes, Urdu font and title-bar direction.
- Missing: separate medicine catalog, indexed shared autocomplete, provisional untracked stock, tablet/strip/pack prices, racks/shelves and allocations/labels, pharmacy text/card inventory, beta packaging and real device/printer validation.
- Repair: F1/F2/F3 shortcuts, F4 confirmation, optional barcode for piece products, scan CTA routing, wrapped medicine rows. Keep receipt Close (the user's newer explicit correction) rather than relabeling it New Sale.

## Files and order

1. **B — Isolation/font**: `BusinessProfile`, `BusinessFeatureFields`, `DigitalServiceLedger`, Register/Inventory/editor visibility, localization/theme, embedded Noto Nastaliq fonts and license. Keep pharmacy restrictions effective even for forged feature flags.
2. **C — Catalog**: new medicine reference entities/import/search service, `PosDbContext`, additive migration and packaged CSV selection. Preserve the supplied 10,068 rows, including 16 duplicate registration values without merging distinct medicines. No invented prices, barcodes or stock.
3. **D/E — Sales and stock**: extend `Product`, `CartLine`, `SaleLine`, `SaleService`, `PurchaseService` and existing view models with reviewed provisional sales, exact configurable selling units, quarantine, reconciliation and locations. Use shared services for all catalog entry points.
4. **F/G — Workflow**: existing scanner controller/receiver and Android decoder; inline pharmacy Register, inventory quick add and receiving; safe shortcuts; existing receipt services and settings for contacts, notes and footer.
5. **H/I — Validation/release**: migrations, focused unit/integration/UI checks, catalog integrity/performance, backup/restore, local license/updater tests, Windows/APK builds and beta deployment notes.

## Release gates

- Medicine CSV redistribution permission is not established by the supplied file. Local testing can proceed; do not claim commercial redistribution clearance.
- Font archive includes SIL OFL 1.1; ship its license with the embedded font.
- Do not claim Windows 7 support for the existing .NET 8 build. Assess supported environments without downgrading security.
- No production FBR calls, cloud deployment, paid services, live telecom operations, physical printer/device testing, or three-site deployment may be claimed without evidence.
- Optional cloud backups and detailed sealed/opened-pack counting are beyond Beta 1. Stock counting can follow stable core checkout, as permitted by the specification.

## Progress

- A–H local implementation completed: shared indexed catalog, inline provisional sales, explicit base/strip/pack pricing, untracked-stock reconciliation, dispensing checks, quarantine audits, racks/shelves and allocation, Code 128/QR labels, combined inventory filters and text/card views, context scanning, checkout shortcuts, immutable invoice profile, credit terms and scheduled local backups.
- Bundled Noto Nastaliq Urdu Regular/Bold/OFL in Windows/PDFs and Regular/OFL in Android. Caption controls remain left aligned in English/Urdu. Pharmacy restrictions apply in the service layer and UI; existing non-pharmacy records remain intact.
- Latest verification: **185 data tests, 978 WPF checks, 8 Android tests and 7 local publisher checks passed**. Final receipt rendering also passed for English/Urdu at 58/80 mm with a grayscale logo; Urdu receipt references/dates remain left to right. Code 128/QR rack label renders passed.
- Schema 10 model snapshot matches migrations. Full backup/restore and upgrade were verified on an isolated copy; every original value in 21 existing tables survived. The installed application was then checked against the same recovery backup: 102 products, 17 sales and one purchase preserved; SQLite integrity `ok`.
- I local deployment completed on 21 November 2026: `D:\SupermarketPos\App\SupermarketPos.exe`, version `1.1.0-beta.1`, opened at the existing store's sign-in screen. Data, recovery archive, previous app and upgrade verification copy are on D:. No fake live sale or business-type change was made.
- APK `POS Scannar 1.8.apk` copied to the user's Downloads; versionCode 10, same signing certificate as 1.7. Test build, not a production signing release.
- Three-site/commercial deployment remains gated by catalog redistribution permission, supported runtime, publisher signing/configuration and real phone/printer/second-PC pilot checks. No final distribution installer, Cloudflare deployment or production FBR connection was created. See `PHARMACY_BETA_RELEASE.md`.

## Standards references checked

- GS1 Digital Link URI syntax: https://ref.gs1.org/standards/digital-link/uri-syntax/1.7.0/ — uncompressed AI 01 identity, ordered 10/21 qualifiers and date attributes are parsed locally; malformed/unsupported links fail closed. Compressed URI syntax is not implemented.
- Microsoft support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core — .NET 8 support ended 10 November 2026. This existing .NET 8 local beta needs a separately verified supported-runtime upgrade before commercial rollout. It does not support Windows 7.
