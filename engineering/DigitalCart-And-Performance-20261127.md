# Digital service cart and CPU contention — 27 November 2026

## Scope and implementation

Extend the existing application. Reuse the Sales styles, payment popup, encrypted per-user drafts, panel-height splitter, table column controls, shortcut router and receipt actions.

- Digital Services: Mobile Load, Mobile Packages, Bank/Wallet Transfer. Online Transfer and Cash Out are exclusive square choices in the last mode.
- Package cards add directly to an editable digital cart. Recipient, selling amount, optional cost/fee/reference remain in the table. One cash/card checkout produces one grouped invoice. Cash-out settlement pays the customer and cannot use Card.
- No verification checkbox or float-funding/setup UI. References are optional. Historical float and transaction records remain available for accurate historical data; new cart quotes have no float movement.
- Atomic checkout, stable confirmation IDs and a unique header prevent duplicate financial posting. Failure rolls back the whole invoice. Digital services do not change physical inventory.
- Additive SQLite migration links transactions to ServiceCheckouts; old records default to cash and retain their IDs.
- F1 Cash, F2 Card, F4 Clear, Ctrl+F Search, Enter field navigation, Ctrl+Shift+Delete Remove, Ctrl+S Save, Ctrl+Shift+O Resume.
- Large display sizes use bounded package results, vertically scrollable workspace and readable horizontally scrollable cart columns.

## Performance observations and changes

On this PC (i7-8850H, 6 cores / 12 threads, Intel UHD 630 and NVIDIA Quadro P3200), the first active-work sample showed testhost ~20.6% CPU, Firefox processes ~28%, and an orphaned profile-wide Git scan ~6.2%. Codex processes were a smaller contributor in that sample. These are short samples, not permanent application averages.

Stopped the orphaned read-only Git scan after confirming its parent had exited. Backed up C:/Users/nas/.git/info/exclude to exclude.before-pos-performance-20261127 and excluded generated AppData and tool caches from the otherwise empty home repository. Tracked files, project repositories and history are untouched.

Build and test packaging scripts now use BelowNormal priority and DOTNET_PROCESSOR_COUNT=2, restored in finally. MSBuild stays single-worker, without persistent compilation/build servers. Unit test collections execute serially. Verification jobs run sequentially, and release staging stays outside OneDrive.

A subsequent sample during the limited build showed overall CPU load 34%, Firefox ~10%, Codex ~3%, and dotnet ~11%. Workloads differ between samples; this does not promise a fixed CPU ceiling. Hardware GPU counters already showed video decode/3D activity for Firefox, GPU rendering for Codex, and compositor activity on both adapters. POS source does not force software rendering; the WPF tier will be recorded by the UI check.

No GPU-compute dependency, global graphics preference, driver change, browser configuration change, or permanent CPU/affinity restriction was added. Git, .NET compilation and ordinary transactional work remain CPU work; moving them to a GPU is not a general-purpose optimization.

Primary guidance: [WPF hardware rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware), [.NET processor-count override](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_processor_count), [Windows scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities).

## Verification

- 220 unit tests passed before final display-layout/concurrency changes.
- First UI run completed digital cart, draft, payment, grouped invoice and cash-out assertions, then reached the broader retail checks before the prior three-minute harness deadline. Screenshots exposed clipping at 200%; that was corrected before release.
- Focused digital/receipt verification: 191 assertions passed. Broader retail, pharmacy, images, keyboard and receipt verification: 345 assertions passed. WPF reported tier 2 / Default.
- Final serial unit and copied live-database upgrade checks are in progress. Release payload and installed hashes are checked before delivery. Never install on a running POS.

Limits: this POS records manual digital services; it does not itself activate a package or perform a bank/provider transfer. Physical printer, real WhatsApp sending and provider APIs are not exercised by the isolated test suite.
