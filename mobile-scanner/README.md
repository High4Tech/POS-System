# Phone Scanner

> **A lightweight, privacy-conscious barcode capture system that turns any Android phone into a real-time data-collection terminal.**


Phone Scanner connects an Android scanning application to a small on-premise Python service. It is designed for teams that need to capture barcode or QR-code data quickly at the point of work—without requiring a cloud account, a proprietary handheld terminal, or a complicated deployment.

The project is intentionally simple where it matters: scan with a phone, send the result over the local network, and view the live stream from a browser. That simplicity makes it suitable for pilots, internal operational tools, event check-in, inventory capture, asset identification, classroom/lab workflows, and future integration with business systems.

---

## 1. Project Vision

Many barcode workflows still depend on manual transcription, paper logs, expensive dedicated scanners, or cloud services that are disproportionate to the task. Phone Scanner explores a practical alternative:

- **Use hardware people already have** — an Android phone with a camera.
- **Keep operational data local** — the default workflow runs on the same LAN.
- **Reduce friction at the point of capture** — scan, validate connectivity, and deliver the event immediately.
- **Provide a clear integration path** — a compact HTTP and Server-Sent Events (SSE) interface can be connected to dashboards, databases, ERP systems, or automation tools.

The current prototype focuses on reliable local capture and live visibility. It is a foundation for a deployable scanning workflow rather than a claim to replace every enterprise inventory platform.

## 2. Key Capabilities

| Capability | What it provides |
| --- | --- |
| Camera-based scanning | Uses the Android rear camera and on-device ML Kit barcode recognition. |
| Broad barcode support | QR Code, EAN-13, EAN-8, UPC-A/E, Code 128, Code 39/93, ITF, Codabar, Data Matrix, PDF417, and Aztec. |
| Real-time delivery | Sends each accepted scan to the local Python server via HTTP. |
| Connection resilience | Keeps unsuccessful uploads in an in-memory queue and retries every two seconds. |
| Live operations view | A browser-based monitor receives scan events through SSE. |
| Operator controls | Continuous/single-scan mode, flash, manual entry, sound, vibration, and a volume-button trigger. |
| Local configuration | The server address and feedback preferences remain on the device after the app is restarted. |
| Low deployment overhead | The server uses Python's standard library for its normal HTTP mode. |

## 3. System Architecture

```text
┌────────────────────┐       POST /scan        ┌─────────────────────┐
│ Android Phone App  │ ──────────────────────> │ Python LAN Server   │
│ Camera + ML Kit    │                          │ API + event bus     │
└────────────────────┘                          └──────────┬──────────┘
                                                            │ SSE /events
                                                            ▼
                                                 ┌─────────────────────┐
                                                 │ Browser Monitor     │
                                                 │ Real-time scan view │
                                                 └─────────────────────┘
```

### Data flow

1. The phone detects a barcode or QR code using the rear camera.
2. The app filters duplicate camera detections within a 1.5-second window.
3. The scan is shown to the operator and queued for delivery.
4. The app sends a JSON payload to `POST /scan` on the configured server.
5. The server publishes the event to connected monitoring pages in real time.
6. If a delivery attempt fails, the app keeps retrying until it succeeds or the operator clears the queue.

## 4. Intended Use Cases

The architecture is deliberately reusable. Typical pilot scenarios include:

- **Inventory and stocktaking** — rapid capture of product codes during counts, receiving, or replenishment.
- **Asset management** — identify equipment, tools, samples, or loaned items at the point of handover.
- **Event and visitor operations** — scan identifiers and present a live arrival stream to an operator.
- **Laboratory and education** — record materials, specimens, books, or learning equipment without buying dedicated scanners.
- **Field operations** — use a phone as a low-cost collection terminal on a controlled local network.

Final business rules, authentication, persistent storage, and downstream actions are intentionally left to the integrating system. This keeps the prototype focused, understandable, and adaptable.

## 5. Quick Start

### Prerequisites

- A computer with **Python 3**.
- An Android phone running the Phone Scanner app.
- The phone and computer connected to the same Wi-Fi or local network.

### Start the receiving server

```powershell
cd server
python server.py
```

The terminal prints a LAN address for the phone, for example:

```text
App address: 192.168.1.23:8765
Monitor page: http://localhost:8765/monitor
```

If Windows Firewall asks for permission, allow Python to communicate on **private networks**.

### Configure the Android app

On the first launch, the app opens its settings dialog automatically. Later, use the **設定 / Settings** button in the top status bar.

Enter the server address displayed by the server, such as `192.168.1.23:8765`, and select **Save**. Use **Test Connection** before scanning to confirm that the phone can reach the server.

Accepted address formats:

| Input | Address used by the app |
| --- | --- |
| `192.168.1.23` | `http://192.168.1.23:8765` |
| `192.168.1.23:9000` | `http://192.168.1.23:9000` |
| `http://192.168.1.23:8765` | Used as entered |
| `https://scanner.example.local:8765` | Used as entered |

The app adds `http://` when a scheme is omitted and adds port `8765` when a port is omitted. The address, sound, and vibration preferences are stored in Android `SharedPreferences`, so they persist after restarting the app.

> **Important:** Do not enter `localhost` for a computer server. On the phone, `localhost` means the phone itself. Use the computer's LAN IP address instead.

### Open the live monitor

On the computer running the server, open:

```text
http://localhost:8765/monitor
```

New scan events appear in real time.

## 6. API Reference

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `GET` | `/ping` | Verifies that the app can reach the server. |
| `POST` | `/scan` | Receives a scan payload. |
| `GET` | `/monitor` | Serves the live monitoring page. |
| `GET` | `/history` | Returns recent in-memory scan records. |
| `GET` | `/events` | Provides a Server-Sent Events stream of scan records. |

Example request:

```json
{
  "code": "4712345678901",
  "format": "ean_13"
}
```

## 7. Security and Privacy Position

The default architecture supports a privacy-conscious local workflow: scans are sent directly to the configured LAN server and no cloud account is required by this project. The included server keeps recent events in memory for live monitoring; it does not include persistent database storage by default.

For production deployments, the organisation should assess its own requirements for network segmentation, authentication, authorization, encryption, retention, audit logs, backups, and data classification. HTTPS is available as an optional server mode, but its self-signed certificate must be trusted by the phone.

```powershell
cd server
python server.py --https
```

The HTTPS mode invokes `openssl` to create a self-signed certificate on first use. For an operational deployment, use an organisation-managed certificate and security policy where appropriate.

## 8. Android Build

This is a native Android project using Gradle, Android Gradle Plugin 8.5.2, Kotlin 2.0.20, `compileSdk 34`, and `targetSdk 34`.

Open the project root in Android Studio and choose **Build > Build APK(s)** for a debug build. For command-line builds, point Gradle to the Android SDK first:

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
gradle assembleDebug
```

The installable debug APK is written to:

```text
app/build/outputs/apk/debug/app-debug.apk
```

The project currently has no release signing configuration. `assembleRelease` therefore creates an unsigned artifact:

```text
app/build/outputs/apk/release/app-release-unsigned.apk
```

Set up an organisation-owned keystore and Gradle signing configuration before distributing a production release or publishing an AAB.

## 9. Roadmap for a Pilot or Funded Implementation

The existing prototype validates the central workflow—camera capture, local delivery, live visibility, and reconnect behavior. A next implementation phase can turn that foundation into a managed operational system:

| Workstream | Potential deliverables |
| --- | --- |
| Workflow discovery | User journeys, barcode standards, exception paths, and acceptance criteria for the target organisation. |
| Data integration | Persistent database, CSV/ERP/WMS connectors, webhooks, and field validation rules. |
| Operational controls | Authentication, user/device identity, roles, audit logs, and configurable retention. |
| Reliability | Durable offline queue, idempotency keys, background sync, health monitoring, and automated backups. |
| Deployment | Signed release builds, managed certificates, installation guide, device onboarding, and network hardening. |
| Evaluation | Pilot metrics such as scan completion rate, delivery latency, operator training time, and reduction in manual entry. |

## 10. Project Structure

```text
app/
  src/main/java/tw/tools/scanner/
    MainActivity.kt           UI, camera scanning, and operator flow
    Prefs.kt                  Local preferences and server-address normalization
    Uploader.kt               HTTP delivery and retry queue
server/
  server.py                   HTTP/HTTPS receiving server and API
  monitor.html                Live browser monitoring page
```

## 11. Troubleshooting

- **The app cannot connect:** Check that the phone and computer share the same network, the server is running, the address and port are correct, and TCP 8765 is allowed through the firewall.
- **`localhost` does not work:** Replace it with the computer's LAN IP address.
- **Scans do not appear in the monitor:** Run **Test Connection** in the app, then make sure the monitor is opened from the same running server.
- **HTTPS fails:** The phone must trust the server certificate; use HTTP for initial LAN testing if necessary.
- **No camera preview:** Grant the app camera permission in Android system settings.

---

# 手機條碼掃描器

> **將一般 Android 手機轉換為即時資料蒐集終端的輕量化、重視隱私的條碼掃描系統。**

Phone Scanner 由 Android 掃描 App 與電腦端 Python 接收伺服器組成。手機掃描條碼或 QR Code 後，App 會透過區域網路將結果傳到伺服器；瀏覽器可即時看到掃描事件。

本專案適合需要在工作現場快速蒐集條碼資料的情境，例如盤點、資產管理、活動報到、實驗室或教學設備管理，以及現場作業試辦。它不需要雲端帳號或專用手持掃描器，可作為後續串接資料庫、ERP、WMS 或自動化流程的基礎。

## 1. 計畫定位與價值

傳統條碼作業常依賴人工抄寫、紙本紀錄、昂貴的專用掃描設備，或與實際需求不成比例的雲端服務。Phone Scanner 提出一個務實的替代方向：

- **善用既有設備**：以具備相機的 Android 手機作為掃描終端。
- **資料可留在內網**：預設資料流在同一個區域網路中完成。
- **降低現場操作摩擦**：掃描、連線確認與事件送出集中在一個操作流程。
- **保留整合彈性**：透過簡潔的 HTTP 與 SSE 介面，可延伸對接儀表板、資料庫、ERP 或自動化工具。

現有原型的目標是驗證「本地掃描、即時傳送、即時可視」的核心流程；企業級的身分驗證、永久儲存與商業規則，則可在導入階段依使用單位需求整合。

## 2. 現有功能

| 功能 | 說明 |
| --- | --- |
| 相機掃描 | 使用 Android 後鏡頭與裝置端 ML Kit 辨識條碼。 |
| 多格式支援 | QR Code、EAN-13、EAN-8、UPC-A/E、Code 128、Code 39/93、ITF、Codabar、Data Matrix、PDF417、Aztec。 |
| 即時傳送 | 每筆掃描結果以 HTTP 傳送至內網 Python 伺服器。 |
| 斷線重試 | 上傳失敗時會暫存於記憶體佇列，每 2 秒重試。 |
| 即時監看 | 瀏覽器以 SSE 接收最新掃描事件。 |
| 現場操作 | 連續／單次模式、手電筒、手動輸入、提示音、震動與音量鍵觸發。 |
| 裝置內設定 | 伺服器位址、提示音與震動偏好會留在手機中。 |
| 低部署門檻 | 一般 HTTP 模式只使用 Python 標準函式庫。 |

## 3. 系統流程

```text
Android 手機 App -- POST /scan --> Python 內網伺服器 -- SSE --> 瀏覽器監看頁面
```

1. 手機後鏡頭辨識條碼或 QR Code。
2. App 會在 1.5 秒內過濾同一條碼的重複相機辨識。
3. 條碼結果顯示給操作人員，並進入傳送佇列。
4. App 將 JSON 資料送至已設定伺服器的 `POST /scan`。
5. 伺服器將事件即時推送給已開啟的監看頁面。
6. 若傳送失敗，App 會持續重試，直到傳送成功或操作人員清空佇列。

## 4. 適用情境

- **庫存與盤點**：收貨、補貨、盤點時快速記錄商品條碼。
- **資產管理**：設備、工具、樣本或借用物品的交接識別。
- **活動與訪客作業**：掃描識別碼並讓後台即時掌握到場狀況。
- **實驗室與教育現場**：記錄材料、書籍、樣本或教學設備，降低專用設備採購門檻。
- **現場作業試辦**：在受控內網中，以手機作為低成本資料蒐集終端。

## 5. 快速開始

### 前置條件

- 一台已安裝 **Python 3** 的電腦。
- 已安裝 Phone Scanner App 的 Android 手機。
- 手機與電腦連線至同一個 Wi-Fi 或區域網路。

### 在電腦啟動接收伺服器

```powershell
cd server
python server.py
```

終端機會印出 App 要填的位址，例如：

```text
App 位址：192.168.1.23:8765
監看頁面：http://localhost:8765/monitor
```

若 Windows 防火牆跳出詢問，請允許 Python 在**私人網路**通訊。

### 在 App 內設定 IP／伺服器位址

首次開啟 App 時，設定視窗會自動出現；之後按上方狀態列右側的「設定」即可開啟。

在「電腦位址」欄位輸入終端機顯示的位址，例如 `192.168.1.23:8765`，按「儲存」後可按「測試連線」確認手機是否連到伺服器。

| 輸入內容 | App 實際使用的位址 |
| --- | --- |
| `192.168.1.23` | `http://192.168.1.23:8765` |
| `192.168.1.23:9000` | `http://192.168.1.23:9000` |
| `http://192.168.1.23:8765` | 原樣使用 |
| `https://scanner.example.local:8765` | 原樣使用 |

未填協定時，App 會補上 `http://`；未填連接埠時，會補上 `8765`。位址、提示音與震動選項儲存在 Android `SharedPreferences`，重開 App 後仍會保留。

> **注意：** 不可填 `localhost` 來連電腦伺服器，因為手機中的 `localhost` 是手機自己；請填電腦的區域網路 IP。

### 開啟即時監看頁

在執行伺服器的電腦瀏覽器開啟：

```text
http://localhost:8765/monitor
```

新掃描的事件會即時顯示。

## 6. API

| 方法 | 路徑 | 用途 |
| --- | --- | --- |
| `GET` | `/ping` | 確認 App 可連到伺服器。 |
| `POST` | `/scan` | 接收掃描結果。 |
| `GET` | `/monitor` | 提供即時監看網頁。 |
| `GET` | `/history` | 回傳近期的記憶體掃描紀錄。 |
| `GET` | `/events` | 提供掃描事件的 Server-Sent Events 串流。 |

範例請求：

```json
{
  "code": "4712345678901",
  "format": "ean_13"
}
```

## 7. 資安與隱私定位

預設流程可在內網中完成：掃描資料直接送往設定的 LAN 伺服器，專案本身不要求雲端帳號。附帶的伺服器僅為即時監看保留近期事件於記憶體，預設不包含永久資料庫。

正式導入時，應依組織規範評估網路分區、驗證與授權、傳輸加密、保存期限、稽核紀錄、備份與資料分類。伺服器支援選用 HTTPS：

```powershell
cd server
python server.py --https
```

HTTPS 模式首次執行會以 `openssl` 產生自簽憑證，手機必須信任該憑證才能連線。實際營運環境建議導入組織管理的憑證與相應的資安政策。

## 8. 建置 Android APK

本專案為原生 Android 專案，使用 Gradle、Android Gradle Plugin 8.5.2、Kotlin 2.0.20，`compileSdk` 與 `targetSdk` 均為 34。

以 Android Studio 開啟專案根目錄後，選擇 **Build > Build APK(s)** 可建立 Debug APK。命令列建置前需指定 Android SDK 路徑：

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
gradle assembleDebug
```

可直接安裝的 Debug APK 會輸出至：

```text
app/build/outputs/apk/debug/app-debug.apk
```

目前尚未設定 release 簽章；執行 `assembleRelease` 只會產生未簽章檔案：

```text
app/build/outputs/apk/release/app-release-unsigned.apk
```

計畫正式發布或上架前，請以組織擁有的 keystore 建立 Gradle signing configuration，產出已簽章的 release APK 或 AAB。

## 9. 後續計畫與可交付成果

現有原型已驗證相機掃描、本地傳送、即時可視與連線失敗重試等核心流程。下一階段可將其發展為可管理的營運系統：

| 工作面向 | 潛在交付成果 |
| --- | --- |
| 流程盤點 | 使用者旅程、條碼規格、例外處理與驗收標準。 |
| 資料整合 | 持久化資料庫、CSV／ERP／WMS 串接、Webhook 與欄位驗證。 |
| 營運控管 | 驗證、使用者與裝置識別、權限、稽核紀錄與保存政策。 |
| 穩定性 | 可持久化的離線佇列、冪等鍵、背景同步、健康檢查與備份。 |
| 部署維運 | 已簽章發布版本、受管理憑證、安裝指引、裝置導入與網路強化。 |
| 成效評估 | 掃描完成率、傳送延遲、操作人員訓練時間與人工輸入減量等指標。 |

## 10. 專案結構

```text
app/
  src/main/java/tw/tools/scanner/
    MainActivity.kt           UI、相機掃描與操作流程
    Prefs.kt                  本機偏好設定與伺服器位址正規化
    Uploader.kt               HTTP 傳送與重試佇列
server/
  server.py                   HTTP/HTTPS 接收伺服器與 API
  monitor.html                即時瀏覽器監看頁面
```

## 11. 疑難排解

- **App 無法連線**：確認手機與電腦在同一網路、伺服器正在執行、IP 與連接埠正確，並檢查防火牆是否允許 TCP 8765。
- **填 `localhost` 無法連線**：改填電腦的區域網路 IP。
- **監看頁沒有掃描紀錄**：先在 App 按「測試連線」，並確認瀏覽器開的是同一台正在執行的伺服器。
- **HTTPS 失敗**：手機必須信任伺服器憑證；初次內網測試可先使用 HTTP。
- **沒有相機畫面**：至 Android 系統設定授予 App 相機權限。
