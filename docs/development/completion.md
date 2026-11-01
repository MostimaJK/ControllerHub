# v0.3.4 集中收尾與驗收

本輪保留既有功能，集中完成性能、生命週期與發佈候選包。這是 Preview 候選版，發佈成品見 GitHub Releases；不宣稱所有遊戲及硬體環境已通過。

## 已完成

| 項目 | 實作與驗證 |
| --- | --- |
| .NET 10 | 固定 SDK 10.0.403、便攜 runtime 10.0.14；不修改系統 PATH |
| 輸入性能 | 固定容量環形緩衝、重用映射與編碼、事件等待；保留鬆鍵順序及斷流中立保護 |
| 背景介面 | 最小化暫停指標刷新，配置輪詢繼續；還原時重畫 |
| 虛擬輸出清理 | 精確比對本次 loopback / 埠 / bus / device，移除後再次查核；失敗保留復原記錄 |
| 實體輸入關閉 | 關閉失敗保留所有權並向呼叫端回報；阻止清理失敗後直接重連 |
| Switch USB | 本輪同一引擎八輪連接／斷開全部通過，覆蓋 Xbox、PS5、Pro1、Steam / Pro2 |
| 回歸檢查 | 注入移除失敗、移除假成功、暫時失敗、錯誤端點、關閉失敗及重連保護 |

實機記錄位於本機忽略的 `artifacts/stabilization-switch-usb.txt`；性能證據及測量限制見 [performance.md](performance.md)。單次八輪測試不等於長時間穩定性保證。

## 2026-11-01 發佈整理

使用者回報一段時間使用暫無問題；移除日常介面的開發浮水印與測試版標示，保留功能診斷。PS5 本輪因手柄不在身邊，未重新實測；先前 USB 實測證據仍見 validation.md。

## 還需要實際環境驗收

- DualSense 藍牙：目前手柄 PS 鍵不能正常無線喚醒，須換可用手柄驗證。
- 《異環》等遊戲長時間駕駛、不同遊戲對虛擬 PS5／體感的支援。
- 音訊轉震動長時間負載：維持實驗功能及預設關閉，本輪不自動啟用。
- HidHide 防重複輸入：需安裝該驅動並實機驗收，本輪不改動。
- 睡眠喚醒、不同藍牙適配器、乾淨 Windows、長時間漂移及資源使用。
- USB 持續寫入停滯、首次讀取拒絕及初始化逾時仍保留在 [已知問題](../known-issues.md)；不能因本輪通過便視為根因已修復。

## 重跑檢查

- `./build.ps1 -Check -Package`：來源與雜湊驗證、編譯、離線檢查及 framework-dependent ZIP。
- `./scripts/build-portable.ps1`：產生內含 .NET runtime 的單 EXE。
- `ControllerHub.exe --test-release-lifecycle <absolute-report>`：Switch USB 八輪模式切換；加 `--dualsense` 改測 PS5 USB，加 `--bluetooth` 改藍牙。需先關閉其他映射程式。限時 120 秒、不啟用音訊、不修改偏好、不開啟 HidHide。
- `ControllerHub.exe --smoke --capture <absolute-directory>`：三語言頁面與介面互動檢查，不連實體手柄。

驗收記錄只能證明該次設備、連線及功能路徑；未完成項目繼續明確標示，不以功能按鈕存在代替測試。
