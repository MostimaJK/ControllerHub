# 開發與打包

## 工具與依賴

在 Windows x64 安裝 Git、.NET 8 SDK。所有命令從專案根目錄執行，PowerShell 7 或 Windows PowerShell 均可。

```powershell
./scripts/bootstrap.ps1
./build.ps1 -Check -Package
```

上游原始碼不是 Git 子模組，也不提交到本倉庫。bootstrap 在 `upstream/` 取得鎖定提交，驗證版本，套用 `patches/ds4windows-fusion.patch`，並校驗 VIIPER／USB/IP／HidHide 二進位雜湊。已套用相同補丁時可重複執行；版本不符或補丁衝突會停止，不覆寫既有修改。第一次執行需要連線 GitHub 與 NuGet。

只驗證現有依賴：

```powershell
./scripts/bootstrap.ps1 -VerifyOnly
```

產物：`dist/ControllerHub.exe`、完整相依檔案、`releases/latest/ControllerHub-v0.3.0-preview-win-x64.zip` 及 `.sha256`。EXE 依賴 .NET Desktop Runtime，並非單檔自包含應用程式。

## 目錄

```text
src/ControllerFusion.App/    WPF 介面、輸入引擎、虛擬輸出與離線檢查
src/ControllerFusion.Input/  編譯進上游組件的內部 API 橋接層
patches/                    上游修改與回歸測試
scripts/                    依賴還原、有限診斷監控
licenses/                   上游與 NuGet 授權資訊
docs/                       使用說明、相容性、截圖及開發文件
upstream/                   可還原的本機依賴（忽略）
dist/                       最新完整程式（忽略）
releases/latest/            最新 ZIP／SHA-256（忽略）
artifacts/latest-validation/ 最新本機診斷證據（忽略）
```

專案已從 ControllerFusion 改名為 ControllerHub；C# namespace、原始碼資料夾及既有使用者設定路徑暫時保留舊識別，避免把品牌調整混入引擎及設定遷移。

## 檢查

```powershell
./dist/ControllerHub.exe --self-test ./artifacts/offline-checks.txt
./dist/ControllerHub.exe --smoke --capture ./artifacts/ui-captures
```

`--self-test` 使用離線資料與本機 TCP 後端，不安裝驅動、不匯入虛擬 USB，也不操作實體手柄。`--smoke` 使用獨立暫存設定和模擬連接，渲染原生 WPF 截圖；它不是硬體測試。

上游整合回歸測試可用：

```powershell
dotnet test ./upstream/DS4Windows/DS4WindowsTests/DS4WindowsTests.csproj -c Release -p:Platform=x64
```

硬體與輸出診斷可能建立虛擬裝置或發送回饋，不應在一般 CI 中自動啟動。有限測試的監控程式會核對 PID、EXE 完整路徑與啟動時間，僅終止它所擁有的測試程序。

## 修改上游與發布

修改 DS4Windows 後，連同新增測試一併更新 patch；新增檔案先以 `git add -N` 納入差異。補丁必須能套用到 lock 中的原始提交。

```powershell
git -C ./upstream/DS4Windows diff --binary --output=../../patches/ds4windows-fusion.patch
```

準備 GitHub 發布時，提交原始碼、文件、授權、lock 和 patch；不要提交 `upstream/`、`dist/`、個人診斷或發布 ZIP。ZIP／SHA-256 放到 Releases，並同步提供該版對應原始碼與依賴／補丁，保留所有適用的第三方授權。本文件不代表已上傳或建立 GitHub Release。
