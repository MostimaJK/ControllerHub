# 參與開發

先閱讀 [開發指南](docs/development/build.md)、[架構](docs/development/architecture.md)與[已知問題](docs/known-issues.md)。

- 請把來源手柄、USB／藍牙、虛擬輸出及回饋類型分開描述，不以虛擬裝置列舉代替遊戲驗證。
- 修改內部 DS4Windows 時，更新 lock 對應的 integration patch，保留適用授權。
- 修復生命週期／原生 I/O 問題時，保留裝置所有權、絕對期限、終止中立與完成確認；加入能重現問題的離線回歸。
- UI／文件修改檢查 smoke 截圖與本機連結。模擬畫面與實機結果分別標示。
- 回報問題時使用診斷預覽，上傳前檢查個人資料；不要提交本機設定、原始裝置日誌、上游 checkout 或二進位發布包。

提交說明請描述具體問題、修改後行為、實際執行的驗證，以及尚未驗證的路徑。
