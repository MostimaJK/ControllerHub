# v0.3.3：.NET 10 與性能優化

更新：2026-10-31。保留 C#／WPF，主程式、DS4Windows 整合依賴及其測試目標升級到 .NET 10 Windows；runtime 10.0.14、SDK 10.0.403 均有明確鎖定。小型單 EXE 解壓啟動器繼續使用 Windows 內建 .NET Framework，不與主程式 runtime 混淆。

## .NET 10 改進與本專案關係

- JIT：方法內聯、去虛擬化、結構體與堆疊配置分析，可降低部分程式碼的執行成本。
- GC／runtime：有記憶體回收及配置效率改善，但本程式使用桌面端工作站 GC，不能照搬伺服器 GC 的個別測試百分比。
- 基礎函式庫：集合、LINQ、文字、JSON、非同步等路徑有多項改進，是否受益取決於實際呼叫。
- WPF：Fluent 樣式、剪貼簿及控制項品質更新；本程式自訂的白／藍介面保持原設計，升級本身不保證降低手柄延遲。

來源：[官方 runtime 更新](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/runtime)、[官方性能分析](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)、[WPF 更新](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)。

## 程式碼改動

1. 預配置 32 槽環形緩衝與封包記憶體。生產端只寫自己的槽位；消費端將資料拷到獨立傳送緩衝，等待非同步寫入完成後才重用該緩衝。
2. 映射使用獨立可重用 DS4State，編碼直接寫入呼叫端 Span。上游發出的輸入快照仍是獨立物件，UI／診斷不會讀到被下一份輸入覆寫的狀態。
3. 喚醒合併使用鎖內計數檢查，避免以 SemaphoreFullException 當正常流程。沒有新輸入時無限期等待事件，由一個可重用計時器在接近 150 ms 斷流期限時檢查／喚醒。
4. 傳送工作重用取消來源和計時器；取消來源一旦真正取消就結束工作階段，不會重置後繼續傳送，保留原本 100 ms 寫入預算。
5. 最小化時暫停 4 Hz 畫面數值刷新、延後隱藏視窗的重建；還原後刷新。遊戲配置偵測使用獨立 2 秒輪詢，最小化不會使此功能停止。

原有新鮮按鍵邊緣順序、40 ms 過期處理、150 ms 斷流保護、明確容量上限及取消／釋放規則保留。

## 本機比較：分清 runtime 與程式碼收益

同一個 .NET 10 SDK 編譯三組自包含程式：舊邏輯＋8.0.33、舊邏輯＋10.0.14、新邏輯＋10.0.14。案例使用 Xbox 封包、體感關閉、沒有按住組合背鍵的合成狀態；體感及組合改鍵的其他配置不涵蓋在 0 B 結果內。各組跑 3 個程序、每項先預熱 10000 次，再取 5 × 20000 次樣本；組別輪替順序，共 15 個樣本／項目。

| 指標 | .NET 8 舊邏輯 | .NET 10 舊邏輯 | .NET 10 優化後 |
|---|---:|---:|---:|
| 映射＋Xbox 編碼配置量／次 | 824 B | 824 B | 0 B |
| 緩衝往返配置量／次 | 112 B | 112 B | 0 B |
| 無輸入 1 秒的 writer 等待完成次數，中位數 | 65 | 65 | 0 |
| 上游輸入快照配置量／次，這次未改 | 776 B | 776 B | 776 B |

這些結果只代表所測的暖機後子路徑，絕不是整個程式零配置，也不是遊戲 FPS 或藍牙延遲提升百分比。SourceSnapshot 留下的配置說明仍有進一步精簡輸入核心的空間。

短迴圈的 ns/op 會受分層 JIT／PGO 與當時系統負載影響，初測與重跑的 runtime 方向並不完全相同，因此本次不宣稱「單升級 .NET 10 固定快多少」。原始 ns/op 與 SHA-256 留在本機報告，沒有挑掉比較慢的樣本。Idle CPU 緊接高負載測試後會受 JIT／GC 影響，不作待機 CPU 節省比例的依據。

本機報告：`artifacts/performance/comparison/summary.json`；原始逐輪 JSON 同目錄；舊邏輯的熱點程式快照保留在被 Git 忽略的 `artifacts/performance/baseline-source/`；歷史成品已移出專案封存，專案內保留逐輪 JSON 與雜湊。這些本機材料不自動上傳。

目前源碼可重跑候選版測試：

```powershell
./build.ps1 -Check
$dotnet = & ./scripts/resolve-dotnet.ps1
& $dotnet ./dist/ControllerHub.dll --performance-check ./performance-result.json
```

如需比較不同版本，應先封存各版本的完整來源、目標框架、runtime 與建置參數，再對各組執行同一份 PerformanceChecks。相同 apphost EXE 不代表相同邏輯，因此報告同時記錄主 DLL、DS4Windows DLL 及 runtimeconfig 的雜湊。

## 相容性證據與限制

- .NET 10 自身整合檢查、四種 VIIPER TCP 模式與鬆鍵／過期／取消檢查通過。
- .NET 10 上 193 項輸入、BLE、USB 回饋與退出等原生／生命週期測試通過。
- Switch 2 Pro 藍牙＋Xbox 輸出持續收到實體報告；使用者確認 Xbox 回饋鏈路有震動，正常退出 CleanupSucceeded=true、ExitCode=0。
- 此輪沒有產生新的扳機讀回量測檔，不宣稱 .NET 10 的藍牙延遲優於 .NET 8。
- 三語 WPF、Google Sans、最小化停刷／配置輪詢保留／還原刷新驗證通過。
- DualSense 實機、長時間音訊、睡眠喚醒、各遊戲長時間性能，沒有因這次升級而自動算驗收通過；沿用既有待驗收清單。
