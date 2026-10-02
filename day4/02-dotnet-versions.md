# Day 4 — .NET 版本差異與 C# 語言版本

> 範例：[`VersionsDemo.cs`](VersionsDemo.cs)
> 跑法：`dotnet run --project day4 -- versions`

✅ 已用 .NET 10 SDK 編譯並實跑驗證過。
⚠ **版本支援期限、語言特性細節請以 Microsoft 官方文件為準。** 本文件的月份是概略描述，實際日期請查官方支援矩陣。

> 這節的實務價值：**看懂文件時不會把不同版本的說法搞混。** 很多教學是 .NET Framework 時代的，有些是 .NET 6/7 的，還有些是 .NET 9/10 的。API 名字很像但行為不同。

---

## 1. 先分清楚三個「版本」

| 名稱 | 例子 | 由誰決定 |
|---|---|---|
| **.NET 版本（TFM）** | `net10.0` | 你的 csproj |
| **C# 語言版本（LangVersion）** | `C# 14` | 通常由 TFM 推導 |
| **Runtime 版本** | `10.0.3` | 機器上實際安裝的 runtime |

**預設對應關係**：

| TFM | 預設 LangVersion |
|---|---|
| `net10.0` | C# 14 |
| `net9.0` | C# 13 |
| `net8.0` | C# 12 |
| `net7.0` | C# 11 |
| `net6.0` | C# 10 |
| `net5.0` | C# 9 |
| `netcoreapp3.1` | C# 8 |
| `net48`（.NET Framework） | C# 7.3 |

⚠ **`.NET Framework` 停在 C# 7.3。** 如果新專案用的是 `net48`（或 `net472`），**所有 C# 8 以上的新語法都不能用**：沒有 nullable reference types、沒有 `record`、沒有 top-level statements、沒有 switch 表達式。

**要判斷手上那份程式碼是新舊，先看 csproj 的 `<TargetFramework>`。**

---

## 2. 執行期 vs 編譯期

```csharp
Console.WriteLine(RuntimeInformation.FrameworkDescription);  // .NET 10.0.3
Console.WriteLine(Environment.Version);                       // 10.0.3
Console.WriteLine(RuntimeInformation.OSDescription);          // Linux ...
Console.WriteLine(Environment.ProcessorCount);
```

**TFM 是 `net8.0`，但 `Environment.Version` 可能是 `10.0.3`。** 這是正常的 —— .NET Core 3.0+ 預設允許在更新的 runtime 上執行（roll-forward）。

### 怎麼控制

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <RollForward>LatestMinor</RollForward>
</PropertyGroup>
```

| `RollForward` | 行為 |
|---|---|
| `Minor` | 預設。捲到同一個 major 的較新 minor |
| `Major` | 允許跨 major（例如 net8.0 的 app 跑在 .NET 10 上） |
| `LatestPatch` | 只捲 patch 版本 |
| `LatestMinor` | 捲到可用的最新 minor |
| `LatestMajor` | 捲到可用的最新 major |
| `Disable` | 不捲，版本不合就失敗 |

⚠ **跨 major 捲不保證相容。** 移除的 API、行為變更都會讓舊 app 壞掉。**正式部署應該把 runtime 版本釘住**，不要依賴 roll-forward。

### `global.json` 釘住 SDK

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

`rollForward` 的值和上面不同（這裡是 SDK 層級）：`patch`、`feature`、`latestPatch`、`latestFeature`、`latestMajor`、`disable`。詳見 [`00-tooling.md`](../00-tooling.md)。

---

## 3. 條件編譯

```csharp
#if NET10_0_OR_GREATER
    // .NET 10+
#elif NET9_0_OR_GREATER
    // .NET 9
#else
    // .NET 8 或更早
#endif
```

常用符號：

| 符號 | 意義 |
|---|---|
| `NET10_0_OR_GREATER` | TFM ≥ net10.0 |
| `NET` | 值的 `100*major + 10*minor`，例如 net10.0 是 `1000` |
| `NETCOREAPP` | 定義了就是 .NET Core 3.0+ |
| `NETFRAMEWORK` | 定義了就是 .NET Framework |
| `WINDOWS` / `LINUX` / `MACOS` | 目標平台 |
| `DEBUG` / `TRACE` | 組態 |

⚠ **`#if` 評估的是編譯時的 TFM，不是執行時的版本。** 這是最常見的誤解 —— `net8.0` 的程式碼在 .NET 10 上跑，`NET10_0_OR_GREATER` **仍然是未定義的**，因為編譯器只看 csproj。

### 實務用途

```csharp
#if WINDOWS
    // Windows 專用：註冊表、Service、Win32 API
#else
    // Linux/macOS 專用
#endif
```

⚠ **能用 `#if` 逃避的問題，應該先用多專案或策略模式解決。** 大量 `#if` 會讓你無法測試另一個分支的程式碼。

---

## 4. 主要語言特性一覽

### C# 8（.NET Core 3.0 / .NET Standard 2.1）

| 特性 | 說明 |
|---|---|
| Nullable reference types | `string?`、`<Nullable>enable</Nullable>` |
| `using` 宣告 | `using var stream = ...;` |
| switch 表達式 | `x switch { 1 => "one", _ => "other" }` |
| Async streams | `await foreach`、`IAsyncEnumerable<T>` |
| 預設介面方法 | 介面可以有實作 |
| 範圍索引 | `array[^1]`、`array[1..3]` |
| Static local functions | `static int F() => 1;` |
| Target-typed `new` | `List<int> a = new();` |

⚠ **async streams（`IAsyncEnumerable<T>`）是採集程式最實用的特性。** `await foreach` 讀取串流時不會把所有東西一次塞進記憶體。

### C# 9（.NET 5）

| 特性 | 說明 |
|---|---|
| `record` | 不可變資料類別，自動有 `Equals` / `GetHashCode` / `ToString` |
| `init` | 只能在物件初始化時設定的屬性 |
| `with` 表達式 | `var b = a with { Port = 502 };` |
| Target-typed `new` 強化 | `Target s = new();` |
| 頂層陳述式 | 不需要 `Program.Main` |
| 模式比對強化 | `not`、`and`、`or` 組合、`Type` 屬性模式 |
| 函式指標 | `delegate*<int, int> fp` |
| Native AOT 預覽 | 跨平台原生編譯 |

⚠ **`record` + `with` 是 API DTO 的最佳組合。** 不可變、值語意、有自動 `ToString`（除錯時省很多事）。

### C# 10（.NET 6）

| 特性 | 說明 |
|---|---|
| 檔案範圍命名空間 | `namespace Foo;` —— 省掉括號 |
| 全域 using | `global using System.Text;` |
| Lambda 明確型別 | `(int x) => x * 2` |
| `record struct` | 結構體版的 record |
| 檔案範圍 namespace + 全域 using 隱含 | 隱含 usings 從這裡開始好讀 |
| ImplicitUsings | `<ImplicitUsings>enable</ImplicitUsings>` |

⚠ **本 repo 用的 `namespace CSharpLearn.DayN;` 就是 C# 10 的寫法。** 全域 using 也是（`Program.cs` 裡的 `global using`）。

### C# 11（.NET 7）

| 特性 | 說明 |
|---|---|
| `required` 成員 | 建構時一定要設定 |
| UTF-8 字串字面值 | `u8"..."` |
| raw string literals | `"""..."""` —— 不處理跳脫，適合 JSON/XML |
| 泛型數學 | `INumber<T>`、`T.Parse` 靜態抽象成員 |
| `scoped` 關鍵字 | 編譯期檢查參數生命週期 |
| `static` 抽象介面成員 | |

⚠ **raw string literals 對工業程式很有用。** 內嵌 JSON 不用再 `\"` 和 `\\`。

```csharp
var json = """
{
    "line": "Line1",
    "tags": [
        { "name": "temp", "value": 25.5 }
    ]
}
""";
```

⚠ **泛型數學（`INumber<T>`）需要 `System.Numerics` using。** 這是寫通用數值演算法（例如 `OopDemo.cs` 的 `Averager<T>`）的基礎。

### C# 12（.NET 8）

| 特性 | 說明 |
|---|---|
| **集合表達式** | `int[] a = [1, 2, 3];` |
| **Primary constructors** | 類別和結構體都能用 |
| `ref readonly` 參數 | |
| 別名任何型別 | `using Point = (int X, int Y);` |
| 內集合的 `ref struct` | |

⚠ **集合表達式是 .NET 8 的頭條特性。** 一份 `[1, 2, 3]` 可以是 `int[]`、`List<int>`、`Span<int>`、`ImmutableArray<int>` —— 取決於目標型別。

⚠ **沒有目標型別會編譯失敗**（CS9176）。`var a = [1, 2, 3];` 不行，必須寫出型別。

⚠ **primary constructor 的參數不會自動變成屬性。** 要用就明確宣告 `public string Name { get; } = name;`，而且它**不是 readonly 語意** —— 內部程式碼仍可重新賦值。

### C# 13（.NET 9）

| 特性 | 說明 |
|---|---|
| **`params` 集合** | `params ReadOnlySpan<T>`，不配置陣列 |
| **`ref struct` 可實作介面** | `Span<T>` 終於能當 `IEnumerable<T>` 之外的東西 |
| `allows ref struct` | 泛型反約束 |
| `\e` 跳脫序列 | ANSI escape code |
| **`System.Threading.Lock`** | 型別化的鎖 |
| Partial properties | 分割屬性定義 |
| 隱式索引 | |

⚠ **`ref struct`（`Span<T>`、`stackalloc` 用的）本來不能放在 heap 上、不能當泛型型別參數、不能實作介面。** C# 13 放寬了這些限制，寫高效能程式時比以前容易。

⚠ **`Lock` 比 `object` 好的地方**：有執行緒身分檢查，`Dispose` 時若被其他執行緒持有會丟 `SynchronizationLockException`。**這是把「靜默的資料損毀」變成「明確的例外」**。

### C# 14（.NET 10）

| 特性 | 說明 |
|---|---|
| **`field` 關鍵字** | 存取自動產生的 backing field |
| **Extension members** | `extension` 區塊 |
| **`?.=` 賦值** | `obj?.Prop = value;` |
| 新的 `using` 陳述式 | 可在 using 之前放資源變數宣告 |
| 巢狀型別的延伸（部分） | |

⚠ **`field` 關鍵字**消除了「宣告一個 private 欄位只為了加驗證」的需求：

```csharp
// 舊
private string _name = "";
public string Name { get => _name; set => _name = value.Trim(); }

// 新
public string Name { get => field; set => field = value.Trim(); }
```

⚠ **`extension` 成員**讓一個 `static class` 可以對同一個型別做**多組**泛型實作（舊寫法一個 class 只能一種），而且可以放進泛型介面實作裡。語法很新，實際使用前請確認官方文件的寫法。

⚠ **`?.=`** 讓「檢查 null 再賦值」變成一句話。但**讀取仍然要用 `?.`**，`x?.Y` 的型別是 `T?`。

---

## 5. .NET 執行模型

| | .NET Framework | .NET Core / .NET 5+ |
|---|---|---|
| 執行模型 | 單一、in-process | 單一、in-process（**已收斂**） |
| 開源 | 否 | 是 |
| 跨平台 | 否 | 是 |
| 支援模式 | 跟作業系統綁定 | 版本獨立 |
| 執行環境 | 已安裝的 CLR | self-contained 或 framework-dependent |
| 目前狀態 | 維護模式 | **主力** |

⚠ **.NET Core 3.0 之後，.NET Core 和 .NET 5+ 已經合併** —— 現在講「.NET Core」通常指的就是「現代 .NET」。`netcoreapp3.1` 這個 TFM 只該用在維護舊專案。

### 部署模型

| | framework-dependent | self-contained |
|---|---|---|
| 目標機需要裝 runtime | **是** | 否 |
| 部署大小 | 小 | 大（每個平台都不同） |
| 版本控制 | 依賴機器上的 runtime | 完全自己控制 |
| 啟動速度 | 較快（JIT 只編譯用到的） | 較慢（但可用 ReadyToRun 改善） |

⚠ **self-contained 必須為每個 RID 分別發佈**（`win-x64`、`linux-arm64`…），不能跨平台共用。

### 重要取捨

⚠ **不要為了「效能」而預設 self-contained。** 啟動時間、檔案大小都是成本。**只有在你無法控制目標機的 runtime 版本時才用 self-contained**（例如出貨到客戶現場）。

---

## 6. 各版本重點差異速查

| 你會遇到的寫法 | 最低版本 | 備註 |
|---|---|---|
| `HttpClient.GetFromJsonAsync` | .NET 5 | 不支援 `WebClient` |
| `Stream.ReadExactlyAsync` | .NET 7 | Day 3 用到 |
| `Process.WaitForExitAsync` | .NET 5 | |
| `System.Threading.Lock` | .NET 9 | C# 13 |
| `Task.WaitAsync` | .NET 6 | |
| `PeriodicTimer` | .NET 6 | 取代 `System.Threading.Timer` |
| `ArgumentNullException.ThrowIfNull` | .NET 6 | 取代手動 null 檢查 |
| `Enum.GetValues<T>()` | .NET 5 | 泛型版本，不用再 `(T[])Enum.GetValues(...)` |
| `DateOnly` / `TimeOnly` | .NET 6 | |
| `KeyedCollection` | .NET 6 | |
| `IAsyncEnumerable<T>` | Core 3.0 | async streams |
| `record` | .NET 5 | C# 9 |
| 集合表達式 `[1, 2, 3]` | .NET 8 | C# 12 |
| Primary constructor | .NET 8 | C# 12 |
| `params` 集合 | .NET 9 | C# 13 |
| `field` 關鍵字 | .NET 10 | C# 14 |
| `?.=` 賦值 | .NET 10 | C# 14 |
| `extension` 成員 | .NET 10 | C# 14 |

⚠ **要確認某個 API 從哪個版本開始，最可靠的方法是查官方 API 文件頁面上的「Applies to」表格。** 那比任何第三方教學都準。

---

## 7. 怎麼查一個 API 是不是你的版本有

1. **官方 API 文件**：<https://learn.microsoft.com/dotnet/api/>
   - 每個 API 頁面下方有 **Applies to** 表格，列出適用的 .NET 版本
2. **編譯器**：直接寫，不合就會給 CS 錯誤訊息，訊息會帶上版本
3. **`Reference Assemblies` NuGet 套件**：
   ```xml
   <ItemGroup>
     <PackageReference Include="Microsoft.NETCore.App.Ref" Version="8.0.0" PrivateAssets="all" />
   </ItemGroup>
   ```
   設定後就可以對「我沒安裝的那個版本」做 API 相容性檢查
4. **`dotnet list package --vulnerable`**：檢查已知漏洞

⚠ **最可靠的是第 1 項。** 教學文章和部落格最容易過時。

---

## 速查

```csharp
// 執行期資訊
using System.Runtime.InteropServices;
RuntimeInformation.FrameworkDescription;
RuntimeInformation.OSDescription;
RuntimeInformation.ProcessArchitecture;
Environment.Version;
Environment.ProcessorCount;

// 條件編譯
#if NET10_0_OR_GREATER
#elif WINDOWS
#endif

// C# 8
using var stream = File.OpenRead(path);
int[]? maybe = null;
await foreach (var x in StreamAsync()) { }

// C# 9
record Sensor(string Name, double Value) { }
var s = new Sensor("Temp", 25.5) with { Value = 26.0 };

// C# 10
namespace Foo;                 // 檔案範圍命名空間

// C# 11
required string Name { get; init; }
var json = """{ "a": 1 }""";

// C# 12
int[] a = [1, 2, 3];
Span<int> s = [1, 2, 3];        // 不進 GC
class C(string name) { public string Name { get; } = name; }

// C# 13
void M(params ReadOnlySpan<int> values) { }
private readonly Lock _gate = new();

// C# 14
public string Name { get => field; set => field = value.Trim(); }
obj?.Prop = 42;
```

---

## 練習

1. 把 csproj 的 `<TargetFramework>` 改成 `net8.0`，重新編譯 `VersionsDemo.cs` —— 觀察 C# 13/14 的語法各自給什麼錯誤訊息。
2. 加一個 `<RollForward>Disable</RollForward>`，在只有舊 runtime 的機器上跑，觀察啟動失敗訊息。
3. 寫一個 `#if NET` 的小工具，印出目前編譯用的 TFM 常數值。
4. 加 `global.json` 釘住 SDK 版本，試著用 `rollForward: disable` 讓它拒絕啟動。
5. 把一個 `record` 參數改成 `required`，體會建構期的約束。
6. 比較 `record` 和 `class` 的 `Equals` / `GetHashCode` 行為（放進 `HashSet<T>` 測試）。
7. 讀 <https://learn.microsoft.com/dotnet/core/compatibility/> 的索引，挑一個和你工作相關的 breaking change 記下來。
