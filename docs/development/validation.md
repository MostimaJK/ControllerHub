# v0.3.0 Preview 驗收狀態

更新：2026-10-31。這是開發預覽版的驗收摘要，不是全部功能已完成的正式發布聲明。

## 已有證據

- Switch 2 Pro：USB／藍牙基本輸入、GL／GR／C、Xbox 普通回饋，使用者已確認。
- Switch 2 Pro：USB 原生 HD、USB PS5 觸覺轉 HD、藍牙兩段 HD 效果及中間停振／恢復，使用者已確認。
- DualSense USB：基本輸入、Xbox 回饋、原生觸覺，以及直接／虛擬 PS5 轉發的左右扳機阻力與解除，使用者已確認。
- Switch 2 USB／藍牙與 DualSense USB：實體六軸與 Windows 虛擬 PS5 讀回資料比對通過。遊戲內軸向與長時間漂移仍待測。
- 四種虛擬模式通過 Windows 消費端檢查；Pro2 完成初始化後讀到不同計數的連續報告。
- 系統音訊 → Switch 2 USB：受限測試感到震動、鍵鼠正常。長時間驗收尚未完成，功能預設關閉。
- USB 退出的 publication 收尾與 callback 忙碌兩個競態已先在離線測試重現，再修正；原始期限、所有權與回呼內防重入契約保留。
- 新版重新插線且未按 Home／SYNC：接收 769 份輸入、運作時約 250 Hz、Xbox 輸出就緒、CleanupSucceeded=true、ExitCode=0。退出後自己的後端與 USBIP 匯入均無殘留。

上述頻率與回報間隔是接收資料的統計，不是實測端到端遊戲輸入延遲。

## 自動檢查基線

- 75 項自身離線整合檢查通過。
- 120 項 USB owner／註冊／退出交易／production coordinator 相關測試通過。
- 34 項格式／原生完成與取消／HD 靜音恢復等回饋檢查通過。
- 四模式 Windows 消費端、六軸與 Pro2 串流檢查通過。

最近的有限本機診斷證據保留於被 Git 忽略的 `artifacts/latest-validation/`。個人裝置日誌不作為公開倉庫內容；提交的上游測試位於 integration patch，可還原後重跑。介面離線示範截圖不作為硬體通過證據。

## 未完成與限制

重新插線後的第一次原生讀取仍可能 `ReadCompletionRejected`，約一秒後由自動重試恢復。最新完整流程通過不代表此根因已修好。

DualSense 藍牙、HidHide、實際遊戲、反覆切換／睡眠喚醒、長時間音訊與性能、多 DPI 等仍待驗收。完整範圍見[功能表](../features.md)、[已知問題](../known-issues.md)與[後續驗收清單](acceptance.md)。

## ControllerHub 改名與整理回歸

最新 ControllerHub.exe 完整重建、75 項離線檢查與 WPF 介面 smoke 均通過。這次整理時 Windows 未列出已連接的 Switch 2 Pro／DualSense，實體輸入診斷收到 0 份資料並逾時（清理成功），不將其記為硬體通過，也不把未找到裝置判為手柄故障。

README 的最新畫面均為已標示的離線 UI 示範；歷史實機驗收記錄保留在本機診斷目錄，不作為公開截圖。

## 三語介面回歸

繁體中文、简体中文、English 選單均以實際 WPF 控制項切換，設定經保存／載入比對；GL／GR／C、傳輸方式與輸出模式不變。18 個語言／頁面組合完成渲染檢查，英文的標準可見 TextBlock 未殘留未翻譯中文（原生語言選項名稱除外）。已確認英文 Typeface 實際解析到隨附 Google Sans，而非僅宣告字體名稱。另有英文 DualSense 與 960×720 首頁／設定頁截圖。這些屬於離線介面檢查，不是新的實體手柄驗收。

## 發布前原始碼重建

從固定提交的乾淨上游 checkout 套用整合補丁，完整建置與 75 項離線檢查通過。驗證過程使用本機 Git 物件快取，不代表首次網路下載也已完成端到端測試。PRO2 的長路徑 checkout 問題已在 bootstrap 以該依賴倉庫的 core.longpaths 設定處理，不修改全域 Git 設定。公開提交排除本機整理紀錄、舊品牌截圖、上游 checkout、編譯快取及個人診斷。
