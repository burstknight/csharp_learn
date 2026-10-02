using System.Diagnostics;

namespace CSharpLearn.Day5;

// Day 5 第四支：struct。
//
// Rust 的 struct 三種：
//   struct S { }   → 欄位私有，預設不可 Copy（move 語意）
//   #[derive(Copy, Clone)] → 明確允許複製
//   衍生 → 用 trait
//
// Rust 的 enum 關鍵是：每個變體可以攜帶不同資料，而且型別系統知道所有變體。
//   enum Shape { Circle { r: f64 }, Rect { w: f64, h: f64 }, Empty }
//   match shape { Circle { r } => .., Rect { w, h } => .., Empty => .. }
//
// C# 對應：
//   - struct          → 值語意，預設可複製（每個 assignment 都複製）
//   - readonly struct → 不可變，適合安全傳遞
//   - record struct   → 自動產生 Equals/GetHashCode/ToString/with
//   - enum            → 只有底層數值，**不能**攜帶資料 → 這是最大的落差
//   - 沒有衍生        → 用 composition（包含）代替
//   - 沒有 sum type   → 用 class + 抽象方法，或 discriminated union 的手寫版

public static class StructDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. struct 的值語意：每次賦值都複製 ---");
        Console.WriteLine();

        var a = new Point(3, 4);
        var b = a;               // 複製，不是「同一個」

        b.X = 99;

        Console.WriteLine($"  a = {a}   （沒被影響）");
        Console.WriteLine($"  b = {b}   （只有 b 改了）");
        Console.WriteLine();
        Console.WriteLine("  Rust 的 struct 也是值語意，但 Rust **不會自動複製**：");
        Console.WriteLine("    let a = Point { x: 3, y: 4 };");
        Console.WriteLine("    let b = a;              // 編譯錯誤：move");
        Console.WriteLine("    let b = a;              // 要明確寫 a.clone()");
        Console.WriteLine();
        Console.WriteLine("  → C# 的隱式複製對 struct 很方便，但隱藏了成本：");
        Console.WriteLine("    struct 越大，每次傳參/賦值的複製成本越高。");
        Console.WriteLine("    一個 1MB 的 struct 當欄位，每次 loop 都會複製 1MB。");
        Console.WriteLine();

        Console.WriteLine("--- 2. struct 不能有循環參考（CS0523）---");
        Console.WriteLine();

        // Rust 的 struct 可以指向自己：
        //   struct Node { value: i32, next: Option<Box<Node>> }   // Box 是 heap 指標
        //   → 連結串表、樹、圖都能用 struct 表達。
        //
        // C# 的 struct 是**值語意**：複製時要把所有欄位逐一複製。
        // 如果欄位又是同型別的 struct，就會無限遞迴。
        // 編譯器直接擋下來，錯誤是 CS0523：
        //   「類型為 'BadStruct?' 的結構成員 'BadStruct.Self'，在結構配置中造成循環」
        //
        // 對照：class 版本的連結串表完全合法（參考不會被遞迴複製）。

        var bad = new BadStruct { Name = "test" };
        Console.WriteLine($"  struct 可以有「自己」的參考欄位（只是不能形成型別循環）: {bad.Name}");

        // class 版本：Rust 的 Box<Node> 的直接對應物
        var head = new Node(1);
        head.Next = new Node(2);
        head.Next.Next = new Node(3);

        var sum = 0;
        for (var node = head; node is not null; node = node.Next)
        {
            sum += node.Value;
        }

        Console.WriteLine($"  class 連結串表: 1 -> 2 -> 3，總和 = {sum}   ← Rust 的 Box<Node>");
        Console.WriteLine();
        Console.WriteLine("  為什麼 class 可以、struct 不行：");
        Console.WriteLine("    class 欄位存的是參考（8 bytes 指標），複製 struct 時只複製指標，");
        Console.WriteLine("    指向的物件不動 → 沒有遞迴。");
        Console.WriteLine("    struct 欄位存的是「值」，複製時要把值整份搬過去 → 會遞迴。");
        Console.WriteLine();
        Console.WriteLine("  對策: 需要自我參考的資料結構（樹、圖、連結串表）一律用 class；");
        Console.WriteLine("        struct 只放沒有自我參考、沒有迴圈的固定大小資料。");
        Console.WriteLine();

        await RecordStructAsync(ct);
        await CompositionInsteadOfInheritanceAsync(ct);
        OverflowSafety(ct);
        await EnumsCantCarryDataAsync(ct);
    }

    // ── record struct ────────────────────────────────────────────────
    private static async Task RecordStructAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 3. readonly record struct：值語意 + 自動相等比較 ---");
        Console.WriteLine();

        var p1 = new Coord(3.0, 4.0);
        var p2 = new Coord(3.0, 4.0);

        // 一般 struct 用 == 會編譯錯誤（沒有定義 ==），record struct 自動有
        Console.WriteLine($"  p1 == p2 → {p1 == p2}  （record struct 自動產生 == ）");
        Console.WriteLine($"  p1.Equals(p2) → {p1.Equals(p2)}");
        Console.WriteLine();

        // with 運算子：不可變地修改
        var moved = p1 with { X = 10.0 };
        Console.WriteLine($"  p1 with {{ X = 10 }} → {moved}");
        Console.WriteLine($"  p1 本身 → {p1}   （with 不改原值）");
        Console.WriteLine();

        // 自動 ToString
        Console.WriteLine($"  ToString() → {p1}");
        Console.WriteLine();

        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    #[derive(Debug, Clone, Copy, PartialEq)]");
        Console.WriteLine("    struct Coord { x: f64, y: f64 }");
        Console.WriteLine();
        Console.WriteLine("  derive(PartialEq) → C# record struct 自動產生");
        Console.WriteLine("  derive(Clone, Copy) → C# struct 本來就會複製，不需要");
        Console.WriteLine("  derive(Debug) → C# record struct 自動產生 ToString()");
        Console.WriteLine("  struct update syntax (S { f: v, ..old }) → C# with { F = v }");
        Console.WriteLine();

        // 雜湊
        var set = new HashSet<Coord> { p1, p2 };
        Console.WriteLine($"  HashSet 裡去重後有 {set.Count} 個元素（p1 和 p2 相等）");
        Console.WriteLine();

        // 普通 struct 沒有 == ，但有繼承自 ValueType.Equals
        var q1 = new RawPoint(1, 2);
        var q2 = new RawPoint(1, 2);

        Console.WriteLine("  普通 struct:");
        Console.WriteLine("    var q1 = new RawPoint(1, 2);");
        Console.WriteLine("    var q2 = new RawPoint(1, 2);");
        Console.WriteLine("    q1 == q2  → 編譯錯誤（沒有定義 == ）");
        Console.WriteLine($"    q1.Equals(q2) → {q1.Equals(q2)}  （ValueType.Equals 慢且要反射）");
        Console.WriteLine($"    q1.GetHashCode() == q2.GetHashCode() → {q1.GetHashCode() == q2.GetHashCode()}");
        Console.WriteLine();
        Console.WriteLine("  → 值語意 + 需要當 dictionary key → 用 record struct，不是 struct。");
        Console.WriteLine("    record struct 會自己產生實值型別的 GetHashCode，不用反射。");
        Console.WriteLine();

        await Task.CompletedTask;
    }

    // ── composition 取代繼承 ─────────────────────────────────────────
    private static async Task CompositionInsteadOfInheritanceAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 4. 沒有衍生：用 composition ---");
        Console.WriteLine();
        Console.WriteLine("  Rust 的 trait: Shape 有 impl Shape for Circle");
        Console.WriteLine("    → 圓形宣告「我實作 Shape」，不是「Shape 有子類別 Circle」");
        Console.WriteLine("    → 可以為同一個既有型別加 impl（orphan rule 限制除外）");
        Console.WriteLine("    → 沒有繼承鏈，沒有 vtable");
        Console.WriteLine();
        Console.WriteLine("  C# 對應:");
        Console.WriteLine("    • interface  ≈ trait（宣告行為，型別宣告實作）");
        Console.WriteLine("    • 沒有 struct 的繼承 → 只能 class 繼承，或用 struct 包含 struct");
        Console.WriteLine("    • 預設 dispatch 是靜態的（除非宣告為 virtual/abstract）");
        Console.WriteLine("    • → struct 裡的「多型」其實是切換（switch on type），不是繼承");
        Console.WriteLine();

        // Rust 的 enum sum type vs C# 的 class 繼承
        Shape[] shapes = [new Circle(2.0), new Rect(3.0, 4.0)];

        foreach (var shape in shapes)
        {
            // C# 用 pattern matching 模擬 Rust 的 match
            var area = shape switch
            {
                Circle c => Math.PI * c.R * c.R,
                Rect r => r.W * r.H,
                _ => throw new UnreachableException(),
            };

            Console.WriteLine($"    {shape,-16} area = {area:F2}");
        }

        Console.WriteLine();
        Console.WriteLine("  這個 switch expression 就是 Rust enum match 的翻譯。");
        Console.WriteLine("  差別：C# 漏一個 case 會編譯通過（_ 分支吃掉），");
        Console.WriteLine("        Rust 漏一個變體會編譯錯誤。");
        Console.WriteLine();
        Console.WriteLine("  → Rust 的「編譯期窮舉性」是超能力，C# 沒有對應的語言機制。");
        Console.WriteLine("    只能靠：程式碼審查 + 測試 + 記得每次加變體回來補 case。");
        Console.WriteLine();

        await Task.CompletedTask;
    }

    // ── 溢位安全 ────────────────────────────────────────────────────
    private static void OverflowSafety(CancellationToken ct)
    {
        Console.WriteLine("--- 5. 整數溢位：Rust panic vs C# 預設不檢查 ---");
        Console.WriteLine();

        // Rust debug: panic（overflow checks on）
        // Rust release: wrap silently（跟你想要的通常不同）
        // C# unchecked (預設): wrap silently，永遠
        //
        // 這裡看到 unchecked 的結果：int.MaxValue + 1 變成 int.MinValue
        int max = int.MaxValue;
        var wrapped = max + 1;    // unchecked by default
        Console.WriteLine($"  unchecked: int.MaxValue + 1 = {wrapped}   （繞回負數了）");
        Console.WriteLine();

        // checked 開啟溢位檢查
        try
        {
            var checkedResult = checked(max + 1);
            Console.WriteLine($"  checked: {checkedResult}");
        }
        catch (OverflowException)
        {
            Console.WriteLine("  checked: int.MaxValue + 1 → OverflowException");
        }

        Console.WriteLine();
        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    debug:  overflow → panic，程式立刻停");
        Console.WriteLine("    release: overflow → wrap silently（跟 unchecked 一樣）");
        Console.WriteLine("    +#![deny(overflow_checks)] 在 release 也檢查");
        Console.WriteLine();
        Console.WriteLine("  → C# 可以用 <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>");
        Console.WriteLine("    專案層級開啟，跟 Rust debug 模式行為一致。");
        Console.WriteLine("    或在特定運算子加 checked 關鍵字。");
        Console.WriteLine();
        Console.WriteLine("  ⚠ unchecked 預設關閉是歷史包袱（效能考量），");
        Console.WriteLine("    但對金額、序號、累計值來說，静默繞回是嚴重 bug。");
        Console.WriteLine("    那些地方務必用 checked 或用 decimal。");
        Console.WriteLine();
    }

    // ── enum 不能攜帶資料 ───────────────────────────────────────────
    private static async Task EnumsCantCarryDataAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 6. Rust enum 可以攜帶資料，C# enum 不行 ---");
        Console.WriteLine();
        Console.WriteLine("  Rust:");
        Console.WriteLine("    enum Event {");
        Console.WriteLine("        KeyPress { key: char },");
        Console.WriteLine("        Resize { w: u32, h: u32 },");
        Console.WriteLine("        Close,");
        Console.WriteLine("    }");
        Console.WriteLine("    // 每個變體可以攜帶完全不同的資料");
        Console.WriteLine("    // match 可以解構：KeyPress { key } => ...");
        Console.WriteLine();
        Console.WriteLine("  C# enum:");
        Console.WriteLine("    enum EventKind { KeyPress, Resize, Close }");
        Console.WriteLine("    // 只能攜帶 int，沒有地方放 char / w / h");
        Console.WriteLine();
        Console.WriteLine("  C# 的三種解法:");
        Console.WriteLine("    1. 用 class + 子類別（每個變體一個類別，繼承基底）");
        Console.WriteLine("    2. 用 record struct + 巢狀型別（每個變體一個 record）");
        Console.WriteLine("    3. 用帶欄位的 struct + enum 標記（手寫 discriminated union）");
        Console.WriteLine();

        // 解法 2：record struct + enum 標記
        Event[] events =
        [
            new Event(EventKind.KeyPress, key: 'A'),
            new Event(EventKind.Resize, width: 800, height: 600),
            new Event(EventKind.Close),
        ];

        foreach (var e in events)
        {
            var description = e switch
            {
                { Kind: EventKind.KeyPress } => $"按下按鍵: {e.Key}",
                { Kind: EventKind.Resize } => $"視窗大小: {e.Width}x{e.Height}",
                { Kind: EventKind.Close } => "關閉視窗",
                _ => throw new UnreachableException(),
            };

            Console.WriteLine($"    {description}");
        }

        Console.WriteLine();
        Console.WriteLine("  這是 C# 版的 sum type，缺點:");
        Console.WriteLine("    • 欄位是 optional 的（可能是 default，語意上無效）");
        Console.WriteLine("    • switch 要手寫多層 case 測試");
        Console.WriteLine("    • 編譯器不保證處理完整");
        Console.WriteLine();
        Console.WriteLine("  結論：C# 沒有 Rust 的 enum-with-data。");
        Console.WriteLine("  需要型別安全就用 sealed class 階層；");
        Console.WriteLine("  只是要節省配置才用 struct + 標記。");
        Console.WriteLine();

        await Task.CompletedTask;
    }
}

// ── 示範用型別 ─────────────────────────────────────────────────────────

internal struct Point
{
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; set; }

    public int Y { get; set; }

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>一般 struct 沒有 == 定義。record struct 才有。</summary>
internal struct RawPoint
{
    public RawPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; set; }

    public int Y { get; set; }
}

/// <summary>record struct：自動產生 ==、Equals、GetHashCode、ToString、with。</summary>
internal readonly record struct Coord(double X, double Y);

/// <summary>手寫的 discriminated union 示範。</summary>
internal enum EventKind
{
    KeyPress,
    Resize,
    Close,
}

internal readonly struct Event
{
    public Event(EventKind kind, char key = default, int width = 0, int height = 0)
    {
        Kind = kind;
        Key = key;
        Width = width;
        Height = height;
    }

    public EventKind Kind { get; }

    public char Key { get; }

    public int Width { get; }

    public int Height { get; }
}

/// <summary>
/// 示範：struct 的欄位可以是自己型別的參考（class），但不能是自己的 struct。
///
/// 把下面的 Self 改成 BadStruct? 會得到編譯錯誤 CS0523：
///   「在結構配置中造成循環」
/// </summary>
internal struct BadStruct
{
    public string Name { get; set; }
}

/// <summary>Rust 的 Box&lt;Node&gt; 的直接對應物：自我參考的資料結構用 class。</summary>
internal sealed class Node(int value)
{
    public int Value { get; } = value;

    public Node? Next { get; set; }
}

// ── Shape 階層（composition / 繼承對照）─────────────────────────────

internal abstract class Shape
{
    public abstract double Area { get; }
}

internal sealed class Circle : Shape
{
    public Circle(double r) => R = r;

    public double R { get; }

    public override double Area => Math.PI * R * R;

    public override string ToString() => $"Circle(r={R})";
}

internal sealed class Rect : Shape
{
    public Rect(double w, double h)
    {
        W = w;
        H = h;
    }

    public double W { get; }

    public double H { get; }

    public override double Area => W * H;

    public override string ToString() => $"Rect({W}x{H})";
}
