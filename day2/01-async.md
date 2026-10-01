# Day 2 — 非同步

> 範例：[`AsyncDemo.cs`](AsyncDemo.cs)
> 跑法：`dotnet run --project day2 -- async`

⚠ **未經編譯驗證**（撰寫環境沒有 .NET SDK）。

如果你只能讀一節，讀〈3. 絕對不要用 `.Result`〉。

---

## 0. 先建立正確的心智模型

| | Thread | async/await |
| --- | --- | --- |
| 是什麼 | OS 執行緒，真正的排程單位 | **語言層的等待機制** |
| 成本 | 每個約 1 MB 堆疊，數量有限 | 幾乎免費 |
| 誰在排 | OS 排程器 | 等待完成的事件回呼 |
| 會不會佔住資源 | 會佔住 | **不會佔住** |

**最重要的一句話**：`await` 等待的時候，執行緒會被**還回 ThreadPool**，之後 I/O 完成時再重新排一個執行緒來繼續往下跑。

所以「async」跟「多執行緒」是兩件事：

- `await` 一個 I/O → **不需要** `Task.Run`，也不會用到額外執行緒
- 你看到 `Task.Run` 才代表真的在用另一條執行緒做 CPU 計算

```csharp
// ✅ 正確：等網路，執行緒被還回去
var data = await client.GetStringAsync(url);

// ❌ 多餘：把 async 方法丟到 ThreadPool，浪費一次排程
var data = await Task.Run(() => client.GetStringAsync(url));
```

---

## 1. async 方法的回傳型別

```csharp
// 有 await → 回傳 Task<T>
public static async Task<double> FetchAsync() { ... }

// 沒有任何 await（真的同步完成）→ 回傳 Task 或 ValueTask<T>
public static ValueTask<string?> GetCachedAsync() { ... }

// 完全不需要 await → 不要用 async 關鍵字，直接回傳值
public static double Compute() => 42;
```

**加了 `async` 但裡面沒有 `await`**，編譯器會警告 `CS1998` — 這是一個訊號：這個方法不需要 `async`。

**C# 裡不允許** `async void`，除了事件處理器。`async void` 讓例外無法被上層捕捉，會直接崩潰整個程序。**所有非事件處理器的 async 方法一律回傳 `Task`。**

---

## 2. 每個 await 之後的程式碼會去哪裡

```csharp
var data = await FetchAsync();
Console.WriteLine("這行在哪裡執行？");
```

三種情況：

1. **Console App**：`await` 之後通常還是同一個執行緒（沒有 SynchronizationContext）
2. **WPF / WinForms**：`await` 之後會回到 **UI 執行緒**（有 SynchronizationContext）
3. **ASP.NET Core**：沒有 SynchronizationContext，之後在哪個執行緒不保證

所以**不要假設 `await` 之後的執行緒是誰**。需要特定執行緒就用 `ConfigureAwait(false)`（見〈5. ConfigureAwait〉）。

---

## 3. 絕對不要用 `.Result` / `.Wait()`

```csharp
// ❌ 絕對不要
var data = FetchAsync().Result;
var data = FetchAsync().GetAwaiter().GetResult();
FetchAsync().Wait();
```

### 為什麼

**a) 浪費執行緒**

`.Result` 會把**呼叫端執行緒**卡住等到 Task 完成。那段時間這個執行緒什麼都不能做。

```csharp
var task = FetchAsync("A", 300, ct);
var blocked = task.Result;      // 這 300ms，這個執行緒完全被浪費
```

**b) 在 WPF / WinForms 會死鎖**

Console 沒有 SynchronizationContext，所以只是浪費。但 WPF、WinForms、舊版 ASP.NET 有。

UI 執行緒呼叫 `.Result` → 它要等 Task 完成 → Task 的 continuation 排在 UI 執行緒上 → **UI 執行緒在等自己** → 永遠等不到。

這是 C# 最經典的 bug，而且**症狀是整個程式沒反應**，很難診斷。

### 正確做法

```csharp
// ✅ 一路 await 到底
var data = await FetchAsync();

// ✅ Console 裡真的要「同步取得」：把工作丟到 ThreadPool
var data = await Task.Run(() => FetchAsync("B", 300, ct));
```

`Task.Run(() => FetchAsync(...))` 是 Console App 中唯一合理的「同步取得非同步結果」寫法 — 因為它不卡住呼叫端執行緒。

**但更好的做法是改變 API**：把方法定義成 `async Task<T>`，讓呼叫端自己決定要不要 await。定義 `.Result` 的 API 等於把問題推給呼叫者。

### `.Result` 和 `.GetAwaiter().GetResult()` 的差別

| | `.Result` | `.GetAwaiter().GetResult()` |
| --- | --- | --- |
| 卡住執行緒 | 是 | 是 |
| 例外包成 `AggregateException` | 是 | 否，直接丟原本的例外 |
| 建議 | 避免 | 避免（但語意較乾淨） |

`AggregateException` 包裝是 `.Result` / `.Wait()` 特有的麻煩 — 因為 `await` 會自動把它拆開，兩邊行為不一致。**這也是 `await` 比較好的理由之一。**

### 絕對不要在這裡 `.Result`

```csharp
lock (someLock)
{
    var result = FetchAsync().Result;   // ❌❌ 死鎖風險 + 持有鎖時卡住整個物件
}
```

---

## 4. 併發：WhenAll / WhenAny

### WhenAll — 全部完成

```csharp
// ❌ 循序：300 + 300 + 300 = 900ms
var a = await FetchAsync("A", 300, ct);
var b = await FetchAsync("B", 300, ct);
var c = await FetchAsync("C", 300, ct);

// ✅ 併發：同時發出，只等最慢的 = 300ms
var results = await Task.WhenAll(
    FetchAsync("A", 300, ct),
    FetchAsync("B", 300, ct),
    FetchAsync("C", 300, ct));
```

**But 這只在 I/O bound 有用。** CPU bound 的工作用 WhenAll 也不會變快，因為 CPU 只有一顆（見 [`day4/01-threading.md`](../day4/01-threading.md)）。

### WhenAny — 誰先完成

```csharp
var first = await Task.WhenAny(taskA, taskB);
var winner = await first;      // ⚠ 取得結果要再 await 那個 task
```

⚠ **WhenAny 結束不代表其他工作結束**，剩下的要自己收尾，否則會被丟著不管。

### 常見錯誤：忘記 await

```csharp
FetchAsync("A", ct);                    // ❌ 立刻返回，Task 被丟掉
_ = FetchAsync("A", ct);                // ✅ 明確表示「我故意不 await」
var t = FetchAsync("A", ct);            // ✅ 存起來，之後 await
```

compiler 會對「未使用的 `Task`」發出 `CS4014` 警告 — 看到它通常代表漏了 `await`。

---

## 5. ConfigureAwait

```csharp
var data = await FetchAsync().ConfigureAwait(false);
```

意思是「`await` 之後不要切回原來的 SynchronizationContext」。

**什麼時候用**：寫 Library / 共用元件時加，呼叫端是 Console 或 ASP.NET，之後的程式碼不需要特定執行緒。少一次排程，長鏈呼叫時累積起來有差。

**什麼時候不用**：WPF ViewModel、任何需要回到 UI 執行緒更新畫面的地方 — 加了會讓你在錯誤的執行緒上碰 UI 物件。

老實說，**多數專案不用特別寫也沒關係**。它是最佳化，不是必要條件。

---

## 6. Task vs ValueTask

| | `Task<T>` | `ValueTask<T>` |
| --- | --- | --- |
| 配置 | 通常配置到堆積（除非快取命中） | 預設不配置 |
| 快取命中 | 要還原成 Task | 可直接回傳結果 |
| 適用 | 幾乎所有情況 | 高頻呼叫 **且** 常常同步完成 |

```csharp
// 典型的 ValueTask 情境：快取
public static ValueTask<string?> GetCachedAsync(string key, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    return ValueTask.FromResult(key == "temperature" ? "Line1.Temp" : null);
}
```

### ValueTask 的四條紀律

1. **不要 await 兩次**（`await` 同一個 `ValueTask` 兩次，第二次可能拿到已消耗的結果）
2. **不要存成欄位**
3. **不能直接丟進 `Task.WhenAll`**（要先 `.AsTask()`）
4. **不要直接插值取值** — `ValueTask<T>` 不是 `T`，忘了 `await` 會印出 `System.Threading.Tasks.ValueTask\`1[...]`

**規則**：你不確定就用 `Task`。ValueTask 的最佳化通常不值得那個風險。

---

## 7. CancellationToken

### 方向：由外而內

```csharp
// ❌ 方法自己 new：呼叫端完全無法取消
public async Task WorkAsync()
{
    using var cts = new CancellationTokenSource();
    cts.CancelAfter(1000);
    await Task.Delay(5000, cts.Token);
}

// ✅ token 從外面傳進來
public async Task WorkAsync(CancellationToken ct) => await Task.Delay(5000, ct);
```

整套程式的取消應該**從最上層（`Console.CancelKeyPress`、HTTP request 中止、Windows 關機事件）一路傳到最底層**。每個 async 方法都接收 `CancellationToken` 並往下傳。

`Program.cs` 就是最上層：

```csharp
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;   // 不要被系統直接砍掉
    cts.Cancel();      // 改成走取消流程，讓各層有機會收尾
};
```

### `Task.Delay` 要帶 token

```csharp
await Task.Delay(1000, ct);           // ✅ 會響應取消
await Task.Delay(1000);               // ❌ 取消不了，會硬等 1 秒
```

**幾乎所有 .NET 的非同步 API 都接受 `CancellationToken`**，包括 `HttpClient`、`Stream.CopyToAsync`、`Socket.ReceiveAsync`、`Channel` 的 `ReadAsync` / `WriteAsync`。**看到非同步方法沒有 token 參數，就是設計有問題。**

### 取消點

```csharp
// 等待型 API：把 token 傳進去就會自己中止
await Task.Delay(1000, ct);
await stream.ReadAsync(buffer, ct);

// 迴圈型程式：手動檢查
foreach (var item in items)
{
    ct.ThrowIfCancellationRequested();
    await ProcessAsync(item, ct);
}
```

`ThrowIfCancellationRequested()` 丟的是 `OperationCanceledException`，這是預期行為。

### OperationCanceledException 要單獨 catch

```csharp
try
{
    await WorkAsync(ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    // ✅ 預期的取消，清潔收尾
}
catch (Exception ex)
{
    // ❌ 這裡應該只處理真正的錯誤
    // 如果取消也掉進來，你會把「使用者按 Ctrl+C」當成故障回報出去
    ReportError(ex);
}
```

`when (ct.IsCancellationRequested)` 這個條件還能區分「是這次呼叫預期的取消」還是「其他來源剛好丟來的取消」。

⚠ 絕對不要寫空的 `catch (Exception) { }` — 那會把取消和真正的錯誤都吞掉。

### 傳給別的方法

```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));
ct.ThrowIfCancellationRequested();
var result = await DoWorkAsync(cts.Token);
```

### 合併多個來源

```csharp
// 應用程式關閉 AND 使用者要求取消 —— 任一發生就取消
using var linked = CancellationTokenSource.CreateLinkedTokenSource(
    appShutdownToken,
    userCancelToken);

linked.CancelAfter(30_000);
```

---

## 8. 例外處理

### await 會自動拆 AggregateException

```csharp
var all = Task.WhenAll(tasks);
try { await all; }
catch (Exception ex)
{
    // ex 是 InvalidOperationException，不是 AggregateException
}
```

這是 `await` 比 `.Wait()` 好用的一個原因 —— 不用剝兩層例外。

### 想看所有失敗原因

```csharp
var all = Task.WhenAll(tasks);
try { await all; }
catch { /* 至少有一個失敗 */ }

if (all.Exception is { InnerExceptions.Count: > 0 } aggregate)
{
    foreach (var inner in aggregate.InnerExceptions)
    {
        Console.WriteLine($"{inner.GetType().Name}: {inner.Message}");
    }
}
```

### WhenAll 不是 fail-fast

**一個 Task 失敗不會取消其他 Task。** 它們會各自跑完，然後 `WhenAll` 才把第一個例外丟出來。

這常常跟直覺相反：

```csharp
var all = await Task.WhenAll(
    FetchFromDeviceA(),      // 慢，但重要
    FetchFromDeviceB(),      // 失敗
    FetchFromDeviceC());

// B 丟例外時，A 還在跑 —— 但 WhenAll 會等 A 完成才丟出來
```

要真的 fail-fast（失敗就立刻回報、取消其他）：

```csharp
var tasks = new[] { FetchA(), FetchB(), FetchC() };
var all = Task.WhenAll(tasks);
var first = await Task.WhenAny(all, WaitForFailureAsync(tasks));
```

或者用 `CancellationTokenSource` 手動把 token 串起來，在失敗時呼叫 `cts.Cancel()`。

### 要 try/catch 就包在 await 外面

```csharp
// ❌ 沒保護到：FetchAsync 開始執行，但例外發生在 await 之後
var task = FetchAsync(ct);
try { var r = await task; }
catch { }

// ✅ 保護到了整段
try
{
    var r = await FetchAsync(ct);
    Use(r);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
catch (IOException ex) { RetryLater(ex); }
```

---

## 9. 常見錯誤速查

| 症狀 | 原因 | 修法 |
|---|---|---|
| `CS4014` 警告 | 呼叫了 async 方法但沒 `await` | 補上 `await` |
| `CS1998` 警告 | 標了 `async` 但沒 `await` | 拿掉 `async` |
| WPF 整個沒反應 | UI 執行緒用了 `.Result` | 一路 `await` |
| 拿到 `AggregateException` | 用了 `.Result` / `.Wait()` | 改用 `await` |
| 順序沒變慢 | 用 `await` 循序呼叫 | 改 `Task.WhenAll` |
| 取消沒反應 | `Task.Delay` 沒帶 token | 傳 `ct` 進去 |
| 取消被當成錯誤 | 空的 `catch (Exception)` | 單獨 catch `OperationCanceledException` |
| 例外看不到 | 只 catch 了外層 | `await` 會自動拆；要看全部用 `.Exception.InnerExceptions` |
| 記憶體一直漲 | 迴圈裡 `new HttpClient()` | 重用一個實例（見 [`day3/04-webapi.md`](../day3/04-webapi.md)） |

---

## 練習

1. 修掉 `.Result` 那段，改用 `Task.Run` 或改成一路 `await`，量測兩者差異。
2. 寫一個 `FetchAllAsync` 接受 tag 清單，支援並行度上限（例如同時最多 5 個連線），用 `SemaphoreSlim` 實作。
3. 加一個 fail-fast 版：任一 tag 讀取失敗就取消其餘工作。
4. 把 `GetCachedValueAsync` 改成 `Task`，比較記憶體配置差異（用 `GC.GetAllocatedBytesForCurrentThread()`）。
5. 在 `AsyncDemo` 最外層加一個 3 秒的 `CancellationTokenSource`，用 `CancelAfter` 讓整支範例能自動結束，體驗取消一路傳下去的效果。
