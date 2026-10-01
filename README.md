# csharp_learn

這個 repo 是一份 3–4 天的 C# / .NET 速成教材，目的是在開始新工作前把手感找回來。

內容刻意只挑 **C# 特有、而且工作上真的會用到** 的東西；C++／Python 共通的部分（迴圈、陣列、條件判斷）不複習。

## 目錄結構

```
CSharpLearn.sln          四個專案的 solution（OmniSharp / VS 需要它才能提供 IntelliSense）
Directory.Build.props    共用設定：TargetFramework、Nullable、ImplicitUsings
.editorconfig            格式與程式碼風格
00-tooling.md            SDK、專案、NuGet／套件管理
study-plan.md            4 天與 3 天壓縮版的時程 + 自我檢核清單
day1/                    語法、物件導向、WPF 建議
day2/                    非同步、取消、Channels
day3/                    Socket framing、Modbus/TCP、MQTT、HttpClient
day4/                    執行緒／行程、.NET 版本差異
```

## 環境需求

- **.NET 10 SDK**（LTS）。版本與支援期限請以 <https://dotnet.microsoft.com/download> 與官方支援矩陣為準。
- 任何支援 .NET 的編輯器。本文以 **Neovim + OmniSharp** 為前提撰寫（OmniSharp 需要有 `.sln`／`.csproj` 才會提供程式碼補全）。

```bash
dotnet --version    # 確認 SDK 存在
```

> 本 repo 的檔案是在**沒有安裝 .NET SDK 的機器**上撰寫的，因此範例程式未經過編譯驗證。
> 請用下面的指令在自己的機器上確認。第一次 build 出現黃色警告是預期中的，見 [`00-tooling.md` 的〈可空參考型別〉](00-tooling.md#可空參考型別)。

## 常用指令

```bash
# 還原套件（第一次、或改了 csproj 之後跑）
dotnet restore

# 建置整個 solution
dotnet build

# 執行某一個範例
dotnet run --project day1 -- syntax
dotnet run --project day1 -- lambda
dotnet run --project day1 -- oop
dotnet run --project day2 -- async
dotnet run --project day2 -- collector
dotnet run --project day3 -- echo
dotnet run --project day3 -- modbus
dotnet run --project day3 -- mqtt
dotnet run --project day3 -- webapi
dotnet run --project day4 -- threading
dotnet run --project day4 -- versions

# 不帶參數會列出該天所有可用的 demo
dotnet run --project day1

# Release 建置（等同發佈前檢查）
dotnet build -c Release
```

> `dotnet run --project day1 -- syntax` 中間那個 `--` 是必要的，它告訴 dotnet 後面的參數要傳給「你的程式」而不是 dotnet 自己。

## 教材索引

| 天 | 教材 | 範例 | 主題 |
| --- | --- | --- | --- |
| 0 | [00-tooling.md](00-tooling.md) | — | SDK、sln/csproj、NuGet、CPM、發佈 |
| — | [study-plan.md](study-plan.md) | — | 時程安排與自我檢核 |
| 1 | [day1/01-csharp-syntax.md](day1/01-csharp-syntax.md) | `SyntaxDemo.cs` | 值型別／參考型別、nullable、pattern matching、record |
| 1 | [day1/02-oop.md](day1/02-oop.md) | `OopDemo.cs` | 介面 vs 抽象類別、泛型、`IDisposable` |
| 1 | [day1/03-wpf-tips.md](day1/03-wpf-tips.md) | — | WPF 專案設定與 MVVM 建議（不含可執行範例） |
| 1 | [day1/04-lambdas-and-closures.md](day1/04-lambdas-and-closures.md) | `LambdaDemo.cs` | Lambda 語法、delegate、**閉包**、async lambda、變異、Expression tree |
| 2 | [day2/01-async.md](day2/01-async.md) | `AsyncDemo.cs` | async/await、`ValueTask`、`Task.WhenAll`、`CancellationToken` |
| 2 | [day2/02-channels-and-collector.md](day2/02-channels-and-collector.md) | `CollectorDemo.cs` | `System.Threading.Channels` 生產者/消費者與背壓 |
| 3 | [day3/01-socket-and-framing.md](day3/01-socket-and-framing.md) | `EchoServerDemo.cs` | TCP stream、粘包、長度前綴 framing |
| 3 | [day3/02-modbus-tcp.md](day3/02-modbus-tcp.md) | `ModbusDemo.cs` | MBAP/PDU、讀 holding register（零外部相依） |
| 3 | [day3/03-mqtt.md](day3/03-mqtt.md) | `MqttDemo.cs` | MQTTnet 5、內嵌 broker、QoS |
| 3 | [day3/04-webapi.md](day3/04-webapi.md) | `WebApiDemo.cs` | `HttpClient`、`System.Text.Json`、Minimal API |
| 4 | [day4/01-threading.md](day4/01-threading.md) | `ThreadingDemo.cs` | ThreadPool、`lock`、`Interlocked`、`Parallel`、`Process` |
| 4 | [day4/02-dotnet-versions.md](day4/02-dotnet-versions.md) | `VersionsDemo.cs` | .NET 8/9/10、C# 12/13/14、`global.json` |

## 讀教材前的提醒

1. **編譯警告是教材的一部分。** `Nullable=enable` 開著，範例裡刻意留了會產生警告的反例，並在註解中標明警告代碼。請先讀警告再改。
2. **不要跳過 `.Result` 的示範。** `day2` 有一個刻意讓程式卡住的例子，這是 C# 裡最常見的錯誤，親眼看到比被告知有用。
3. **`day3` 的 MQTT 範例會在本機 1883 port 開一個 broker。** 如果那個 port 已被占用（Docker、mosquitto），範例會直接失敗，這是預期行為。
4. 版本與套件資訊會過期。教材內所有「版本」「支援期限」都請以官方文件為準。