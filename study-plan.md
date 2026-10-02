# 學習計畫

目標：**3–4 天，每天 1–2 小時**，把 C# / .NET 的手感找回來，能開始新工作。

## 使用方式

- 每天照著下面的表走，**先讀教材再跑範例**。
- 跑完範例後，對照該天的〈自我檢核〉逐條打勾。**沒把握的條目就是你要重讀的段落。**
- 本 repo 的 15 支範例都已經用 .NET 10 SDK 編譯並實跑驗證過（`dotnet build CSharpLearn.sln` 0 errors，15 支 demo 全部正常結束）。不過換機器、換 SDK 版本或改動範例之後，「能不能跑」仍然是檢核的一部分 —— 讀懂錯誤訊息就是學習。

## 為什麼是這些內容

取捨原則：**C++／Python 共通的東西不複習**，只學 C# 特有或工作上真的會用到的：

| 優先 | 主題 | 理由 |
| --- | --- | --- |
| 必讀 | 值型別／參考型別、`Nullable` | 跟 C++ 的物件模型不同，寫錯會有額外成本 |
| 必讀 | async/await + `CancellationToken` | PLC 採集、Web API 全靠它；`.Result` 是最常見的錯 |
| 必讀 | Socket framing | 底層通訊必備，也是最容易寫錯的地方 |
| 必讀 | 專案與套件管理 | 每天都在用，但很少人真正搞懂 |
| 選讀 | OOP 細節 | 你已有 C++ 基礎，只補 C# 特有語境 |
| 選讀 | Threads / Process | 需要時再深入，先建立模型 |
| 略過 | WPF 撰寫 | 工作上才會用到，先看設計建議 |
| 略過 | LINQ 進階寫法 | 用到再查 |

---

## 4 天版

### Day 0 — 工具（30–45 分鐘）

| 時間 | 內容 |
| --- | --- |
| 10 分 | 確認 SDK：`dotnet --version`、`dotnet --list-sdks` |
| 20 分 | 讀 [`00-tooling.md`](00-tooling.md) 的〈Solution 與專案檔〉、〈`TargetFramework` 與 `LangVersion`〉 |
| 20 分 | 跑 `dotnet build`，並**主動找出所有警告**、用 `dotnet build -p:WarningLevel=9999` 看隱藏的 |
| 10 分 | 讀〈可空參考型別〉，確認自己看得懂警告代碼 |

**自我檢核**

- [ ] `dotnet build` 成功；如果有警告，我知道它在說什麼
- [ ] 我能說出 `TargetFramework` 和 `LangVersion` 的差別
- [ ] 我知道 `Directory.Build.props` 存在，以及它影響哪些專案
- [ ] 我理解為什麼 `NuGetAudit` 出現警告時**不**該直接 `Suppress`
- [ ] 我知道 `dotnet run --project day1 -- syntax` 中間那個 `--` 的用途

---

### Day 1 — 語法、Lambda 與物件導向（約 2.5 小時）

| 時間 | 內容 |
| --- | --- |
| 30 分 | 讀 [`day1/01-csharp-syntax.md`](day1/01-csharp-syntax.md)，跑 `dotnet run --project day1 -- syntax` |
| 25 分 | **對付所有警告**：把 `SyntaxDemo.cs` 每個警告都修掉，並用註解寫下你改了什麼 |
| 50 分 | 讀 [`day1/04-lambdas-and-closures.md`](day1/04-lambdas-and-closures.md)，跑 `dotnet run --project day1 -- lambda` |
| 30 分 | 讀 [`day1/02-oop.md`](day1/02-oop.md)，跑 `dotnet run --project day1 -- oop` |
| 10 分 | 讀 [`day1/03-wpf-tips.md`](day1/03-wpf-tips.md) |

> Lambda 這節刻意排在 Day 1，因為它是 Day 2（`async` lambda、LINQ）和 WPF（`Command`、事件）的共同前置。
> 如果你對 C++/Python 的 lambda 已經熟，可以把時間壓到 30 分，但**第 3 節（閉包）和第 5 節（async lambda）一定要讀完**。

**自我檢核**

- [ ] 我能解釋 `struct` 傳給 method 為什麼拿到的是副本，`class` 為什麼不是
- [ ] `string` 與 `string?` 的差別我講得出來
- [ ] `CS8600`／`CS8602`／`CS8618` 各自在什麼情況出現
- [ ] 我知道 `record` 適合什麼情況、和 `class` 的差別
- [ ] 我能說出 lambda 為什麼必須指定 `Func<>` 或 `Action<>`（或自訂 delegate）
- [ ] **我知道為什麼 `for` 迴圈捕獲變數會印出 `3 3 3`，而 `foreach` 不會**
- [ ] **我知道 `async` lambda 為什麼要回傳 `Task` 而不是 `T`**
- [ ] 我知道 `async void` / `async Action` 呼叫後不會等
- [ ] 我能說出什麼情況該用 `interface`、什麼情況該用 `abstract class`
- [ ] 我知道 `IDisposable` 為什麼需要 `using`

---

### Day 2 — 非同步與資料流（約 2 小時）

| 時間 | 內容 |
| --- | --- |
| 40 分 | 讀 [`day2/01-async.md`](day2/01-async.md)，跑 `dotnet run --project day2 -- async` |
| 20 分 | 讀 `day2/02-channels-and-collector.md` 的 Channel 章節 |
| 30 分 | 跑 `dotnet run --project day2 -- collector`，改容量參數看背壓行為 |
| 30 分 | **動手改寫**：把你的 PLC 採集 loop 用 `Channel` 寫一版草稿 |

**自我檢核**

- [ ] 我知道為什麼 `await` 的函式不會佔住執行緒（與 `Task.Run` 的差別）
- [ ] 我**親眼見過** `.Result` 卡住的樣子，並能解釋原因
- [ ] `Task.WhenAll` 和「用 `foreach` 加 `await`」差在哪裡
- [ ] `Task` 和 `ValueTask` 什麼時候該用哪個
- [ ] 我知道 `CancellationToken` 為什麼要從外面傳進來，而不是在函式裡 `new`
- [ ] `OperationCanceledException` 為什麼通常要另外 catch（而不要跟一般例外混在一起）
- [ ] 我能說出 Channel 的 bounded 容量在解決什麼問題

---

### Day 3 — 網路與協定（約 2 小時）

| 時間 | 內容 |
| --- | --- |
| 30 分 | 讀 [`day3/01-socket-and-framing.md`](day3/01-socket-and-framing.md)，跑 `dotnet run --project day3 -- echo` |
| 25 分 | 讀 [`day3/02-modbus-tcp.md`](day3/02-modbus-tcp.md)，跑 `dotnet run --project day3 -- modbus` |
| 25 分 | 讀 [`day3/03-mqtt.md`](day3/03-mqtt.md)，跑 `dotnet run --project day3 -- mqtt` |
| 20 分 | 讀 [`day3/04-webapi.md`](day3/04-webapi.md)，跑 `dotnet run --project day3 -- webapi` |
| 20 分 | 選讀：Modbus 離線範例的位元組組裝，重寫一個讀輸入暫存器 |

**自我檢核**

- [ ] 我能解釋「粘包」為什麼會發生，以及 framing 要怎麼做
- [ ] 我知道 `Stream.Read` 為什麼可能讀不到完整一筆資料
- [ ] 我能說出 MBAP header 裡每個欄位的意義
- [ ] `0x03` 和 `0x04` 的差別是什麼
- [ ] MQTT 的 QoS 0／1／2 行為差異，以及什麼場景該用哪個
- [ ] 我知道 MQTTnet 5 和網路上 v4 教學的 API 差在哪（`MqttFactory` → `MqttClientFactory`）
- [ ] 我知道為什麼 `HttpClient` 不該在迴圈裡 `new`

> **注意**：`mqtt` 範例會在本機 1883 port 開 broker。如果該 port 已被占用，範例會直接失敗，換個 port 或先關掉佔用程式。

---

### Day 4 — 執行緒、行程與版本（約 1.5 小時）

| 時間 | 內容 |
| --- | --- |
| 40 分 | 讀 [`day4/01-threading.md`](day4/01-threading.md)，跑 `dotnet run --project day4 -- threading` |
| 25 分 | 讀 [`day4/02-dotnet-versions.md`](day4/02-dotnet-versions.md) |
| 20 分 | 跑 `dotnet run --project day4 -- versions` |
| 10 分 | 收尾：把 `README.md` 的指令全部跑一次 |

**自我檢核**

- [ ] 我知道 ThreadPool 是什麼、為什麼不該自己 `new Thread`
- [ ] CPU-bound 和 I/O-bound 的處理方式差在哪裡
- [ ] `lock`、`Interlocked`、`SemaphoreSlim` 各自解決什麼問題
- [ ] **我知道 `SemaphoreSlim.Release()` 為什麼一定要在 `finally` 裡**（漏掉會讓程式永久死鎖，`threading` 範例第 4 節可以親自驗證）
- [ ] 我什麼時候該開一個 `Process`（而不是 Thread）
- [ ] .NET 8／9／10 的生命週期差異，以及 LTS／STS 的差別
- [ ] 我知道 C# 語言版本和 TargetFramework 是獨立的
- [ ] 我能說出 `global.json` 在解決什麼問題

---

## 選讀 bonus：Day 5 — Rust 觀念對照到 C# 設計

**這一天不在 4 天／3 天主線裡，跳過完全不影響完成度。**
只有在你已經會 Rust、想搞清楚「C# 少了哪一層保護、要自己補哪條紀律」時才值得看。
約 1.5 小時，離線可跑，零外部相依。

| 時間 | 內容 |
| --- | --- |
| 25 分 | 讀 [`day5/01-rust-to-csharp.md`](day5/01-rust-to-csharp.md) 的對照表 + 所有權章節，跑 `dotnet run --project day5 -- ownership` |
| 25 分 | 讀借用章節，跑 `dotnet run --project day5 -- borrowing`（含 `Span<T>` 與 boxing 的實際量測） |
| 25 分 | 讀錯誤處理章節，跑 `dotnet run --project day5 -- errors` |
| 20 分 | 讀 struct 章節，跑 `dotnet run --project day5 -- struct` |

**自我檢核**

- [ ] 我能說出 Rust 用 move／borrow 在編譯期保證了哪四件事
- [ ] 我知道 `using` 對應 Rust 的 `drop`，而 `await using` 是 C# 特有的（非同步 drop）
- [ ] 我知道 `readonly struct` **不等於** `&T`（前者每次呼叫都複製，後者才是借用）
- [ ] 我知道 `Span<T>` 為什麼不能跨 `await`、不能當欄位，以及該用什麼取代
- [ ] 我能說出什麼情況該用 `TryXxx`、什麼情況該讓例外丟出去
- [ ] 我知道 `Task.WhenAll` 裡某個 task 失敗時，其他 task 會怎樣
- [ ] 我知道 C# 沒有 sum type，要型別安全該用 `sealed class` 階層

---

## 3 天壓縮版

時間不夠就這樣：

| 天 | 內容 | 省略 |
| --- | --- | --- |
| Day 1 | `00-tooling.md`（只看 Solution/csproj、TargetFramework、NuGet 三節）+ `day1/01-csharp-syntax.md` + `day1/04-lambdas-and-closures.md` 的第 1、3、5 節（語法、閉包、async lambda）+ `day1/02-oop.md` 的介面與泛型 | WPF 建議、OOP 進階、變異、Expression tree |
| Day 2 | `day2/01-async.md` + `day2/02-channels-and-collector.md` 的 Channel 章節 | `ValueTask`、`ConfigureAwait` 細節 |
| Day 3 | `day3/01-socket-and-framing.md` + `day3/02-modbus-tcp.md` + `day3/03-mqtt.md`；`day4/01-threading.md` 只讀 CPU vs I/O bound 與 `lock`/`Interlocked` | HttpClient、執行緒池細節、版本差異 |

**取捨理由**：語法 → 非同步 → framing/Modbus/MQTT 這條線是 PLC 工作的核心路徑。HttpClient 與版本差異用到再查，`00-tooling.md` 是查得到的文件，不需要記。

⚠ **Lambda 不可壓縮。** 閉包和 async lambda 這兩節是整份教材最容易寫錯的地方，而且錯誤在編譯器幫你抓的情況下會直接變成 runtime bug。壓縮版只省掉變異、Expression tree、效能那幾節。

---

## 怎麼算完成

3–4 天後，你應該能做到這些（不需要參考資料）：

- [ ] 看到警告就懂，`Nullable` 開著不會怕
- [ ] 寫出正確的 `async` 方法，並知道絕不用 `.Result`
- [ ] 知道 lambda 捕獲的是變數而不是值，能避開 `for` 迴圈的 `3 3 3` 問題
- [ ] 自己設計一個 framing protocol 處理 TCP 粘包
- [ ] 讀得懂 Modbus/TCP 的十六進位封包
- [ ] 用 `Channel` 寫出一個不會把記憶體吃爆的採集 loop
- [ ] 知道 `global.json`、`Directory.Build.props`、csproj 各自負責什麼
- [ ] 進到新 repo 先看哪 10 個檔案（見 `00-tooling.md` 最後一節）

沒達到的條目直接對應到對應天的教材重讀即可。

---

## 開始新工作後的延伸

這份教材結束後，建議依序補：

1. **LINQ** — `Select`／`Where`／`Aggregate` 與 deferred execution 的陷阱
2. **Entity Framework Core** — 若工作碰資料庫
3. **ASP.NET Core Minimal API + `BackgroundService`** — 把 Day 2 的 Channel 概念接上真實框架
4. **DI（`Microsoft.Extensions.DependencyInjection`）** — console app 也可以用，不必等 ASP.NET
5. **xUnit 測試** — 本 repo 目前沒有測試專案，開始改別人的專案時要馬上補