# GitHub 發布準備

建議倉庫名稱：`ControllerHub`。簡介可用：

> Windows controller workspace for Switch 2 Pro and DualSense, with USB/Bluetooth input, virtual gamepads, motion and haptics.

建議 topics：`controller`、`switch-2`、`dualsense`、`windows`、`wpf`、`xinput`、`haptics`。

## 原始碼

提交 `src`、`docs`、`scripts`、`patches`、`licenses`、README、COPYING、CHANGELOG、THIRD_PARTY_NOTICES、DEPENDENCIES.lock.json 及 Git 設定檔。`.gitignore` 排除上游 checkout、編譯快取、發布包和本機診斷。歷史成品已移出主專案，不需要上傳。

先檢查 `git status --short`，再建立提交。公開前檢查暫存差異與截圖，確認沒有裝置識別、個人設定或機密。使用本機 GitHub CLI 核對發布帳號為 MostimaJK；本專案的倉庫為 `MostimaJK/ControllerHub`。

## 預覽包

執行 `./build.ps1 -Check -Package`，取得 `releases/latest/ControllerHub-v0.3.0-preview-win-x64.zip` 與 `.sha256`。將這兩個檔案作為 GitHub Release 附件；不要塞進普通 Git 原始碼歷史。

版本標記可用 `v0.3.0-preview`，並勾選 pre-release。發布文字應引用 CHANGELOG 和已知問題，清楚標示 DualSense 藍牙、長時間音訊／性能、HidHide 等尚未驗收。

同步保留該版原始碼快照、依賴的固定提交、整合補丁與第三方聲明；發布二進位時也需提供可取得的對應原始碼。不要移除後端或圖例的授權檔案。現有本機編譯／自測成功不等同乾淨系統安裝與所有遊戲已通過。
