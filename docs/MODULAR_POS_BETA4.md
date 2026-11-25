# Windows POS 1.1.0-beta.4

## Shared POS

- Dashboard, Register, Inventory, Purchases, Sales & Reports, Customers and Settings use one responsive top navigation. The More menu holds links that exceed the available width. The Register retains its product area, cart and checkout.
- Sales & Reports filters saved invoices by number or original reference, date range, customer, phone, cashier, payment method, status and amount. Receipt actions reuse the existing preview, print, PDF and WhatsApp workflows. Reprints show the amounts recorded at checkout.
- Register customer selection shows posted outstanding balances. Udhar shows the current invoice, prior balance and projected balance before posting. Customers can open the transaction ledger. Previous purchases can populate a new cart, and frequent purchases can add currently eligible products. Repeat Sale uses current prices and stock and leaves the cart uncommitted.
- Inventory product history uses saved invoice lines. It shows batch details or device identifiers where the active business permits them. Invoice margins use stored costs and identify missing costs.
- Returns use original sale amounts and quantities. Cash refunds require confirmation of cash handed over. Card refunds stay pending until staff record an external result. Udhar returns reduce receivables and record any excess as customer credit. The return record keeps the reason, operator, reference and returned lines.

## Business profiles

- Pharmacy keeps medicine lookup, batch and expiry tracking, packaging, stock verification, and dispensing controls. Product Name is the only required field for saving an unconfigured product. Checkout still validates sale setup. Inventory editing keeps on-hand stock read-only and uses the existing stock actions. Table rows grow to show wrapped details.
- Business settings resolve capabilities for the shared views and the relevant transaction services. Pharmacy and Grocery hide the device-tracking columns and actions. Electronics and compatible retail profiles retain serial, IMEI and warranty support. Loose goods and packaging depend on the configured profile. Pharmacy disables wallet, mobile recharge and package sales.
- Returns match the original batch or device identity when applicable. Tracked, placed, or unverifiable stock stays outside saleable inventory. Eligible ordinary stock can be received back in the same transaction as the return.

## Keyboard shortcuts

| Key | Active context and action |
| --- | --- |
| F1 / F2 / F3 / F4 | Register Cash / Card / Udhar / Clear Cart |
| Ctrl+F | Focus a visible search field on the current page |
| Ctrl+S | Save the open product editor when editing is permitted |
| Ctrl+P | Print the selected saved invoice in Sales & Reports |
| Ctrl+H | Sales history for the focused Inventory row or open product |
| Ctrl+B | Selected Register customer's ledger, or open Customers |
| Ctrl+G | Open Sales & Reports invoice history |
| Ctrl+Alt+C / F12 | Register customer selection and details |
| Ctrl+Alt+T | Frequent products for the selected Register customer |
| Ctrl+Alt+R | Previous purchases for the Register customer, or repeat the selected invoice in Sales & Reports |
| Ctrl+Alt+I | Open the focused Inventory row's product details |
| Alt+F1 / Alt+F12 | Open the selected customer's ledger in Register or Customers |
| Alt+F3 | Product labels in Inventory |

Keyboard help is shared. Modal and payment contexts take priority, and normal editing keys retain their existing behavior.

## Data and verification

- Schema 12 records whether the pharmacy selling unit is configured. Existing products keep their existing configuration. Schema 13 adds return records and per-invoice receivable reductions.
- An isolated copy of the prior database passed migration and SQLite integrity checks. All original columns and rows in 31 tables had matching fingerprints after migration. The sample cleanup identified products using the original barcode, name and category seed keys. It left linked records and unmatched products alone.
- All 195 automated tests passed, including cash, card and Udhar returns. All 769 desktop checks passed, including top navigation at multiple widths, English and Urdu layouts, and pharmacy product setup and stock handling. Receipt rendering passed at 58 mm and 80 mm with a grayscale logo. Both rack label formats rendered.
- Product and customer invoice lines load on demand. There is no timed comparison against an earlier build.
- The self-contained Windows x64 local build passed verification as version 1.1.0-beta.4.

## Remaining limits

- Card refunds are recorded from the terminal result. There is no live processor integration or FBR credit-note posting. Returns are blocked if FBR integration is required.
- Customer credits are recorded in the ledger. Applying that credit to a later sale still needs a dedicated workflow.
- Return disposition for located, perishable, batch-tracked and serialized inventory is conservative. Reintroducing such stock requires separate handling and verification.
- Shortcut coverage is the matrix above. Other form saves and rack-label printing remain available through their existing controls.
- Physical printer, mobile scanner hardware and a live card terminal were outside the automated checks.
