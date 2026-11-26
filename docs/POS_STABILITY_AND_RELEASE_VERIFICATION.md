# POS stability and release verification

## A. Root causes

| Symptom | Cause and correction | Evidence |
| --- | --- | --- |
| New shortcuts absent on the desktop | The installed copy was on beta.5 when beta.6 source was first delivered. The current shortcut targets `D:\SupermarketPos\App\SupermarketPos.exe`. `Build-LocalDesktop.ps1` records provenance and `Update-LocalDesktop.ps1` validates the staged payload, backs up the store and previous binaries, replaces the app at that path, and verifies the process and installed hashes. | Desktop shortcut target read from the `.lnk`. An old payload without Release provenance was rejected. Build and installation evidence below. |
| Ctrl+Alt+T did nothing | The Register omitted the binding if no customer summary existed. A summary could also represent a name without a usable account identity, which produced an empty history. `Shortcuts.cs` and `PosViewModel.Customer.cs` now use the sales account identity and explain the prerequisite. | Real Ctrl+Alt+T events reached the WPF handler. The same chord gave guidance without a customer and opened recorded purchases after selection. |
| Product history ignored a selected Inventory row | The prior fix replaced keyboard-focus guessing with the actual grid selection. That fix is included in this build. | Regression harness verifies the selection still applies after focus leaves the grid. |
| Popups took the whole viewport | The shared `InAppDialog` card stretched inside the overlay. The card now centers and caps its height while the backdrop covers the window. Customer, checkout and history forms use a scrollable body with fixed actions where needed. | Customer chooser measured about 620 × 313 in a 1306 × 813 captured window. Product history with one result and the Clear Cart confirmation also used their natural height. |
| Large blank or clipped actions | Reports docked every action ahead of a fill-width heading. A separate margin label then overlapped the invoice-items heading. The actions wrap, and the heading and margin share the same layout. The return dialog groups its actions below the scrollable form. | The final focused WPF checks verify that the heading and margin never intersect and that Close and Return Items fit side by side at the tested window size. |
| White label on pale hover and hard shadows | The secondary template replaced the selected Cards/List button background while keeping its white label. Primary and secondary states now tint the existing fill, disabled states have readable tokens, and standard CTA shadows are gone. | Shared theme resources and actual Register/checkout screenshots. |
| Redundant work on shortcuts and customer history | The router walked the visual tree looking for search fields even on unrelated shortcuts. Customer history materialized all matching sales and legacy rows before limiting results. Scope the visual lookup to the relevant command and cap indexed account records before loading invoice lines, while still matching older records. | Focused history and keyboard tests. |

## B. Keyboard shortcuts

The same registry drives dispatch and both help tables. The table distinguishes real Windows input on the isolated WPF app from direct command or routed-event coverage. No live sale, refund or print job was submitted. A compact inline message handles missing selection where useful. Billing and permission guards remain in the existing commands.

| Shortcut | Scope and expected action | Key event and handler | UI or operation observed | Installed build test | Status |
| --- | --- | --- | --- | --- | --- |
| F1 / F2 / F3 | Register Cash / Card / Udhar | Windows events received and handled for all three | Each checkout opened. Escape canceled and retained the cart. | Requires authenticated store session | Isolated pass |
| F4 | Register clear cart | Windows event received and handled | Confirmation shown. Escape retained the cart. | Requires authenticated store session | Isolated pass |
| Ctrl+Alt+T | Register Frequently Purchased | Windows event received and handled, repeatedly | No-customer guidance, then the seeded customer's actual purchased product. Add staged one item in cart. | Requires authenticated store session | Isolated pass |
| Ctrl+Alt+C | Register customer selection | Windows event received and handled | Searchable picker opened, genuine fixture selection populated name and phone, Save attached the customer. | Requires authenticated store session | Isolated pass |
| Ctrl+Alt+R | Register previous purchases / Reports repeat invoice | Windows event received and handled in Register | Prior invoice opened in selector. Repeat displayed Add, Replace, and Cancel. Reports direct-command repeat covered separately. | Requires authenticated store session | Register isolated pass |
| Ctrl+B | Register / Customers ledger or Customers navigation from other pages | Windows event received and handled in Register | Customers ledger showed the sale, payment and balance. | Requires authenticated store session | Register isolated pass |
| Ctrl+H | Register / Inventory product sales history | Windows event received and handled in Register | Recorded product sale, receipt, quantity, unit and price shown. Inventory selected-row regression also covered by the harness. | Requires authenticated store session | Register isolated pass |
| Ctrl+G | Sales & Reports | Windows event received and handled | Invoice history opened following modal Escape. | Requires authenticated store session | Isolated pass |
| Ctrl+Shift+O | Reports receipt preview / Register resume draft | Windows event received and handled in both pages | Selected invoice rendered with its original amounts. The saved draft restored its item and customer. | Requires authenticated store session | Isolated pass |
| Ctrl+Shift+H | Keyboard Shortcuts | Windows event received and handled in Reports | Two tables and the active-page subtitle. Automated sizing and focus checks span six pages. | Requires authenticated store session | Isolated pass |
| Escape | Close active in-app modal or payment | Windows events received, handled by payment or modal layer | Customer-history, receipt, checkout, and Clear Cart returned to the underlying page. | Requires authenticated store session | Isolated pass |
| Ctrl+F | Focus active search | Registry, layout and previous installed build probes | Focuses Register medicine search and Reports search in the prior installed build. | Prior build only | Retest final release |
| Ctrl+S | Register save draft / Inventory save product | Windows event received and handled in Register | Draft saved and restored without a stock movement or sale. Inventory save remains separately scoped. | Pending | Register isolated pass |
| Ctrl+P | Reports selected receipt or generated report | Registered | Receipt preview and print data paths covered separately. | Pending | Real print queue / printer pending |
| Ctrl+Alt+I | Register / Inventory product details | Windows event received and handled in Register | Details showed the selected item, barcode, price, stock, and unit. | Pending | Register isolated pass |
| F12 | Register customer details | Windows event received and handled | The existing customer picker opened with keyboard focus. | Pending | Isolated pass |
| Alt+F1 / Alt+F12 | Register / Customers account summary | Both Windows events received and handled | Both opened the ledger for the selected customer. | Pending | Isolated pass |
| Alt+F3 | Inventory shelf labels | Registered | Label rendering covered. | Pending | Printer/real chord pending |
| Shift+V | Authorized Register / Reports margin | Windows event received and handled in Register | Owner saw margin and the missing-cost notice. Cashier restrictions also exercised in the harness. | Pending | Register isolated pass |
| Ctrl+Alt+L | Register Cards/List | Registered, previous installed input | Both directions verified on the previous installed build. | Prior build only | Retest final release |
| Ctrl+Shift+Delete | Remove selected Register line | Windows event received and handled | Selected cart line removed. The saved draft remained available. | Pending | Isolated pass |
| Ctrl+Shift+R | Return selected sale | Windows event received and handled | The selected invoice's return form opened. Cash return and stock disposition separately covered with isolated data. | Pending | Isolated pass |
| Ctrl+Shift+N | New Inventory product | Registered for authorized editing | Existing product editor path. | Pending | Real chord pending |
| Enter | Add selected product from the Register list | Register list input handler and focused search coverage | Existing product selection pathway. | Pending | Real list-chord retest pending |

Alt+F7 is outside the registry because this product model has no product-notes field. Native text edit keys are left to WPF. A full installed-build keyboard pass needs a signed-in operator and a test store. Do not use the production store for financial posting tests.

## C. Dialog and layout audit

| Dialog | Correction | Height/scroll behavior and evidence |
| --- | --- | --- |
| Register customer selector | Removed the redundant optional toggle in the dedicated selector. Kept it during checkout. Grouped the fields and paired Save and Cancel. | Compact card observed with actual customer selection. Scroll body and fixed actions for larger type. |
| Shortcut Help | Retained two registry-derived tables, header and Close actions. | Centered card with capped body scrolling. Six-page harness covers current page, overlap, smaller windows, Urdu, and modal focus. |
| Frequently Purchased / Previous Purchases | Reuse existing customer invoices, selectable rows, paired actions, empty state and no horizontal clipping. | Real keys reached both workflows, and the recorded product could be added. List-only scrolling in final layout. |
| Product Details / Product Sales History | Shared bounded card. History rows naturally size with pinned actions and a height-capped grid. | Product history and details opened with real Ctrl+H and Ctrl+Alt+I in Register. |
| Customer Ledger | Compact grid, filter and paired actions. | Real Ctrl+B verified a sale/payment pair. The grid bounds scrolling when results grow. |
| Invoice preview / Repeat Sale | Shared centered shell and retained receipt actions. Repeat keeps its explicit cart decision. | Real Ctrl+Shift+O and Ctrl+Alt+R showed both. |
| Cash / Card / Udhar | Consolidated checkout keys, focused the overlay, and enabled safe Escape. Form body scrolls with fixed actions. | All three payment modes opened via real keys in an isolated checkout. |
| Discount / Price override | Inline fields in Register. | Existing commands and value validation retained. |
| Product Add/Edit and bulk editor | Existing in-page overlay forms continue to use their bounded viewbox. | Full desktop checks include the product editor. |
| Returns / Refund confirmation / Rack and stock adjustment | Shared card improvements reach the return and stock-management panels. The return form retains its own scrollable body. | Partial return and stock disposition exercised with isolated data. Dedicated visual checks for every panel are pending. |
| Supplier / Purchase / Scanner Pairing / Native notices and settings | Existing standalone WPF windows use their existing bounds and sizing; they were not converted to in-app overlays. | Full harness covers related form flows where available. Physical pairing and every native-dialog state are pending. |

## D. Theme and contrast

| Component | States | Foreground / surface | Verification |
| --- | --- | --- | --- |
| Primary action | Normal, hover, press | White on brand blue. Translucent dark state over the existing fill. | Observed normal and hover in WPF; contrast ratios below. |
| Secondary action | Normal, hover, press | Dark slate on white. The same state tint retains the button's own fill and label. | Observed normal and hover. |
| Disabled CTA | Disabled | `#475569` on `#E2E8F0`. | Observed in the empty Register. |
| Active navigation | Normal, hover, press | `#1E40AF` on white with dark state tint. | Normal active navigation observed; computed state contrast. |
| Cash / Card / Udhar / Clear | Normal and disabled | White on dark green, teal-blue, brand blue, or red; neutral disabled pair. | Observed enabled and disabled on the isolated Register. |
| Inputs and selected table row | Keyboard focus / selection | Existing blue focus border; dark text on light blue selection. | Search/payment focus and selected invoice and cart rows observed. |
| Error and success | Normal | Dark red `#B91C1C` and green `#15803D` on light surfaces. | Token contrast calculated. |

Calculated WCAG text contrast for normal / hover / press is **5.17 / 5.83 / 6.22** for primary, **17.85 / 14.70 / 13.27** for secondary, and **8.72 / 7.18 / 6.48** for the active navigation item. Disabled text is **6.15:1**. Normal Cash, Card and Clear text are **5.02:1**, **5.93:1** and **6.47:1**. Red and green status text on white are **6.47:1** and **5.02:1**. These ratios use the shared fills and opacity.

This is a component/state audit, not a claim that every screen and custom color passes WCAG AA. User-selected category colors and device-specific rendering need their own coverage.

## E. Performance

Baseline ran on Windows NT 10.0.26300 x64, Intel Core i7-8850H with 12 logical processors, 1600 × 1000 WPF fixture, 101 shop products and 10,068 separate medicine reference rows. Category and cart figures include WPF rendering. The first medicine query is cold. Small sample sizes and desktop input automation overhead limit interpretation. These are not measurements on a 4 GB retail PC.

| Operation | Baseline | Quiet repeat | Notes |
| --- | ---: | ---: | --- |
| First reference import | 8,737 ms | 8,406 ms | First selection only. |
| Register data load | 237 ms | 210 ms | Isolated fixture. |
| First Register render | 1,504 ms | 2,757 ms | Target gap remains. |
| First medicine query | 602 ms | 466 ms | First search misses the 200 ms target. |
| Warm medicine queries | 34 ms average / 63 ms worst | 38 ms average / 70 ms worst | Seven warm queries, too few for a stable p95. |
| Ten Inventory filters and renders | 6,247 ms | 11,324 ms | With column virtualization off. Still slower than baseline. |
| Four category loads and render | 106 ms | 206 ms | Aggregate across four loads. |
| Ten quantity edits and render | 185 ms | 475 ms | Aggregate across ten edits. |

An earlier run overlapped update staging and recorded 20,318 ms across the ten Inventory changes. In the quiet repeat, the same Inventory run took 12,394 ms with column virtualization and 11,324 ms without it. The latter averaged 1,132 ms and peaked at 1,406 ms. Row recycling stays enabled. Global column virtualization was removed after the comparison. General Inventory search now uses the same 120 ms input debounce as Pharmacy to skip intermediate queries during rapid typing. The measured bottleneck remains collection refresh plus WPF layout, rather than a SQLite query on each keystroke.

The quiet desktop harness used 416 MiB working set and 218,781 ms process CPU including the preceding screens and test harness. This is not idle production memory. Focused small-fixture timings include 43–65 ms for invoice history, 15–23 ms for invoice filtering, 415–749 ms for Frequently Purchased, and 583–763 ms for the customer ledger including layout. The last two exceed the latency targets.

The final Release audit recorded 80 ms for invoice history load, 28 ms for filtering, 584 ms for Frequently Purchased, 770 ms for the return dialog, and 1,462 ms for customer ledger including layout. These are individual observations from correctness checks, with load and rendering combined where applicable.

Read-only `EXPLAIN QUERY PLAN` against the isolated SQLite file found indexes for exact barcode, category, exact receipt, invoice date range, customer account, product sales, posted returns, stock movement and product/lot. The customer-account query sorts matching rows before taking the limit. Name substring and cross-product expiry probes scanned their respective tables. The desktop uses in-memory substring filters for the loaded shop catalog. Adding a plain B-tree index would not accelerate that substring behavior. The medicine catalog is a separate reference table with bounded, cancellable search.

## F. Windows 7

The shipping application uses WPF on `net8.0-windows`, self-contained `win-x64`. EF Core/SQLite 8.x, QuestPDF 2026.6, SkiaSharp 3.119, the scanner/printing APIs, and Velopack must all be evaluated for a legacy port. Microsoft identifies .NET 6 as the last modern .NET supported on Windows 7, and its support ended in 2024. This target cannot be certified for Windows 7. [Microsoft .NET Windows support](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

Windows 7 SP1 was unavailable for execution. A maintenance branch for .NET Framework 4.8 would need a different persistence provider or port, dependency and native launcher changes, plus clean-machine validation for install, printing, PDF, scanning, Urdu and low memory. It would still run on an unsupported OS. No framework downgrade or alternate installer was made. [Microsoft .NET Framework system requirements](https://learn.microsoft.com/dotnet/framework/get-started/system-requirements)

## G. Installation verification

- Existing identity: `SupermarketPos.exe` / `SupermarketPos.dll`, data at `D:\SupermarketPos`, desktop shortcut to `D:\SupermarketPos\App\SupermarketPos.exe`.
- Baseline: `1.1.0-beta.7+061fcf25fd2e3cd8d216fdd48eecf8d219ba47f0`. Thirty older rollback directories were observed. The update uses the one existing application path and leaves historical copies untouched.
- Commercial packaging still uses Velopack pack ID `SupermarketPos`. A signed distributable requires the existing publisher key and code-signing setup. This task uses the local in-place deployment, so a distributable installer hash does not apply.
- Final build: `1.1.0-beta.7+g061fcf2.d6225092a110`, Release / self-contained win-x64. UTC build timestamp `2026-11-26T06:00:38.2691459Z`. Base revision `061fcf25fd2e3cd8d216fdd48eecf8d219ba47f0` with local changes. Source manifest SHA-256 `D6225092A1100C1F4BB8DCF2D830778914DE68386BAD3854EF83A9CC1DAF9A17`.
- Installed executable SHA-256 `FD7E9E982F414FE88B915F761EC310586DFA24901A25A67028E8C376108AB762`. WPF assembly SHA-256 `34C06B6A9AF7C11F82BDFAE42BA3E9C0E6FA6131A4E013770C36104B279311B3`. Both files and the runtime configuration match the Release payload. One process, PID 15056, launched automatically with a main window at the path resolved from `Tech Store POS.lnk`. The data path remains `D:\SupermarketPos`.
- SQLite and store data backup `D:\SupermarketPos\Backups\Before-update-20261126-060451593.zip`. Previous binaries archived as `D:\SupermarketPos\Backups\Previous-application-20261126-060451593.zip`. Both archives opened and their database and executable entries were verified. No schema migration was added.

## H. Tests

- Full Release unit and integration suite: 198 passed.
- Baseline performance run: passed.
- Isolated Windows keyboard smoke: actual `Alt, Control` was recorded for Ctrl+Alt+T and its panel populated from a seeded invoice. The smoke fixture writes to its own SQLite directory.
- Final focused WPF audit: 127 checks passed against copies of the four verified Release application assemblies, including the registry, modal focus, customer-selector bounds at 100% and 200%, historical transactions, invoice item heading layout and the return dialog footer. The first attempt hit the harness's 60-second bound while Release deployment was running. The sequential rerun allowed three minutes for each audit stage.
- Full desktop rerun: 852 checks passed. The isolated WPF harness used copies of the four Release application assemblies from the verified payload. The harness allows three minutes for the shortcut and pharmacy stages and targets the Clear Cart confirmation directly for its routed Escape assertion. Physical key presses were exercised separately in the isolated keyboard fixture. Authenticated checks in the installed store remain separate.

## I. Outstanding verification

- Authenticated installed-build checks, real key presses marked pending above, real printer/card terminal/scanner hardware, production-size database behavior, dedicated low-spec hardware and Windows 7 SP1.
- The benchmark targets for cold medicine search, Inventory filter and render, and the customer history and ledger workflows are still open.
- The commercial signed updater is distinct from this local update and was not exercised against the live publisher service.
