# Register UX correction

Extend the current WPF application; preserve the cart, scanner, tax and posting services.

- BusinessForm / BusinessSettingsWindow / SearchDropDown: ordinary hover tooltips, stretch forms and consistent searchable fields.
- PosView / PosViewModel.Measured / LooseGoodsPanel: two contextual entries and inline quantity, amount and package choices.
- DigitalServicesView / package editor / DigitalServiceLedger: compact transactions and configured network/account catalog; keep confirmation and idempotency.
- ReceiptPreviewWindow / ReceiptPreviewDialog / InAppDialog: receipt overlay with Print, WhatsApp and Download; request missing number inline.
- Additive package metadata migration and focused ledger/UI regression checks. Back up the installed data before updating; do not build a distribution installer.

## Behavior

Register has two optional entries: Loose Goods (also used for configured boxes) and Digital Services. Each uses the existing catalog area temporarily; closing restores the category/product grid and leaves the cart intact. The contextual workspace reserves room for the cart at large display sizes.

Receipts now use a modal layer inside the existing window. Print, WhatsApp and Download remain the primary actions. A missing recipient or printer is requested inline only when its action is clicked; sharing still opens a draft and does not send or attach a file automatically.

Digital Services shows only essential fields and configured fee summaries. Its menu retains history and reports. Provider/package editing is in Settings → Business, licensing & support → Digital service configuration. No provider packages/prices are seeded. The catalog filters active provider records by network and account type. Manual recording still requires a real provider result, explicit verification and reference before posting; pending/failed rows do not post. Saved package tariffs are checked at the persistence boundary, and details are snapshotted into the transaction.

The additive ServiceCatalog migration preserves existing records and prices. Explicit legacy Prepaid/Postpaid types are carried forward. Other legacy packages remain in management as Unspecified until the owner classifies them. Existing free-text fee/commission policies are retained; numeric settlement defaults must be configured from the provider agreement, rather than parsed from prose. Schema generation is 3.

Settings uses available window space without shrinking individual tabs. Field label rows align across columns. At 200% on a short screen, the content viewport can scroll while navigation and Close remain available. Help icons use native hover tooltips with no click action or manual mouse capture. Editable dropdowns have independent filtered views, preserving other fields that use the same source.

## Validation and local installation — 20 November 2026

- 152 .NET tests passed, including package snapshots, unconfirmed/duplicate posting prevention, configured charges and legacy catalog migration.
- 520 desktop checks passed in isolated data, including English/Urdu at 100%/200%, two Register entries, inline cart visibility, package filters, receipt actions, missing-recipient input and dropdown filtering after returning to the screen. Screenshots: `engineering/qa-data/register-ux-20261120-return-check/screens`.
- The self-contained Windows build was published from this modified checkout and verified as a local development build. No distribution installer or APK was generated.
- Installed in `D:\SupermarketPos\App`. Pre-update database/media backup: `D:\SupermarketPos\Backups\Before-register-ux-20261120.zip`; previous application: `D:\SupermarketPos\Backups\App-before-register-ux-20261120`.
- All 14 business-table fingerprints match before/after migration. Existing data includes 102 products, 8 categories, 14 sales, 25 sale lines and 1 purchase.
- Native startup was observed without an error dialog. The application resumes the existing unfinished Urdu business setup (Completed=false, Step=0), matching the pre-update backup. Setup completion and authentication were not automated.
- Physical printing, real WhatsApp delivery and telecom activation were not exercised; receipt tests use fake printer/handoff adapters, and activation remains with the authorized provider.

## Receipt dismissal correction — 20 November 2026

The shared receipt popup now has a localized Close action in its header, including after checkout. Clicking the dimmed area or pressing Escape also dismisses it; clicking inside does not. Both sale and digital-service receipts use this shared host. Closing restores the underlying screen and cancels any unfinished inline receipt prompt without changing the saved transaction.

548 isolated desktop checks passed. Dismissal checks raise the actual Close event and cover backdrop, inside-click and Escape behavior in English/Urdu at 100%/200%. Printing and WhatsApp use test adapters; physical printing was not retested. The installed build is in `D:\SupermarketPos\App`; pre-update backups are `D:\SupermarketPos\Backups\Before-receipt-close-20261120.zip` and `D:\SupermarketPos\Backups\App-before-receipt-close-20261120`.

Post-install verification found one new sale after restart. All 16 pre-update sales and 31 pre-update sale lines remain identical; product stock changes match that new sale exactly. The other 11 checked business tables retain their pre-update fingerprints.
