# Day 2 — Channels 與採集架構

> 範例：[`CollectorDemo.cs`](CollectorDemo.cs)
> 跑法：`dotnet run --project day2 -- collector`
> 上一節：[`01-async.md`](01-async.md)

⚠ **未經編譯驗證**（撰寫環境沒有 .NET SDK）。

PLC 採集程式的核心形狀是「**不停生產、不停消費，而且兩邊速度不一樣**」。這節就是解決那個落差。

---

## 1. 問題：生產與消費速度不一樣

```
PLC ──每 100ms 送 20 筆──▶ [ PLC ] ──每 5 秒才處理一次 ──▶ 寫進資料庫
                            │
                     這裡怎麼緩衝？
```

三種做法，兩種是錯的：

| 做法 | 問題 |
| --- | --- |
| 生產者直接呼叫消費者 | 消費者慢，生產者就被拖慢，最後 PLC 輪詢逾時 |
| `ConcurrentQueue<T>` 緩衝 | 會**無限成長**。PLC 斷線重連時資料暴增，記憶體被吃光 |
| **bounded Channel** | 容量有上限，滿了有明確的處理策略 ✅ |

`System.Threading.Channels` 是 .NET Core 3.0+ 內建的（`Microsoft.NETCore.App`，不用加套件），就是為了取代上面的第二種寫法。

---

## 2. 基本用法

### 建立

```csharp
using System.Threading.Channels;

// 無上限：等同 ConcurrentQueue，永遠不會阻塞
var unbounded = Channel.CreateUnbounded<TagSample>();

// 有上限：容量滿了怎麼辦由 FullMode 決定
var bounded = Channel.CreateBounded<TagSample>(new BoundedChannelOptions(200)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true,     // 只有一個讀取端 → 通知可以省略，吞吐更好
    SingleWriter = true,
});

// 簡寫（用預設 FullMode = Wait，容量 1）
var simple = Channel.CreateBounded<int>(5);
```

`SingleReader` / `SingleWriter` 在你確定只有一組生產者/消費者時打開，內部可以省略跨執行緒的同步成本。**不確定就别開** — 開錯會導致資料損毀。

### 寫入

```csharp
await channel.Writer.WriteAsync(sample, ct);   // 通道滿且模式為 Wait 時會等待
channel.Writer.TryWrite(sample);               // 回傳 bool，滿了或已關閉就是 false
```

### 讀取

```csharp
// a) foreach：適合「每筆都要處理」
await foreach (var sample in channel.Reader.ReadAllAsync(ct))
{
    Process(sample);
}

// b) while：適合「知道大概要收到幾筆」
while (await channel.Reader.WaitToReadAsync(ct))
{
    while (channel.Reader.TryRead(out var sample))
    {
        Process(sample);
    }
}

// c) 單筆
var one = await channel.Reader.ReadAsync(ct);
```

### 關閉

```csharp
channel.Writer.TryComplete();          // 不再寫入，讀取端會在讀完後結束
channel.Writer.TryComplete(exception); // 帶著錯誤原因關閉
```

⚠ **一定要在 `finally` 裡 `TryComplete()`**，否則消費端的 `while (await WaitToReadAsync())` 會永遠等下去。

---

## 3. 背壓（Backpressure）

這是 Channel 最重要的性質。

```csharp
var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(5)
{
    FullMode = BoundedChannelFullMode.Wait,
});

var consumer = Task.Run(async () =>
{
    await foreach (var item in channel.Reader.ReadAllAsync(ct))
    {
        await Task.Delay(20, ct);     // 慢慢消化
    }
});

for (var i = 1; i <= 20; i++)
{
    await channel.Writer.WriteAsync(i, ct);   // ← 這裡會「等」
}
```

容量 5、消費者每筆 20ms，所以生產 20 筆要花 400ms 以上。`WriteAsync` 在通道滿時**會等待**，逼生產者降速。

**沒有任何資料遺失，也沒有記憶體暴增。** 這就是背壓。

### 對照：如果用無上限 channel

```csharp
var channel = Channel.CreateUnbounded<int>();   // 永遠不阻塞
```

生產者會瞬間寫完 20 筆然後返回。但如果生產者是 PLC 輪詢、消費者要處理 100 筆，**資料會一直堆在記憶體裡直到 OOM**。這是工業程式最常見的崩潰原因之一。

---

## 4. 通道滿了怎麼辦：FullMode

| 模式 | 行為 | 適用情境 |
| --- | --- | --- |
| `Wait`（預設） | `WriteAsync` 等待有空間；`TryWrite` 回傳 `false` | **不能漏資料**（記錄、警報） |
| `DropOldest` | 丟掉最舊的，騰空間給新的 | **只要最新狀態**（儀表板、監控畫面） |
| `DropWrite` | 丟掉新來的這一筆 | 慢速消費端可以接受跳過 |

```csharp
// 容量 3，寫入 1..5

// DropOldest → 留下 3, 4, 5
// DropWrite  → 留下 1, 2, 3
// Wait       → 第 4 筆 TryWrite 回傳 false，必須等
```

### 怎麼選

問自己一個問題：**丟掉的資料，丟得起嗎？**

- 設備警報、稽核紀錄、生產履歷 → **丟不起** → 用 `Wait`，讓生產者降速，並且要處理「處理不來怎麼辦」
- 溫度濕度顯示、狀態燈 → **丟得起** → 用 `DropOldest`

**`DropOldest` 的 TryWrite 會回傳 `true`**（因為它成功丟掉舊的換進新的），所以不能用回傳值判斷「有沒有成功」。

### `Wait` 模式會被卡住

```csharp
var bounded = Channel.CreateBounded<int>(1);   // 容量 1
bounded.Writer.TryWrite(1);
var ok = bounded.Writer.TryWrite(2);          // false，滿了
await bounded.Writer.WriteAsync(2, ct);       // 會等到有空間
```

如果用 `Wait` 卻忘記帶 `ct`，整個系統會永久停住。**`WriteAsync` 一律帶 token。**

---

## 5. 批次處理

```csharp
while (await reader.WaitToReadAsync(ct))
{
    // TryRead 把已排隊的資料一次倒出來
    while (reader.TryRead(out var sample))
    {
        latest[sample.Tag] = sample;
    }

    // 這裡才做「批次」動作：一次更新畫面 / 一次寫資料庫
    await SaveBatchAsync(latest.Values);
}
```

**為什麼要這樣**：如果每收到一筆就更新一次 WPF 畫面或寫一次資料庫，1000 筆就是 1000 次 IO。改成「倒完再批次處理」可能只需要 3 次。

範例裡的 `ConsumeAsync` 就是這個形狀 — 每 500ms 輸出一次彙整結果，中間的資料只更新字典。

---

## 6. 完整採集程式骨架

把前兩節合起來，這就是工業採集程式的標準形狀：

```csharp
public sealed class PlcCollector(string device, string host, int port, int intervalMs)
{
    private readonly Channel<TagSample> _channel = Channel.CreateBounded<TagSample>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });

    // 採集迴圈：只負責「盡快拿到資料」
    public async Task ProduceAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var values = await ReadAllTagsAsync(host, port, ct);
                foreach (var sample in values)
                {
                    await _channel.Writer.WriteAsync(sample, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;                      // 預期中的取消，乾淨退出
            }
            catch (Exception ex)
            {
                // 連線失敗不是例外狀況，是日常。記錄後繼續，不要讓迴圈死掉。
                Console.Error.WriteLine($"[{device}] 採集失敗：{ex.Message}");
            }

            await Task.Delay(intervalMs, ct);
        }

        _channel.Writer.TryComplete();
    }

    // 處理迴圈：只負責「慢慢處理」
    public async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var sample in _channel.Reader.ReadAllAsync(ct))
        {
            await HandleAsync(sample, ct);
        }
    }
}
```

### 這個骨架的四個關鍵設計

**1. 取消要分兩種**

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }  // 預期
catch (Exception ex) { /* 記錄後繼續 */ }                                       // 真正的錯誤
```

**2. 連線失敗要 continue，不是 rethrow**

PLC 會斷線、會被重啟、網路會抖。這些是**日常**不是例外。迴圈裡 `throw` 出去等於整個採集停擺。

**3. 一定要有 `TryComplete()`，而且放 `finally`**

```csharp
try { await ProduceAsync(ct); }
finally { _channel.Writer.TryComplete(); }
```

少了這行，消費端會在 `WaitToReadAsync` 永久等待。

**4. 速率限制用 `Task.Delay` 而不是迴圈計時**

```csharp
await Task.Delay(intervalMs, ct);   // ✅ 會響應取消
```

不要用 `while (sw.Elapsed < interval) { }` 這種計時迴圈 — 它不會響應取消，停止時要等到時間到。

---

## 7. 延伸到 ASP.NET Core

工作上如果採集服務跑在 ASP.NET Core（常見做法：Minimal API + `BackgroundService`），`Channel` 的角色完全一樣：

```csharp
public sealed class CollectorWorker(Channel<TagSample> channel, ILogger<CollectorWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 把 ASP.NET 的 ApplicationStopping 接到採集迴圈
        await foreach (var sample in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(sample, stoppingToken);
        }
    }
}

builder.Services.AddSingleton(
    Channel.CreateBounded<TagSample>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = true,
    }));
```

`BackgroundService.ExecuteAsync` 的 `CancellationToken` 在應用程式關閉時會被取消 — 這就是「取消從最上層傳下來」的實際例子。

---

## 速查

```csharp
// 建立
Channel.CreateUnbounded<T>();
Channel.CreateBounded<T>(capacity);
Channel.CreateBounded<T>(new BoundedChannelOptions(n) { FullMode = ..., SingleReader = true });

// 寫
await writer.WriteAsync(item, ct);
writer.TryWrite(item);
writer.TryComplete();
writer.TryComplete(exception);

// 讀
await foreach (var x in reader.ReadAllAsync(ct)) { }
while (await reader.WaitToReadAsync(ct)) { while (reader.TryRead(out var x)) { } }
await reader.ReadAsync(ct);
reader.TryRead(out var x);
```

---

## 練習

1. 把 `Section1` 的容量從 5 改成 1 和 1000，量測產生生產時間，體會背壓的強度。
2. 把 `FullMode` 改成 `DropWrite`，重跑，看留下的資料有什麼不同。想想哪個情境會用到它。
3. 在 `CollectorDemo` 加上 `DropOldest` 的**丟棄計數**（在 `FullMode` 手動檢查 `TryWrite` + 比較 `Count`），顯示「總共丟了幾筆」。
4. 把 `ConsumeAsync` 改成「每 10 筆輸出一次平均值」，而不是每 500ms。
5. 把整個採集架構加上重連邏輯：連線失敗時指數退避（1s → 2s → 4s，上限 30s）後重試，並在恢復時清空 channel 的舊資料。
6. 改用 `ReadAllAsync` 之外的方法（例如自己實作 `IAsyncEnumerable`）讀取，體會 `yield return` 與 channel 的差別。
