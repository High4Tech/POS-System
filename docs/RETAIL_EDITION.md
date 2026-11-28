# Wijgandu Retail

Standalone Tech Retail edition of the existing High4Tech Windows POS. This source is published in [High4Tech/pos_backup](https://github.com/High4Tech/pos_backup). The full application remains in [High4Tech/POS-System](https://github.com/High4Tech/POS-System).

## Included behavior

- Local store/owner setup, optional logo, English/Urdu and local username/password. No online signup or commercial activation.
- Physical sales, inventory, purchasing, receipts, customer/supplier balances, loose/packaged goods, IMEI/serial and warranty options.
- Mobile load, mobile packages and bank/wallet transfer/cash-out recording. Direct transaction form → payment → invoice, without a normal digital cart.
- Fixed/percentage service charge defaults and per-transaction overrides; Pakistani recipient validation; optional provider costs and customer names.
- Separate digital Customers and Reports, grouped by phone/account/IBAN. Existing old saved batches remain recoverable.
- The existing scanner, backup and keyboard workflows are retained.

The shared projects stay intact. The standalone build flag selects the simple Retail experience; it does not remove full-edition source or migrate an existing store to another business type.

## Build the installer

On Windows x64 with .NET 8 SDK, the existing dependencies and Velopack CLI:

```powershell
dotnet test tests/SupermarketPos.Tests/SupermarketPos.Tests.csproj
./engineering/Build-TestRetail.ps1 -Version '1.2.0-beta.7' -Vpk '<path to vpk.exe>' -StandaloneRetail
```

Use `-StandaloneRetail`; running the shared WPF project without that edition selection opens the full development application.

The script creates an installer under `D:/Retail POS/Releases/1.2.0-beta.7/packages`. The main executable is `Wijgandu.exe`, and the default data folder is `%LOCALAPPDATA%\WijganduRetail\Data`, separate from `D:\SupermarketPos`. Do not include an existing store database or `Protected` files in a shared installer.

The verified local installer is unsigned. Digital services are manual records; actual activation/transfers require the authorized provider. Catalog prices are reference snapshots, not a live provider feed.

See [release verification](engineering/Direct-Digital-20261128.md) and [versions/recovery](docs/VERSIONS_AND_RECOVERY.md). Preserve matching data backups when using older application versions. Source and third-party notices retain their existing licenses.
