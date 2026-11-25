# POS feature and keyboard shortcut audit

Audit date: 25 November 2026 (Asia/Karachi). Installed build: **1.1.0-beta.5**. Repository baseline: `332d774` plus the existing local Windows changes. The audit builds the current source and uses isolated SQLite data. Evidence from source inspection is distinguished from executed tests.

## A. Executive coverage summary

<!-- AUDIT_COUNTS -->

Status legend: **W** implemented & tested, **U** implemented and not independently tested, **P** partially implemented, **B** broken/regression, **M** missing, **I** intentionally excluded, **D** deferred/requires clarification. **UI only** is reserved for a visible dead action. **Blocked** is reserved for required verification prevented by an external dependency.

## B. Feature matrix

| Feature | Business Type | Status | UI Entry | Implementation | Test Evidence | Next Action |
| --- | --- | --- | --- | --- | --- | --- |
| A1 Responsive top navigation, no sidebar, page navigation | All | W | Native main shell | MainWindow and MainViewModel page commands | Desktop layout at requested widths | — |
| A2 Native Windows title bar and store name | All | W | Window caption | WindowCaption and StoreProfile | Desktop title-bar checks and rendered shell | — |
| A3 Scanner connection status | All | W | Top bar | ScannerController and status bindings | Scanner lifecycle tests and shell rendering | Physical device check separate |
| A4 Account/sign out and overflow destinations | All | U | More | MainWindow.OpenMore and MainViewModel.Logout | Source and layout, each action not rerun on the live app | — |
| A5 Window resizing | All | W | Native caption, resized shell | WindowCaption and FitNavigation | Rendered 1920, 1366 and 1280 widths | — |
| A6 Modal focus, Escape and content sizing | All | P | Shortcut help and other overlays | InAppDialog and ShortcutReference | Shortcut focus and sizing passed; legacy host still defaults to a stretched outer card | Apply compact sizing selectively after testing each legacy modal |
| A7 English and Urdu UI | All | W | Onboarding and Store profile | LocalizationManager and resource files | English/Urdu desktop screenshots including font resolution | Translation completeness outside tested screens not established |
| B1 Register Cards, List, saved view preference and category strip | All | W | Register | PosViewModel.Catalog and PosView | Desktop search/restoration tests | — |
| B2 Product text search and barcode lookup | All | W | Register search and scanner | PosViewModel.Scanning and RegisterBarcodeLookup | RegisterBarcodeTests and desktop search checks | Physical scanner throughput not measured here |
| B3 Medicine reference autocomplete | Pharmacy | W | Register medicine search | MedicineSearchBox and MedicineCatalogService | MedicineCatalogTests and Pharmacy WPF checks | — |
| B4 Direct Add to Cart and remove row | All | W | Cards, list, scanner and cart row | PosViewModel and Cart | Desktop and SaleServiceTests | — |
| B5 Inline quantity and selling unit | All where applicable | W | Register cart | CartLineViewModel and CartLine | RegisterRestorationTests and MeasuredRetailTests | — |
| B6 Authorized price adjustment | All | P | Editable cart price | CartLineViewModel, PosViewModel.Catalog, SaleService | Pharmacy owner check and override regression tests; general profiles allow signed-in cashiers by design in source | Define and enforce one cross-profile role rule if required |
| B7 Cart discounts and totals | All | W | Cart discount / checkout summary | PricingService and Discount types | PricingServiceTests, DiscountAndPaymentTests and checkout tests | — |
| B8 Cash checkout and change | All | W | Register F1 | OpenCashCommand and SaleService | F1 WPF, cash/change and persistence tests | — |
| B9 Card checkout | All | W | Register F2 | OpenCardCommand and CardPayment | F2 WPF and posting tests | Payment records an external terminal settlement; device integration unverified |
| B10 Udhar checkout and current balance | All | W | Register F3 | OpenCreditCommand, CreditPayment and SaleService | F3 WPF and persisted partial payment tests | — |
| B11 Clear Cart confirmation | All | W | Register F4 | ClearCartCommand and guarded overlay | WPF confirmation and Escape test | — |
| B12 No inventory movements on draft changes | All | W | Cart quantity, unit and repeat | Cart, StockLocationService and SaleService | MeasuredRetailTests, restoration tests and focused probe | — |
| C1 Product creation and editing | All | W | Inventory → New/Edit | ProductsViewModel and InventoryService | Existing desktop saves and checks | — |
| C2 Medicine name, composition, strength, formulation and manufacturer | Pharmacy | W | Pharmacy product editor | ProductEditorView, ProductEditViewModel and medicine snapshots | Reference prefill and editor checks; source-missing fields stay editable | — |
| C3 Optional barcode | Pharmacy | W | New Product | ProductEditorView and BarcodeService | PharmacyDispensingTests optional barcode case | Other profiles may require a barcode or assigned code |
| C4 One contextual Sell as selector; no Add Full Details Later | Pharmacy | W | New/Edit Product | ProductEditorView and ProductEditViewModel | Name-only save and layout checks | — |
| C5 Product prices and package conversions | All where applicable | W | Inventory editor and Register cart | Product, CartLine and PharmacyInventory | Packaging and pharmacy unit tests | — |
| C6 Opening stock, movement and correct totals | All | W | Inventory editor and stock actions | InventoryService and PharmacyInventory | Opening pack movement, stock and location tests | — |
| C7 Supplier assignment | All | U | Editor supplier selector | ProductsViewModel → Product.SupplierId | Source and layout; selector-to-persistence happy path not independently rerun | — |
| C8 Rack/shelf receiving and balances | Pharmacy | W | Rack management and purchase draft | StockLocationService and PurchaseService | Transfers, shelf balances and receipt tests | Label reliability tracked separately |
| C9 Rack QR label round trip | Pharmacy | B | Rack label export/scan | LocationLabelDocument and decoder | Suite returned null for a rendered QR in one run | Investigate generation/quiet zone and random payloads |
| C10 Batch and expiry | Eligible products; additional Pharmacy rules | W | Product, Purchase, Register | InventoryBatch, FbrCompliance and PharmacyInventory | InventoryBatchTests, ExpiryInventoryTests, and location cases | — |
| C11 Needs Setup and Stock Untracked | Pharmacy | W | Inventory filters/status and Register | ProductRowViewModel, PharmacyInventory and SaleService | Name-only and untracked posting tests | — |
| C12 Inventory import/export | All | P | Inventory More → Import / template / shelf labels | BulkRecordService and ProductsViewModel | CSV import tested; downloadable template and labels exist; no full product data CSV export found | Add full data export if required |
| C13 Inventory filters | All with Pharmacy extensions | W | Inventory toolbar | ProductsViewModel | Desktop filter and search verification | — |
| D1 Customer creation/editing | All | P | Checkout customer details / Customers → Customer details | SaleCustomer, BulkRecordService and CustomerFieldsViewModel | Sale-attached customer persistence tested; no standalone customer master | Confirm whether independently managed accounts are required |
| D2 Customer search and details | All | W | Customers and Register chooser | CustomersViewModel and CustomerFieldsView | Focused invoice/customer fixture and desktop context checks | — |
| D3 Credit customer identification and outstanding balance | All | W | Udhar / Customers | Sale.CustomerIdentity, Sale.Outstanding and CustomerRow | AccountsAndDevicesTests and focused probe | Identity based on recorded email/phone |
| D4 Ledger and partial repayment | All | W | Customers → Customer ledger / Receive payment | CustomersViewModel and TradeLedger | Idempotent partial-payment probe and ledger dialog open | — |
| D5 Customer transaction summary | All | P | Customers and Alt+F1 / Alt+F12 | CustomerRow and ViewLedgerCommand | Outstanding and ledger exist; both keys open one ledger | Define any distinct summary totals or grouping |
| D6 Frequently purchased and previous invoices | All | W | Register customer actions | PosViewModel.Customer and SaleHistory | Focused customer dialogs opened from a posted invoice; selection routing source-inspected | History capped at 500 candidate invoices per customer |
| D7 Repeat previous sale | All | W | Reports, Customers and Register | RepeatSaleCommand and PosViewModel.Repeat | Focused check uses current price, preserves existing cart, and posts nothing | Tracked items with batch or serial need manual selection |
| D8 Repayment reversals and customer credit redemption | All | M | — | TradeLedger posts receipts; customer credit exists only on returns | No offset/reversal action for account payments found | Design linked compensating entries and credit application |
| E1 Invoice number/reference, date, customer/phone, cashier, payment, status and amount filters | All | W | Sales & Reports → Sales History | ReportsViewModel.LoadHistory and FilterInvoices | Focused combinations and source inspection of all filter fields | Data is materialized for entire selected date window |
| E2 Saved item snapshots, historical prices, discounts and VAT | All | W | Sales History and receipt | Sale, SaleLine and ReceiptDocument | Persistence tests, focused current-price change test and receipt rendering | Receipt header uses current store metadata |
| E3 Original tender, paid, due and current account balance | All | W | Receipt and customers | ReceiptDocument and Sale.Outstanding | Cash/change, credit tests and read-only receipt actions | Current outstanding belongs in account/history, original due in receipt |
| E4 Reprint, PDF and WhatsApp draft | All | W | Receipt preview | ReceiptPreviewWindow, ReceiptPrinter and InvoiceHandoff | Desktop fake printer/handoff and PDF assertions | Physical printer and external WhatsApp sending not tested |
| E5 Repeat invoice | All | W | Sales History → Repeat | MainViewModel and PosViewModel.Repeat | Focused repeat staging and immutable source confirmation | — |
| E6 Per-invoice audit history | All | P | History and returns | Sale snapshots, SaleReturns, AccountPayments, BusinessAudits | Return reason, operator and receipts retained; no unified invoice event timeline or print audit | Add a read-only linked event timeline |
| F1 Return against original invoice and cash refund | All eligible profiles | W | Sales History → Return Items | ReturnInvoicePanel and ReturnsService.RecordAsync | Cash confirmation, original totals and stock movement tests | FBR-required profiles intentionally block local returns |
| F2 Card refund state | All eligible profiles | W | Return Items → Request/Confirm/Fail | ReturnsService.ResolveCardAsync | Pending then external confirmation test | No live terminal refund integration |
| F3 Udhar adjustment and partially paid return | All eligible profiles | W | Return Items | ReturnsService and Sale.ApplyReturnCredit | Actual outstanding/remainder test and repayment-then-return probe | Remainder is stored as credit; redemption missing |
| F4 Mixed cash/card refund split | All | I | — | Sale stores a single method; Udhar supports initial cash | Partial Udhar tested; no split-tender sale model | Decide business rules before adding split tenders |
| F5 Partial line, multiple partial returns, over-return prevention | All eligible profiles | W | Return qty | ReturnsService proportional rounding and accumulated return qty | Cash return and focused repeated-quantity test; see results below | — |
| F6 Batch or device identity at return | Tracked batches/devices | U | Return identity field | ReturnsService matches original lot / serial / IMEI | Source guard; return-specific scan/identity path distinct from receiving tests | Exercise real batch and device inputs |
| F7 Safe inventory disposition and quarantine | All, stricter for Pharmacy | P | Return Items → Restock verified | ReturnsService.PostAsync and StockMovement | Ordinary restock idempotent; tracked/placed stock barred from saleable stock | No dedicated quarantine inventory record/workflow or release chain |
| F8 Return authorization, audit record, idempotency | All eligible profiles | W | Admin Return Items | auth.RequireAdmin, SaleReturns and unique confirmation ID | Cashier denied, replay and date/status tests | Replay does not compare submitted identity once already posted |
| F9 Return receipt/credit note | All | M | — | No separate return document or FBR credit-note path found | Return records appear in original invoice’s return dialog | Add a separate document and handle FBR in an authorized workflow |
| G1 Sales reports | All | W | Sales & Reports → X/Z and PDF | ReportService, ReportDocument | Report arithmetic and sales history code exercised | Reports are read-only summaries; Z is not a closing lock |
| G2 Purchase reports | All | P | Purchases → History / Supplier balances | PurchaseService and PurchasesViewModel | Receipt/settlement tested; no dedicated period report export found | Clarify required report format |
| G3 Inventory reports | All | P | Inventory filters, lots, labels and movement history | ProductsViewModel and StockLocationService | Stock and movement tests; no dedicated valuation/movement statement | Define inventory valuation/report requirements |
| G4 Outstanding balances, ledger and item sales history | All | W | Customers / Inventory editor | TradeLedger, SaleHistory and WPF history tables | Focused dialogs and accounting probe | Item history capped at 500 invoices |
| G5 Cashier activity report | All | P | Sales History search by operator | Sale.CashierName and ReportsViewModel | Saved cashier field and search; no consolidated operator report | Define staff totals and allowed viewers |
| G6 Invoice discounts and miscellaneous charges | All | P | Cart discount | PricingService and SaleLine discounts | Discount math tested; generic surcharge entry absent | Define taxable/non-taxable surcharge before adding |
| G7 Invoice margin and cost permission | All | B | Reports margin; Dashboard gross profit; invoice line Unit cost | Sale.GrossProfit, ReportsViewModel.ShowMargin, DashboardViewModel and SaleItemsView | Cashier profit exposure reproduced; invoice item column binds cost without role condition | Apply one cost/margin permission to all UI and export paths |
| G8 Historical COGS and missing-cost protection | All | W | Invoice margin | Immutable SaleLine.UnitCost; Sale.GrossProfit returns null if any unknown | Current cost edit / missing-cost tests; pack basis computed by sale service | — |
| G9 Return adjustments and historical accounting accuracy | All | P | Sales History → return totals, Customers | ReportsViewModel and ReturnsService | Receivable and original accounting verified; X/Z aggregate posted sales without subtracting return amounts or restated COGS | Define gross-versus-net, tender, tax and margin treatment in outputs |
| H1 Invoice templates and customer-associated quotations | All | M | — | Receipt settings and CSV templates are present; neither saves reusable carts or quotations | No UI/service/entity path found | Optional; define semantics if requested |
| H2 Delivery orders | All | M | — | No delivery order workflow found | Code inventory | Optional |
| H3 Due items, refused-sale tracking, item associations and item notes | All | D | — | Reference terms have no defined POS equivalents; Udhar dues and receipt notes do not cover these workflows | Code inventory | Business-rule decision before implementation |
| H4 Area-wise customer search | All | P | Customers search | CustomerRow.Matches includes address substring | Address lookup present; no structured area grouping | Optional |
| H5 Invoice header information | All | W | Receipt preview and settings | ReceiptDocument and BusinessProfile | Rendered English/Urdu receipts; receipt number/date/shop header | No special header editor hotkey |
| H6 Post/lock status | All | P | Checkout and history | SaleService atomic posting and immutable lines | Saved transactional data verified; no separate draft/post/lock workflow | Optional workflow definition |
| H7 Printing history | All | M | — | ReceiptPrinter and handoff have transport outcomes but no per-invoice print journal | No persistent print audit API found | Optional |
| H8 Multiwindow/workspace cycling | All | I | One workspace and More | MainViewModel.CurrentViewModel and modal documents | Single-workspace navigation inspected and resized | Intentional top-navigation model |
| X1 Local backup and restore | All | W | Business settings → Data & backups | DataBackup and BackupSchedule | CommercialStorageTests and prior isolated migration | Live store data untouched in this audit |
| X2 Licensing | Commercial mode | W | Onboarding and Business settings | LicenseClient and ProtectedLocalStore | Local activation/records-only fixtures | Production issuer calls and current activation not tested |
| X3 Digital service principal separated from revenue | Enabled, eligible non-pharmacy | W | Digital Services | DigitalServiceLedger and ServicePosting | Posting, duplicate, stale-price and reversal tests | External provider activation is manual or separately integrated |
