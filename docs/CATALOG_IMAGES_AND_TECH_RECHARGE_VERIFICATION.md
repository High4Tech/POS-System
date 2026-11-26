# Catalog preferences and Tech Retail recharge

## Changes

Catalog images are off by default for existing and new stores. Settings → Business, licensing & support → Business features has one English/Urdu option, “Show product and category images”. It controls photographs and initials in Sales categories/products, inventory rows, product/category image choosers and low-stock notifications. Stored image files and references are preserved so switching the setting off is reversible. Store branding and invoice logos are separate settings.

Image visibility is independent of business type, search fields, product pricing and quantity controls. The ordinary product form remains available with images off. Pharmacy can explicitly enable catalog images through the same setting.

The existing shared inventory table now exposes category, stock, expiry, batch and barcode filters to retail stores as well. Pharmacy retains its medicine search, manufacturer, formulation, setup and stock-location controls. Retail summaries say products rather than medicines. Switching business types clears stale medicine filters. Status badges use a six-pixel corner radius through the shared badge style.

New Tech Retail profiles enable mobile recharge by default. The current local Tech Retail store's recharge flag is enabled separately after a database backup. This exposes Load and Mobile Packages through the single Digital Services entry point. Wallet cash-in/cash-out remains separately configurable. Pharmacy restrictions remain enforced by the backend.

Digital transactions still require configured providers and package records. No public package prices or benefits were invented. Confirmed financial posting requires the existing external provider confirmation, and repeated confirmations cannot post twice. Enabling a POS module does not itself activate a network package.

A click-through also found that the product collection's outer white container covered the Digital Services workspace. Its visibility now follows the existing context-panel state, so services and Loose Goods occupy the shared workspace and closing them restores the collection. This preserves the existing height divider and cart.

## Verification

The full unit/integration run passed 203 tests. New coverage includes settings serialization and defaults for Tech Retail, Pharmacy and Retail, plus confirmed Tech Retail load/package postings and idempotency with no physical inventory or sales postings.

The Release desktop run passed 150 assertions. It exercised images off/on in all three business types at 100% and 200%, visible search and price inputs, shared category filters, editor controls, low-stock preferences, settings binding and badge corners. It also verified that Load is available, the service workspace is visible without the collection covering it, and Close restores the collection. Rendered Sales, inventory, editor and service screens were inspected. The isolated click-through used real Windows input without posting live transactions.

The desktop build completed with no warnings or errors. Source unit/integration and desktop checks ran against isolated databases. Release publishing and installed-file verification use the required local build/update scripts.

Local evidence is under engineering/qa-data/catalog-settings-20261126-183559. Real provider activation, mobile carrier hardware and physical printing are outside these isolated checks.

## Installed update

The required scripts built and installed 1.1.0-beta.7+gcac3994.36a30f7a829d into D:\SupermarketPos\App and automatically launched process 23024. Desktop source revision cac39948e7471f0ad765f7b1d0eefb5cd84fbb5e was pushed to codex/tech-retail-keyboard and the remote hash matched. Subsequent report and diagnostic-reader changes do not change the Windows application source.

The configuration backup is D:\SupermarketPos\Backups\Before-catalog-settings-20261126-191853.zip. The update's data backup is Before-update-20261126-141904334.zip and application rollback backup is Previous-application-20261126-141904334.zip in the same folder.

The published EXE SHA256 is 0F144C55B71350C1687E125EE5BDD909974CB711B51CC4C1F71C5203116E34B8. The WPF assembly SHA256 is 03B492DB8D641F317D6FF25BDB34273A42542C592B7E49FBE1D662B972F63A46. Source fingerprint is 36A30F7A829D6E7E29A101D19F15183C1C8A3F5E4F404096FE1860B244C575DB. Release build metadata verification passed. No final distribution installer was created.

Independent verification matched the installed EXE, WPF assembly and runtime configuration hashes to the payload. Both database archives were read successfully and contained a 5,382,144-byte pos.db. The application archive contained a readable 163,840-byte executable. All 14 business-table fingerprints matched the values collected before enabling recharge and installing the update. The automatically launched process was responding at the target of the existing Tech Store POS desktop shortcut.

The diagnostic setup-status reader now reports the saved catalog-image preference and computed module availability. It read back BusinessType=electronics, EnableCatalogImages=false, MobileRechargeAvailable=true and DigitalServicesAvailable=true from the live database. Evidence is installed-evidence.json and installed-profile.json in the local QA folder. Provider/carrier activation was not performed against a real account.
