# Day 4 — 執行緒、鎖、Parallel、Process

> 範例：[`ThreadingDemo.cs`](ThreadingDemo.cs)
> 跑法：`dotnet run --project day4 -- threading`

✅ 已用 .NET 10 SDK 編譯並實跑驗證過。輸出裡的時間數字會依你的機器而不同。
另外第 4 節的 `Parallel.For` **結果數字刻意不正確** —— 那是在示範共用變數的競態。

> 這節對你的意義：PLC 採集程式幾乎一定有多執行緒需求。**懂 async 不代表懂執行緒競爭** —— 這兩件事要分開學。

---

## 1. .NET 的執行緒模型

C# 的 `async` / `await` **不會建立執行緒**。這是第一個要釐清的事。

```
Task.WhenAll(Enumerable.Range(0, 100).Select(i => ReadDeviceAsync(i)))
```

這行建立 100 個 Task，但它們**共用同一個執行緒**（或很少幾個）。原因是 `await` 遇到「還沒完成的非同步操作」時，會把目前的方法**暫停**並**把執行緒還給 ThreadPool**，等操作完成後再排回來繼續執行。

所以：

- **async/await = 讓執行緒閒下來，不是讓工作同時跑**
- **Task.Run = 真的需要另一個執行緒**（CPU bound 工作）

⚠ 這個區分搞錯，就會出現「我明明用了 Task.WhenAll 為什麼還是很慢」—— 因為那些工作本質上是 CPU bound，`await` 不會讓它們並行。

---

## 2. ThreadPool

`.NET` 所有執行緒工作的預設來源。

```csharp
ThreadPool.GetMinThreads(out var minWorker, out var minIo);
ThreadPool.GetMaxThreads(out var maxWorker, out _);
ThreadPool.GetAvailableThreads(out var available, out _);
ThreadPool.GetPendingWorkCount(out var queued);
```

### 不要自己 new Thread

```csharp
// ❌ 不要
var thread = new Thread(() => DoWork());
thread.IsBackground = true;      // 忘了設的話程序不會結束
thread.Start();
```

理由：

1. **ThreadPool 會重用執行緒**。`new Thread` 每次都是新的，成本高
2. 自己管的執行緒數量不受控 → 幾百個就把自己搞死
3. **前景執行緒（`IsBackground = false`）會阻止程序結束** —— 這是常見的「程式跑完卻不退出」的原因
4. 執行緒數不足時 ThreadPool 會在約 1 秒內注入新執行緒（`ThreadPool.InjectionRate`）

**唯一的例外**：`Main` 執行緒和明確要用前景執行緒的極少數情境。

### 環境變數

| 變數 | 用途 |
|---|---|
| `DOTNET_ThreadPool_MinThreads` | 提高執行緒池下限 |
| `DOTNET_ThreadPool_MaxThreads` | 提高上限（**很少需要**） |
| `DOTNET_gcServer` | 用工作站 GC 換吞吐量 |

⚠ **先量測再調。** 執行緒不足的症狀通常是「吞吐量上不去但 CPU 只用了一兩成」—— 那是執行緒在等 I/O，不是 CPU 不夠。盲目拉高執行緒數只會增加 context switch 和記憶體用量。

---

## 3. CPU bound vs I/O bound

這是決定「要不要 `Task.Run`」的唯一依據。

| | I/O bound | CPU bound |
|---|---|---|
| 例子 | 讀 PLC、呼叫 API、讀檔 | 影像處理、加密、壓縮、解析大量資料 |
| 在等什麼 | 等別人（網路/磁碟） | 等自己算完 |
| 直接 `await` | ✅ 正確做法 | ❌ 會卡住整個執行緒 |
| `Task.Run` | ❌ 多餘 | ✅ 必要 |
| 併發上限 | 幾百、幾千 | ≈ CPU 核心數 |

```csharp
// ✅ I/O bound：直接 await，100 個也沒問題
await Task.WhenAll(devices.Select(d => ReadAsync(d, ct)));

// ✅ CPU bound：丟進 ThreadPool
await Task.WhenAll(items.Select(i => Task.Run(() => Process(i), ct)));

// ❌ CPU bound 忘了 Task.Run：整個方法會被卡住，其他 await 都等不到
var results = items.Select(i => Process(i)).ToArray();
```

⚠ 判斷方式：**把工作換成 `Task.CompletedTask` 會不會讓它變快？** 會 → CPU bound（`Task.Run`）；不會 → I/O bound。

---

## 4. 競爭條件

```csharp
private int _count;

// ❌ 競態：++ 是「讀 → 加 → 寫」三步
public int Increment() => ++_count;
```

兩個執行緒同時進來：

```
執行緒 A 讀 _count = 100
執行緒 A 加 → 101
                    執行緒 B 讀 _count = 100   ← 還是 100！
                    執行緒 B 加 → 101
執行緒 A 寫 101
                    執行緒 B 寫 101
結果：101，但應該是 102
```

⚠ **這種 bug 執行 100 次可能不會出現一次。** 在單核心機器上幾乎不會重現，在 4 核的樹莓派上跑大資料量就會。**「沒重現」不代表「沒問題」**，這是最難除錯的一類 bug。

### 解法 a：Interlocked（單一變數）

```csharp
public int Increment() => Interlocked.Increment(ref _count);
```

| 方法 | 用途 |
|---|---|
| `Interlocked.Increment` / `Decrement` | 加減 |
| `Interlocked.Add` / `Exchange` | 設值（回傳舊值） |
| `Interlocked.CompareExchange` | 條件式設值（CAS） |
| `Interlocked.Read` | 64-bit 值的原子讀取 |

⚠ **64-bit 的數值（`long`、`double`、`IntPtr`）在 32-bit 平台（例如 32-bit ARM）上，`++` 可能只寫一半。** 用 `Interlocked.Read` / `Interlocked.Add` 才安全。

### 解法 b：lock（多個欄位 / 複雜邏輯）

```csharp
private readonly object _gate = new();

public void Update()
{
    lock (_gate)
    {
        _a = ...;
        _b = _a * 2;      // 必須和 _a 的更新保持一致
    }
}
```

### 解法 c：`System.Threading.Lock`（C# 13 / .NET 9+）

```csharp
private readonly Lock _gate = new();

public int Increment()
{
    lock (_gate)
    {
        return ++_count;
    }
}
```

比 `object` 更好：**有執行緒身分檢查**，可以偵測「在沒有持有鎖的狀態下釋放」這類 bug（執行期丟例外，而不是等到資料損毀）。效能也略好。

⚠ `Lock` 只能用在 `lock` 陳述式上，**不能拿來取代 `Monitor.Enter` / `Semaphore`**。

### 怎麼選

| 情況 | 用 |
|---|---|
| 單一整數的增減 | `Interlocked` |
| 一次要改多個欄位且要保持一致 | `lock` |
| 不確定 | `lock`（成本低，正確性重要） |
| 只是要避免重複執行 | `SemaphoreSlim(1, 1)` 或 `Interlocked` 做 gate |

---

## 5. Volatile

```csharp
public int Value => Volatile.Read(ref _count);
```

`volatile` 關鍵字或 `Volatile.*` 方法**保證不會被重新排序或被快取在 register 裡**。

⚠ **`Volatile` 不是鎖，也不保證原子性。** 它只保證「這個值會被看到」。`Volatile.Increment` 根本不存在（`Interlocked.Increment` 本身已經有相同的記憶體排序保證）。

`Volatile` 的正確用途：

- 一個執行緒在迴圈裡讀，另一個執行緒在寫（單向旗標）
- lock-free 的單寫多讀結構

**不要**用 `volatile` 來「修」上面那個 `++` 的競態。

---

## 6. 限制併發數量

**這是工業採集最常用的一招。** 你有 20 台設備，但 PLC 最多同時接 4 條連線。

```csharp
using var semaphore = new SemaphoreSlim(4, 4);

async Task ReadAsync(Device device, CancellationToken ct)
{
    await semaphore.WaitAsync(ct);
    try
    {
        await device.ReadAsync(ct);
    }
    finally
    {
        semaphore.Release();      // ← 一定要在 finally
    }
}
```

⚠ **`Release()` 漏掉就是死鎖。** 任何例外都會永久洩漏一個配額，跑滿 4 次之後所有工作都卡住 —— 而且沒有任何錯誤訊息。這種 bug 現場會表現為「跑一小時後整個系統沒反應」。

`SemaphoreSlim` 是 `Semaphore` 的輕量版，支援 `WaitAsync`（`Semaphore` 只有阻塞式 `Wait`，會卡執行緒）。

### 另一種做法：`Channel` 的 bounded capacity

```csharp
var channel = Channel.CreateBounded<TagSample>(new BoundedChannelOptions(4)
{
    FullMode = BoundedChannelFullMode.Wait,   // 满了就等（背壓）
});
```

用 channel 的話，**併發控制是由 channel 的容量決定的**，不需要額外的 semaphore。細節見 [`day2/02-channels-and-collector.md`](../day2/02-channels-and-collector.md)。

---

## 7. Parallel

```csharp
// ✅ 有 thread-local 累加，正確
long total = 0;
Parallel.For(
    0, data.Length,
    () => 0L,                                    // 初始化
    (i, _, localSum) => localSum + data[i] % 7,   // 累加（執行緒私有）
    localSum => Interlocked.Add(ref total, localSum));   // 合併

// ❌ 在 body 裡寫共用變數：競態，結果不對
long broken = 0;
Parallel.For(0, data.Length, i => broken += data[i] % 7);
```

⚠ **`Parallel.For` 的 body 會被同時執行多次。** 在裡面寫任何共用變數都是 bug。`ThreadingDemo.cs` 會把兩種寫法並排跑 —— 你會看到 `broken` 的值比 `total` 小。

`Parallel.ForEachAsync` 是非同步版本（.NET 6 起）。實際 API 與各版本可用性請以官方文件為準。

---

## 8. Process

`Task` / `Thread` 都在**同一個程序**裡。要呼叫外部程式或需要完全隔離時用 `Process`。

```csharp
var psi = new ProcessStartInfo
{
    FileName = "dotnet",
    Arguments = "--version",
    RedirectStandardOutput = true,     // ⚠ 這三個要一起設
    RedirectStandardError = true,
    UseShellExecute = false,           // ⚠ 必須 false 才能 Redirect
    CreateNoWindow = true,             // ⚠ 只在 UseShellExecute = false 時有效
};

using var process = Process.Start(psi)!;
```

### ⚠ 兩大死鎖陷阱

**1. 只讀一個輸出流**

```csharp
// ❌ 如果 stderr 塞滿 OS 的緩衝區，它會永遠等不到你讀 → 互相等待
await process.StandardOutput.ReadToEndAsync();
```

**2. 沒設逾時**

子行程卡住時 `WaitForExit` 會永久阻塞。

**正確寫法**：

```csharp
var outputTask = process.StandardOutput.ReadToEndAsync(ct);
var errorTask = process.StandardError.ReadToEndAsync(ct);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await process.WaitForExitAsync(cts.Token);      // .NET 5+

var output = await outputTask;
var error = await errorTask;
var exitCode = process.ExitCode;
```

⚠ **兩個 `ReadToEndAsync` 都要「開始」讀之後才去等結束。** 先 await 完 stdout 再開始讀 stderr，中間對方可能已經把 stderr 塞滿了。

### 怎麼選

| 需求 | 用 |
|---|---|
| 需要非同步等待 | `Task`（`await`） |
| 需要平行計算 | `Task.Run` / `Parallel` |
| 需要呼叫外部程式 | `Process` |
| 需要完全隔離（第三方工具可能崩潰） | `Process` |
| 需要限時執行並在超時時強制結束 | `Process.Kill(entireProcessTree: true)` |

⚠ 程式碼壞掉時**盡量不要讓整個程序崩掉**。跑不受信任的解析器、外部工具、使用者輸入的腳本時，包在 `Process` 裡 —— 崩了也只會死那個子行程。

---

## 速查

```csharp
// ThreadPool 資訊
ThreadPool.GetAvailableThreads(out var a, out _);
ThreadPool.GetPendingWorkCount(out var queued);

// CPU bound
await Task.Run(() => HeavyWork(), ct);

// 併發限制
using var sem = new SemaphoreSlim(4, 4);
await sem.WaitAsync(ct);
try { }
finally { sem.Release(); }

// 原子操作
Interlocked.Increment(ref _value);
Interlocked.Add(ref _total, amount);
Volatile.Read(ref _flag);

// lock
lock (_gate) { }
lock (new Lock()) { }        // .NET 9+

// Parallel
Parallel.For(0, n, () => 0L, (i, _, local) => local + F(i), local => Interlocked.Add(ref total, local));

// Process
var psi = new ProcessStartInfo
{
    FileName = "dotnet", Arguments = "--version",
    RedirectStandardOutput = true, RedirectStandardError = true,
    UseShellExecute = false, CreateNoWindow = true,
};
using var p = Process.Start(psi)!;
var o = p.StandardOutput.ReadToEndAsync(ct);
var e = p.StandardError.ReadToEndAsync(ct);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await p.WaitForExitAsync(cts.Token);
```

---

## 練習

1. 把 `Section3_InterlockedVsLock` 的 `Iterations` 從 100,000 調到 10,000,000，觀察三種寫法的差距（`lock` 的 overhead 會明顯出來）。
2. 寫一個**刻意有 bug** 的版本：在 `Task.Run` 裡直接 `list.Add()`（`List<T>` 不是 thread-safe），跑大資料量看會不會漏資料或丟例外。
3. 用 `Channel.CreateBounded` 取代 `SemaphoreSlim` 做併發限制，比較兩種寫法的差異。
4. 改 `Section4`，讓它同時讀 200 台設備，觀察記憶體和執行緒數變化。
5. 實作一個會 timeout 就 `Kill` 的 `RunProcessAsync(string exe, string args, TimeSpan timeout)`。
6. 用 `Process` 實作 `ping` 呼叫，解析輸出成 `PingResult` record。
7. 量測：寫一個 benchmark 比較 `Task.Run` 和 `Parallel.For` 在 1 個 / 4 個 / 8 個核心的樹莓派上的差異。
