# Digital selling correction

Reuse DigitalServicesView.Record as the cashier page. Move configuration to Settings and history/report sections to Reports. MainWindow/MainViewModel keep the active digital destination selected, put Digital Services after Sales, and Settings between Scanner and More. InAppDialog blocks background input without changing enabled styles.

Package selection searches configured and bundled offers in the selling workspace. Selection/Enter/double-click opens a sale popup with one editable price, optional cost and recipient/provider result fields. No template-import confirmation or product creation CTA. Preserve validation, manual provider result confirmation, idempotency and ledger history. Do not create physical products from digital packages or display digital items in physical Sales. Preserve existing records.

Verify focused finance tests, navigation/modals/package keyboard actions, English/Urdu at 100/200%, then build/update the existing app with backups and hashes. Refresh the separate test installer after verification.

## Verification — 27 November 2026

- All 215 unit/integration checks pass, including optional package cost, unknown-profit reporting, cashier selling versus administrator configuration, and legacy digital-item rejection by physical sales/purchases.
- A backup of the closed store was migrated in isolation. All original columns and rows in 32 existing tables were preserved; the EF snapshot and SQLite integrity check passed. The migration adds only the two cost-known flags, with existing costs retained as recorded.
- The protected register draft was retained unchanged (SHA256 7430CB1C420754D55A3F9FD32053D5B6605A03B1BA54161C7970CFEB7F6C2240).

The sale flow no longer creates physical inventory for packages. Bundled offers are available directly beside configured packages; unsupported postpaid combinations show the existing empty state. Configuration remains in Settings, and digital history and totals are in Reports. This supersedes the mixed physical/digital cashier workflow in the previous beta.

Actual bank/carrier operations remain manual through an authorized provider. No live transfer, carrier activation or physical print is performed by these tests.
