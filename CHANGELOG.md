# Changelog

## 0.3.0 Preview — 2026-10-31

- 發布後補充單 EXE 便攜版：包含 .NET 8.0.33、自動準備並校驗本機快取；新增對應 portable-source 附件，不改寫已發布版本 tag。

- 新增繁體中文／简体中文／English 的即時語言切換與保存，英文內嵌 Google Sans。
- 首頁標題統一為 Controller Hub；語言切換不影響控制器識別與映射。

- 修正 Windows 首次還原上游依賴時的長路徑 checkout 問題。
- 專案對外名稱統一為 ControllerHub；保留既有設定路徑與內部識別。
- Switch 2 Pro／DualSense 輸入，Xbox／PS5／Pro1／Pro2 虛擬輸出。
- 加入連接成功膠囊、電量／充電顯示、GL／GR／C 本機映射。
- 接入 Switch 2 HD、PS5 USB 觸覺與自適應扳機、六軸轉發和靜置校準。
- 加入實驗性音訊轉震動、診斷預覽、配置與恢復選項。
- 修正 UI 執行緒阻塞、輸出過早就緒、四聲道格式、HD 靜音後無法恢復及 USB 退出競態。
- 整理依賴鎖定、整合補丁、重建腳本、文件與發布包；清除過期本機產物。

此為預覽版，尚未完成的藍牙、遊戲與長時間穩定性項目見 [已知問題](docs/known-issues.md)。
