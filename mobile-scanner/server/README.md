# 手機條碼掃描器

把手機變成內網條碼掃描器。手機掃到的條碼會送到這台電腦的 Python 服務，
可在監看頁即時查看並匯出 CSV。

電腦端只用 Python 標準函式庫，不裝套件也能跑；服務**不會模擬鍵盤輸入**。

---

## 一、電腦端

```bash
python server.py          # 讓 Android App 透過內網 HTTP 上傳
python server.py --https  # 讓手機瀏覽器在 Wi-Fi 模式能使用相機
```

---

## 二、手機連上來，二選一

### A. USB 線（Android，最穩、不用憑證）

手機開發者選項開啟 **USB 偵錯**，接上線後在電腦執行：

```bash
adb reverse tcp:8765 tcp:8765
```

手機瀏覽器開 `http://localhost:8765`。

`adb reverse` 是把手機的 localhost 轉接到電腦，所以瀏覽器把它當成本機、
直接放行相機權限，**不需要任何憑證**。網路完全走 USB 線，Wi-Fi 斷了也能用。

adb 從 Android SDK Platform-Tools 下載，解壓即用。

### B. Wi-Fi 內網（Android / iPhone 都可以）

```bash
python server.py --https
```

終端機會印出網址（例如 `https://192.168.1.23:8765`），手機和電腦要在同一個 Wi-Fi。
第一次開會跳憑證警告 → 選「進階」→「繼續前往」。這是自簽憑證的正常現象。

> **必須用 https。** 直接開 `http://192.168.x.x:8765` 相機一定打不開,
> 瀏覽器只在 HTTPS 或 localhost 底下開放相機。

iPhone 想免除每次的憑證警告，可以把 `cert/server.crt` 傳到手機安裝，
再到「設定 → 一般 → 關於本機 → 憑證信任設定」打開信任。

---

## 三、開始掃

1. 手機頁面按「開啟相機」，允許權限
2. 電腦瀏覽器開 `http://localhost:8765/monitor`
3. 對準條碼 → 嗶一聲 → 資料會出現在監看頁

電腦另外開 `http://localhost:8765/monitor` 可以看即時清單、一鍵複製全部、匯出 CSV。

---

## 參數

| 參數 | 作用 |
|---|---|
| `--https` | 啟用自簽憑證（Wi-Fi 模式必需） |
| `--port 8765` | 換連接埠 |
| `--host 0.0.0.0` | 指定監聽位址；預設讓同一內網的手機可連線 |

---

## 支援的條碼

QR Code、EAN-13/8、Code 128、Code 39/93、UPC-A/E、ITF、Codabar、Data Matrix、PDF417、Aztec。

Android Chrome 用系統內建的解碼器（快、準、免下載）。
iPhone Safari 沒有內建，頁面會自動載入 ZXing 函式庫,**這時手機需要能上網一次**。
想完全離線的話，把 `@zxing/library` 的 `umd/index.min.js` 抓下來，
放成 `vendor/zxing.js`（跟 server.py 同層），頁面就會優先用本地版本。

---

## 卡住的時候

| 症狀 | 原因 |
|---|---|
| 按開啟相機沒反應、跳「需要安全連線」 | 用了 http + IP。改 USB 模式或 `--https` |
| 憑證警告過不去 | Chrome 的空白頁面點一下再輸入 `thisisunsafe` 也可以通過 |
| 條碼掃到了，電腦沒反應 | 開 `http://localhost:8765/monitor` 查看紀錄；本服務不會打字到其他程式 |
| 掃一次連續送出好幾筆 | 正常防呆是 1.5 秒,可改 scanner.html 裡的 `DEDUPE_MS` |
| 手機螢幕一直暗掉 | 頁面有要求 wake lock,iOS Safari 不支援,請調長自動鎖定 |

---

## 安全性

伺服器對整個內網開放，沒有密碼——同一個 Wi-Fi 底下的人只要知道網址，
就能送入掃描紀錄。在家裡或辦公室內網用沒問題，
**在公共 Wi-Fi 請改用 USB 模式**，或加上 `--host 127.0.0.1` 只聽本機（搭配 adb reverse 剛好夠用）。
