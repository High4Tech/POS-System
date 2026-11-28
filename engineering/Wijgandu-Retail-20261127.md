# Wijgandu retail update — 27 November 2026

Continue the existing POS; preserve business data and prior versions. No upstream replacement or new application architecture.

Implementation scope: reuse the service cart, checkout, product editor and isolated local setup. Add provider fee defaults and recipient validation; adjust keyboard routes; brand executable, caption, setup and package as Wijgandu. Build the independent retail edition from the same project with a separate package ID, mutex and data folder. Verify both editions before updating the installed desktop app.

The supplied original image is copied unchanged into Assets/wijgandu.png (SHA256 BB9239917ED1DBF93EAEAEE98924B667ACDE3D0162B25C83E55D9FC66CD377BC). Convert-AppIcon.ps1 creates six Windows icon sizes without cropping. App branding does not assign or replace the business logo.

Digital services: Prepaid/Postpaid is a mutually exclusive Mobile Load choice. Packages retain their configured account type. Cart More and provider-reference controls are removed. Shared payment/invoice behavior remains. Default charges are saved per provider as fixed rupees or a percentage, rounded to two decimal places. A row override affects only that row. Existing defaults migrate as fixed values without changing historic transactions.

Recipients: local mobile numbers must have 11 digits beginning 03; equivalent +92/0092 forms normalize to the same recipient. Pakistani IBANs require the 24-character country/bank/account structure and MOD97 checksum. Domestic bank-account lengths vary by bank; provider settings can override published defaults. Unknown banks require a valid mobile/IBAN until their account lengths are configured. Validation checks format, not account ownership or bank reachability.

Primary format references: [SBP IBAN guidance](https://www.sbp.org.pk/circulars/psd-circular-no-02-of-2012), [SWIFT IBAN registry](https://www.swift.com/sites/default/files/files/iban-registry-v101.pdf), [UBL Funds bank-specific IBFT formats](https://www.ublfunds.com.pk/alerts-and-statements/ibft-account-number-formats/).

Windows navigation uses Win+H/S/D/P/T/R/C/X/Z while the app is active, where native registration succeeds. Alt+the same letter remains available. Win+T is Stock. Conflicts are shown in the shortcut reference; reserved OS combinations are not stolen with hooks or registry changes. Registration is released on deactivation. Isolated QA never installs app hotkeys; the native availability probe always unregisters immediately. [Microsoft RegisterHotKey rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

The standalone edition uses %LOCALAPPDATA%/WijganduRetail/Data and WijganduRetailApp package identity. Initial setup requests local credentials and store/owner details, with an optional business logo. No external account/license or pharmacy/business-type chooser appears. Tech retail enables loose, packaged and digital services; images stay off by default. No expiry checkbox appears on new retail products. The original multi-business edition retains its pharmacy workflows.

Verification results and installed/package hashes are recorded below after checks complete. Real provider transactions, physical printer output, WhatsApp sending and physical Windows-key presses are outside the automated checks.

## Verification

Serial regression run: 233/233 unit/integration tests passed (wijgandu-beta4-final.trx). New cases cover mobile normalization/rejection, IBAN checksum, bank-specific account lengths, percentage/fixed fees, persistence and rollback before invalid destinations can post. Older fixtures now use valid recipient data. The transport-label test uses the existing adjustable clock, since its real-clock heartbeat could expire under scheduling stalls; dedicated disconnect/expiry tests are unchanged. The restricted-sandbox backup failure passed when run with normal Windows filesystem access against temporary databases.

WPF behavior, standalone first-run setup, published payload, installed hashes and separate installer verification follow below. No live store data was edited by these tests.

The full WPF retail workflow passed 372 assertions, including receipt actions, pharmacy/retail catalog preferences, new product types/warranty, digital default-charge overrides and invalid-recipient blocking, keyboard cart entry and English/Urdu layouts at 100% and 200%. WPF reported rendering tier 2 / Default. Six PNG-backed Windows icon frames were checked at 256/128/64/48/32/16 pixels.

The focused follow-up passed 230 assertions, including actual Alt page routing and opening/closing the existing scanner dialog. Native registration probes reported H/S/D/P/T/R/C/X/Z unavailable on this PC; all have the tested Alt fallback. No Windows keypress was simulated and no OS shortcut setting was changed.

The standalone installer uses the friendly shortcut name Wijgandu Retail so installing it on the same PC does not replace the existing Wijgandu desktop shortcut. Its executable product name and app caption remain Wijgandu.

The standalone beta.4 payload passed 23 setup assertions and 230 checkout/receipt/navigation assertions. Visual review then caught the logo chooser using an unresolved resource key; it now reuses Profile_ChooseLogo in both languages. The final deliverables advance to beta.5 and are rebuilt from the corrected source. The interim beta.4 artifacts remain versioned; they were not installed over the live POS.

Actual desktop launch exposed a WPF packaging conflict that the library-based harness did not: the assembly declared assets/app.ico as content, while publishing copied it to Branding/Wijgandu.ico. MainWindow's real entry-assembly lookup failed with XamlParseException. The updater restored the beta.3 binaries; its data and application archives remain intact. The copy now preserves Assets/app.ico, matching the declared content URI, and Build-LocalDesktop asserts the file/hash at that exact path. Final delivery advances to beta.6 and requires actual executable first-run and completed-store startup checks before retrying installation.

## Final beta.6 delivery verification — 28 November 2026

Installed desktop: 1.2.0-beta.6+gd5210b9.1ea4966afb09. Source d5210b9cdbf1497cc7e6816363e4bd9079d6ecd6; fingerprint 1EA4966AFB09EADB6411546B3517FDDC5F8903FD8F77E7D48F0130F0BBDEF68C. All 428 installed files match the Release payload. EXE SHA256 C9229CD8867A66C1BDC0BAAACB0D4B5BD5E4621EF5147FF7019512619C2A8B20; WPF DLL SHA256 7BE825ED226E88E09CCED38DE3588963245B744917BB1C1619F3F02315E2AA6D. The startup icon is published at Assets/app.ico, SHA256 5F3AE0828ED00A78E214E41FC411D720B459330E1FD20625C49D1CEE48A80A14.

The real executable verifier rejected the broken beta.5 package, then verified beta.6's real completed-store window. The separate retail executable passed both real first-run and completed-store startup with no new diagnostic reports. These checks inspect only their own temporary QA process windows and stop only those owned QA processes; they use no UI Automation or Windows key injection.

The required updater installed beta.6 and automatically launched PID 18600. Final shortcut verification reopened PID 9052 from D:/SupermarketPos/App/SupermarketPos.exe through the renamed Wijgandu.lnk, matching its target and official icon. The process had Normal priority; no build CPU-count override was inherited. The previous Tech Store POS shortcut is backed up under D:/SupermarketPos/Backups. Existing business logo and store identity remain separate from app branding.

Updater checkpoints: Before-update-20261127-165413138.zip (SHA256 30048EE351F1B448262DEEB06BBED7B4A340298A0CB5649641B588B4E5DD6688) and Previous-application-20261127-165413138.zip. The data archive restored through the app's restore service; the copied database passed EF snapshot/SQLite integrity checks and preserved all original columns/rows in 33 tables. The previous application archive has 427 entries and its EXE/DLL hashes match beta.3. The earlier before-update-20261127-163335244 checkpoint also remains available, alongside the original pre-update data and schema checkpoints.

Separate installer: D:/Retail POS/Wijgandu-Retail-1.2.0-beta.6-Setup.exe, 111,409,086 bytes, SHA256 9EBEA8F95ADB263B8EAA39F0E95DBD2FEDCA78815BE2057CAEE78BDA879C2066. Packaged EXE/DLL/runtime-config hashes match the published retail payload, the startup icon matches the source, and the package contains no pos.db or Protected store data. Setup notes are beside the installer. The unshared beta.5 top-level installer was moved into its Superseded checkpoint folder and marked DO-NOT-INSTALL.

Business logic regression: 233 tests passed. Broad retail UI: 372 assertions. Published beta.5 desktop and retail behavior: 230 assertions each; standalone setup/settings/product restrictions: 23 assertions. The beta.6 change was confined to icon packaging and its build/startup verification; actual entry-assembly startup is now checked in addition to the library harness.

Limits: installer is unsigned. A clean second-PC installation, real telecom/bank operations, physical printer output and real WhatsApp sending were not tested. Windows rejected the requested Win navigation registrations on this PC; the Alt+letter alternatives (Stock: Alt+T) were tested, and conflicts are shown in the shortcut reference. Installed post-login work was not performed; the exact business workflows were exercised in isolated stores.
