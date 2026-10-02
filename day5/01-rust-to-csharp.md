# Day 5（選讀 bonus）：把 Rust 的安全觀念帶進 C#

> **這一天不在 4 天主線裡。** 4 天的目標是「恢復 C#／.NET 的手感，能開始新的工作」。
> 這一天是給「已經會 Rust，想知道 C# 怎麼處理同樣的問題」的人看的。
> 如果你只是要找工作，直接跳過這裡，不會有任何影響。

## 為什麼要有這一天

Rust 有一套「資源與錯誤的處理保證」，而且是**編譯器強制的**：
忘了釋放會編譯失敗、忘記處理錯誤會編譯失敗、把參考帶出生命週期也會編譯失敗。

C# 沒有這些保證。C# 的做法是**約定 + 工具鏈**：
`using` 是約定、Analyzer 是工具鏈、程式碼審查是保險。

兩邊都「對」，但設計哲學不同。把 Rust 觀念帶進來的最大價值不是硬翻譯語法，
而是**讓你知道 C# 少掉了哪一層保護，以及要自己補哪條紀律**。

## 執行

```bash
dotnet run --project day5 -- ownership
dotnet run --project day5 -- borrowing
dotnet run --project day5 -- errors
dotnet run --project day5 -- struct
```

四支範例都離線可跑，沒有任何外部相依，跑完自動結束。

---

## 快速對照表

| Rust 概念 | Rust 保證了什麼 | C# 對應機制 | C# 少了什麼 |
| --- | --- | --- | --- |
| 所有權（move） | 每個值一個擁有者，擁有者離開就 drop | `IDisposable` + `using` | 編譯器不追蹤「誰是擁有者」 |
| 借用 `&T` | 生命週期不能超過被借用者 | `in` / `ref readonly` / `readonly struct` | 生命週期檢查 |
| 借用 `&mut T` | 同時只能一個可變借用 | 無 | 編譯器完全不檢查 |
| `Result<T, E>` | 不 match 就編譯失敗 | `TryXxx` 慣例（部分覆蓋） | 大部分方法沒有 |
| `?` 運算子 | 錯誤自動上傳 | 沒有（要手寫 try/catch） | — |
| `panic!` | 明確標記「這裡會炸」 | `throw` / `UnreachableException` | — |
| enum with data | 每個變體可攜帶不同資料 | `sealed class` 階層 / 手寫 union | 沒有 sum type |
| match 窮舉性 | 漏一個變體編譯失敗 | `switch` + `default` 丟例外 | 漏一個 case 編譯通過 |
| `Send` / `Sync` | 非執行緒安全的值不能跨執行緒 | 無 | 資料競爭編譯通過 |
| 整數溢位 | debug 模式 panic | `checked` 關鍵字（預設關閉） | 預設靜默繞回 |

---

## 1. 所有權（`ownership`）

### Rust 保證了什麼

```rust
let s = String::from("hi");
drop(s);
println!("{s}");   // ❌ E0382: borrow of moved value: `s`
```

編譯器保證四件事：

1. 每個值**恰好一個擁有者**，move 會轉移擁有權。
2. 擁有者離開 scope 時**必定** drop，資源不會漏。
3. 借用結束前，被借用的值**不能被 move**（迭代器失效被擋下）。
4. `Send` / `Sync` 標記決定值能不能跨執行緒。

### C# 的做法

```csharp
using var stream = new MemoryStream();
// 離開 scope 自動 Dispose()
```

編譯器保證的**只有一條**：`using` / `await using` 離開 scope 必定呼叫 `Dispose`。

編譯器**不**保證的：

- 這個 `IDisposable` 應該被誰擁有 → 你要自己記錄
- 有沒有漏掉某個 `using` → 少寫一個就漏了，沒有任何提示
- 擁有者還活著時，有沒有人拿著它的參考 → 無從得知
- 那個參考有沒有跨執行緒 → 不檢查

### `using` 對應 Rust 的 `drop`

| | Rust | C# |
| --- | --- | --- |
| 同步釋放 | 離開 scope 自動 drop | `using` / `using var` |
| 非同步釋放 | 手動 `block_on(async { .. })` | `await using` |
| 提前釋放 | `drop(x)` | 離開 block 或呼叫 `Dispose()` |
| 不釋放 | `std::mem::forget(x)` | 不寫 `using` |

`await using` 是 C# 特有的便利：Rust 沒有非同步 drop，
`async` 區塊結束時 drop 只能同步進行，所以常見寫法是手動在 `async` 函式裡 drop。
C# 把這件事變成語言層級的 `await using`，記得在 `async` 方法裡用 `await using` 而不是 `using`。

### 真實資源：HttpClient

`HttpClient` 是最容易踩的坑，因為它**實作** `IDisposable` 但幾乎不該被 Dispose：

```csharp
// ❌ 每次一個新連線池 → socket 耗盡（SocketException: Address already in use）
foreach (var url in urls)
{
    using var client = new HttpClient();
    await client.GetStringAsync(url);
}

// ✅ 長駐一個實例
private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
```

Rust 這邊是 `reqwest::Client` 內部 `Arc<Inner>`，`clone()` 廉價，所以到處 clone 沒問題。
C# 的 `HttpClient` 沒有 `Clone()`，只能靠紀律。
ASP.NET Core 用 `IHttpClientFactory` 解決這件事（它處理 DNS 更新與生命週期）。

### C# 紀律

1. 所有 `IDisposable` 一律宣告在 `using` / `await using` 裡，**不要存成欄位**。
2. 需要長生命週期 → 用包裝型別（`Lazy<T>` + 自行管理）或 `IHttpClientFactory`。
3. 跨執行緒前確認該物件有執行緒安全，或用 immutable 快照代替共用。

---

## 2. 借用（`borrowing`）

### Rust 的借用規則

```rust
let mut v = vec![1, 2, 3];
let first = &v[0];   // 不可變借用
v.push(4);           // ❌ E0502
println!("{first}");
```

編譯器擋下的原因：`push` 可能讓 `v` 重新配置，`first` 就指向已釋放的記憶體。

### C# 的對應

| Rust | C# | 保證程度 |
| --- | --- | --- |
| `&T`（不可變借用） | `in T` / `ref readonly T` | 強：方法內不能改 |
| `&mut T`（可變借用） | `ref T` | 弱：沒有「唯一性」檢查 |
| `&[T]`（借用 slice） | `ReadOnlySpan<T>` | 強：不能跨 `await`、不能存欄位 |
| `&mut [T]` | `Span<T>` | 強（同上） |
| `&str` | `ReadOnlySpan<char>` / `ReadOnlySpan<byte>`（`u8`） | 強 |

### `readonly struct` ≠ `&T`

`readonly struct` 是 C# 裡最容易跟借用搞混的東西：

- `readonly struct` **每次呼叫都是複製**，不是借用 → 跟 Rust 的 `T`（move + Copy）比較像。
- `in` / `ref readonly` 才是真正的借用 → 對應 Rust 的 `&T`。

`readonly struct` 的價值不在效能（它一樣會複製），而在**語意**：
編譯器禁止你在任何方法內改它的欄位，所以拿到它的方法不可能偷偷改到原值。

### `Span<T>` 為什麼是 `ref struct`

`Span<T>` 借用的記憶體來自 `stackalloc`、陣列或 `ArrayPool`。
把它存成欄位或跨 `await` 使用，借用就可能失效 —— 這正是 Rust 的 lifetimes 在做的事，
只是換成編譯器錯誤：

- 不能當類別欄位
- 不能跨 `await` / `yield`
- 不能當泛型型別參數 `T`（泛型可能把它存進堆積）

需要跨 `await` 或存欄位時，用 `ReadOnlyMemory<T>`：它是正常 struct，
但 `.Span` 取得借用時會在**執行期**檢查有沒有已被 Dispose。
這是「執行期檢查」取代「編譯期檢查」的取捨。

### Boxing：`struct` 跟泛型合不來

`struct` 進不了泛型方法而不付出代價：

```csharp
var manual = 0.0;
foreach (var m in measurements) { manual += m.Value; }   // 0 bytes

var viaLinq = measurements.Sum(m => m.Value);             // 每個 m 都要 boxing
```

`day5/borrowing` 會用 `GC.GetAllocatedBytesForCurrentThread()` 實際量出來給你看。
先量再優化 —— LINQ 清楚得多，除非真的在熱路徑上每秒跑幾百萬次。

---

## 3. 錯誤處理（`errors`）

### Rust：`Result` 強迫你處理

```rust
fn read_config(p: &str) -> Result<Config, ConfigError> { ... }

match read_config("a.toml") {
    Ok(cfg) => println!("{}", cfg.port),
    Err(e)  => eprintln!("{e}"),
}

// 或用 ? 提早返回
fn setup(p: &str) -> Result<(), Box<dyn Error>> {
    let cfg = read_config(p)?;   // Err 就直接往上傳
    start(cfg);
    Ok(())
}
```

不 `match` 完整就編譯失敗。`?` 讓錯誤上傳只是一行。

### C#：TryXxx 慣例

C# 有一套幾乎同構的機制，藏在 BCL 的命名慣例裡：

```csharp
if (int.TryParse(text, out var value)) { /* ok */ }
if (dict.TryGetValue(key, out var v)) { /* ok */ }   // 不是 KeyNotFoundException
if (queue.TryDequeue(out var item)) { /* ok */ }
```

看名字就知道「這方法不會丟例外」。整套 .NET 都遵守這個慣例。

`TryGetValue` 存在的理由不只是「不拋例外」，而是**一次查找完成**：

```csharp
// ❌ 兩次查找，中間若有其他執行緒改動就不一致
if (scores.ContainsKey("carol")) { var s = scores["carol"]; }

// ✅ 一次查找
if (scores.TryGetValue("carol", out var s)) { }
```

### 什麼時候用例外、什麼時候用 TryXxx

| 情況 | 用什麼 | 理由 |
| --- | --- | --- |
| 輸入解析失敗（預期內） | `TryParse` | 失敗是常見分支，不該走例外 |
| 查不到 key（預期內） | `TryGetValue` | 同上 |
| 網路 I/O 失敗 | 例外 | 失敗少見，而且要附帶診斷資訊 |
| 狀態機轉移錯誤 | 例外 | 屬於程式錯誤，該炸 |
| 解析設定檔失敗 | 兩者都可 | 見下面「要不要自訂 Result」 |

**不要**用例外做正常流程控制。`try/catch` 有真實成本（stack unwinding），
在每秒呼叫幾百萬次的路徑上量得出來。

### 要不要自己定義 `Result<T>`？

多數時候**不要**。理由：

1. 整個 .NET 生態系用例外，自訂 `Result` 會跟所有 API 格格不入。
2. `TryXxx` 已經覆蓋八成需求。
3. 大量 `throw` 的成本在熱路徑是實測得出來的。

考慮自訂的只有三種情況：

1. 錯誤是**預期的常見分支**（例如解析外部設定檔）。
2. 錯誤值本身**攜帶資訊**呼叫端需要（HTTP 狀態碼、錯誤分類 enum）。
3. 介面契約要求不能拋例外（每毫秒呼叫一次的 hot path）。

### 錯誤分類照 Rust 的 error enum 設計

Rust 社群的好習慣是把錯誤拆成 enum，讓呼叫端能 `match` 決定要不要重試：

```rust
enum ConfigError {
    NotFound(PathBuf),          // 不要重試
    InvalidSyntax { line: u32 },// 不要重試
    PermissionDenied,           // 不要重試
    TransientIo,                // 可以重試
}
```

C# 對應到「自訂例外型別 + 集中的分類」。重試判斷不要寫成一長串
`if (ex is X || ex is Y || ...)`，用一個 `CanRetry` 屬性或分類 enum 集中管理。

### `UnreachableException` 對應 `unreachable!()`

Rust 的 `match` 有窮舉性：加一個 enum 變體，所有沒處理的 `match` 都編譯失敗。
C# 的 `switch` 沒這個保證。慣例是給 `default` 丟 `UnreachableException`：

```csharp
switch (state)
{
    case DeviceState.Idle:    return "待機中";
    case DeviceState.Running: return "運轉中";
    case DeviceState.Faulted: return "故障";
    default:
        throw new UnreachableException($"沒處理 {state}");
}
```

這個 `throw` **不是防禦性程式碼，是文件**：「我認為這裡到不了，
如果到了就是我的 enum 漏了 case」。這是 C# 對 Rust 窮舉性最接近的替代品。

### `Task.WhenAll` ≠ `join_all`

行為看起來一樣，但有一個大差異：

```csharp
var results = await Task.WhenAll(tasks);
```

- 任何一個 faulted → 整個 `WhenAll` faulted，**其他 task 還是會跑到底**。
- Rust 的 future 被 drop 時，任務**真的會停**。
- C# 沒有人 await 的 `Task` 還是會跑到完成。

所以最常見的坑：`cts.Cancel()` **不會**停掉已在執行的 `Task`。
那個 task 必須自己檢查 token 或捕獲 `OperationCanceledException`。

---

## 4. struct（`struct`）

### 值語意

C# 的 `struct` 每次賦值都複製：

```csharp
var a = new Point(3, 4);
var b = a;      // 複製
b.X = 99;       // a 不變
```

Rust 的 `struct` **不會自動複製**，move 語意，要寫 `a.clone()`。
C# 的隱式複製對 struct 很方便，但隱藏了成本：
一個 1MB 的 struct 當欄位，每次 loop 都複製 1MB。

### `record struct` ≈ `#[derive(...)]`

| Rust | C# |
| --- | --- |
| `#[derive(Debug)]` | `record struct` 自動產生 `ToString()` |
| `#[derive(PartialEq)]` | `record struct` 自動產生 `==` |
| `#[derive(Clone, Copy)]` | struct 本來就會複製，不需要 |
| `struct update syntax` | `with { Field = v }` |

**要當 dictionary key 就用 `record struct`，不要用 `struct`。**
一般 `struct` 沒有定義 `==`，`Equals` 繼承自 `ValueType`，要靠反射，慢而且可能不正確。

### 沒有繼承 → composition

Rust 的 trait 是「型別宣告實作什麼」，不是「基底類別有什麼子類別」：

```rust
impl Shape for Circle { ... }   // 圓形宣告「我實作 Shape」
```

C# 對應 `interface` + 型別宣告實作。差別在於 struct 沒有繼承，
所以 struct 裡的「多型」其實是 `switch` on type，不是繼承。

### 沒有 sum type → 手寫 discriminated union

Rust 的 enum 可以攜帶資料：

```rust
enum Event {
    KeyPress { key: char },
    Resize { w: u32, h: u32 },
    Close,
}
```

C# 的 `enum` 只能攜帶整數。兩種解法：

**解法 1：`sealed class` 階層**（型別安全，推薦）

```csharp
abstract record Event;
record KeyPress(char Key) : Event;
record Resize(int Width, int Height) : Event;
record Close : Event;
```

**解法 2：`struct` + enum 標記**（省配置，但欄位變成 optional）

```csharp
enum EventKind { KeyPress, Resize, Close }

readonly struct Event
{
    public EventKind Kind { get; }
    public char Key { get; }        // 只有 Kind == KeyPress 時有意義
    public int Width { get; }       // 只有 Kind == Resize 時有意義
    public int Height { get; }
}
```

解法 2 的問題：欄位是 optional 的（可能是 `default`，語意上無效），
而且 `switch` 要寫多層 case 測試。`day5/struct` 會實際跑一遍。

**結論：需要型別安全就用 `sealed class` 階層，只是要省配置才用 `struct` + 標記。**

### 整數溢位

| | Rust | C# |
| --- | --- | --- |
| debug | panic，程式立刻停 | `checked` 才檢查（預設**不**檢查） |
| release | 靜默繞回 | 預設不檢查 |

C# 可以在 csproj 開 `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>`，
或用 `checked` 關鍵字包住特定運算子。

對金額、序號、累計值來說，靜默繞回是嚴重 bug —— 那些地方務必 `checked` 或用 `decimal`。

---

## 帶走的三件事

1. **不要硬翻譯。** Rust 沒有對應機制的地方（繼承、sum type、窮舉性），
   與其繞路硬做，不如接受 C# 的做法並知道代價在哪。
2. **每個 C# 缺少的 Rust 保證，都對應一條可寫的紀律。**
   本文每節的「結論」就是那條紀律，把它貼進你的 code review checklist。
3. **Analyzer 補上編譯器沒有的部分。** `dotnet format analyzers`、
   `Roslynator`、以及 IDE 的 CA 規則（`CA2000` 會抓漏掉的 `Dispose`，
   `CA1031` 會抓 catch-all）能做到 Rust 做不到的一層保護。
   開啟 `<AnalysisMode>Recommended</AnalysisMode>` 試試。
