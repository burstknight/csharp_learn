# AGENTS.md

Personal C# study repo: 繁體中文 notes + 可執行練習，目標是讓自己在一兩週內恢復 C#/ .NET 的手感，能開始新的工作。

## Layout
- 根目錄：`README.md`（課程入口與索引）、`00-tooling.md`（SDK／專案／套件管理）、`study-plan.md`（4 天與 3 天壓縮版時程）。
- `CSharpLearn.sln` 底下有 `day1`／`day2`／`day3`／`day4` 四個 console project，每個資料夾內放該日的教材（`01-*.md`）與對應範例（`*Demo.cs`）。
- 共用設定集中在 `Directory.Build.props`，格式設定在 `.editorconfig`。**不要**在個別 csproj 重複宣告 `TargetFramework`／`Nullable`／`ImplicitUsings`。
- `README.md` 保持繁體中文，新增教材時要同步更新索引。

## Project conventions
- 目標框架 `net10.0`（LTS），語言版本跟隨 TFM 預設（C# 14），不要在 csproj 內另外釘 `LangVersion`。
- `Nullable=enable`。**編譯警告是教材的一部分**，不要為了讓輸出乾淨而關閉它或加 `NoWarn`。在教材中用註解標明會出現的警告代碼（CS8600/CS8602/CS8604/CS8618）並示範修法。
- 每個範例檔是一個 `public static class XxxDemo`，提供 `public static Task RunAsync(CancellationToken ct)`。**不要在 `*Demo.cs` 裡寫 top-level statements**——會和 `Program.cs` 的進入點衝突。
- 進入點是各專案的 `Program.cs`，用引數挑選要跑的 demo：`dotnet run --project day1 -- oop`。不要改成互動式選單。
- 命名空間用 file-scoped：`namespace CSharpLearn.DayN;`，對應 csproj 的 `RootNamespace`。
- `ImplicitUsings=enable` 已開啟，`System`、`System.Linq`、`System.Threading`、`System.Threading.Tasks` 等不用重複 using；其他命名空間（`System.Net`、`System.Text`、`System.Text.Json`、`MQTTnet`…）仍需明確 using。
- 套件版本一律釘選確切版本，不使用浮動版本（`5.*`）。repo **不**採用 Central Package Management，版本直接寫在 csproj 的 `PackageReference`；CPM 只在 `00-tooling.md` 中講解。
- 程式碼註解一律繁體中文。

## Scope
- 主題：C# 特有語法、物件導向、非同步、執行緒／行程、Socket framing、Modbus/TCP、MQTT、HttpClient、.NET 版本差異。
- WPF 只提供使用與設計建議（`day1/03-wpf-tips.md`），**不建立 WPF 專案**。
- 目前沒有測試專案，也沒有 CI。教材的驗證手段是 `study-plan.md` 的自我檢核清單加上使用者自己跑 `dotnet build`／`dotnet run`。
- 範例應盡量零外部相依，Modbus 範例尤其不要引入套件。

## Language
- 文件、說明、程式碼註解：**繁體中文**。
- Commit messages：**English**，Conventional Commits（`docs(day3): Add socket, Modbus, MQTT and HttpClient notes`）。

## Toolchain
- **本機沒有安裝 .NET SDK**（`dotnet`、`mono`、`msbuild`、`csc`、`nuget` 都不存在，也沒有 `~/.dotnet`、`/usr/share/dotnet`）。使用者明確要求不要在這台機器上安裝或執行 .NET。
- 因此**絕對不要宣稱 build／test 通過**，也不要為了驗證而去安裝 SDK。撰寫時要格外保守，只使用自己確定的 API。
- 套件版本、.NET 支援期限等易過期的資訊，若未經當下確認，請在教材中註明「以官方文件為準」。
- `.gitignore` 是 Visual Studio 模板，`bin/`、`obj/`、`.vs/` 已被忽略。

## Git
- 預設分支 `master`（不是 `main`）；遠端是 LAN 內自架 Gitea（`ssh://gitea@192.168.0.11:2222`）。
- 沒有 CI，push／PR 不會自動跑任何東西，review 在本機進行。
- 沒有獲得使用者明確同意前不要 commit。
