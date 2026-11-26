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
