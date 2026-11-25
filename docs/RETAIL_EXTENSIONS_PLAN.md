# Extensions of the existing local POS

Reuse decimal pricing/VAT, atomic SaleService and PurchaseService, batches/expiry, customer-sale identities and balances, managed media, receipt rendering and existing MVVM/styles. Preserve the modified checkout/scanner/licensing modules.

1. Extend Domain/Business/BusinessProfile.cs with business type and four independent feature flags/defaults. Extend Views/OnboardingWindow.xaml.cs and BusinessSettingsWindow; fix fixed-position selected tabs, help dismissal, shared StoreProfile identity and blue palette. Add existing-customer choice to CustomerFieldsViewModel/View.
2. Extend Product.cs, CartLine.cs/Cart.cs, SaleLine.cs, Purchase.cs and PurchaseService.cs for integer grams/ml/piece stock, optional loose barcode/internal identity and one package conversion. Extend ProductEditViewModel/ProductEditorView, ProductsViewModel, PosViewModel and PurchasesViewModel. Add a measured-sale dialog using existing forms. Preserve transaction and batch guards.
3. Add Domain/Services and Infrastructure service-ledger module with providers, package catalog, idempotent confirmation and compensating reversals. Add a gated Digital Services view and separate report totals; principal never enters retail revenue.
4. Extend ReceiptPreviewDialog/Window and existing ReceiptService for the three saved-invoice actions. Add small Windows printer and WhatsApp handoff services, local PDF cache/manual attachment access. No automated financial transfers/messages or cloud uploads.

SQLite: additive retail migration for integer product stock/package metadata, sale/purchase unit snapshots, service providers/packages/transactions/postings. Business features remain in the existing JSON configuration. Retain old columns/records; backfill quantities with exact decimal-text conversion. Update EF snapshot.

Verify defaults, gram rounding/amount ceilings, purchase and package conversions/barcodes, insufficient stock, batches, idempotent service postings/reversals and isolated report income, phone normalization/handoff fallbacks, PDF reuse and invoice action independence. Exercise English/Urdu/scale layouts. Build and back up before installing into the existing D-drive app. Physical printer/WhatsApp delivery cannot be claimed without a real hardware/user test. No final installer.
