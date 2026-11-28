# Wijgandu Retail

Standalone Tech Retail edition of the existing High4Tech Windows POS. This source is published in [High4Tech/pos_backup](https://github.com/High4Tech/pos_backup). The full ORMA application remains in [High4Tech/POS-System](https://github.com/High4Tech/POS-System).

## Included behavior

- Local store/owner setup, optional logo, English/Urdu and local username/password. No online signup or commercial activation.
- Physical sales, inventory, purchasing, receipts, customer/supplier balances, loose/packaged goods, IMEI/serial and warranty options.
- Mobile load, mobile packages and bank/wallet transfer/cash-out recording. Direct transaction form → payment → invoice, without a normal digital cart.
- Fixed/percentage service charge defaults and per-transaction overrides; Pakistani recipient validation; optional provider costs and customer names.
- Separate digital Customers and Reports, grouped by phone/account/IBAN. Existing old saved batches remain recoverable.
- The existing scanner, backup and keyboard workflows are retained.

The shared projects stay intact. The standalone build flag selects the simple Retail experience; it does not remove full-edition source or migrate an existing store to another business type.

## Download

The [1.2.0-beta.8 testing release](https://github.com/High4Tech/pos_backup/releases/tag/retail-v1.2.0-beta.8) includes the shareable Windows installer and signed update package. Windows may warn about the unsigned testing installer.

Installer SHA256: `A407015EF9B87CE0792873ADED4FA832D1A6A5D3B1FB03048AECC1134FAC1B0C`. The signed full package SHA256 is `5E9FBDA37398A9FFDB8BC147EF95E51B688A8904B73DFD726EA32230C8EA7BDC`.

## Build the installer

On Windows x64 with .NET 8 SDK, the existing dependencies and Velopack CLI:

```powershell
dotnet test tests/SupermarketPos.Tests/SupermarketPos.Tests.csproj
./engineering/Build-TestRetail.ps1 -Version '1.2.0-beta.8' -Vpk '<path to vpk.exe>' -StandaloneRetail
```

Use `-StandaloneRetail`; running the shared WPF project without that edition selection opens the full development application.

The script creates an installer under `D:/Retail POS/Releases/1.2.0-beta.7/packages`. The main executable is `Wijgandu.exe`, and the default data folder is `%LOCALAPPDATA%\WijganduRetail\Data`, separate from `D:\SupermarketPos`. Do not include an existing store database or `Protected` files in a shared installer.

The verified local installer is unsigned. Digital services are manual records; actual activation/transfers require the authorized provider. Catalog prices are reference snapshots, not a live provider feed.

See [release verification](https://github.com/High4Tech/POS-System/blob/main/engineering/Direct-Digital-20261128.md) and [versions/recovery](https://github.com/High4Tech/POS-System/blob/main/docs/VERSIONS_AND_RECOVERY.md). Preserve matching data backups when using older application versions. Source and third-party notices retain their existing licenses.

## Retail updates

Standalone Retail checks its public, signed testing feed without a commercial license. Only packages authenticated by the embedded Retail signing key, matching WijganduRetailApp and the declared version/schema, can install. Older beta.7 installations need the new installer once to gain this updater. Offline use stays available. The full ORMA edition retains its existing license-based update policy.

The publisher signing key is held in Windows current-user protected storage outside the repository and store data. Preserve that publisher Windows profile and its protected key; losing it requires an explicit key-rotation installer. Private keys must never be included in a customer installer or Git.
