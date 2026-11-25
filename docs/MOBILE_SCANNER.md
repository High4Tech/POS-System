# POS Scannar

**POS Scannar** is the Android companion for the Windows POS. Install the signed test APK on Android 7.0 or newer. The Android package ID lets it update older test builds signed with the same certificate. After upgrading from 1.2, use **Settings → Forget pairing** on the phone if it still has the old pairing, then pair with the updated desktop.

## Pair and reconnect

1. Set up the local transport using the table below. Select the same method in Windows and Android.
2. On Windows, open **Phone scanner** in the POS sidebar. Select the local adapter and click **Pair a phone**.
3. Open POS Scannar and point its camera at the QR code. To pair manually, enter the displayed IP, port, fingerprint, and one-time code, or paste the pairing link.
4. Once paired, leave the phone app open for scanning. Windows remembers the adapter and phone. Reopen either app to reconnect when the same local endpoint is available.

| Method | Setup |
| --- | --- |
| Wi-Fi | Connect both devices to the same trusted local network. Windows can also use Ethernet on that subnet. Keep the phone on Wi-Fi when Android reports that the network has no internet. |
| Bluetooth PAN | Pair the devices in their system Bluetooth settings, enable Bluetooth tethering on Android, and join the phone’s Personal Area Network on Windows. Select the Bluetooth adapter in the POS before pairing. |
| USB tethering | Use a data-capable USB cable, unlock the phone, and enable USB tethering in Android settings. Wait for the Remote NDIS or CDC NCM adapter to appear on Windows and choose it in the POS. |

Bluetooth PAN and USB require compatible Android and Windows hardware and drivers. Android may turn off tethering on disconnect. Re-enable tethering or rejoin the PAN if required by the device. Physical Bluetooth and USB delivery requires device verification. [Windows Bluetooth PAN](https://support.microsoft.com/en-gb/windows/hardware/bluetooth/connect-to-a-bluetooth-network-in-windows) · [Android tethering](https://support.google.com/android/answer/9059108)

The desktop protects its identity and trust data with Windows user-scoped DPAPI, and the phone protects credentials with Android Keystore. Access sessions are valid for 24 hours by default. The phone uses its separate trust credential to renew over pinned HTTPS when needed. The POS supports one trusted phone at a time. **Disconnect** keeps trust, and **Forget phone** revokes it. Pair again after revocation, moving the POS to a different Windows account, or moving to a new local address that the phone has never seen. Local endpoint discovery is currently based on the saved address and adapter. [Windows data protection](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)

Windows Firewall exceptions should be scoped to the installed POS on a trusted Private network, the displayed TCP port, and the local subnet. The default port is 8765. If it is occupied at first pairing, POS Scannar tries 8766–8769 and remembers the selected port. If an exception is needed, use this from an elevated PowerShell window with the displayed port:

```powershell
New-NetFirewallRule -DisplayName 'POS Scannar' -Direction Inbound -Action Allow -Profile Private -Program 'D:\SupermarketPos\App\SupermarketPos.exe' -Protocol TCP -LocalPort 8765 -RemoteAddress LocalSubnet
```

## Use scanning

- **Register → Scan** adds known products to the cart up to the available stock. Repeat a physical scan by moving the barcode out of the camera frame and back or pressing a volume key. Unlisted barcodes remain in a desktop notice while the next products are scanned.
- **Products & Inventory → New Product / Edit → Scan barcode** captures one barcode. If another record already uses it, choose **View existing product**. A USB keyboard scanner or typed barcode can use Enter in the barcode field.
- **Purchases → New purchase** lets you select or add a supplier and enter an invoice reference and date. Click **Scan products** or type a barcode and press Enter. Repeated scans increase quantities. You can change the quantity or unit cost in the draft. To create an unlisted product at zero stock, choose **Add to Inventory** in its notice. Save it, then scan it again to add it to the purchase.
- **Confirm stock receipt** applies the purchase and its stock movements in one SQLite transaction. It saves the latest purchase cost separately from the selling price. Use the supplier filter in **Purchase history** and **Stock movements** in a product’s editor to trace changes.

Checkout, purchase confirmation, product creation, and workflows outside these screens pause business input while the phone can remain connected. The scan reply identifies receipt of the event and its processed result. An unconfirmed delivery is never replayed into a later draft.

**Sounds & feedback** is available in Windows Store profile and Android Settings. Windows uses the local system sound scheme. Android plays the original short beep through the media stream and offers a volume slider, test cue, and optional vibration. Successful sale and purchase scans sound only after the register confirms them.

On Android, ZXing-C++ decodes barcodes locally. The camera checks that the barcode is inside the crop area and sees the same reading in two frames before sending it.

Purchase history records receipts and supplier names. Payables, returns, purchase orders, purchase-level tax and discounts, and partial receipts are outside this existing stock-receiving flow.

## Build and verification

Use .NET 8 for Windows, and JDK 17, Gradle 8.7, Android platform 34 and build tools 34.0.0 for Android.

```powershell
dotnet test tests/SupermarketPos.Tests -c Release
dotnet publish src/SupermarketPos.Wpf -c Release -r win-x64 --self-contained true -p:PosDataDirectory=D:/SupermarketPos
gradle :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

Run the Gradle command in the sibling Android checkout. The test APK is debug-signed. Camera, audio output, and Bluetooth/USB transports require testing on a compatible physical phone.

## Modified areas

- Windows connection and routing: `Infrastructure/Scanning`, `ScannerController`, `ScannerStorage`, `ScannerFeedback`, and the sidebar / connection panel.
- Windows workflows: Register, the shared product editor, the Purchases view, and Store profile sound controls.
- SQLite: the supplier and stock movement entities, purchase receipt service, persistence model, and additive `20261117190000_SupplierReceipts` migration.
- Android: the launcher label, pairing preferences, HTTPS uploader, scanner states, and the bilingual sound settings.
- Tests: Windows HTTPS restart and renewal tests, receipt idempotency and stock-movement tests, and the existing Android scanner checks.

See [security audit](MOBILE_SCANNER_SECURITY.md) for the prior baseline and dependency review.
