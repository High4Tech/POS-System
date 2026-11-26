# Register keyboard and Tech Store verification

The implementation and evidence are recorded in [Tech Retail and keyboard-first Sales](TECH_RETAIL_KEYBOARD_FIRST_IMPLEMENTATION.md).

| Capability | Tech Retail | Pharmacy | Grocery | General Retail |
| --- | --- | --- | --- | --- |
| Shared Sales, purchases, customers, reports | Yes | Yes | Yes | Yes |
| Exact device serial/IMEI and warranty | Product-specific | Hidden | Hidden | Hidden |
| Brand, Model and SKU editor | Yes | Hidden | Hidden | Hidden |
| Medicine reference and dispensing controls | Hidden | Yes | Hidden | Hidden |
| Tablet/strip/pack pricing | Hidden | Configured per product | Existing package policy | Existing package policy |
| Loose goods | Existing policy | Hidden | Configurable | Configurable |

The source changes cover the existing top navigation, shared shortcut registry, Sales view and model, product editor, device records, additive product metadata migration, backup compatibility, build/update scripts, and isolated tests. No historical product or invoice conversion is performed.

| Files | Reason |
| --- | --- |
| src/SupermarketPos.Wpf/MainWindow.xaml and MainWindow.xaml.cs | Remove duplicate shop caption and reclaim navigation width |
| src/SupermarketPos.Domain/Localization/Strings.resx and Strings.ur.resx | Simple navigation, Tech Retail and Sales captions |
| src/SupermarketPos.Wpf/Views/PosView.xaml and PosView.xaml.cs | Collection header, Cart customer control, ordered editors and focus transitions |
| src/SupermarketPos.Wpf/ViewModels/PosViewModel.cs, PosViewModel.Catalog.cs, PosViewModel.Customer.cs and PosViewModel.Pharmacy.cs | Accurate counts, manual-add events, shared customer selection and Pharmacy focus |
| src/SupermarketPos.Wpf/ViewModels/CartLineViewModel.cs | Invalid quantity state blocks checkout |
| src/SupermarketPos.Wpf/Common/Shortcuts.cs | Slash search and top-navigation bindings in the existing registry |
| src/SupermarketPos.Domain/Entities/Product.cs | Validated optional Brand, Model and SKU |
| src/SupermarketPos.Infrastructure/Persistence/Migrations/20261126090000_TechRetailDetails.cs and PosDbContextModelSnapshot.cs | Additive metadata migration |
| src/SupermarketPos.Wpf/ViewModels/ProductEditViewModel.cs, ProductsViewModel.cs and Views/ProductEditorView.xaml | Load, edit and save Tech Retail metadata |
| src/SupermarketPos.Application/Abstractions/ITradeLedger.cs and Infrastructure/Persistence/TradeLedger.cs | Include Brand and Model in existing device reporting |
| src/SupermarketPos.Wpf/Views/SerialRecordsWindow.xaml and SerialRecordsWindow.xaml.cs | Device metadata, search and keyboard selection |
| src/SupermarketPos.Infrastructure/Commercial/DataBackup.cs | Accept this application's new schema during restore |
| engineering/ModelTools/Program.cs | Backed-up configuration switch and read-only verification |
| engineering/UiChecks/RetailKeyboardFlow.cs, KeyboardSmoke.cs and Program.cs | Isolated focus, permissions, scanner dispatch and real-key fixture |
| tests/SupermarketPos.Tests/AccountsAndDevicesTests.cs | Metadata persistence and validation regression |
| engineering/Build-LocalDesktop.ps1 and Update-LocalDesktop.ps1 | Existing installation, Release provenance, backups and binary verification |

The same source commit also includes the previously tested local stabilization changes to shared dialogs, styles, customer history, reports and test harnesses. Their earlier evidence is recorded in POS_STABILITY_AND_RELEASE_VERIFICATION.md.
