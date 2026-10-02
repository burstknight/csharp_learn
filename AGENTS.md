# AGENTS.md

Personal C# study repo: 繁體中文 notes + 可執行練習，目標是讓自己在一兩週內恢復 C#/ .NET 的手感，能開始新的工作。

## Layout
- 根目錄：`README.md`（課程入口與索引）、`00-tooling.md`（SDK／專案／套件管理）、`study-plan.md`（4 天與 3 天壓縮版時程）。
- `CSharpLearn.sln` 底下有 `day1`／`day2`／`day3`／`day4`／`day5` 五個 console project，每個資料夾內放該日的教材（`01-*.md`）與對應範例（`*Demo.cs`）。
- `day5/` 是**選讀 bonus**：把 Rust 的所有權／借用／錯誤處理／struct 觀念落到 C# 設計上，不在 4 天主線裡。
- 共用設定集中在 `Directory.Build.props`，格式設定在 `.editorconfig`。**不要**在個別 csproj 重複宣告 `TargetFramework`／`Nullable`／`ImplicitUsings`。
- `README.md` 保持繁體中文，新增教材時要同步更新索引。

## Project conventions
- 目標框架 `net10.0`（LTS），語言版本跟隨 TFM 預設（C# 14），不要在 csproj 內另外釘 `LangVersion`。
- `Nullable=enable`。**編譯警告要分類處理**：
  - **刻意保留**：教材的反例（CS8600/CS8602/CS8604/CS8618 已在註解標明），留着讓讀者練習。修掉它們等於刪掉教材。
  - **必須修掉**：真正的 bug 或寫法問題（例如 CS0414「已指派但從未使用」、CS9264、CS0704、CS1922、CS7036）。
- 每個範例檔是一個 `public static class XxxDemo`，提供 `public static Task RunAsync(CancellationToken ct)`。**不要在 `*Demo.cs` 裡寫 top-level statements**——會和 `Program.cs` 的進入點衝突。
- 進入點是各專案的 `Program.cs`，用引數挑選要跑的 demo：`dotnet run --project day1 -- oop`。不要改成互動式選單。
- 命名空間用 file-scoped：`namespace CSharpLearn.DayN;`，對應 csproj 的 `RootNamespace`。
- `ImplicitUsings=enable` 已開啟，`System`、`System.Linq`、`System.Threading`、`System.Threading.Tasks` 等不用重複 using；其他命名空間（`System.Net`、`System.Text`、`System.Text.Json`、`MQTTnet`…）仍需明確 using。
- 套件版本一律釘選確切版本，不使用浮動版本（`5.*`）。repo **不**採用 Central Package Management，版本直接寫在 csproj 的 `PackageReference`；CPM 只在 `00-tooling.md` 中講解。
- 程式碼註解一律繁體中文。

## Scope
- 主題：C# 特有語法、物件導向、非同步、執行緒／行程、Socket framing、Modbus/TCP、MQTT、HttpClient、.NET 版本差異。
- 選讀 bonus（`day5/`）：Rust 的所有權／借用／`Result`／struct 觀念對照到 C# 設計。**不要**順手把 Rust 的寫法硬翻譯成 C#（例如到處 `Result`、到處 `Dispose`），教材要點出「Rust 保證了什麼、C# 用哪條紀律補」。
- WPF 只提供使用與設計建議（`day1/03-wpf-tips.md`），**不建立 WPF 專案**。
- 目前沒有測試專案，也沒有 CI。教材的驗證手段是 `study-plan.md` 的自我檢核清單加上使用者自己跑 `dotnet build`／`dotnet run`。
- 範例應盡量零外部相依，Modbus 範例尤其不要引入套件。

## Language
- 文件、說明、程式碼註解：**繁體中文**。
- Commit messages：**English**，Conventional Commits（`docs(day3): Add socket, Modbus, MQTT and HttpClient notes`）。

## Toolchain
- **本機已安裝 .NET SDK 10.0.401**：`~/.dotnet/dotnet`（`~/.dotnet` 就是 SDK 目錄），已掛在 `PATH` 上。`dotnet` 找不到時用絕對路徑 `~/.dotnet/dotnet`。
  Runtime：`Microsoft.NETCore.App 10.0.12`。`mono`／`msbuild`／`csc`／`nuget` 沒有獨立安裝（都由 SDK 提供）。
- **可以而且應該**在本機跑 `dotnet restore`／`dotnet build`／`dotnet run` 來驗證範例。
- 網路可用（`nuget.org` 可連線）。套件已還原到 `~/.nuget/packages`，`day3` 用的 MQTTnet 是 `5.2.0.1603`。
- **交付前的強制驗證流程**（改動任何 `.cs`／`.csproj` 後都要跑，缺一不可）：
  1. `dotnet build CSharpLearn.sln` —— 必須 0 errors。
  2. 每支 demo 都要實際執行並確認輸出正確、程式會正常結束（不是靠讀程式碼判斷）。清單共 15 支：
     `dotnet run --project dayN -- <key>`，其中 day1 為 `syntax`／`lambda`／`oop`、day2 為 `async`／`collector`、day3 為 `echo`／`modbus`／`mqtt`／`webapi`、day4 為 `threading`／`versions`、day5 為 `ownership`／`borrowing`／`errors`／`struct`。
  3. 回報結果時附上實際執行的指令與輸出摘要。**絕對不要聲稱沒跑過的東西可用**；沒驗證就明說沒驗證。
- **所有 `.cs`／`.md` 必須是 UTF-8（無 BOM）。** 曾經發生過 `day1/LambdaDemo.cs` 整份被 double-encoded（UTF-8 位元組被當 latin-1 讀取後再編碼，產生 3000+ 個 C1 控制字元），一個人為失誤就讓整個專案多出 300 多個編譯錯誤。懷疑時用這個檢查（`c1ctrl` 必須是 0）：
  ```bash
  python3 -c "import sys;t=open(sys.argv[1],encoding='utf-8').read();print(sum(1 for c in t if 0x80<=ord(c)<=0x9f))" <檔名>
  ```
- 套件版本、.NET 支援期限等易過期的資訊，若未經當下確認，請在教材中註明「以官方文件為準」。
- `.gitignore` 是 Visual Studio 模板，`bin/`、`obj/`、`.vs/` 已被忽略。

## Git
- 預設分支 `master`（不是 `main`）；遠端是 LAN 內自架 Gitea（`ssh://gitea@192.168.0.11:2222`）。
- 沒有 CI，push／PR 不會自動跑任何東西，review 在本機進行。
- 沒有獲得使用者明確同意前不要 commit。
