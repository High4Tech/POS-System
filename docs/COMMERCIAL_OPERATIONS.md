# Commercial extension of this existing POS

This extends the locally modified WPF/SQLite application. The existing register, stock, purchasing, batches, customer/supplier balances, warranties/IMEIs, receipt preview and mobile scanner modules remain in place. No upstream clone or reset was used. The publisher service is the sibling `../../publisher` folder; engineering tools are in `../../engineering`.

The subsequent optional store-type, measured/package inventory, manual services and three invoice actions are documented in [RETAIL_EXTENSIONS.md](RETAIL_EXTENSIONS.md). The current commercial schema generation is 2, adding RetailMeasurements and DigitalServices to generation 1's BusinessOnboarding migration. New releases must advertise suitable schema bounds; the example manifest now permits generations 1–2.

## Current delivery status

Implemented locally: resumable language-first setup; existing-owner authentication; fresh strong administrator creation; one-use hashed owner recovery; registration/tax profiles; optional encrypted local documents; per-product effective-dated tax rules; invoice preferences; offline signed activation; separate local publisher API/admin dashboard; authenticated R2-compatible release delivery; Velopack feed/import/install safeguards; consistent data backups, staged restore and relocation; reviewed, consent-based support reports.

**Not deployed or certified:** Cloudflare production/staging resources, real commercial signing keys, a publicly trusted Windows signing certificate, a Velopack-installed signed 1.0.0 → 1.0.1 upgrade, and an officially authorized FBR provider. No purchase, paid service or final shareable installer was made. Normal development builds are explicitly shown as development mode and retain the current store's access while the publisher is being configured.

The current manually copied D:\SupermarketPos\App installation is not a Velopack installation. The updater detects this and refuses to apply an update. It must first be installed with the reviewed signed Velopack installer when the owner asks to create the final installer. Preparing source/scripts is not the same as testing that commercial installer.

On 2026-11-19 the verified local development build was installed into that existing application folder and launched successfully at language selection. Startup migration preserved all counted records: 102 products, eight categories, 12 sales, 22 sale lines, one purchase/line, one supplier and two users. The consistent full data backup is `D:\SupermarketPos\Backups\Before-commercial-onboarding-20261119.zip`; the previous application is retained in `D:\SupermarketPos\Backups\App-before-commercial-20261119`. The owner completes their own business details and existing-account authentication; the test-store configuration was not installed into the real database.

## Local Windows development

The onboarding follow-up fixes remove the modal-window maximum-size cap, group/style the registration radios, and validate fields on their own pages (including required registered identity, contacts, license shape, tax identifiers, passwords and storage paths). Errors appear below their inputs and block Continue until corrected; unfinished draft text may be resumed but cannot complete setup without validation. Complex fields have localized hover/click information controls. A previous unfinished setup's inherited logo is replaced by a Your/logo placeholder until an explicit upload; a sample full receipt is shown without recording a sale. The files are `OnboardingWindow.xaml/.cs`, `OnboardingViewModel.cs`, `BusinessForm.cs`, `SetupInvoicePreview.cs`, `BusinessProfile.cs`, `BusinessSetup.cs`, `LicenseClient.cs`, localized resources and the setup-width resource in `DesktopTextSize.cs`/`Theme.xaml`.

Use PowerShell 7, a .NET 8 SDK and Node. Run in the existing workspace; do not clone a replacement project.

```powershell
Set-Location 'C:\Users\nas\OneDrive\Documents\ChatGPT\Local POS Sokution'
dotnet test .\upstream\tests\SupermarketPos.Tests\SupermarketPos.Tests.csproj
dotnet build .\upstream\src\SupermarketPos.Wpf\SupermarketPos.Wpf.csproj
dotnet run --project .\engineering\UiChecks\UiChecks.csproj -- .\engineering\qa-data\a-new-test-folder
```

The UI tool creates an isolated database and renders real WPF windows in English/Urdu at 100%/200%. Use a **new** scratch folder each run. It never uses the installed shop database, saved bootstrap folder pointer, or a live license. Its `.gitignore` excludes databases, screenshots and binaries.

Fresh application startup calls database initialization with `createDefaultUser:false`: it does not create admin/admin. Existing users are preserved, authenticate with their current credentials, and do not receive a replacement account. Setup drafts are stored locally without passwords. A fresh owner password requires at least 12 characters with a letter and digit and no more than 72 UTF-8 bytes. The recovery code is displayed once, stored only as a BCrypt hash, consumed after use and rate-limited after repeated failures. An authenticated owner can replace it using their current password.

Settings → Business, licensing & support contains the new modules. Existing Store Profile still owns store name, address, logo, language, operators, password and display size. New profile saves synchronize those existing fields. Unregistered settings hide the FBR/tax tabs; correcting registration and saving exposes them.

## Local publisher: free, no account required

```powershell
Set-Location .\publisher
npm ci
npm test
node .\tools\local-secrets.mjs
npm run migrate:local
npm run dev
```

The test suite uses local workerd/D1 and local R2 emulation, not a cloud account. Test secrets are generated in memory. `local-secrets.mjs` is a separate manual development setup: it creates ignored `.dev.vars` and `local-secrets/*.pem` exclusively and refuses to overwrite them. Never reuse these keys or administrator tokens in production. Do not paste private keys into chat, source files, build properties or logs.

Open `http://localhost:8787/admin`. Read the local administrator token from your ignored `.dev.vars` privately and paste it into the dashboard. It stays in browser memory, not local/session storage. Every administrative API request requires the bearer token; hiding a button is not authorization. HTTPS is mandatory outside development. For production, put an additional identity boundary such as Cloudflare Access in front of publisher administration.

1. Create a test customer using only publisher contact information.
2. Generate a **test** perpetual retail license, one seat, permitted major version 1 and a chosen update-entitlement date.
3. Copy the key from the creation response immediately; only its SHA-256 hash is stored and it cannot be retrieved later.
4. View customers, licenses, activations, transfer requests, releases, reports and audit records using Refresh.
5. Use Offline activation to paste an exported activation request and obtain the signed response JSON. Save that response and import it on the matching Windows device.
6. Request a transfer from the Windows app; approve/release the old activation in the publisher dashboard; activate the replacement device. A revoked device cannot reactivate with the same license.

The D1 seat-limit triggers enforce concurrency; activation retries preserve identity. No customer POS database, catalog, invoices or FBR credentials are sent to this service. Reports contain only allowlisted technical fields and an explicitly approved description. The admin dashboard can filter reports by version/category and mark them Open, Investigating or Resolved with a fixed release reference.

For a local development desktop build, set `POS_DEV_API=http://127.0.0.1:8787` and `POS_DEV_PUBLIC_KEY` to the **public** PEM in the launching process. Development mode does not require activation; it can exercise both activation paths. Commercial builds ignore these environment overrides and embed their reviewed HTTPS endpoint/public key at build time. `LicenseClient` supports dependency injection for local test hosts; production endpoints reject HTTP.

## Offline license behavior and limits

Certificates use ECDSA P-256/SHA-256 with fixed-width P1363 signatures. The desktop contains a public verification key only. Certificates bind the license, activation and maximum permitted major version to a SHA-256 fingerprint derived locally from the Windows machine identity. The raw Windows identity is not transmitted. Protected certificates use current-user Windows DPAPI and are checked locally without a network request at startup.

Purchased versions remain valid after maintenance ends. A commercial build has an immutable publication date, so a certificate for an older purchased version cannot authorize a later build outside its entitlement. Eligible update metadata is checked separately. Offline operation and server availability are independent. Invalid/missing activation provides access to existing records and blocks business writes; it does not remove or encrypt the database. Restoring a data backup on another computer requires that computer's legitimate activation.

An indefinitely offline perpetual certificate cannot be instantly remotely revoked after a transfer. The original offline certificate can continue working; server APIs, downloads and reactivation reject the revoked activation. This is an explicit limitation, not a claim of perfect copy protection. OS reinstallation, replacement or cloned Windows images can also require publisher review of device identity. Extending update entitlement requires refreshing activation to obtain a newly signed certificate; the old certificate remains perpetual for its eligible version.

## Cloudflare staging / production preparation

No Cloudflare resources have been created by this work. In a free account, create **separate** D1 databases, secrets, signing keys, customers and releases for staging and production. Update each placeholder ID in `publisher/wrangler.jsonc` before deployment.

```powershell
npx wrangler login
npx wrangler d1 create pos-publisher-staging
npx wrangler d1 create pos-publisher-production
# Edit database_id in the matching environment; then:
npx wrangler d1 migrations apply pos-publisher-staging --remote --env staging
npx wrangler secret put ADMIN_TOKEN --env staging
npx wrangler secret put SIGNING_PRIVATE_KEY --env staging
npx wrangler secret put SIGNING_PUBLIC_KEY --env staging
npx wrangler deploy --env staging
```

Use a separate high-entropy administrator token (at least 32 characters) and P-256 key pair for each environment. Enter secret values using Wrangler's secret prompt; do not put them in the command text or Git. Restrict access to signing keys and keep a protected publisher recovery copy. Repeat the corresponding commands with `--env production` only after the staging pilot and signed upgrade/restore tests pass. The default workers.dev domain is sufficient for a pilot; a custom domain is unnecessary.

Development R2 is emulated locally. Staging/production do **not** inherit that local bucket binding. If the account supports R2 without unwanted billing setup, create separate private release buckets and add a `RELEASES` binding to the corresponding environment. Otherwise leave it absent and finish offline package testing; the download endpoint returns storage unavailable. Do not enable public bucket access. Upload only reviewed update packages, never shop backups. Free-tier quotas and account enrollment must be checked in the owner's account; this work does not promise unlimited free hosting. Increasing paid capacity later changes resources/limits, not the desktop data architecture.

## Signed Windows releases — prepare now, package only on request

The project pins Velopack 1.2.161. Its custom `Program.Main` runs Velopack hooks before WPF. Release channels are `development`, `testing` and `stable`; production customers use stable unless explicitly enrolled in testing. Versions use a three-part numeric core, e.g. 1.0.1. Major upgrades require an appropriate licensed major version. Bugfix/security/minor/major metadata is explicit; the current policy requires an eligible publication date for all update kinds.

`engineering/Prepare-CommercialRelease.ps1` builds the **existing** project. Without `-CreateInstaller` it prepares binaries only. It requires an HTTPS API, public P-256 verification key, immutable release date and a testing/stable channel. Commercial project builds fail when required configuration is absent. No signing private key enters the desktop build.

```powershell
.\engineering\Prepare-CommercialRelease.ps1 -Version 1.0.1 `
  -PublisherApi 'https://YOUR-STAGING-WORKER.workers.dev' `
  -PublicKeyFile 'C:\ProtectedPublisherKeys\staging-public.pem' `
  -PublishedAt '2026-11-19T00:00:00Z' -Channel testing
```

After the owner explicitly requests the final installer, use the same preparation script with `-CreateInstaller`, a matching pinned `vpk` tool and a trusted code-signing certificate in the current user's certificate store (`-SigningThumbprint`). This invokes Velopack signing during packaging and verifies the packaged main executable's Authenticode trust. Review the installer signature too. No self-signed certificate has been installed into Windows trust stores by this work.

Copy `publisher/release.example.json` into an ignored private release folder. Set the actual version, edition, channel, minimum application version, compatible commercial schema range, publication date, update kind, signer certificate thumbprint and release notes. Commercial schema generation 1 includes migration `20261119140000_BusinessOnboarding`; change compatibility deliberately for future schema generations.

```powershell
Set-Location .\publisher
node .\tools\sign-release.mjs .\releases\metadata.json `
  .\releases\SupermarketPos-1.0.1-full.nupkg `
  'C:\ProtectedPublisherKeys\staging-private.pem' .\releases\release.signed.json
```

The tool fills the actual filename, size and SHA-256, signs the full metadata, self-verifies and refuses to overwrite output. Upload the matching package to private R2, then publish the signed envelope and matching object key through the authenticated admin dashboard. The Worker checks eligibility before streaming bytes; it exposes no shared administrator secret to clients. The current retail extension requires compatibility with commercial schema generation 2; do not publish it with a generation-1-only maximum.

The desktop verifies signed metadata, channel/edition/version/schema/entitlement, complete package size/hash and pinned trusted Authenticode publisher. It synthesizes a local Velopack feed only from that verified envelope, then rechecks the exact Velopack cached package before installation. Offline import selects `release.signed.json` with its matching full nupkg alongside it and uses the same checks. A hash obtained from an unsigned download manifest is never the trust anchor.

Install remains manual. Sale/cart, payment and purchase drafts block it; they are not silently cleared. The app checks free space, makes an integrity-checked database/media backup, stops the scanner, closes connections and invokes Velopack. History distinguishes `install-requested` from `startup-verified`. An unavailable server does not interrupt selling or an otherwise valid offline license. Release notes and download/remind-later controls are available.

## Data protection, backups and recovery

Data stays in the selected local folder (this PC: D:\SupermarketPos), separate from binaries. A bootstrap DPAPI-protected folder pointer is the only relocation information under LocalAppData. Relocation requires an empty writable local destination and is performed at the next start using a consistent backup, before any EF/scanner context opens. The source folder is retained. It never copies an active database with a naive File.Copy.

Settings → Data & backups creates a ZIP containing a SQLite online-backup snapshot plus Media, CategoryImages, Scanner, Documents, PDFs and root JSON/text preferences. It verifies `PRAGMA quick_check`. The archive is **not encrypted as a whole**; protect backup media because it contains shop records and pairing data. Business document attachments remain DPAPI-encrypted and are normally readable only by the same Windows user; export individual documents explicitly before migrating to another Windows account. Activation and FBR credential blobs are excluded from portable backups; reactivation/reconfiguration may be needed on a new PC. Same-user folder relocation preserves those protected blobs.

Restore validates archive paths, allowed contents, duplicate entries, size limits, SQLite integrity and application schema compatibility. The user confirms the concrete restore file. Restoration is staged and only applied at the next start. The prior database/media backup is retained, media is copied before the verified database is installed last, and an unfinished stage is retried before opening business contexts. A restore intentionally restores the snapshot date; finish all work first.

Before an additive migration the app makes a consistent database backup. On migration failure it closes connections, retains the failed database and WAL/SHM for investigation, restores the compatible previous database and exits with a recovery notice. This path is tested. It does **not** continue operating on a partly migrated schema.

For a failed installed update, stop the POS, keep the complete data folder, diagnostics and pre-update backup, and contact support. Restore the matching known-good application/database pair. Do not run an old executable against a successfully migrated newer database, remove migration history rows, or automatically downgrade schemas. A successful commercial installer rollback still needs a signed two-version pilot test before production release.

## FBR and local tax boundary

Formal business registration, income-tax registration and sales-tax registration are separate. FBR describes individual NTN/registration as a 13-digit CNIC and AOP/company NTN as seven digits; an STRN is distinct. See [FBR registration guidance](https://www.fbr.gov.pk/categ/apply-for-ntn/51147/30849/71148) and [FBR STRN invoice notice](https://download1.fbr.gov.pk/Docs/2019891982838814SALESTAXCIRCULARNO.3OF2019--STRNINVOICE.pdf). Format checks do not verify a taxpayer against FBR records or determine their legal obligations.

Local product rules record HS code, sale type, classification, rate, authority/SRO reference and effective dates, reject overlapping rules and preserve the existing inclusive VAT arithmetic. Tax-exclusive entry converts to the existing inclusive selling-price representation. Rule sources and rates must be verified by the business's qualified adviser; no generic Pakistan-wide rate or legal exemption has been invented. An effective rule that disagrees with the checkout VAT rate blocks checkout until corrected.

FBR POS and Digital Invoicing have distinct adapter routes. Only a **local mock sandbox** is implemented. Configure provider `mock`, route POS or Digital, sandbox, appropriate sales-tax registration, and status Sandbox. A permitted registered test sale stores its mapping and validation state locally. The settings test validates that stored invoice, reports validation codes, and never contacts a production tax endpoint. Invoices retain the sale-time registered header and local tax snapshot; legacy invoices without a snapshot retain their previous current-header behavior.

No official FBR number, QR or certification badge is generated. Declaring mandatory transmission blocks ordinary checkout/setup completion because no production adapter is authorized; configuring later never silently enables it. Existing VAT rates are not removed by disabling connectivity. See [FBR integration FAQs](https://urdu.fbr.gov.pk/faqs/173967/173969) for the official licensed-integrator context.

Before production FBR use, obtain the business's actual authorizations and provider contract, implement/test the route-specific official schema and authoritative acceptance response, secure the production credentials, and verify returns, cancellations, credit/debit adjustments and outage procedures for that scheme. There is deliberately no functioning “activate production” switch until that work is authorized and tested. A mock validation is not evidence of legal compliance.

## Executed checks and release gates

- 109 .NET regression/integration tests passed: existing sale/purchase/stock/batch/credit/IMEI behavior plus onboarding/resumption, field validation and incomplete drafts, strong/recovery credentials, tax mapping/arithmetic, immutable registered snapshot, perpetual certificates, tampering, entitlement, DPAPI restart/device mismatch, read-only writes, backup/restore/traversal and failed-migration recovery.
- Seven local publisher tests passed: administrator authorization, one-time license key exposure, concurrent seat limits/idempotency, offline activation, transfer/revocation, signed release eligibility, authenticated local R2 delivery and denied expired entitlement, consent/allowlist/report filtering/status, rate limits, HTTPS and release-signing tool safeguards.
- 579 real WPF harness checks passed across English/Urdu, RTL, 100%/200%, all setup pages, all registered settings tabs, recovery, bindings and visible action bounds. Interaction checks verify exclusive registration selection, field-specific required/format errors, Continue disable/re-enable, legacy-logo removal, sample invoice preview and maximize to the desktop work area. Captured screens were inspected; complex-field guidance uses tooltips to retain readable layouts at 200%. Velopack reads/downloads the generated local feed and rejects changed/unsigned packages; active drafts prevent installation.
- No new Android build, physical phone camera/Wi-Fi/Bluetooth test or printer hardware test was performed for this commercial extension. Those scanner implementations were preserved. The subsequent retail extension adds Windows spooler printing with configured-printer/error adapter tests; actual paper output remains unverified. See RETAIL_EXTENSIONS.md for its executed checks.
- **Release gate still open:** trusted signed install/update/restart across two versions, interrupted install recovery, offline signed full-package import on a Velopack installation, physical second-PC activation, Cloudflare staging pilot and authorized production FBR acceptance.

The Windows code changes are concentrated in startup/composition, the new Business/Commercial modules, SQLite additive migration/snapshot, setup/settings/recovery windows, localization and targeted checkout/receipt hooks. Publisher source and tests are separate from customer business services. The initial named-file plan is in `COMMERCIAL_UPGRADE_PLAN.md`.

Primary updater references: [Velopack Windows CLI](https://docs.velopack.io/reference/cli/content/vpk-windows), [code signing](https://docs.velopack.io/packaging/signing), [release channels](https://docs.velopack.io/packaging/channels). No claims of fully tested commercial deployment should be made until the release gates above are executed.
