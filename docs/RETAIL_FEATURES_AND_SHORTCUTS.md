# Retail operations and keyboard shortcuts

## Feature coverage

| Feature | Business scope | Implementation | UI entry | Shortcut | Test |
| --- | --- | --- | --- | --- | --- |
| Payments and Clear Cart | Shared | Atomic posting, SQLite confirmation identity and safe cart clear | Register payment actions | F1–F4 | Checkout and shortcut tests |
| Saved cart | Shared | One encrypted draft per operator, revalidate current stock and units on restore | Cart toolbar | Ctrl+S / Ctrl+Shift+O | Draft round trip |
| Invoice search | Shared | Parameterized date, text, method, status and price queries, 100 invoices per page | Sales & Reports | Ctrl+G / Ctrl+Shift+O | Filter probes |
| Receipt actions and A4 reports | Shared | Existing immutable receipt amounts, PDF, WhatsApp and selected print queue | Invoice preview / generated report | Ctrl+P | Rendering, physical print unverified |
| Customer accounts | Shared | Sale-attached details, posted receivables, partial repayment and running ledger | Customers / Register chooser | Ctrl+B / Alt+F1 / F12 | Focused ledger probe |
| Sales returns | Eligible profiles | Partial cash, externally confirmed card and Udhar, idempotent refund and traceable disposition | Sales & Reports → Return Items | Ctrl+Shift+R | Return cases |
| Repeat Sale and frequent items | Shared | Current prices/stock, Add/Replace/Cancel and real purchase frequency | Invoice / Register customer | Ctrl+Alt+R / Ctrl+Alt+T | Repeat and dialog probes |
| Product history and information | Shared | Historical prices, authorized customer names and read-only product information | Cart selection / Inventory | Ctrl+H / Ctrl+Alt+I | Item history probe |
| Margin and audit | Owner | Currency-safe margin math, missing cost warning, guarded cost views and linked events | Register / selected invoice | Shift+V for margin | Cashier restrictions |
| Purchases and inventory | Eligible profiles | Reused stock movement, receiving, physical units, batch/expiry and labels | Purchases / Inventory | Ctrl+F / Alt+F3 | Existing inventory tests |
| One top navigation and help | Shared | Native title bar, responsive top bar, localized two-table help | Top navigation / More | Ctrl+Shift+H | Desktop checks |

## Abuzar reference mapping

| # | Abuzar shortcut and function | Retail equivalent or exclusion | Actual shortcut / access | Status | Reason |
| ---: | --- | --- | --- | --- | --- |
| 1 | Ctrl+X / Close module | Windows cut and Esc for modal | — | Excluded | Outside retail scope or preserves an existing key |
| 2 | Ctrl+I / Insert invoice row | Enter on selected Register List product | Enter | Equivalent | Reuses an existing workflow or a registered command |
| 3 | Ctrl+D / Delete invoice row | Ctrl+Shift+Delete on selected cart row | Ctrl+Shift+Delete | Equivalent | Reuses an existing workflow or a registered command |
| 4 | Ctrl+N / New module | Ctrl+Shift+N opens New Product | Ctrl+Shift+N | Equivalent | Reuses an existing workflow or a registered command |
| 5 | Ctrl+S / Save record | Ctrl+S saves Register draft or Inventory product | Ctrl+S | Implemented | Reuses an existing workflow or a registered command |
| 6 | Ctrl+P / Print | Ctrl+P prints selected invoice or generated report | Ctrl+P | Implemented | Reuses an existing workflow or a registered command |
| 7 | Tab / Next field | Tab | Tab | Reused | Reuses an existing workflow or a registered command |
| 8 | Shift+Tab / Previous field | Shift+Tab | Shift+Tab | Reused | Reuses an existing workflow or a registered command |
| 9 | Ctrl+Tab / Cycle modules | Top navigation replaces window cycling | — | Excluded | Outside retail scope or preserves an existing key |
| 10 | Ctrl+E / Post or lock | F1/F2/F3 plus Complete Payment | F1/F2/F3 | Equivalent | Reuses an existing workflow or a registered command |
| 11 | Alt+F mnemonic / File | More and Store Profile | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 12 | Alt+P mnemonic / Purchase | Purchases top navigation | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 13 | Alt+S mnemonic / Sale | Register top navigation | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 14 | Alt+T mnemonic / Transaction | Sales & Reports and Customers | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 15 | Alt+R mnemonic / Reports | Ctrl+G opens Sales & Reports | Ctrl+G | Equivalent | Reuses an existing workflow or a registered command |
| 16 | Alt+B mnemonic / Basic data | Inventory and Customers | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 17 | Alt+M mnemonic / Maintenance | Store Profile | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 18 | Alt+G mnemonic / Manage | Store Profile and relevant modules | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 19 | Alt+W mnemonic / Windows | Single workspace | — | Excluded | Outside retail scope or preserves an existing key |
| 20 | Alt+H mnemonic / Help | Ctrl+Shift+H | Ctrl+Shift+H | Equivalent | Reuses an existing workflow or a registered command |
| 21 | Ctrl+Q / Save and post | F1/F2/F3 plus Complete Payment | F1/F2/F3 | Equivalent | Reuses an existing workflow or a registered command |
| 22 | Ctrl+G / Previous invoices | Ctrl+G | Ctrl+G | Reused | Reuses an existing workflow or a registered command |
| 23 | Ctrl+A / Invoice template | Native Select All, template module excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 24 | Ctrl+Alt+D / Delivery order | Delivery excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 25 | Ctrl+U / Due fulfillment items | Fulfillment excluded, Udhar is separate | — | Excluded | Outside retail scope or preserves an existing key |
| 26 | Ctrl+H / Item sales history | Ctrl+H in Register or Inventory | Ctrl+H | Implemented | Reuses an existing workflow or a registered command |
| 27 | Ctrl+B / Previous invoice balance | Ctrl+B for customer ledger or Customers | Ctrl+B | Equivalent | Reuses an existing workflow or a registered command |
| 28 | Ctrl+Z / Save normal invoice | Native Undo, payment confirmation posts sale | Page action | Equivalent | Reuses an existing workflow or a registered command |
| 29 | Shift+V / Invoice margin | Shift+V in Register or Reports for owner | Shift+V | Implemented | Reuses an existing workflow or a registered command |
| 30 | Ctrl+Alt+C / Select customer | Ctrl+Alt+C in Register | Ctrl+Alt+C | Reused | Reuses an existing workflow or a registered command |
| 31 | Ctrl+Alt+T / Top items | Ctrl+Alt+T with Register customer | Ctrl+Alt+T | Implemented | Reuses an existing workflow or a registered command |
| 32 | Ctrl+Alt+I / Item information | Ctrl+Alt+I on selected product | Ctrl+Alt+I | Implemented | Reuses an existing workflow or a registered command |
| 33 | Ctrl+Alt+R / Recurring invoice | Ctrl+Alt+R repeats a saved transaction into cart | Ctrl+Alt+R | Equivalent | Reuses an existing workflow or a registered command |
| 34 | F4 / Expand box | Native resizing, F4 reserved for Clear Cart | — | Excluded | Outside retail scope or preserves an existing key |
| 35 | F1 / Account ledger | Alt+F1, F1 reserved for Cash | Alt+F1, | Equivalent | Reuses an existing workflow or a registered command |
| 36 | F10 / Invoice header | Ctrl+Shift+O in Reports opens full invoice | Ctrl+Shift+O | Equivalent | Reuses an existing workflow or a registered command |
| 37 | F11 / Area-wise customers | Structured area management excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 38 | F12 / Customer information | F12 opens Register chooser | F12 | Reused | Reuses an existing workflow or a registered command |
| 39 | Alt+F1 / Balance summary | Alt+F1 opens ledger with running balance | Alt+F1 | Equivalent | Reuses an existing workflow or a registered command |
| 40 | Alt+F2 / Refused sale | Refused-sale logging excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 41 | Alt+F3 / Barcode printing | Alt+F3 exports shelf labels, rack labels use their panel | Alt+F3 | Reused | Reuses an existing workflow or a registered command |
| 42 | Alt+F6 / Item associations | New associations module excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 43 | Alt+F7 / Item notes | Product notes are absent | — | Unsupported | No internal product notes workflow |
| 44 | Alt+F8 / Customer quotations | Quotations excluded | — | Excluded | Outside retail scope or preserves an existing key |
| 45 | Alt+F12 / Transaction summary | Alt+F12 opens customer ledger | Alt+F12 | Equivalent | Reuses an existing workflow or a registered command |

## Active registry

| Key | Action / context | Eligibility |
| --- | --- | --- |
| F1, F2, F3, F4 | Cash, Card, Udhar and Clear Cart in Register | Current cart and payment validation |
| Ctrl+F | Focus a supported search on the active page | Visible search, Purchases requires owner |
| Ctrl+S | Save Register draft or Inventory product | Ready cart or owner with editable product |
| Ctrl+P | Print selected invoice or generated A4 sales report | Sales & Reports and an available Windows queue |
| Ctrl+G | Open Sales & Reports | Signed-in workspace |
| Ctrl+H | Product sales history | Selected Register cart item or Inventory product |
| Ctrl+B | Selected customer ledger or Customers navigation | Customer required for ledger |
| Ctrl+Alt+C, F12 | Customer selector/details | Register outside payment |
| Ctrl+Alt+T | Frequently Purchased | Register with selected customer |
| Ctrl+Alt+R | Previous Purchases or Repeat selected Sale | Register customer or selected Reports invoice |
| Ctrl+Alt+I | Product information | Selected Register cart item or Inventory product |
| Alt+F1, Alt+F12 | Customer ledger | Register or Customers with selected customer |
| Alt+F3 | Shelf label PDF | Inventory, products present, editor closed |
| Shift+V | Margin | Owner, Register or selected Reports invoice, focus outside text editing |
| Ctrl+Shift+O | Resume draft or open selected saved invoice | Register with empty cart or selected Reports invoice |
| Ctrl+Shift+N | New Product | Owner in Inventory, writable mode |
| Ctrl+Shift+R | Return Items | Owner and selected Reports invoice, writable mode |
| Ctrl+Shift+Delete | Remove selected cart item | Register, row selected |
| Ctrl+Alt+L | Switch Cards/List | Register |
| Ctrl+Shift+H | Keyboard Shortcuts | Signed-in workspace outside modal/payment |
| Enter | Sign in, search, barcode submission, add selected product, choose medicine, measured item or validated contact entry | Focused control and actual command guards |
| Escape | Close active popup, context panel or suggestions | Active transient UI |
| Down | Focus medicine suggestions | Visible suggestions |
| Up/Down | Resize eligible panel divider | Focused splitter |
| Ctrl+A/C/V/X/Z/Y and Tab/Shift+Tab | Native editing and focus navigation | Compatible WPF control |

## Business feature matrix

| Profile | Shared | Profile-specific |
| --- | --- | --- |
| General Retail | Register, customers, invoices, returns, item history, inventory and authorized margin | Configurable measured and packaged goods |
| Pharmacy | Same shared core | DRAP, medicine attributes, tablet/strip/pack, batches/expiry, dispensing and quarantine outside saleable stock |
| Grocery | Same shared core | Weight/volume, price by amount and packaging |
| Electronics and Mobile | Same shared core | Device serial/IMEI, warranties and return identity |
| Digital-enabled | Same shared core | Configured providers only, separate service principal and revenue |

## Financial integrity, performance, database and Windows build

The Windows WPF application and desktop harness built with zero warnings and errors. The automated suite passed 198 tests, including replaying a checkout confirmation without another sale or stock movement. The full desktop regression suite passed 840 checks. The focused desktop candidate passed 81 additional assertions, including draft recovery, Add/Replace/Cancel for Repeat Sale, the six-page keyboard reference, protected cashier cost access, historic invoice snapshots, filtering, and partial repayment and return posting. An earlier concurrent desktop run timed out in the harness before the successful reruns. The self-contained local Windows x64 beta.6 build compiled and its development metadata passed verification.

On the focused small test fixture, measured data load was 173 ms for Register, 25 ms for Inventory, 71 ms for invoice history and 20 ms for invoice filtering. The ledger opened in 709 ms, Frequently Purchased in 336 ms, product sales history in 787 ms, repeat staging in 27 ms and return confirmation in 530 ms. First shell render took 1.67 s. Shortcut help showed 30 binding identities across the six measured pages. It took 2.58–7.29 s in the run with concurrent work and 1.29–1.67 s in an earlier lower-load run. These desktop-harness measurements include visual layout and do not measure a production catalog or a cold process start.

Schema 14 adds a nullable unique checkout identity and indexes for invoice date, customer identity and product-to-invoice lookup. SQLite query plans used those indexes and the existing return invoice/status index. The schema 13 → 14 test preserved every original row and column in 32 tables. SQLite integrity was verified, and the POS restore service successfully restored a schema 14 backup. Drafts live in Windows-protected local storage.

## Source versions

`source-baseline` is the oldest available source tree. `checkpoint-before-ui-restoration-20261121` and `checkpoint-before-business-modules-20261125` recover the available checkpoint source. Files outside each captured checkpoint were carried forward from beta.5. `v1.1.0-beta.5` preserves the Windows baseline. An older app requires a database backup that matches its schema.

## Remaining limits

- Draft restore covers ordinary, measured and packaged products. Batch, serial, location and dispensing checks require the interactive cart.
- Customer details remain invoice-linked. A standalone customer master, opening balances, ledger adjustment/reversal actions and applying accumulated return credits at checkout are pending.
- Split tender, voids, standalone return receipts, official FBR credit notes, quarantine release and a persistent print journal are pending. Wrong or unverified tracked merchandise remains outside saleable stock.
- Period return totals appear separately from X/Z posted sales totals. Physical printers, scanners, terminals and production-size query throughput require field verification.

