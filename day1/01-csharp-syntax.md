# Day 1 — C# 特有語法

> 範例：[`SyntaxDemo.cs`](SyntaxDemo.cs)
> 跑法：`dotnet run --project day1 -- syntax`

✅ 這支範例已用 .NET 10 SDK 編譯並實跑驗證過，而且**刻意保留了會產生編譯警告的反例**（CS8600 / CS8602）。每一處上面都有標明警告代碼的註解。看到黃色警告是預期中的，先讀再改。

對照族群是 C++ 和 Python，所以這裡不講迴圈、陣列、if — 只講**兩種語言裡沒有的東西**。

---

## 1. 值型別 vs 參考型別

這是轉 C# 最容易踩的地方。

| | struct | class |
| --- | --- | --- |
| 存放在 | 值型別：值本身 | 參考型別：物件的參考 |
| 賦值／傳參 | **複製一份** | **複製參考** |
| 預設值 | 不可為 `null` | 可為 `null` |
| 繼承 | 不能繼承其他 struct，只能實作介面 | 可以 |
| 宣告 | `struct` | `class` |

```csharp
var a = new PointValue(1, 2);
BumpStruct(a);          // 改的是副本
Console.WriteLine(a);   // 還是 (1, 2)

var b = new PointRef { X = 1, Y = 2 };
BumpClass(b);           // 改的是 b 指向的那個物件
Console.WriteLine(b);   // 變成 (99, 2)
```

**跟 C++ 的差異**：C++ 的 struct 傳參預設也拷貝，但你可以用 `&` 或引用參數。C# 沒有指標語法，要讓 method 改到呼叫端的 struct，**必須明寫 `ref`**：

```csharp
BumpStructByRef(ref a);   // 這樣才會改到 a
```

**跟 Python 的差異**：Python 沒有 struct／class 的值語意差別（物件都是參考），所以從 Python 來的人常以為「C# 也是全部都參考」 — 不對。

### 常見實務陷阱

用 struct 裝一個大的陣列欄位，然後在迴圈裡呼叫 method：

```csharp
struct Buffer { byte[] Data = new byte[1024]; }

void Process(Buffer b) { /* 沒改也沒關係 */ }

foreach (...) { Process(buffer); }   // 每次呼叫都複製整個 Buffer 參考欄位的 struct
```

這是熱路徑上的隱藏成本。**預設用 class**，只有在你確定「值語意」或「不超過 16 bytes、不含參考」時才用 struct。

---

## 2. Nullable 參考型別

C++ 有 `nullptr`、Python 有 `None`，但兩者都不會在**編譯期**告訴你「這裡可能已經是 null」。C# 補上了這塊。

```csharp
string  definite = "PLC-01";    // 宣告為「不會是 null」
string? maybe = FindTag("x");  // 宣告為「可能是 null」
```

`?` **只加在參考型別**。值型別本來就能用 `Nullable<T>`（`int?`）表示 null，語法不同但概念一致。

### 你會遇到的警告

| 代碼 | 意義 | 修法 |
| --- | --- | --- |
| `CS8600` | 把可能為 null 的值指派給不可空型別 | 宣告成 `T?` |
| `CS8602` | 對可能為 null 的參考取成員 | `if (x is not null)` 或 `x?.` |
| `CS8603` | 可能回傳 null，但回傳型別不可空 | 回傳型別加 `?` |
| `CS8604` | 把可能為 null 的參數傳給不可空參數 | 先檢查 |
| `CS8618` | 非可空欄位／屬性在結束建構式時仍為 null | 給預設值或在 ctor 賦值 |

範例裡的反例：

```csharp
string humidity = FindTag("humidity");    // ⚠ CS8600
string? humidityOk = FindTag("humidity"); // ✅ 型別說清楚

string? maybeName = FindTag("temperature");
Console.WriteLine(maybeName.Length);      // ⚠ CS8602

if (maybeName is not null)                // 修法一：型別檢查
    Console.WriteLine(maybeName.Length);

Console.WriteLine(maybeName?.Length);     // 修法二：null 傳播，結果是 int?
Console.WriteLine(maybeName?.Length ?? -1); // 修法三：補上替代值
```

### `?.` 的兩個陷阱

**陷阱 1：直接插值會印出空白**

```csharp
string? name = null;
Console.WriteLine($"名字：{name}");            // 印出「名字：」，看不出有問題
Console.WriteLine($"名字：{name ?? "未設定"}"); // 這樣才看得出來
```

**陷阱 2：連續存取要用括號**

```csharp
config?.Plc?.Station?.Name    // ✅ 任一格是 null，整個結果就是 null
config.Plc.Station.Name       // ❌ 這是第一個 null 就炸
```

### 關於 `!`（null-forgiving）

```csharp
string s = maybe!;   // 編譯器：「我保證它不是 null」
```

**盡量不要用。** 它只是關掉警告，不做任何執行期檢查。除非你**非常確定**（例如剛剛才檢查過，而且同一個 lock 區塊內不會被別人設成 null），否則掩蓋掉的警告會變成執行期的 `NullReferenceException`。

### 不要關掉 Nullable

本 repo 開著 `Nullable=enable` 是刻意的。實務上關掉它等於放棄 C# 唯一會在編譯期幫你抓 null 錯誤的機制，而你的新工作專案幾乎都開著。

---

## 3. switch 運算式與樣式比對

C# 的 pattern matching 做的事情比 C 的 `switch` 和 C++ 的 `std::variant` 配對都多：**同時比對型別和內容**。

```csharp
private static string Describe(object? value) => value switch
{
    null => "沒有值",

    int i when i > 100 => $"大整數 {i}",   // when = 額外條件
    int i => $"整數 {i}",                  // 順序有意義：先測到的先用

    string { Length: > 0 } s => $"非空字串：{s}",  // 巢狀樣式
    string => "空字串",

    double => "浮點數",

    _ => "其他型別",   // 預設分支必備（除非型別已窮舉）
};
```

### 三種位置都要記住

**a) switch 運算式**（`=>`）：有值，適合賦值或 `return`。

**b) switch 陳述句**（`case ... =>`）：有多個陳述句要執行時用。

```csharp
string level = code switch
{
    0 => "正常",
    1 => "警告",
    _ => "嚴重",
};

// 有多行邏輯時改用陳述句寫法
switch (code)
{
    case 0:
        HandleNormal();
        break;
    case 1:
        HandleWarning();
        break;
    default:
        HandleCritical();
        break;
}
```

**c) 屬性樣式**：直接測欄位／屬性。

```csharp
// 巢狀：同時要型別對 而且 Length > 0
if (value is string { Length: > 0 } s) { }

// 巢狀：Unit 要嘛是 "C" 要嘛是 "F"
if (unit is "C" or "F") { }

// var pattern：給比對結果一個新名字，型別自動推斷
if (TryRead(out var value)) { }
```

### 好用的比對形式一覽

| 形式 | 意義 |
| --- | --- |
| `x is null` / `is not null` | null 檢查（比 `== null` 好，因為不會誤用 `==` 的多載） |
| `x is T t` | 測型別並取變數 |
| `x is T` | 只測型別 |
| `string s` | 型別樣式，可省略名稱 |
| `{ Prop: v }` | 屬性樣式 |
| `> 5`、`>= 1 and <= 10` | 關聯式樣式 |
| `"A" or "B"` | 邏輯或 |
| `not null` | 邏輯非 |
| `{ Length: > 0 } s` | 巢狀：型別 + 屬性 |

---

## 4. 集合運算式（C# 12）

```csharp
// 舊寫法
List<int> oldList = new List<int> { 1, 2, 3 };
int[] oldArray = new int[] { 1, 2, 3 };

// C# 12 集合運算式
List<int> newList = [1, 2, 3];
int[] newArray = [1, 2, 3];
Span<int> span = [1, 2, 3];     // 不配置到堆積，適合熱迴圈
List<int> empty = [];

int[] extended = [.. newArray, 99, 100];   // 展開（spread）
int[][] jagged = [[1, 2], [3, 4, 5]];      // 巢狀
```

目標型別決定集合的種類，同一個 `[1, 2, 3]` 可以是陣列、`List<T>`、`Span<T>`、`ImmutableArray<T>`…

**為什麼重要**：少寫型別名稱之外，更重要的是**錯誤會被編譯器抓到**。`new int[] { 1, 2, 3 }` 給到 `string[]` 會編譯失敗，`[1, 2, 3]` 給到 `string[]` 也一樣 — 但後者不會讓你在搜尋時找到一堆 `new List<int>` 噪音。

---

## 5. record 與 with 運算式

`record` 自動產生 `Equals`、`GetHashCode`、`ToString`，以及 `with` 運算式。適合**不可變資料**。

```csharp
private sealed record TagReading(string Name, string Unit, double Value);

var temp = new TagReading("Line1.Temp", "°C", 25.5);   // target-typed new

// 內容相同就相等（class 預設是「同一個參考」才相等）
var sameAsTemp = new TagReading("Line1.Temp", "°C", 25.5);
Console.WriteLine(temp == sameAsTemp);            // True
Console.WriteLine(ReferenceEquals(temp, sameAsTemp)); // False

// with：產生新的記錄，原的不變
var alarm = temp with { Value = 85.0 };
```

### record vs class 怎麼選

| 用 record | 用 class |
| --- | --- |
| 資料為主、幾乎不變 | 有可變狀態、行為多 |
| 想用 `with` 做不可變更新 | 需要繼承體系 |
| 相等性應該看「內容」 | 相等性應該看「身分」 |

**要小心的地方**：`record` 的 `ToString()` 會印出**所有屬性**。有敏感資訊（密碼、token、連線字串）時要記得覆寫或標 `[JsonIgnore]`。

### `with` 對 class 也行

`with` 其實是「複製 + 套用初始化子」，`class` 也能用（編譯器幫你合成 clone 方法）。但 `record` 預設把每個屬性設成 `init`-only，所以更安全。

```csharp
class Cfg
{
    public string Host { get; init; } = "";   // init：只能在物件初始化或 with 裡設定
    public int Port { get; init; }
}

var a = new Cfg { Host = "10.0.0.1", Port = 502 };
var b = a with { Port = 503 };                // ✅
a.Port = 504;                                 // ❌ init 屬性不能這樣改
```

`init` 這個關鍵字很好用：從宣告就說明「這是設定、不是狀態」。

---

## 6. primary constructor、Index 與 Range

### primary constructor（C# 12）

原本只有 `record` 有，現在一般類別也能用：

```csharp
private sealed class ModbusGateway(string host, int port, byte unitId)
{
    public string Host { get; } = host;
    public int Describe() => $"{Host}:{port} (unit {unitId})";
}
```

**什麼時候適合**：小型資料持有者（DTO、設定物件、值物件）。
**什麼時候不適合**：C# 12 的 primary constructor 參數會被「捕捉」成欄位，如果你只是想省建構式，傳統寫法仍然更清楚。

### Index 與 Range

```csharp
string text = "temperature";

text[^1]     // 最後一個字元 → "y"
text[..3]    // 從頭取 3 個 → "tem"
text[6..]    // 從第 6 個到結尾 → "rature"
text[1..^1]  // 去掉頭尾 → "emperature"
```

`^1` 是 `Index` 型別（`System.Index`），`[6..]` 是 `Range` 型別（`System.Range`）。兩者都可以用在陣列、`Span<T>`、`String` 這些支援索引的型別上。

---

## 7. 字串插值與格式化

```csharp
double v = 25.5;

$"{v}"        // 25.5
$"{v:F1}"     // 25.5   (1 位小數)
$"{v,10:F2}"  // 靠右對齊，寬 10 → "     25.50"
$"{v:E3}"     // 科學記號
$"{a}-{b}"    // 巢狀插值要加 $
```

**插值字串就是 `string.Format` 的現代寫法**，可以放心用在熱路徑（編譯器產生的程式碼比 `string.Format` 還快）。

⚠ **文化差異**：`$"{v:F2}"` 的小數點符號**會跟著執行環境的文化**變。在德國語系機器上會印 `25,50`。要固定格式就用：

```csharp
v.ToString("F2", CultureInfo.InvariantCulture)
// 或整份程式強制不變文化：<InvariantGlobalization>true</InvariantGlobalization>
```

工業系統讀檔、寫檔、做協定解析時，這個坑很容易踩（`double.Parse` 同理）。

---

## 速查

```csharp
// 型別
string  s   = "text";
string? sn  = null;               // 可能為 null
int     i   = 42;
var     v   = 3.14;               // 型別由右側推斷
const   Max = 100;               // 常數，必須是編譯期已知值

// 空值處理
s?.Length           // null 傳播 → int?
s ?? "default"      // null 合併
s ??= "default"     // 為 null 才賦值
s is not null       // 推薦的 null 檢查寫法

// 集合
int[] a = [1, 2, 3];
List<string> b = ["x", "y"];
a.Length           // 陣列長度；List 用 .Count
a[^1]              // 最後一個

// 字串
$"{name}: {value:F2}"
string.Join(", ", a)
s.Split(',')
s.Trim()
s.StartsWith("Line1", StringComparison.Ordinal)   // 比對字串建議都帶 StringComparison

// 型別判斷
x is T t
x switch { ... }
x is string { Length: > 0 } s
```

---

## 練習

1. 修掉 `SyntaxDemo.cs` 裡所有標記為反例的地方，把修正寫成註解。做到 `dotnet build` 乾淨。
2. 用 `Describe` 的樣式比對加一個分支：處理 `DateTimeOffset`，顯示「今天／昨天／YYYY-MM-DD」。
3. 把 `TagReading` 改成 `record struct`（值型別版本），比較記憶體與語意差異。
4. 寫一個方法接受 `object`，用 `switch` 運算式把不同型別轉成 PLC 需要的字串格式。
