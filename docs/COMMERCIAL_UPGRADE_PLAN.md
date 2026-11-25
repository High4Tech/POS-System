# Upgrade of the existing local POS

The current modified repository is the source of truth. No upstream checkout, reset, catalog replacement, or scanner rewrite is part of this work.

## Inspection

- Startup/composition: `src/SupermarketPos.Wpf/App.xaml.cs`, `Infrastructure/DependencyInjection.cs`.
- MVVM shell and existing settings: `Wpf/ViewModels/MainViewModel.cs`, `SettingsViewModel.cs`, `Views/SettingsView.xaml`, `Common/StoreProfile.cs`.
- English/Urdu live resources: `Domain/Localization/Strings*.resx`, `Wpf/Localization/LocalizationManager.cs`.
- SQLite: `Infrastructure/Persistence/PosDbContext.cs`, migrations, repositories, `DatabaseSeeder.cs`; existing data is under D:\SupermarketPos.
- BCrypt/local accounts: `Application/Services/AuthService.cs`, `Domain/Entities/User.cs`.
- Per-product inclusive VAT: `Domain/Pricing/PricingService.cs`; invoices: `Infrastructure/Documents/ReceiptDocument.cs`, `ReceiptService.cs`.
- Storage/media: `Wpf/Common/PosStorage.cs`, `Infrastructure/Persistence/PosMediaStore.cs`. Consistent migration backups exist; a full user-facing backup/restore workflow does not yet exist.
- Existing project publishes a self-contained Windows app; no Velopack integration or commercial backend exists.
- Scanner transports/pairing, checkout, inventory, purchases, account balances and serial tracking remain in their existing modules.

## Implementation sequence and files

1. Add durable, versioned business configuration, audit events, tax classifications and fiscal transmission state under `Domain/Business`, `Application/Abstractions/IBusinessSetup.cs`, `Infrastructure/Business`; extend `PosDbContext.cs`, add an additive migration and update its snapshot. Preserve all business records and existing identities.
2. Add resumable `Wpf/Views/OnboardingWindow` and its view model using existing controls/resources; extend `App.xaml.cs`, `PosStorage.cs`, settings and language resources. Language is the first interactive setup screen. Existing users authenticate before profile changes; fresh users create a strong local administrator. Recovery uses a locally generated one-time owner code, stored only as a hash.
3. Add separate FBR POS/digital-invoice adapter contracts, mock sandbox, local validation, protected credentials and declared mandatory-integration guard at sale completion. Extend product tax metadata/editor and receipt mapping without replacing existing VAT arithmetic. Production integration stays unavailable without an implemented authorized provider.
4. Add perpetual device-bound ECDSA certificates, DPAPI storage, online/offline activation and transfer requests under `Infrastructure/Commercial`; add isolated `publisher/` Cloudflare Worker, D1 migrations, authenticated admin UI and local integration tests. No customer POS data enters the publisher database.
5. Add Velopack entry point/update UI, signed release-envelope verification, entitlement/channel checks, staging, offline import, consistent full backups and guarded install. Add publisher release tooling and Windows scripts, without producing the final shareable installer yet.
6. Add local redacted diagnostics and explicit-review/consent support UI. Publisher support management uses authenticated endpoints. No automatic report uploads.
7. Run existing regression tests plus onboarding, signature, device-seat, tax mapping, update tamper/eligibility and privacy tests. Exercise local Wrangler/D1 and WPF English/Urdu at 100%/200%. Document executed checks and remaining external prerequisites accurately.

## Boundaries

- Development mode is visibly identified and cannot be packaged as a stable commercial release. It never embeds a private signing key. Existing installation is not locked out while publisher deployment is pending.
- Expired maintenance or an unavailable API never invalidates an otherwise valid perpetual certificate.
- FBR configuration is not certification. NTN and STRN are distinct. Mock results cannot create an official number or authorize production checkout for a declared mandatory business.
- Cloudflare deployment, production FBR and trusted Authenticode signing require publisher credentials; finish local implementation and supply deployment instructions when those are unavailable.
