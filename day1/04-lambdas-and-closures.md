# Day 1 — Lambda 與閉包

> 範例：[`LambdaDemo.cs`](LambdaDemo.cs)
> 跑法：`dotnet run --project day1 -- lambda`

✅ 已用 .NET 10 SDK 編譯並實跑驗證過。刻意保留 CS8601 / CS8602 教學反例。

這章是專門為「已經會 C++/Python 的 lambda，但不熟 C# 的 lambda」寫的。**兩個語言的 lambda 概念很像，但 C# 有一個根本性的差異，踩到會很痛。**

---

## 0. 先給你一個座標

| | Python | C++ | C# |
| --- | --- | --- | --- |
| 語法 | `lambda x: x*2` | `[](int x){ return x*2; }` | `x => x * 2` |
| 型別 | 不需要 | 不需要 | **需要指定 delegate 型別** |
| 捕獲 | 自動，全都用 reference to cell | **要手動寫 `[&]` / `[=]`** | 自動，不用寫 |
| 捕的是值還是變數 | **變數**（cell） | 看捕獲列表 | **變數** |
| 多行 | 只能單一 expression | 可以 | 可以（用 `{ }` block） |
| 非同步 | 要 `async def` | 要 coroutine | **要回傳 `Task`** |

**最重要的一列是「型別」和「捕獲」。**

- **C++ 的人**要特別注意：C# 的 lambda **永遠是「by reference to 變數」**，行為等同 C++ 的 `[&]`。你寫不出 `[=]` 那種「值拷貝」語意。
- **Python 的人**要特別注意：C# 的閉包語意和 Python 幾乎一模一樣（都是捕獲變數本身），這點你可以放心。

```csharp
// Python
def make_counter():
    n = 0
    def inc():
        nonlocal n
        n += 1
    return inc

// C# —— 語意完全相同
var counter = 0;
void Increment() => counter++;
Increment();   // counter 變成 1
```

---

## 1. 根本差異：lambda 是 delegate

**C# 沒有「函式值」這種獨立型別。** lambda 必須依附在一個 **delegate 型別** 上。

```csharp
// 你要告訴編譯器：這是什麼形狀的函式？
Func<int, int> square = x => x * x;
```

常用的三種：

| 型別 | 形狀 | 用途 |
| --- | --- | --- |
| `Action` | `void ()` | 不回傳值 |
| `Action<T>` | `void (T)` | 不回傳值，有參數 |
| `Func<TResult>` | `TResult ()` | 回傳值 |
| `Func<T, TResult>` | `TResult (T)` | 最常用 |
| `Func<T1, T2, TResult>` | `TResult (T1, T2)` | 多參數 |

命名規則：`Func` 的最後一個泛型參數永遠是**回傳型別**，前面的都是**參數**。這個設計不好記，但一旦記住就不會搞混。

### 也可以自己宣告 delegate

```csharp
internal delegate bool TryParseIntDelegate(string text, out int result);

TryParseIntDelegate tryParse = (string text, out int value) => int.TryParse(text, out value);
```

**為什麼還要自訂 delegate？** 兩個原因：

1. 可讀性。`Func<string, int, bool>` 讀起來像「字串和整數進、bool 出」，不如 `TryParseIntDelegate` 清楚
2. **delegate 可以有 `out` / `ref` 參數，`Func` 不行。** 這是硬限制

### 不用 lambda 也可以

```csharp
Func<double, double> absolute = Math.Abs;      // 方法群組轉換
Func<string, int> length = "Modbus/TCP".Length;
```

當方法已經存在時，直接給方法名稱就行，**不用包 lambda**。這是效能和可讀性的小重點。

---

## 2. 語法細節

```csharp
Func<int, int> a = x => x * x;              // 單一參數，括號可省略
Func<int, int, int> b = (x, y) => x + y;     // 多參數，括號必須有
Func<int> c = () => 42;                      // 無參數，空括號必須有
Action d = () => Console.WriteLine("hi");    // 無回傳值
```

### 單行 vs 多行

```csharp
// 單一 expression → 直接寫
Func<int, int> square = x => x * x;

// 多行 → 用 block，裡面要 return
Func<int, string> describe = n =>
{
    var doubled = n * 2;
    return $"{n} → {doubled}";
};
```

⚠ **「block 開頭是陳述句」時，`return` 不能省。** 常見錯誤：

```csharp
Func<int, int> wrong = x => { x * 2; };     // ❌ 沒有 return
```

`x * 2` 是一個「算完就丟掉」的陳述句，編譯器會說 `not all code paths return a value`。

⚠ 這和 JavaScript 的 arrow function 不同 —— JS 的 `{ x * 2 }` 也不會自動回傳，但很多人會搞錯，C# 也是一樣不會。

### 不要在 lambda 裡塞副作用

```csharp
// ⚠ 能動，但難讀
var names = list.Where(s => { Console.WriteLine($"檢查 {s}"); return s.Length > 3; });

// ✅ 分開
var names = list.Where(s => s.Length > 3);
```

`Where` / `Select` / `FirstOrDefault` 這些 LINQ 方法的 lambda **應該是純函式**。原因是 LINQ 可能多次呼叫它（例如最佳化或延遲執行），有副作用會造成難以追蹤的行為。

---

## 3. 閉包 ★ 最重要的一節

**如果只看一節，看這節。**

### 捕獲的是「變數」，不是「值」

```csharp
var counter = 0;
void Increment() => counter++;
Increment();
Increment();

Console.WriteLine(counter);   // 2
```

lambda 改了外層的 `counter`。C# 編譯器會把 `counter` 搬到一個heap 上的「**閉包物件**」（display class），lambda 持有它的參考。

**這和 C++ 的差異**：C++ 裡 `auto f = [x] { x = 10; };` 改的是**拷貝**，外層的 `x` 不變。C# **沒有這個選項**，永遠改得到外層變數。

### ★ for 迴圈的經典坑

```csharp
var actions = new List<Action>();
for (var i = 0; i < 3; i++)
{
    actions.Add(() => Console.Write($"{i}"));
}

foreach (var a in actions) a();
```

輸出：

```
333
```

**不是 `012`。** 因為三個 lambda 捕獲的是**同一個 `i`**。迴圈結束時 `i == 3`，所以三個都印 3。

**修法：在迴圈內複製一份變數。**

```csharp
var actions = new List<Action>();
for (var i = 0; i < 3; i++)
{
    var captured = i;      // ← 每次迭代都是新的區域變數
    actions.Add(() => Console.Write($"{captured}"));
}

foreach (var a in actions) a();   // 012
```

這個坑在 C++ 裡也一樣存在（`[&]` 捕獲 `i`），所以你應該已經有印象。**但它很常被忽略，因為測試通常用不到三個以上。**

### 參數也能捕獲

```csharp
void TryMutateParameter(int value)
{
    Action mutate = () => value = 99;
    mutate();
    Console.WriteLine(value);   // 99
}
```

⚠ 這個行為在**閱讀程式時非常容易看漏** —— 看到 `Action mutate = () => value = 99;` 要立刻意識到它會改呼叫端的變數。

### 捕捉 `this` 會讓物件活著

```csharp
public Action MakeLogger()
{
    return () => Console.WriteLine($"{_name} 建立 {_created} 個");
}
```

這個 lambda 在**實例方法**裡，會隱含捕捉 `this`。**只要 lambda 還活著，`this` 就不會被 GC 回收。**

對長時間執行的採集服務來說，這是潛在的記憶體洩漏來源。`LambdaDemo.cs` 的 `TagCapture` 示範了這個情況。

---

## 4. foreach 的捕獲是安全的

```csharp
var actions = new List<Action>();
foreach (var n in new[] { 10, 20, 30 })
{
    actions.Add(() => Console.Write($"{n}"));
}

foreach (var a in actions) a();   // 10 20 30  ✅
```

`foreach` 沒有上面那個問題。**原因在語言層面：**

```csharp
// 你寫的 for
for (var i = 0; i < 3; i++) { ... }

// 編譯器改寫成（每個 scope 都是新的 i）
{
    int i = 0;
    for (; i < 3; i++) { ... }
}
```

`i` 只宣告一次，所以三個 lambda 共用它。

```csharp
// 你寫的 foreach
foreach (var n in ...) { ... }

// 編譯器改寫成
{
    foreach (int n in ...) { ... }   // 每個 iteration scope 重新宣告 n
}
```

⚠ **注意這裡的不一致**：`for` 改寫後 `i` 在迴圈外仍可見（`i == 3`），`foreach` 的 `n` 改寫後在迴圈外不可見。

⚠ 這是 **C# 5 的行為變更**。C# 5 之前 `foreach` 的捕獲和 `for` 一樣會壞。**看舊程式碼時要注意。**

`Parallel.For` 的迭代變數也是每次迭代獨立的。

---

## 5. async lambda ★ 第二容易錯的地方

**規則：非同步 lambda 的回傳型別必須是 `Task` 或 `Task<T>`。**

```csharp
// ❌ 編譯錯誤 CS0019
Func<int, int> wrong = async n => n;

// ✅
Func<int, Task<int>> right = async n =>
{
    await Task.Delay(100);
    return n;
};
```

寫的時候記得：**把 `int` 換成 `Task<int>`，把 `void` 換成 `Task`。**

### ⚠ async Action 呼叫後不會等

```csharp
Action fireAndForget = async () =>
{
    await Task.Delay(100);
    Console.WriteLine("這行很晚才印");
};

fireAndForget();
Console.WriteLine("這行馬上印");   // ← 這個先印
```

`Action` 的回傳型別是 `void`，**沒有 Task 可以等**。`fireAndForget()` 立刻返回，裡面的 `await` 還在背景跑。

**這在事件處理器上特別危險**：

```csharp
private async void Button_Click(object sender, RoutedEventArgs e)
{
    await LoadDataAsync();
}
```

`async void` 方法裡的例外**無法被呼叫端 catch**，而且應用程式可能在你 await 完成前就被關掉。**盡量避免 `async void`。**

### 修法：用 `Func<Task>`

```csharp
Func<Task> trackable = async () =>
{
    await Task.Delay(100);
    Console.WriteLine("這次呼叫端有等");
};

await trackable();   // ✅ 等得到
```

需要回傳值時用 `Func<T, Task<T>>`。

### 這個坑在 LINQ 裡會變得很隱晦

```csharp
// ⚠ 這是 IEnumerable<Task<int>>，不是 IEnumerable<int>
var results = tags.Select(async t => await ReadAsync(t));

// 拿到的是一串「還沒完成的 Task」
foreach (var task in results) Console.WriteLine(task.Result);
```

解法：

```csharp
// 1. 本來就該用 foreach
tags.ForEach(async t => await ReadAsync(t));

// 2. 或 SelectMany（記得用 SelectMany 不是 Select）
var results = await Task.WhenAll(tags.Select(t => ReadAsync(t)));
```

⚠ **`Select` + `async` 幾乎都是 bug。** 看到就停下來想一下。

---

## 6. 明確標註參數型別

```csharp
Func<int, int> inferred = x => x + 1;      // ✅ 有目標型別，可以省略

var explicitTypes = (int x, int y) => x + y;  // ✅ var 推導 → 必須明確標註

// var bad = (x, y) => x + y;               // ❌ CS0815 沒有目標型別可以推導
```

**什麼時候要寫出來：**

| 情況 | 範例 |
|---|---|
| 用 `var` | 必須寫 |
| 目標型別不明顯（工廠 API 的 `void ForEach<T>(Func<T, bool> p)`） | 建議寫 |
| 型別看起來和語意不符 | 建議寫 |
| 純粹想讓讀的人輕鬆 | 隨你 |

程式風格上沒有強制規定。**核心程式碼我傾向寫，`Select(x => x.Value)` 這種就不寫。**

### 拋棄參數

```csharp
Func<int, int, int> firstOnly = (x, _) => x;    // 第二個參數不要
```

---

## 7. static lambda（C# 9 起）

```csharp
Func<int, int> normal     = x => x * 3;         // 會被推導成「有捕捉」
Func<int, int> noCapture  = static x => x * 3;  // 明確不捕捉
```

`static` lambda **不能參照**外層的區域變數或 `this`：

```csharp
var outer = 10;
Func<int, int> tries = static x => x + outer;   // ❌ 編譯錯誤
```

**為什麼要主動加 `static`：**

1. 沒有捕獲 → 編譯器不必配置閉包物件，少一次 heap allocation
2. **能編譯 = 保證這個 lambda 沒有副作用**，意圖清楚
3. 沒有狀態 → 可以安全放進 `static readonly` 欄位快取

**判斷時機**：如果 lambda 裡面沒有任何外層變數，加 `static` 沒壞處。如果用了 `static` 編譯不過，代表你可能不小心在裡面改了不該改的東西。

---

## 8. 多播 delegate

`delegate` 可以 `+=` 疊加，呼叫時**全部都會執行**。

```csharp
Action<string> log = m => Console.WriteLine($"A: {m}");
log += m => Console.WriteLine($"B: {m}");
log += m => Console.WriteLine($"C: {m}");

log("PLC 離線");   // 三行依序印出
```

### ⚠ 三個陷阱

**1. 有回傳值時只取最後一個**

```csharp
Func<int, string> pick = n => $"A{n}";
pick += n => $"B{n}";

pick(1);   // "B1"  ← A 的結果被丟掉了
```

**多載 delegate 不要有回傳值。** 需要多個結果就用 `void`，或改用 `List` 收集。

**2. 其中一個丟例外，後面的都不執行**

```csharp
log += m => throw new InvalidOperationException();
log("x");   // B 印了，然後這裡炸掉，C 永遠不會執行
```

**3. `-=` 比對的是 delegate 實例，不是程式碼**

```csharp
Action<string> a = m => Console.WriteLine("A");
Action<string> b = m => Console.WriteLine("A");

// a 和 b 是不同的 delegate 實例 → a -= b 什麼都不會發生
```

⚠ **例外：不捕獲任何東西的 lambda 會被編譯器快取成 singleton**（沒有閉包物件 = 沒有 `target` 差异），所以那種情況下 `a -= b` **會**成功。`LambdaDemo.cs` 的 Section 8 用的是這種情況。

**這是 C# 事件的底層機制。** `event` 就是「只能 `+=` 和 `-=` 的多播 delegate」——用 `event` 宣告，外部就無法用 `=` 覆蓋整個處理程序清單。

```csharp
public event EventHandler<AlarmEventArgs> Alarm;

// 外部（class 外）
publisher.Alarm += Handler;      // ✅ 可以訂閱
publisher.Alarm = null;          // ❌ 編譯錯誤（用 event 的原因）

// 如果是 public Action<...> Alarm（沒有 event）
publisher.Alarm = null;          // ⚠ 外部可以把整個清單清掉
```

---

## 9. 變異（variance）

`Func` 和 `Action` 的參數宣告有 `in` / `out` 修飾：

```csharp
public delegate TResult Func<in T, out TResult>(T arg);
public delegate void Action<in T>(T arg);
```

| | 宣告 | 規則 | 結果 |
| --- | --- | --- | --- |
| `T` | `in` | contravariant | 能吃 `Base` 的也能吃 `Derived` |
| `TResult` | `out` | covariant | 能回 `Derived` 的才能當 `Base` 用 |

```csharp
Func<object, string> anyToString = o => o.ToString() ?? "(null)";
Func<string, string> f = anyToString;    // ✅ 吃 object 的可以吃 string
```

**規則：泛型型別參數有 `in` → 可以從 Base 指派到 Derived（安全）；`out` → 只能從 Derived 指派到 Base。**

對照 C# 泛型類別的 `in` / `out` 概念（`IEnumerable<out T>`），但 delegate 的預設是 `in T, out TResult`。

**實務上**：`Task<TResult>` 和 `ValueTask<TResult>` 都有 `out` 變異，所以 `Task<object>` 能指派給 `Task<string>`（null 時例外）。這個在 `async` 串接時有用到。

---

## 10. Expression tree

**同一個 lambda 可以是「真的會執行的程式」，也可以是「一棵描述程式的樹」。**

```csharp
// A. delegate — 會真的執行
Func<int, bool> isEven = n => n % 2 == 0;
isEven(4);   // 4 % 2 == 0 → true

// B. expression tree — 存成資料
Expression<Func<int, bool>> isEvenTree = n => n % 2 == 0;
isEvenTree.ToString();   // "n => (n % 2) == 0"
```

要真的執行才 `.Compile()`：

```csharp
var compiled = isEvenTree.Compile();
compiled(4);   // true
```

### 為什麼需要這個

**EF Core（Entity Framework）就是靠它把 LINQ 翻譯成 SQL：**

```csharp
db.Tags.Where(t => t.Name == "Temp" && t.Value > 20);
```

↓ 翻譯成

```sql
WHERE Name = 'Temp' AND Value > 20
```

**代價**：翻譯器只認得能轉成 SQL 的運算子。

| 可以 | 不可以 |
| --- | --- |
| `==` `>` `<` `&&` `\|\|` | 呼叫你自己寫的方法 |
| `t.Name.StartsWith("x")` | 迴圈、`if` 擷取變數 |
| `t.Tags.Any(x => x.Id == 1)` | 區域變數參照 |

⚠ **所以 EF 的 query 裡不能塞自訂方法。** 這是最常見的 EF 錯誤。

```csharp
// ❌ EF 不認得
db.Tags.Where(t => IsValidTag(t.Name))

// ✅ 要寫成 EF 看得懂的樣子，或先取出來在記憶體中過濾
db.Tags.Where(t => t.Value > 0 && t.Name != "")
```

`LambdaDemo.cs` 的 Section 10 會把 tree 的節點印出來，看了就會懂。

---

## 11. 效能

| 做法 | 配置次數 | 說明 |
| --- | --- | --- |
| `var f = x => x * 2;` | 1 | 無捕獲，編譯器快取成 singleton |
| `var f = static x => x * 2;` | 1 | 一樣，但明確標示意圖 |
| `var f = x => x + offset;` | 1 | 有捕獲 → **每次宣告都要配置一個閉包物件** |
| 迴圈內 `actions.Add(() => Log(i))` | N | **每次迭代都配置一個閉包物件** |
| `Func<int,int> f = Math.Abs;` | 1 | 方法群組，無捕獲 |

**實務結論：**

高頻迴圈（每秒幾萬次）避免在迴圈內建立捕獲 lambda，改成迴圈外建立 + 傳參數：

```csharp
// ⚠ 每次迭代都配置
for (var i = 0; i < 1_000_000; i++)
{
    Process(i, () => Log($"item {i}"));
}

// ✅ 迴圈外一次
var log = new Logger();
for (var i = 0; i < 1_000_000; i++)
{
    Process(i, log.Log);   // 傳方法群組，不配置
}
```

**但不要為了這個讓程式變難讀。** 除非真的在 profile 裡看到 hot path，否則先寫可讀的版本。

---

## 速查

```csharp
// 基本
Func<int, int>    a = x => x * 2;         // 有回傳
Action            b = () => Console.WriteLine("x");  // 無回傳
Func<int,int,int> c = (x, y) => x + y;     // 多參數
Func<int, string> d = x => { return $"{x}"; };  // block 要 return

// 閉包 ★
var list = new List<Action>();
for (var i = 0; i < 3; i++)
{
    var captured = i;              // ← 少了這行會印 3 3 3
    list.Add(() => Console.Write(captured));
}

// async lambda ★
Func<int, Task<int>> ok  = async n => { await Task.Delay(1); return n; };
// Func<int, int> bad = async n => n;      // ❌ 要 Task<int>

// static
Func<int, int> f = static x => x * 3;     // 不捕捉，無閉包配置

// 多播
Action<string> log = m => Log(m);
log += m => SendEmail(m);
log.GetInvocationList();

// 變異
Func<object, string> any = o => o.ToString() ?? "";
Func<string, string> str = any;            // ✅

// expression tree
Expression<Func<int, bool>> tree = n => n % 2 == 0;
var compiled = tree.Compile();
```

---

## 練習

1. 印出 `for`、`foreach`、`while` 三種迴圈捕獲變數的結果，親眼比較差異。
2. 把 `LambdaDemo.cs` 的 Section 3 改成**不修**的版本，看 `broken` 印出 `[3][3][3]`。
3. 寫一個 `MakeCounter()` 回傳 `Action`，連續呼叫觀察計數遞增（對照 Python 的 `nonlocal`）。
4. 寫一個 `MakeMultipliers(params int[] values)`，回傳一個 `int[]` 的轉換結果，體驗 `params`。
5. 寫 `Func<int, int, int>` 示範 `out` 做不到的事，改用自訂 delegate 完成。
6. 把 `async` 加到 `Select` 上，看型別是 `IEnumerable<Task<int>>` 還是 `IEnumerable<int>`。
7. 把 Section 9 的變異例子**反向**寫一次，確認編譯不過，記下錯誤訊息。
8. 用 `Expression` 手動組出 `x => x * 2 + 1`，然後 `.Compile()` 執行。
9. 對比 C++：`[=]` 和 C# 的行為差異，寫下你的結論。
10. 讀 [`day2/02-channels-and-collector.md`](../day2/02-channels-and-collector.md) 裡的 `SingleReader` / `SingleWriter`，理解那也是用靜態 lambda 最佳化。
