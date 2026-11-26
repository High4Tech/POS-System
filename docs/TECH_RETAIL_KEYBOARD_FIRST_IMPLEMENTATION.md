# Tech Retail and keyboard-first Sales

## GitHub

The verified remote is https://github.com/High4Tech/POS-System.git. The local main branch and origin/main were at 061fcf2 when work began. The feature branch is codex/tech-retail-keyboard. It includes the prior locally verified stabilization work that was still uncommitted, plus this task's changes. Publishing and final deployment status will be recorded after the release gates pass.

## Business profiles and data

The local test store was configured as Pharmacy before this task. After the POS was closed, a SQLite-consistent backup was created at D:\SupermarketPos\Backups\Before-tech-retail-20261126-125154.zip. Only BusinessConfigurations.ProfileJson and its revision were changed. BusinessType is now electronics and MedicineTemplate is none. The display label is Tech Retail. The original inventory and historical transactions are retained.

The existing electronics/mobile capability controls device fields and services. Pharmacy still owns its reference search, medicine fields, batches, dispensing checks, and tablet/strip/package selling. Grocery and General Retail retain their existing loose goods and packaging policies.

Migration 20261126090000_TechRetailDetails adds Brand, Model and Sku to Products with empty defaults. Backup compatibility was updated for the new schema. The new metadata persists independently of serial tracking and stock. Existing records need no manual conversion.

Read-only hashes of all 15 checked business tables matched the pre-switch backup after the configuration change. This covered products, categories, sales and invoice lines, suppliers, purchases and their lines, batches, devices, account payments, providers, packages and service records.

## Navigation and Sales

The shop name appears in the native title bar. Top navigation uses Home, Sales, Products & Stock, Purchases, Reports, Customers and Settings. Scanner status reflects the actual connection.

The customer control is in the Cart header and opens the shared customer selector. It supports filtering existing accounts, clearing the current selection, and entering a new customer's details. Cancel preserves the current customer and cart. Cash and card keep the existing optional customer rules.

The product container shows the category or Search Results heading, filtered shop-product count and Cards/List toggle. Reference-only medicines are kept in Pharmacy's separate search. Cards/List preference remains local to Sales. Cart columns start with Product, Qty, Unit, Price and Total. Pending pharmacy tax setup retains its conditional field. Secondary cart actions are accessible through More and their existing shortcuts.

## Keyboard behavior

| Keys or action | Result | Evidence |
| --- | --- | --- |
| Search, Down, Enter | Highlight and add a shop product | Real Windows input on isolated WPF app |
| Manual add | Focus Qty and select the value | Real input and focused WPF checks |
| Qty, Enter | Validate and commit, then select Unit if needed or authorized Price | Real single-unit input and isolated multi-unit checks |
| Unit, Enter | Use the configured conversion and price, then advance | Isolated strip check |
| Final Enter | Return to product search | Real Windows input |
| Invalid Qty | Retain editing and block checkout | Focused WPF check |
| Unauthorized price | Skip price editing | Focused WPF check |
| Ctrl+Alt+C | Open customer selector with search focus | Real Windows input |
| Customer search, Down, Enter, Enter | Choose and save existing customer, return to search | Real Windows input |
| Ctrl+Alt+T, Enter | Open actual prior purchased products and add selected product with Qty focus | Real Windows input using seeded sale |
| Ctrl+F | Focus page search | Existing central registry and WPF checks |
| / | Focus search outside editable controls | Focused WPF routing check |
| / inside editable text | Preserve text editing | Real Windows key inserted a slash into Sales search |
| Alt+1 through Alt+7 | Home, Sales, Products & Stock, Purchases, Reports, Customers, Settings | Central registry |
| Alt+M | Top navigation More menu | Central registry |
| F1–F4 | Cash, Card, Udhar, Clear Cart | Existing registry and focused checks, F1 and Escape retested with real input |
| Escape | Cancel payment or active modal | Real Windows input |

The manual-product event is raised by manual collection selection and Frequently Purchased. Phone and scanner dispatch use their existing path and do not trigger Qty autofocus. A focused dispatch check verifies that scan/search focus remains in place. Physical scanner hardware remains a separate verification gate.

## Tech Retail

Brand, Model and SKU are editable on Tech Retail products and included in shop search. Device reporting includes Brand and Model, and searches those fields alongside identifiers, customer, invoice and availability/warranty text.

Existing product-specific tracking captures exact serial/IMEI on receiving and sale, prevents duplicate identities and duplicate device sale, and stores purchase, sale and warranty references. The device selection window supports Down from search and Enter to select. Accessories retain ordinary quantity inventory.

Returns use the existing shared return engine. A serialized return requires the sold serial or IMEI. Existing safeguards prevent automatic restocking of tracked devices. Reason and disposition remain part of the recorded return. Warranty snapshots retain the terms captured at sale.

## Verification

The final unit/integration run passed 199 tests. The device and storage tests also passed a 15-test rerun. The shared desktop suite passed 844 checks, including Pharmacy. The final Release Sales flow passed 24 assertions including quantity, unit conversion, price permission, payment focus and manual Pharmacy entry focus. The focused desktop audit passed 127 checks, including the final shortcut registry. Logs are under engineering/qa-data/tech-retail-gates-20261126-130625 in the local workspace.

The isolated Windows app was operated with real keys for product search, Down, Enter, Qty, Price, customer selection, Frequently Purchased, Cash and Escape. These tests used a synthetic store and financial records. Production records were not used for test posting.

## Performance

An observed focused audit recorded 234 ms for Register data load, 60 ms for invoice history and 19 ms for its filter. Frequently Purchased took 703 ms including layout, and customer ledger took 1,518 ms including layout. These observations exceed some previous latency targets and are not low-spec certification.

## Installation

The authoritative path is D:\SupermarketPos\App\SupermarketPos.exe, launched by Tech Store POS.lnk. The required local build and update scripts retain this identity, back up store and application files, verify payload hashes, and launch the updated app. Final release identity and installation results are pending this task's final gates.

## Remaining verification

Printer, card terminal and physical barcode scanner hardware, dedicated 4 GB hardware and Windows 7 execution were unavailable. The application targets .NET 8 WPF and has no supported Windows 7 build. Full keyboard-only journeys through every purchase, return and device state remain broader than the physical key checks listed above. Authenticated installed-store testing requires a signed-in session after installation.
