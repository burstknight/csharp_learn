// Lambda 與閉包 —— 給有 C++ / Python 背景的人看的重點版。
//
// ⚠ 本檔案未經編譯驗證（撰寫環境沒有 .NET SDK）。
//
// 這個檔案刻意示範「會踩到的坑」，而不只是正確寫法：
//   Section3（閉包）與 Section5（async lambda）是整個 repo 裡最容易寫錯的地方。

// Expression / ExpressionType 不在 ImplicitUsings 裡。
using System.Linq.Expressions;

namespace CSharpLearn.Day1;

public static class LambdaDemo
{
    public static Task RunAsync(CancellationToken ct)
    {
        Section1_BasicSyntax();
        Section2_DelegateTypes();
        Section3_Closure();
        Section4_ForVsForeach();
        await Section5_AsyncLambda();
        Section6_ExplicitTypes();
        Section7_StaticLambda();
        Section8_Multicast();
        Section9_Variance();
        Section10_ExpressionTree();

        return Task.CompletedTask;
    }

    // ── 1. 基本語法 ──────────────────────────────────────────────────
    private static void Section1_BasicSyntax()
    {
        Console.WriteLine("--- 1. Lambda 基本語法 ---");

        // 單一參數：括號可以省略
        Func<int, int> square = x => x * x;
        Console.WriteLine($"  square(5) = {square(5)}");

        // 多個參數：括號不能省略
        Func<int, int, int> add = (a, b) => a + b;
        Console.WriteLine($"  add(3, 4) = {add(3, 4)}");

        // 沒有參數：空括號不能省略
        Func<int> answer = () => 42;
        Console.WriteLine($"  answer() = {answer()}");

        // 沒有回傳值 → 用 Action
        Action greet = () => Console.WriteLine("  → 這行由 Action 印出");
        greet();

        // 回傳值有多行 → 主體要改成 block，block 裡用 return
        Func<int, string> describe = n =>
        {
            var kind = n switch
            {
                < 0 => "負數",
                0 => "零",
                _ => "正數",
            };

            return $"{n} 是{kind}";
        };

        Console.WriteLine($"  describe(-3) = {describe(-3)}");
        Console.WriteLine();

        Console.WriteLine("  ⚠ 「block 開頭是陳述句」時不能省略 return，即使型別可以推論：");
        // 下面這行會是編譯錯誤，註解起來當對照
        // Func<int, int> wrong = x => { x * 2; };        // ❌ 沒有 return
        Func<int, int> right = x =>
        {
            var doubled = x * 2;      // 中間要做事情
            return doubled;           // ← 一定要寫 return
        };

        Console.WriteLine($"  right(21) = {right(21)}");
        Console.WriteLine();
    }

    // ── 2. Lambda 其實是 delegate ─────────────────────────────────────
    private static void Section2_DelegateTypes()
    {
        Console.WriteLine("--- 2. Lambda 其實是 delegate ---");

        // 同一個 lambda 可以指派給不同型別的 delegate
        Func<int, int> asFunc = x => x + 1;
        TransformInt asCustom = x => x + 1;
        Action<int> asAction = x => Console.WriteLine($"    Action 版收到 {x}");

        Console.WriteLine($"  asFunc(10)     = {asFunc(10)}");
        Console.WriteLine($"  asCustom(10)   = {asCustom(10)}");
        asAction(10);

        // 方法群組（method group）轉換：不需要寫 lambda
        Func<double, double> absolute = Math.Abs;
        Func<string, int> length = "Modbus/TCP".Length;
        Console.WriteLine($"  Math.Abs(-3.5) = {absolute(-3.5)}");
        Console.WriteLine($"  \"Modbus/TCP\".Length = {length()}");
        Console.WriteLine();

        // out 參數也能用 lambda
        TryParseIntDelegate tryParse = (string text, out int value) => int.TryParse(text, out value);

        if (tryParse("502", out var port))
        {
            Console.WriteLine($"  tryParse(\"502\") → {port}");
        }

        if (tryParse("不是數字", out var _))
        {
            Console.WriteLine("  這行不會印");
        }
        else
        {
            Console.WriteLine("  tryParse(\"不是數字\") → 失敗（out 參數是 null 以外的預設值）");
        }

        Console.WriteLine();

        // var 也能推導（C# 10 的 natural type）
        var doubled = (int x) => x * 2;
        Console.WriteLine($"  var doubled(10) = {doubled(10)}   ← 型別推導為 Func<int, int>");
        Console.WriteLine();
    }

    // ── 3. 閉包 ★ ────────────────────────────────────────────────────
    private static void Section3_Closure()
    {
        Console.WriteLine("--- 3. 閉包：捕獲的是「變數」，不是「值」 ---");

        // 3a. 區域變數：lambda 可以改到外層的變數
        var counter = 0;
        void Increment() => counter++;
        Increment();
        Increment();
        Console.WriteLine($"  counter = {counter}   ← 外層變數被 lambda 改到了");
        Console.WriteLine();

        // 3b. 參數也一樣（不是只有區域變數）
        void TryMutateParameter(int value)
        {
            Action mutate = () => value = 99;
            mutate();
            Console.WriteLine($"  TryMutateParameter(1) 後 value = {value}   ← 參數被改掉了");
        }

        TryMutateParameter(1);
        Console.WriteLine();

        // 3c. ★ for 迴圈：所有 lambda 共用同一個 i
        var broken = new List<Action>();
        for (var i = 0; i < 3; i++)
        {
            broken.Add(() => Console.Write($"[{i}]"));
        }

        Console.Write("  for 迴圈（不修）: ");
        broken.ForEach(a => a());
        Console.WriteLine("   ← 預期 [0][1][2]，實際 [3][3][3]");
        Console.WriteLine();

        // 修法：在迴圈內複製一份變數
        var repaired = new List<Action>();
        for (var i = 0; i < 3; i++)
        {
            var captured = i;      // ← 每次迭代都有一個新的區域變數
            repaired.Add(() => Console.Write($"[{captured}]"));
        }

        Console.Write("  for 迴圈（修好）: ");
        repaired.ForEach(a => a());
        Console.WriteLine("   ← [0][1][2]");
        Console.WriteLine();

        // 3d. 實例方法裡的 lambda 會隱含捕捉 this
        var tag = new TagCapture("Line1.MixingTank.Temp");
        var log = tag.MakeLogger();
        tag.NextId();
        tag.NextId();
        Console.WriteLine("  呼叫 lambda（捕捉了 this）：");
        log();
        Console.WriteLine("    ⚠ 這個 lambda 讓 TagCapture 實例無法被 GC 回收，");
        Console.WriteLine("      因為閉包持有對它的參考。長時間執行的採集服務要留意。");
        Console.WriteLine();
    }

    // ── 4. for vs foreach ────────────────────────────────────────────
    private static void Section4_ForVsForeach()
    {
        Console.WriteLine("--- 4. foreach 的捕獲是安全的 ---");

        var fromForEach = new List<Action>();
        foreach (var n in new[] { 10, 20, 30 })
        {
            fromForEach.Add(() => Console.Write($"[{n}]"));
        }

        Console.Write("  foreach 迴圈: ");
        fromForEach.ForEach(a => a());
        Console.WriteLine("   ← [10][20][30] 正確");
        Console.WriteLine();

        // 為什麼安全？因為 C# 5 起 foreach 的迭代變數「每次迭代都是新的變數」。
        // 編譯器把 for (var i = 0; ...) 的 i 編譯成 int i = 0;
        //                                      for (int i = 0; ...) { ... }   ← 每次迭代重新宣告
        //
        // 也就是說 for 迴圈「本來可以」安全，但 C# 語言團隊選擇維持舊語意相容，
        // 所以 for 仍然是共用同一個變數。這個差異是歷史包袱，不是設計疏忽。

        Console.WriteLine("  差異來源：");
        Console.WriteLine("    for (var i = 0; i < 3; i++)   → i 只有一個，三個 lambda 共用");
        Console.WriteLine("    foreach (var n in ...)        → n 每次迭代都是新的");
        Console.WriteLine("  ⚠ 這是 C# 5 的行為改變。寫舊的 C# 程式時 foreach 捕獲也會壞。");
        Console.WriteLine();
    }

    // ── 5. async lambda ★ ────────────────────────────────────────────
    private static async Task Section5_AsyncLambda()
    {
        Console.WriteLine("--- 5. async lambda ---");

        // ❌ 這行編譯不過（CS0019），註解起來當對照：
        //   非同步 lambda 的回傳型別必須是 Task<T> / Task，不能是 T
        // Func<int, int> wrong = async n => n;

        Func<int, Task<int>> addThenDouble = async n =>
        {
            await Task.Delay(20);
            return n * 2;
        };

        Console.WriteLine($"  await addThenDouble(21) = {await addThenDouble(21)}");
        Console.WriteLine();

        // ⚠ async Action 呼叫之後「不會等」—— 這是最容易誤會的一點
        Action fireAndForget = async () =>
        {
            await Task.Delay(100);
            Console.WriteLine("    → 這行在呼叫端早已返回之後才印出");
        };

        Console.WriteLine("  呼叫 async Action：");
        fireAndForget();
        Console.WriteLine("  ← 馬上就回來了，async Action 不會被 await");
        Console.WriteLine();

        // 修法一：把型別宣告成 Func<Task>，就拿得到 Task 可以等
        Func<Task> trackable = async () =>
        {
            await Task.Delay(100);
            Console.WriteLine("    → 這次呼叫端有等");
        };

        Console.WriteLine("  用 Func<Task> 就能 await：");
        await trackable();
        Console.WriteLine("  ← 等到上面那行印完才回來");
        Console.WriteLine();

        // 修法二：本來就要回傳值時，回傳 Task<T> 而不是 Task
        Func<int, Task<int>> withResult = async n =>
        {
            await Task.Delay(50);
            return n * 10;
        };

        Console.WriteLine($"  Func<int, Task<int>>: await withResult(3) = {await withResult(3)}");
        Console.WriteLine();

        Console.WriteLine("  ⚠ 這個坑在 LINQ 裡特別常見：");
        Console.WriteLine("    list.Select(async x => await DoAsync(x))   → 回傳 IEnumerable<Task<int>>");
        Console.WriteLine("    要嘛改用 list.ForEach(async x => await DoAsync(x))");
        Console.WriteLine("    要嘛外面包一層 SelectMany");
        Console.WriteLine();
    }

    // ── 6. 明確型別 ──────────────────────────────────────────────────
    private static void Section6_ExplicitTypes()
    {
        Console.WriteLine("--- 6. 什麼時候要明確標註參數型別 ---");

        // 有目標型別 → 可以省略
        Func<int, int> inferred = x => x + 1;
        Console.WriteLine($"  inferred(1) = {inferred(1)}");

        // 用 var → 參數型別「必須」明確標註（否則編譯器無從推導）
        var explicitTypes = (int x, int y) => x + y;
        Console.WriteLine($"  explicitTypes(2, 3) = {explicitTypes(2, 3)}");

        // 下面這行不行：
        //   var bad = (x, y) => x + y;      // ❌ CS0815 沒有目標型別可以推導
        Console.WriteLine();

        // 需要明確標註的三種情況：
        // 1. 沒有目標型別（用 var、當引數傳給沒有名稱的參數）
        // 2. 轉型時要靠明確型別決定語意
        // 3. 想要讓閱讀者不用追型別（工廠API、複雜的 LINQ）

        // 拋棄參數
        Func<int, int, int> firstOnly = (x, _) => x;
        Console.WriteLine($"  firstOnly(7, 999) = {firstOnly(7, 999)}");
        Console.WriteLine();

        // 三種寫法的比較
        Console.WriteLine("  同一件事的三種寫法：");
        Console.WriteLine("    delegate(int x) { return x * 2; }   ← 明確 delegate，冗長");
        Console.WriteLine("    x => x * 2                           ← 最常用");
        Console.WriteLine("    new Func<int, int>(x => x * 2)        ← 沒人這樣寫");
        Console.WriteLine();
    }

    // ── 7. static lambda ─────────────────────────────────────────────
    private static void Section7_StaticLambda()
    {
        Console.WriteLine("--- 7. static lambda（C# 9 起）---");

        // 普通 lambda：會被推導成「有捕捉」
        Func<int, int> normal = x => x * 3;

        // static lambda：明確宣告「不捕捉任何東西」
        Func<int, int> noCapture = static x => x * 3;

        Console.WriteLine($"  normal(5)     = {normal(5)}");
        Console.WriteLine($"  noCapture(5)  = {noCapture(5)}");
        Console.WriteLine();

        // 差別在於能不能參照外層狀態。下面兩行都編譯不過：
        // var outer = 10;
        // Func<int, int> tries = static x => x + outer;   // ❌ static lambda 不能參照區域變數
        // Func<int, int> triesThis = static x => x + this.Field;  // ❌ 也不能參照 this

        Console.WriteLine("  為什麼要寫 static：");
        Console.WriteLine("    • 沒有捕捉 → 編譯器不必配置「閉包物件」，少一次記憶體配置");
        Console.WriteLine("    • 能編譯 = 保證這個 lambda 沒有副作用，程式碼意圖清楚");
        Console.WriteLine("    • 沒有狀態 → 可以安全地被當成 static 快取欄位");
        Console.WriteLine();

        // 沒有狀態就可以放進 static 欄位
        Console.WriteLine($"  SharedDoubler(8) = {SharedDoubler(8)}   ← 存在 static 欄位，重複呼叫不會重新配置");
        Console.WriteLine();
    }

    private static readonly Func<int, int> SharedDoubler = static x => x * 2;

    // ── 8. 多播 delegate ─────────────────────────────────────────────
    private static void Section8_Multicast()
    {
        Console.WriteLine("--- 8. 多播 delegate ---");

        Action<string> log = message => Console.WriteLine($"    A: {message}");
        log += message => Console.WriteLine($"    B: {message}");
        log += message => Console.WriteLine($"    C: {message}");

        Console.WriteLine("  呼叫一次，執行全部：");
        log("PLC 離線");
        Console.WriteLine();

        Console.WriteLine($"  GetInvocationList() 長度 = {log.GetInvocationList().Length}");
        Console.WriteLine("  ⚠ 依「+= 的順序」執行，不是註冊順序以外的方式。");
        Console.WriteLine("  ⚠ 其中一個處理程序丟例外 → 後面的都不會執行。");
        Console.WriteLine();

        // 回傳型別只取「最後一個」的結果
        Func<int, string> pick = n => $"A{n}";
        pick += n => $"B{n}";

        Console.WriteLine($"  有回傳值的多播: pick(1) = {pick(1)}   ← 只看得到最後一個");
        Console.WriteLine();

        // -= 移除
        log -= message => Console.WriteLine($"    C: {message}");
        Console.WriteLine($"  -= 之後長度 = {log.GetInvocationList().Length}（比對的是同一個 delegate 實例）");
        Console.WriteLine();

        // 這就是 C# 事件的底層機制
        Console.WriteLine("  C# 的 event 就是「只能 += 和 -= 的多播 delegate」。");
        Console.WriteLine("  用 event 宣告，外部就無法覆蓋（assign）整個處理程序清單。");
        Console.WriteLine();
    }

    // ── 9. 變異（variance）────────────────────────────────────────────
    private static void Section9_Variance()
    {
        Console.WriteLine("--- 9. 變異：delegate 型別之間的隱含轉換 ---");

        // Func<in T, out TResult>
        //   T 用 in  → contravariant：能吃 Base 的，一定也能吃 Derived
        //   TResult 用 out → covariant：能回 Derived 的，才能指派給要回 Base 的
        Func<object, string> anyToString = o => o.ToString() ?? "(null)";
        Func<string, string> stringToString = anyToString;   // ✅ 成立
        Console.WriteLine("  Func<object, string> → Func<string, string>  ✅");
        Console.WriteLine();

        // 反方向不成立
        // Func<string, string> bad = stringToString;          // ❌ 會用到 string 但型別只保證 object
        Console.WriteLine("  Func<string, string> → Func<object, string>  ❌ 不行");
        Console.WriteLine();

        // Action<in T> 只有 contravariant
        Action<object> logAnything = o => Console.WriteLine($"    收到 {o}");
        Action<string> logString = logAnything;                // ✅
        Console.WriteLine("  Action<object> → Action<string>  ✅");
        Console.WriteLine();

        // ValueTask<out TResult> 也有變異（.NET 6 起）
        Console.WriteLine("  ValueTask<T> 和 Task<T> 都是 out 的變異 → Task<Base> 可指派給 Task<Derived>");
        Console.WriteLine();

        // 實務上的意義
        Console.WriteLine("  實務意義：");
        Console.WriteLine("    可以把「吃大範圍型別」的 handler 指派給「吃小範圍型別」的 delegate，");
        Console.WriteLine("    少寫一層轉接。這在 DI 註冊和事件訂閱時很常見。");
        Console.WriteLine();
    }

    // ── 10. Expression tree ───────────────────────────────────────────
    private static void Section10_ExpressionTree()
    {
        Console.WriteLine("--- 10. Expression tree（先把 lambda 當資料存）---");

        // delegate：拿到之後「真的會執行」
        Func<int, bool> isEven = n => n % 2 == 0;
        Console.WriteLine($"  delegate  isEven(4) = {isEven(4)}");

        // expression tree：拿到之後是「一棵樹」，可以分析、翻譯、改寫
        Expression<Func<int, bool>> isEvenTree = n => n % 2 == 0;
        Console.WriteLine($"  tree 的字串: {isEvenTree}");
        Console.WriteLine($"  Body 節點型別: {isEvenTree.Body.GetType().Name}");

        // 想要真的執行才 Compile
        var compiled = isEvenTree.Compile();
        Console.WriteLine($"  tree.Compile() 之後 isEvenTree(4) = {compiled(4)}");
        Console.WriteLine();

        // 走訪樹
        Walk(isEvenTree.Body, 0);
        Console.WriteLine();

        Console.WriteLine("  為什麼要這種東西？");
        Console.WriteLine("    • EF Core：把 lambda「翻譯成 SQL」送到資料庫執行");
        Console.WriteLine("      db.Tags.Where(t => t.Name == \"Temp\")  →  WHERE Name = 'Temp'");
        Console.WriteLine("    • 所以 EF 不能執行你沒辦法翻譯的 C# 程式碼");
        Console.WriteLine("      （例如呼叫無法翻譯的自訂方法）");
        Console.WriteLine("    • LINQ to Objects 用 delegate（真的跑），");
        Console.WriteLine("      LINQ to Entities/SQL 用 expression tree（翻譯後才跑）。");
        Console.WriteLine();

        Console.WriteLine("  ⚠ 教學階段先不用會寫 tree，先會讀就好。");
    }

    private static void Walk(Expression node, int depth)
    {
        if (node is not BinaryExpression binary)
        {
            return;
        }

        var pad = new string(' ', (depth + 1) * 2);
        Console.WriteLine($"{pad}{binary.NodeType}: {Describe(binary.NodeType)}");
        Walk(binary.Left, depth + 1);
        Walk(binary.Right, depth + 1);
    }

    private static string Describe(ExpressionType type) => type switch
    {
        ExpressionType.Modulo => "%（取餘數）",
        ExpressionType.Equal => "==（等於）",
        ExpressionType.Add => "+（相加）",
        ExpressionType.Multiply => "*（相乘）",
        _ => "其他",
    };
}

// ── Section 2 用的自訂 delegate 型別 ─────────────────────────────────
internal delegate int TransformInt(int value);

internal delegate bool TryParseIntDelegate(string text, out int result);

// ── Section 3 用的型別：示範 lambda 捕捉 this ────────────────────────
internal sealed class TagCapture
{
    private readonly string _name;
    private int _created;

    public TagCapture(string name)
    {
        _name = name;
    }

    public string Name => _name;

    public int NextId() => ++_created;

    /// <summary>
    /// 這個 lambda 隱含捕捉了 <c>this</c>，所以它會讓 TagCapture 實例一直活著。
    /// 長時間執行的採集服務如果到處是這種 lambda，要留意有沒有記憶體洩漏。
    /// </summary>
    public Action MakeLogger()
    {
        return () => Console.WriteLine($"    [log] {_name} 已建立 {_created} 個實例");
    }
}
