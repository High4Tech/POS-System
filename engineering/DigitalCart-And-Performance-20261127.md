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

After the Release build exited and with no production POS process open, one overall CPU reading was 94%. A following five-second sample identified a Firefox content process at 15% plus other browser activity; a later system-inclusive sample was much lower. These fluctuations are not an idle POS workload. Exact browser-tab inspection was blocked by automatic privacy review; process-level counters were used instead.

No GPU-compute dependency, global graphics preference, driver change, browser configuration change, or permanent CPU/affinity restriction was added. Git, .NET compilation and ordinary transactional work remain CPU work; moving them to a GPU is not a general-purpose optimization.

Primary guidance: [WPF hardware rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-taking-advantage-of-hardware), [.NET processor-count override](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_processor_count), [Windows scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities).

## Verification

- 220 unit tests passed before final display-layout/concurrency changes.
- First UI run completed digital cart, draft, payment, grouped invoice and cash-out assertions, then reached the broader retail checks before the prior three-minute harness deadline. Screenshots exposed clipping at 200%; that was corrected before release.
- Focused digital/receipt verification: 191 assertions passed. Broader retail, pharmacy, images, keyboard and receipt verification: 345 assertions passed. WPF reported tier 2 / Default.
- Final serial regression run: 220 / 220 unit tests passed. Copied live-database upgrade preserved every original column/row in all 32 existing tables; EF snapshot and SQLite integrity checks passed.
- Checkpoint: D:/SupermarketPos/Backups/Before-digital-cart-beta3-20261127.zip, 29 entries, 5,398,528-byte database. No production POS process was running during the checkpoint.
- Release payload and installed hashes are checked before delivery. Never install on a running POS.

Limits: this POS records manual digital services; it does not itself activate a package or perform a bank/provider transfer. Physical printer, real WhatsApp sending and provider APIs are not exercised by the isolated test suite.

## Installed beta.3 verification

Installed version: 1.2.0-beta.3+g81d5246.d3a7e592d77d, code revision 81d5246949afe619374d03a9c42f7bacbe6e1973. Source fingerprint D3A7E592D77DFCC8EF7C25CE014DFD846EFD060197A119CE02D1AA0C89B7BBB9.

The published Release assemblies passed the 191 focused digital/receipt assertions again, including the final Sales tile type sizing. Installed EXE SHA256: 169B6BCDF3B8F678820501243AD9799FCCC22B318417AACD2E1090AB2A461D13. Installed WPF DLL SHA256: 43908B8679EB81786991625833B24FCAF8F2432457E1BDB91F983B8CD038197B. EXE, DLL and runtime configuration match the verified payload; the updater compared every installed payload file.

Updater backups: Before-update-20261127-083311949.zip and Previous-application-20261127-083311949.zip under D:/SupermarketPos/Backups. The data archive was verified, staged and restored into an isolated QA folder with the application's restore service. The previous-application archive contains 427 entries and preserves all three binary hashes recorded before installation.

Automatically launched PID 16680 from D:/SupermarketPos/App/SupermarketPos.exe, matching the existing Tech Store POS desktop shortcut. The app reopened at Sign In with Normal process priority and without a DOTNET_PROCESSOR_COUNT override. Installed post-login interaction was not performed; the exact published binaries were exercised in the isolated UI harness.

### Desktop-inspection observer effect

Foreground idle samples taken while the desktop-inspection session was connected measured 10.47% and 4.53% CPU. Method-name-only stacks showed the UI dispatcher waiting for messages and an accessibility thread in Automation.ElementProxy.GetRuntimeId; no business operation was active in that snapshot. After resetting/detaching the inspection session, a fresh 10.03-second sample used 0.140625 CPU seconds: 0.12% of the PC's 12 logical processors. This is a Sign In idle measurement, not a loaded-sale benchmark. Leave desktop inspection detached when assessing normal idle behavior.

Microsoft's dotnet-stack diagnostic tool was installed only in the external temporary build-tools folder; it is not an application dependency or a resident service. Browser contents and settings were left private, as requested. No real provider activation, bank transfer, physical printer job or WhatsApp message was performed.