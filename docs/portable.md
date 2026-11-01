# 單 EXE 便攜版

從 Release 下載對應版本的 **ControllerHub-v<版本>-preview-portable-win-x64.exe** 後即可執行，不需要手動解壓，也不需要另外安裝 .NET 10 Desktop Runtime。目標系統仍為 Windows 11 x64；小型解壓啟動器使用 Windows 內建的 .NET Framework 4.x。

## 啟動與設定

第一次開啟會顯示準備視窗，將完整應用程式、.NET 10.0.14、後端與授權解壓到：

```text
%LOCALAPPDATA%/ControllerHub/Portable/<payload-hash>/
```

之後會校驗並重用該版本快取。準備完成後啟動器立即退出，日常使用時不會多保留一個啟動器程序。不同內容使用不同快取，避免覆寫使用中的檔案。

既有偏好與遊戲配置仍在 `%LOCALAPPDATA%/ControllerFusion`，與 ZIP 版共用。此處的「便攜」表示應用程式免安裝，並不表示設定會跟著 EXE 在不同電腦間搬移。

## 驅動與體積

USB/IP Client 系統驅動仍需首次安裝；有線與藍牙來源都可能使用虛擬 USB 輸出。單 EXE 包含安裝入口，但不會自行提權或靜默安裝驅動。HidHide 維持可選。

因為包含 .NET 與全部後端，檔案比原先 ZIP 大。ZIP 版繼續提供給已安裝 .NET、偏好管理完整目錄的使用者。兩者的功能與預覽版限制相同。

完整退出程式後，可手動清理對應版本快取；下次啟動會重新準備。若偵測到檔案損壞，啟動器會在沒有該版本主程式執行時重新解壓，舊快取保留為 `.previous-*`，不直接刪除其中檔案。

## 原始碼與重建

請取得與 EXE 相同版本的原始碼。既有 v0.3.0 portable-source 附件對應舊 .NET 8 版；v0.3.4 使用同一 Release 的對應原始碼與 .NET 10.0.14。

在 Windows、PowerShell 7、Git、.NET 10 SDK 環境執行：

```powershell
./scripts/bootstrap.ps1
./scripts/build-portable.ps1
```

腳本會還原 lock 中固定的 .NET runtime，發佈完整自包含 payload，保留其授權，再用 Windows 內建 .NET Framework 編譯器產生單 EXE 及 `.sha256`。`Directory.Build.targets` 僅在便攜打包參數存在時釘選 runtime；普通 ZIP 建置不受影響。

## 驗證範圍

已驗證路徑越界與重複項拒絕、檔案／整包雜湊、快取修復、並行啟動、中文／空格／引號參數、首次／再次啟動。單 EXE 內實際執行 75 項離線檢查與三語 WPF smoke，並確認 coreclr／hostfxr 從解壓目錄載入。這些不代表新增的實體手柄、遊戲或驅動驗收。
