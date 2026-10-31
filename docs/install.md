# 安裝指南

## 系統需求

- Windows 11 x64：目前的主要開發與實測環境。其他 Windows 版本未完成驗收。
- ZIP 版需要 [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)；單 EXE 已包含此執行階段。ZIP 版請選 **Desktop Runtime**，不是只有 ASP.NET Core Runtime。
- Switch 2 Pro 或 PS5 DualSense、可傳輸資料的 USB 線，或可正常工作的藍牙適配器。
- 虛擬輸出需要 usbip-win2 Client 驅動。初次安裝驅動需要系統管理員權限；應用程式本身不是驅動安裝器。

不需要 Python、Node.js 或瀏覽器執行環境。

## 取得與解壓

從 [ControllerHub Releases](https://github.com/MostimaJK/ControllerHub/releases/tag/v0.3.0-preview) 選擇：

- `ControllerHub-v0.3.0-preview-portable-win-x64.exe`：單檔直接執行，已包含 .NET 8；首次會自動建立本機快取。
- `ControllerHub-v0.3.0-preview-win-x64.zip`：完整解壓，需另外安裝 .NET 8 Desktop Runtime x64。

單 EXE 的具體行為見[便攜版說明](portable.md)。以下解壓步驟適用 ZIP 版。GitHub 的「Download ZIP」下載的是原始碼，不是可直接執行的程式包。

1. 將發布 ZIP 完整解壓到固定資料夾。
2. 保留 `ControllerHub.exe`、同目錄 DLL、`Backend` 和其他隨附資料；不能只複製一個 EXE。
3. 安裝 .NET Desktop Runtime 後啟動 `ControllerHub.exe`。

可用 PowerShell 檢查發布 ZIP 雜湊，與同名 `.sha256` 檔比較：

```powershell
Get-FileHash ./ControllerHub-v0.3.0-preview-win-x64.zip -Algorithm SHA256
```

## 安裝虛擬輸出驅動

USB/IP 的「USB」指 Windows 虛擬裝置的輸出方式。即使實體手柄透過藍牙連線，虛擬 Xbox／PS5／Pro 輸出仍會使用這條鏈路。

1. 透過程式內的 USB/IP 安裝入口，或執行 `Backend/USBip-0.9.7.7-x64.exe`。
2. 在 Windows 權限視窗確認安裝。
3. 選取 **Main Files／Client**；不必安裝 GUI、SDK 或 PDB 元件。
4. 若安裝器要求重啟，儲存工作後依提示重啟，再開 ControllerHub。

ControllerHub 內的入口會使用固定版本並核對檔案 SHA-256，不會靜默安裝驅動。這是隨附的第三方驅動元件，不需要另外常駐開啟它的 GUI。只測實體輸入時，可先使用「僅測試實體輸入」。

## HidHide 是可選的

HidHide 用來處理遊戲同時收到「實體手柄」和「虛擬手柄」而重複輸入的情況，並非連接手柄的前提。

此版本提供整合與安裝入口，但尚未完成實機驗收。不要為了第一次啟動就啟用全域隱藏；應先確認基本輸入與虛擬輸出正常。需要使用時，閱讀 HidHide 的設定說明並確認應用程式允許清單。Switch 2 自訂傳輸的隱藏狀態不能視為已驗證。

## 升級與移除

升級前先正常關閉程式，將新版解壓到乾淨資料夾，避免新舊 DLL 混用。從舊 ControllerFusion 改名為 ControllerHub 後，仍沿用 `%LOCALAPPDATA%/ControllerFusion` 的設定與輸出記錄，保留既有配置。

移除應用程式：先正常退出，再刪除解壓資料夾。設定若需一併移除，請先備份上述資料目錄。USB/IP／HidHide 是獨立系統元件，應透過 Windows「已安裝的應用程式」管理；其他映射工具可能也在使用，應用程式資料夾刪除不會自動卸載它們。
