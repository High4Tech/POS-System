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

The Release WPF run passed 259 assertions, including package Enter/double-click, optional cost, pending/confirmed sale posting, a single receipt overlay, restored background input, the selected Digital Services header, Reports history/totals, and English/Urdu layouts at 100%/200%. Existing keyboard, pharmacy, catalog-image preference and invoice-action checks remained green. Evidence: engineering/qa-data/digital-selling-beta2-5 outside the Git checkout.

## Installed desktop

The required Build-LocalDesktop and Update-LocalDesktop scripts installed 1.2.0-beta.2+gf2e79c0.ec49369b0d3e into D:\SupermarketPos\App. The automatic startup process was 4260, responding at the target of the existing Tech Store POS desktop shortcut. Native accessibility confirmed the existing store's Sign In screen.

Independent comparison matched the EXE, DLL and runtime configuration to the verified payload. EXE SHA256: 680C43CA479D96E7A0DBC3F58E55933A114E2984770E9923EC14B8232977CD08. DLL SHA256: 8D8EA5C7A64156D2AE2EFB7E4936BDA2F287B5EA5270AF44EA1A5780E16DF067. Source fingerprint: EC49369B0D3EDB392987A39A96E4D07F4C259C933CAE8873728E19F787842EA8.

Update backups in D:\SupermarketPos\Backups: Before-update-20261127-054821588.zip (29 entries, 5,398,528-byte database) and Previous-application-20261127-054821588.zip (427 entries, 163,840-byte EXE). The separate pre-migration backup is Before-digital-selling-beta2-20261127.zip. The protected cart draft hash remained unchanged after installation. Live sales/provider operations were not performed during verification.

## Separate testing edition

The local-credential setup passed 12 assertions. The exact packaged High4TechPosTest.exe started successfully with its own fresh QA directory, remained responsive at High4Tech POS Test, and left setup incomplete until local credentials are entered. That controlled smoke-test process was closed; the installed desktop store process was left running. No existing store data or accounts were used by this test.

The refreshed installer is C:\Users\nas\Downloads\High4Tech-POS-Test-1.2.0-beta.2-Setup.exe (107,453,080 bytes). SHA256: 352E15AD57D86265071075F7449002C662204AD841145434F9F33587047AD5B1. All three packaged executable/assembly/runtime-configuration hashes matched the test payload. This is the separate, unsigned testing edition; installation on a second PC and physical provider/printer operations remain untested. No final production distribution installer was generated.

The source checkpoint is tag v1.2.0-beta.2 at f2e79c022651cab24547a4019ec3fbe9e73ecf3a. The previous beta tag remains available.
