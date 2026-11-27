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
