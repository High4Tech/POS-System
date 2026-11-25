# Register restoration — local beta 2

This extends the existing modified Windows application. Origin is `https://github.com/High4Tech/POS-System.git`. No upstream checkout, reset, push, cloud deployment, APK change or final distribution installer was performed.

## Visible changes

- Register restores All/categories, text-first pharmacy product cards, cart and totals. Cards/List is a persistent Register preference; both views share selection logic and preserve the cart/query/category. Cards are bounded to 24 per page, with a separate scrollable cart area.
- Configured medicines add directly in their default unit. Quantity, configured tablet/strip/outer-pack unit and authorized transaction price are edited in the cart. Separate prices and exact base quantities are retained. Missing prices block checkout; cart overrides do not rewrite master prices.
- Catalog-only medicines stage an inline cart row. Local identity/initial price/unit are remembered at confirmation, without inventing stock, cost or profit. Product-specific batch, source, prescription and controlled-reference checks remain; the universal review checkbox is removed.
- New Product opens the complete contextual editor. Available reference attributes prefill the existing record. Tablet/capsule strip controls, default selling unit and opening-stock unit are contextual. Required/invalid input is marked on the field and invalid saves retain the draft.
- Inventory remains a table. Search/New Product/Racks/More replace the scattered actions; combined filters stay visible. Imports, categories, labels, stock verification, header selection and bulk actions remain available.
- Racks/shelves have compact location, transfer and label sections. Transfers require an explicit source/destination, quantity and reason; preserve product/batch/expiry/hold and physical totals; and audit/post each confirmation once. Deactivation keeps history and rejects stock, default-product references and active children.
- Native Windows captions replace custom controls, with OS buttons on the right and English/Urdu content direction retained. Product text uses appropriate Latin/Urdu fonts. Large-display layouts reserve space for selling controls.
- Fresh migration replay no longer deletes products or inserts demo inventory. Engineering fixtures now seed their own test products explicitly.

## Data protection and compatibility

The additional `DefaultSellingUnit` migration is schema **11**: default selling-unit selection and a nullable unique stock-transfer confirmation. Backup/update compatibility metadata and the EF snapshot match it. Earlier migration history and existing business records are retained. Do not run an older executable against the upgraded database.

Local source checkpoint: `engineering/checkpoints/restore-ui-before-20261121` (source only). Real business data/backup/verification copies stay on D:.

- Backup: `D:\SupermarketPos\Backups\Before-register-restoration-20261121.zip`.
- Upgraded rehearsal: `D:\SupermarketPos\Verification\register-restoration-20261121`.
- Rehearsal verified SQLite integrity and identical existing columns/rows in **31 tables**, including the current **102 products, 20 sales and 1 purchase**.

## Verification

- 192 domain/data/scanner/accounting tests passed.
- **729 desktop checks passed** on the final source, covering English/Urdu, 100%/200%, register/cart/inline units, staged medicine checkout, product field validation, inventory filters, modal routing, native-caption configuration, onboarding, digital services and update/feed boundaries, including rack workspace render/interaction checks.
- 58/80 mm English/Urdu receipt output and grayscale logo/text plus both rack barcode formats rendered on the D: verification copy. This is not a physical printing test.
- Local self-contained Windows build: `1.1.0-beta.2`, schema 11, personal D: data-directory setting, commercial enforcement disabled and no embedded publisher endpoint/private signing keys. The shared installer has not been generated.

## Installed on this PC

Installed at `D:\SupermarketPos\App`. The prior executable folder is retained at `D:\SupermarketPos\App-before-register-restoration-20261121`. Installed `SupermarketPos.dll` SHA-256 matches the tested release output: `220B91EDB5E7BDCDD96F4E41CC80107B34312BB4CCEA3420513C34D53FF2499C`.

The live migration passed the same 31-table preservation/integrity check. Counts remained 102 products, 20 sales and 1 purchase. The installed application launched successfully to the existing owner's sign-in screen; native minimize/maximize/close were observed. No live account authentication or test transaction was performed. Selling flows were tested with isolated fixture data.

## Performance method and limits

Measurements use an isolated WPF/SQLite harness on this PC: 12 logical processors, Windows NT 10.0.26300, 1600×1000, English 100%, 101 fixture shop products and 10,068 reference entries. Timings include the specified awaited operations/layout. Baseline results are in `engineering/qa-data/restore-baseline-20261121/performance.json`; after measurements are retained separately. This is not process cold-start, Release-build benchmarking, camera latency or a three-site field trial.

The baseline pharmacy screen hid categories/cards. Restoring those controls adds rendering work, so those first-render figures are not equivalent workloads. The measured runs also varied in general machine load. No overall speed improvement is claimed. Search remains indexed, bounded and cancellable; category filtering uses cached shop data; cards are paginated; Inventory typing is debounced. Actual supported phone/printer/site testing remains required.

For transparency, the last recorded after-run measured Register data load 149 ms, first render 570 ms, four category changes/render 44 ms, scan/cart render 92 ms, ten cart edits/render 99 ms, six Cards/List changes 31 ms, Inventory load 53 ms/render 445 ms, ten explicit filter/render operations 2,300 ms, and editor opening/render 99 ms. Catalog queries ranged from 4.5–64 ms after the first 483 ms query. These are debug-harness observations, with background load, rather than an advertised latency guarantee. The final font/layout polish followed this measurement; no timing claim is made for that polish.

## Remaining commercial release gates

This is a local beta, not a certified or production-ready three-pharmacy rollout. Physical scanner/network/low-end-phone tests, thermal/A4 printer checks, clean second-PC installation/update/recovery, CSV redistribution permission and authorized licensing/signing/FBR deployment remain outstanding. The existing .NET 8 runtime also needs a supported-runtime upgrade and verification before commercial release. Existing Cloudflare/Velopack protections are retained; no official tax acceptance or telecom activation is claimed.
