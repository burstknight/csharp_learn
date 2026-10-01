# Day 1 — 物件導向

> 範例：[`OopDemo.cs`](OopDemo.cs)
> 跑法：`dotnet run --project day1 -- oop`

你用 C++ 已經會 OOP，所以這裡**不複習**封裝、繼承、多型的定義。只講 C# 特有的設計選擇，以及實務上做決策的準則。

---

## 1. 介面 vs 抽象類別

C++ 沒有介面，所以這是最需要建立直覺的部分。

C# 的 `interface` **可以**有預設實作（要有主體的成員）：

```csharp
internal interface ISensor
{
    string Name { get; }
    string Unit { get; }
    Task<double> ReadAsync(CancellationToken ct);
}
```

### 判斷準則

| 情況 | 選哪個 |
| --- | --- |
| 不同品牌／協定**沒有**任何共用實作 | `interface` |
| 有一堆共用欄位、共用實作、需要繼承 | `abstract class` |
| 型別可能被外部提供、你要消費它 | `interface`（依賴抽象，不要依賴實作） |
| 想要限制繼承層級、避免誤用 | `sealed` / `abstract` 標清楚 |

範例裡刻意兩種都放：

```csharp
internal abstract class SensorBase(string name, string unit) : ISensor
{
    public string Name { get; } = name;      // 共用實作
    public string Unit { get; } = unit;      // 共用實作
    public abstract Task<double> ReadAsync(CancellationToken ct);
}

internal sealed class SimulatedSensor(...) : SensorBase(name, unit) { ... }
internal sealed class ModbusSensor(...)    : SensorBase(name, unit) { ... }
```

`SensorBase` 提供了名稱／單位的處理，這是**真的有東西可以共用**的時候 — 這正是抽象類別的價值。

### 為什麼 `sealed` 重要

```csharp
internal sealed class SimulatedSensor { }   // 別人不能繼承，設計不會被破壞
```

C# 預設讓你繼承，很多團隊忘了加 `sealed`。`sealed` 的好處：

1. 編譯器知道沒有子類別 → **可能最佳化**（例如去虛擬化）
2. 明確表達意圖：「這不打算被繼承」
3. 避免有人亂繼承造成維護地獄

新寫的類別，除非你真的要讓人繼承，否則加 `sealed`。

---

## 2. 多型在實務上的價值

```csharp
ISensor[] sensors =
[
    new SimulatedSensor("Line1.Temp", "°C", 25.0, 0.5),
    new SimulatedSensor("Line1.Humidity", "%", 60.0, 2.0),
    new ModbusSensor("Line2.Pressure", "bar", address: 0x0000),
];

foreach (var sensor in sensors)
{
    var value = await sensor.ReadAsync(ct);   // 呼叫端完全不知道背後是什麼協定
}
```

**這就是重點**：把「模擬」換成「真的 Modbus」、或換成「MQTT 訂閱」，`foreach` 那一行一行都不用改。

### 錯誤隔離

採集系統裡，**通訊失敗是日常，不是例外狀況**。所以讀值一定要包起來：

```csharp
foreach (var sensor in sensors)
{
    try
    {
        var value = await sensor.ReadAsync(ct);
        Console.WriteLine($"{sensor.Name} = {value:F2} {sensor.Unit}");
    }
    catch (Exception ex) when (ex is NotSupportedException or IOException)
    {
        // 單一裝置壞掉，不該讓整個採集迴圈停擺
        Console.WriteLine($"{sensor.Name} 讀取失敗：{ex.Message}");
    }
}
```

`when` 子句（exception filter）可以只攔特定例外，比 `catch (Exception)` 安全。注意**不要寫空的 `catch`** — 那會把 `OperationCanceledException` 也吞掉，讓取消機制失效（見 day2）。

---

## 3. 泛型與 where 限制

### 基本泛型

跟 C++ template 觀念接近，但 C# 的泛型是**階層式**的：泛型型別只在「型別」層級泛用，`new()` 限制才開啟「值型別層級」的泛用。

```csharp
internal sealed class Averager<T>
{
    private readonly List<T> _samples = [];
    public void Add(T sample) => _samples.Add(sample);
}
```

### where 限制：讓編譯器知道你限制什麼

沒有 `where` 的話，`T` 是完全未知的，你不能對它做算術：

```csharp
internal sealed class Averager<T> where T : INumber<T>
{
    public T Average()
    {
        var sum = T.Zero;
        foreach (var sample in _samples) sum += sample;
        return sum / T.FromInt32(_samples.Count);
    }
}
```

`INumber<T>` 是 .NET 7 引入的**泛型數學**介面，把所有數值型別（`int`、`double`、`decimal`…）的 `+`、`-`、`*`、`/`、`CompareTo` 收進同一個介面。稱為「**靜態抽象成員**」（static abstract members，C# 11）。

**這是 C# 比 C++ 強的地方**。C++ 沒有這個機制，你得寫 template + SFINAE，麻煩又難讀。

### 限制的代價與檢查時機

```csharp
var invalid = new Averager<string>();   // ❌ 編譯期就失敗，不是執行期
```

**這是泛型限制最大的價值**：錯誤在編譯期被抓到，不是執行期。設計 API 時寧可多加限制。

常用限制：

| 限制 | 意義 |
| --- | --- |
| `where T : class` | T 必須是參考型別 |
| `where T : struct` | T 必須是非 null 的值型別 |
| `where T : new()` | T 必須有无參數建構式 |
| `where T : SomeBase` | 繼承限制 |
| `where T : ISomeInterface` | 介面限制 |
| `where T : notnull` | T 不能是 null |
| `where T : unmanaged` | T 不能含參考型別欄位（`unsafe` 互操作常用） |

多個限制用逗號接：`where T : class, IDisposable, new()`

---

## 4. IDisposable 與 using

### 什麼時候需要實作

持有** unmanaged 資源**的類別一定要實作 `IDisposable`：

- 檔案句柄（`FileStream`、`StreamWriter`）
- 網路連線（`Socket`、`TcpClient`、DB connection）
- GDI handle、COM 物件
- 任何 `AllocHGlobal`

純 managed 的記憶體（`new` 出來的物件）**不用**手動釋放，GC 會收。

### `using` 就是 `try/finally`

```csharp
using var writer = new CsvFileWriter(path);
writer.WriteLine("name,unit");
// 離開這個 scope（包含丟例外的情況）時，Dispose 保證被呼叫
```

等價於：

```csharp
var writer = new CsvFileWriter(path);
try
{
    writer.WriteLine("name,unit");
}
finally
{
    writer.Dispose();
}
```

### Dispose 可能被呼叫多次

這不是可選的防禦，是**必要**的：

```csharp
public void Dispose()
{
    if (_disposed) return;    // ← 必須有
    _disposed = true;
    _writer.Dispose();
}
```

理由：別人可能用 `using` 之外又手動呼叫；`AsyncServiceProvider`、DI 容器、ASP.NET 中介層在特定時機也會再呼叫一次。`Dispose` **必須**可以安全重複呼叫。

### 同步 vs 非同步釋放

```csharp
public void Dispose() { }                    // IDisposable
public ValueTask DisposeAsync() { }         // IAsyncDisposable（.NET Core 3.0+）

await using var conn = await OpenAsync();   // 非同步釋放要寫 await using
```

有非同步清理（如 flush 網路、寫資料庫）時實作 `IAsyncDisposable`。

---

## 5. static vs 實例成員

```csharp
internal static class TagNaming
{
    private static int _created;
    public static int Created => _created;
    public static string Next(string device) => $"{device}/TAG{++_created:D4}";
}
```

`static` 成員屬於**型別**，不屬於任何物件。判斷準則：

| 用 static | 用實例 |
| --- | --- |
| 不依賴任何物件狀態 | 依賴物件狀態 |
| 全域唯一的東西（設定、計數器、factory） | 每個物件各自一份 |
| 工具方法、擴充方法的容器 | 資料持有者 |
| 建構子本身就是 static 的類別 | 有多個建構子的型別 |

**`static` 欄位在多執行緒下不安全**。`++_created` 是「讀—加—寫」三步，不是原子操作。真的需要跨執行緒共用計數器時，用 `Interlocked.Increment`（見 [`day4/01-threading.md`](../day4/01-threading.md)）。

---

## 6. 唯讀集合與 iterator

### 用 `IReadOnlyList<T>` 當公開型別

```csharp
internal sealed class SensorHub
{
    private readonly IReadOnlyList<ISensor> _sensors;
    public SensorHub(IReadOnlyList<ISensor> sensors) => _sensors = sensors;
    public int Count => _sensors.Count;
}
```

**公開欄位／屬性的型別要是「最小權限的那個」**。公開 `List<ISensor>` 會讓外部 `hub.Sensors.Add(...)` 改壞內部狀態。

進階做法是用 `ImmutableArray<T>`，從型別層級就完全不可變（要 `using System.Collections.Immutable;`）。

### `yield return` 延遲求值

```csharp
public IEnumerable<string> Names
{
    get
    {
        foreach (var sensor in _sensors)
        {
            yield return sensor.Name;
        }
    }
}
```

`yield return` 讓方法變成 **iterator**：呼叫時**不執行**任何東西，`MoveNext()` 每呼叫一次才往下走一步。

**好處**：串接多個 LINQ 操作時不會產生中間集合。資料量小或鏈很長時差異很明顯。

**陷阱**：`yield return` 的方法如果回傳 `List<T>` 就會出錯（必須是 `IEnumerable<T>`、`IList<T>` 等介面），而且每次呼叫 `GetEnumerator()` 都是**新的** enumerator — 把它存成欄位重複使用會出問題。

---

## 7. 屬性（property）vs 欄位

C# 沒有裸的 public 欄位慣例，一律用屬性：

```csharp
// ❌ 不要
public string Name;

// ✅
public string Name { get; init; } = "";                  // 只能在初始化時設定
public int Port { get; private set; }                    // 外部讀得到，內部才寫
public double Cached => _expensiveComputation;           // 計算屬性，不配置記憶體
public string? Description { get; set; }                // 可空
public string Required { get; set; } = "";               // ⚠ 這裡會有 CS8618 風險
```

`init` 存取子（C# 9）讓「設定」和「狀態」在型別上分開 — 從宣告就說明這是設定、不是狀態。要用它就得給預設值或用 required，否則 `CS8618`。

---

## 速查

```csharp
sealed class Foo : IFoo                     // 不給人繼承
abstract class Base { public abstract void M(); }  // 需要被繼承但不能直接用
interface IFoo { void M(); }                // 只有契約

private readonly List<T> _items = [];       // readonly 參考：參考不能換，內容可以改
var invalid = new Averager<string>();       // where 限制：編譯期就擋掉

using var resource = new StreamWriter(p);   // 保證釋放
void Dispose() { if (_disposed) return; _disposed = true; }

public string Name { get; init; } = "";     // init：只能在初始化設
public int Port { get; private set; }

IEnumerable<T> Lazy() { yield return x; }   // 延遲求值
```

---

## 練習

1. 把 `ModbusSensor.ReadAsync` 換成真的能讀值的版本（提示：[`day3/02-modbus-tcp.md`](../day3/02-modbus-tcp.md)）。
2. 新增一個 `MqttSensor`，實作 `ISensor`，用 MQTT 訂閱取得值。驗證多型：呼叫端一行都不用改。
3. 為 `SensorBase` 加一個共用實作：連續讀取失敗 3 次就標記為 `Unhealthy`，並讓 `SensorHub` 能查詢。
4. 把 `Averager<T>` 的限制從 `INumber<T>` 拿掉，改用 `dynamic`，觀察編譯器有什麼反應，再體會一次 `where` 的價值。
5. 把 `SensorHub` 的 `_sensors` 改成 `ImmutableArray<ISensor>`。
