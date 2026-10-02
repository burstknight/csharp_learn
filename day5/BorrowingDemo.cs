using System.Runtime.InteropServices;
using System.Text;

namespace CSharpLearn.Day5;

// Day 5 第二支：借用（borrowing）。
//
// Rust 的借用規則：
//   - 任何數量「不可變借用」(&T) 可同時存在
//   - 「可變借用」(&mut T) 同時只能有一個，且不能與不可變借用並存
//   - 借用的生命週期不能超過被借用者
// 編譯器用 lifetimes 標註 + 靜態檢查強制這些規則。
//
// C# 沒有借用檢查器。對應的概念是：
//   - readonly struct      → 類似「不可變借用」：呼叫端拿到複本，無法改到原值
//   - in 參數              → 以唯讀參考傳遞，避免複製結構
//   - ref readonly 參數     → 明確的「只讀借用」，禁止在方法內改動
//   - Span<T> / ReadOnlySpan<T> → 借用一段記憶體（但限於同步、不跨 await、不存欄位）
//   - ReadOnlyMemory<T>    → 可跨 await、可存欄位，但拿到 Span 要再檢查

public static class BorrowingDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. Rust 借用檢查器擋下的典型錯誤 ---");
        Console.WriteLine();
        Console.WriteLine("  fn main() {");
        Console.WriteLine("      let mut v = vec![1, 2, 3];");
        Console.WriteLine("      let first = &v[0];   // 不可變借用");
        Console.WriteLine("      v.push(4);           // ❌ E0502：可變借用與不可變借用衝突");
        Console.WriteLine("      println!(\"{first}\");");
        Console.WriteLine("  }");
        Console.WriteLine();
        Console.WriteLine("  為什麼不行：push 可能讓 v 的記憶體重新配置，");
        Console.WriteLine("  first 就會指向已釋放的記憶體。Rust 在編譯期擋掉。");
        Console.WriteLine();
        Console.WriteLine("  C# 的 List<T> 沒有這個保證。List.Add() 可能觸發重新配置，");
        Console.WriteLine("  你手上的 Span<T> 會指向舊的緩衝區（不會立刻爆，但內容是舊的）。");
        Console.WriteLine();

        ReadonlyStructSemantics();
        InParameters();
        SpanBorrowing();
        RefStructRules();
        await BoxingAndAllocationAsync(ct);
    }

    // ── readonly struct ──────────────────────────────────────────────
    private static void ReadonlyStructSemantics()
    {
        Console.WriteLine("--- 2. readonly struct：最接近「不可變借用」的概念 ---");
        Console.WriteLine();

        var original = new Measurement(25.5, 30.0);

        // struct 是值語意：這裡是把整個結構「複製」一份給方法。
        // 方法內改 copy 不會影響 original。
        var copy = Scale(original, 2.0);
        Console.WriteLine($"  original: {original}");
        Console.WriteLine($"  copy    : {copy}   ← 方法改了 copy，不是 original");
        Console.WriteLine();

        // readonly struct：編譯器禁止你在任何方法內改它的欄位。
        // 包含唯讀欄位，所以每個方法都要防禦性複製一份。
        // 這保證了「拿到 readonly struct 的方法不可能偷偷改到原值」——
        // 這是 Rust &T 最接近的對應物。
        var safe = original.Normalized();
        Console.WriteLine($"  original.Normalized(): {safe}   ← 回傳複本，original 沒變");
        Console.WriteLine();

        Console.WriteLine("  對照 Rust:");
        Console.WriteLine("    Rust  fn f(m: &Measurement)  → 只讀借用，絕不複製");
        Console.WriteLine("    C#    readonly record struct  → 每個呼叫都複製（值語意）");
        Console.WriteLine("    C#    readonly ref            → 真正的唯讀借用，不複製");
        Console.WriteLine();
        Console.WriteLine("  ⚠ 所以 C# 的 readonly struct 是「安全但慢」，");
        Console.WriteLine("    Rust 的 &T 是「安全又快」。要又快又安全，C# 用 readonly ref。");
        Console.WriteLine();
    }

    private static Measurement Scale(Measurement value, double factor)
    {
        // 這裡的 value 是複本，改它不影響 caller
        value = new Measurement(value.Value * factor, value.Limit);
        return value;
    }

    // ── in 參數 ──────────────────────────────────────────────────────
    private static void InParameters()
    {
        Console.WriteLine("--- 3. in 參數：明確的「唯讀借用」---");
        Console.WriteLine();

        var sensor = new Measurement(25.5, 30.0);

        // 沒有 in：大 struct 每呼叫一次就複製一次（放堆疊，成本低，但大 struct 仍有意義）
        var a = AverageWithoutIn(sensor);
        Console.WriteLine($"  AverageWithoutIn: {a:F2}");

        // 有 in：傳唯讀參考，不複製
        var b = AverageWithIn(in sensor);
        Console.WriteLine($"  AverageWithIn(in): {b:F2}");
        Console.WriteLine();

        // ref readonly：跟 in 幾乎一樣，但語意更明確（方法簽章上看得出來）
        var c = AverageRefReadOnly(ref sensor);
        Console.WriteLine($"  AverageRefReadOnly(ref): {c:F2}");
        Console.WriteLine();

        Console.WriteLine("  ⚠ in / ref readonly 保證「方法內不能改它」：");
        Console.WriteLine("    嘗試在方法內 sensor.Value = 0; → 編譯錯誤 CS8331");
        Console.WriteLine("    這是 Rust &T 的直接對應物：真實的借用，不是複本。");
        Console.WriteLine();
        Console.WriteLine("  什麼時候值得用 in：");
        Console.WriteLine("    struct 很大（> 16 bytes，約 2 個 cache line）才有意義");
        Console.WriteLine("    小 struct 傳值通常比傳參考快，JIT 會做 inline");
        Console.WriteLine("  結論：先寫一般版本，除非 profile 證明有問題，否則不要加 in");
        Console.WriteLine();
    }

    private static double AverageWithoutIn(Measurement value)
    {
        return (value.Value + value.Limit) / 2.0;
    }

    private static double AverageWithIn(in Measurement value)
    {
        return (value.Value + value.Limit) / 2.0;
    }

    private static double AverageRefReadOnly(ref readonly Measurement value)
    {
        return (value.Value + value.Limit) / 2.0;
    }

    // ── Span 借用 ────────────────────────────────────────────────────
    private static void SpanBorrowing()
    {
        Console.WriteLine("--- 4. Span<T>：真的借用記憶體，零配置 ---");
        Console.WriteLine();

        // stackalloc：把緩衝區放在 stack 上，完全沒有 GC 參與
        Span<byte> buffer = stackalloc byte[8];
        buffer[0] = 0xAB;
        buffer[1] = 0xCD;
        Console.WriteLine($"  stackalloc 8 bytes: {Convert.ToHexString(buffer[..2])} ...");

        // 借用既有陣列的一段，不複製
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
        var middle = data.AsSpan(2, 3);          // 借用 data[2..5]，沒有配置
        Console.WriteLine($"  data.AsSpan(2, 3): {Convert.ToHexString(middle)}");

        // 借用字串的 UTF-8 內容 —— 這是 Rust 的 &[u8] 對應物
        var utf8Bytes = "CSharpLearn"u8;        // UTF-8 字串面值，資料在靜態區
        Console.WriteLine($"  \"CSharpLearn\"u8 的第一片: {Convert.ToHexString(utf8Bytes[..3])}");
        Console.WriteLine();

        // 對 Rust 寫法：
        //   let s = b"hello";
        //   let part = &s[1..3];   // 借用，不複製
        // C# 的 Span 做的是完全一樣的事。
        Console.WriteLine("  Rust 對照:");
        Console.WriteLine("    let s = b\"hello\";");
        Console.WriteLine("    let part = &s[1..3];        // &[u8] 借用");
        Console.WriteLine("    C#    var part = data.AsSpan(2, 3);  // Span<byte> 借用");
        Console.WriteLine();

        // ReadOnlySpan：借來只能讀，不能寫
        var ro = data.AsSpan(0, 3);
        Console.WriteLine($"  ReadOnlySpan: {Convert.ToHexString(ro)}（不可寫入）");
        Console.WriteLine();
    }

    // ── ref struct 的限制 ────────────────────────────────────────────
    private static void RefStructRules()
    {
        Console.WriteLine("--- 5. Span<T> 為什麼是 ref struct（不能存欄位）---");
        Console.WriteLine();
        Console.WriteLine("  ref struct 不能：");
        Console.WriteLine("    • 當作類別的欄位          → 生命週期會超過 stack 借用的範圍");
        Console.WriteLine("    • 跨 await / yield         → 借用的記憶體可能已經失效");
        Console.WriteLine("    • 當作泛型型別參數 T       → 泛型可能把它存進堆積");
        Console.WriteLine("    • 在 async 方法裡用（除非方法本身不是 async）");
        Console.WriteLine();
        Console.WriteLine("  這些限制正是 Rust 的 lifetimes 在做的事，只是換成編譯器錯誤。");
        Console.WriteLine("  Rust: fn parse(s: &str) -> u32  → 借用不能活得比 s 長");
        Console.WriteLine("  C#  : Span<T> 不能離開方法      → 借用的生命週期限制在方法內");
        Console.WriteLine();
        Console.WriteLine("  需要跨 await / 存欄位時，用 ReadOnlyMemory<T>：");
        Console.WriteLine("    • 它是正常的 struct，可以存欄位");
        Console.WriteLine("    • 但要 .Span 取得借用時，會在執行期檢查有沒有已被 Dispose");
        Console.WriteLine("    • 這是「執行期檢查」取代「編譯期檢查」的取捨");
        Console.WriteLine();

        // 示範 ReadOnlyMemory 可以跨 await
        var memory = new ReadOnlyMemory<byte>([0xDE, 0xAD, 0xBE, 0xEF]);
        Console.WriteLine($"  ReadOnlyMemory 可跨 await: {Convert.ToHexString(memory.Span)}");
    }

    // ── 装箱：借用不了就退而求其次 ──────────────────────────────────
    private static async Task BoxingAndAllocationAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 6. 為什麼 struct 方法常數呼叫泛型擴充方法？---");
        Console.WriteLine();
        Console.WriteLine("  原因：泛型方法要 T 不受限於 ref struct，");
        Console.WriteLine("  就必須把 struct 裝進 object（boxing），在堆積配置。");
        Console.WriteLine("  所以 measurements.Sum() 會配置，而手寫迴圈不會。");
        Console.WriteLine();

        // 手寫迴圈：零配置
        var measurements = new Measurement[4];

        for (var i = 0; i < measurements.Length; i++)
        {
            measurements[i] = new Measurement(20.0 + i, 100.0);
        }

        // 用 GC 直接量配置量，讓「boxing 是真的在堆積配置」變成可驗證的事實。
        var before = GC.GetAllocatedBytesForCurrentThread();

        var sum = 0.0;
        foreach (var m in measurements)
        {
            sum += m.Value;
        }

        var manualAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 泛型 LINQ：Measurement 是 struct → 每次迭代都要 boxing
        before = GC.GetAllocatedBytesForCurrentThread();

        var sumLinq = measurements.Sum(m => m.Value);

        var linqAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  手寫迴圈:   {sum:F1}  → 本次配置 {manualAllocated} bytes");
        Console.WriteLine($"  LINQ Sum(): {sumLinq:F1}  → 本次配置 {linqAllocated} bytes");
        Console.WriteLine();
        Console.WriteLine("  → 結果一樣，配置量差很多。差距就是每個 Measurement 裝進 object 的成本。");
        Console.WriteLine("  → 注意 Console.WriteLine 本身也會配置，所以這個數字是「上限估計」，");
        Console.WriteLine("    真正的重點是兩者的相對差異，不是絕對值。");
        Console.WriteLine();

        Console.WriteLine("  結論：");
        Console.WriteLine("    • struct 的熱路徑避免 LINQ 和 Convert.ToXxx（都會 boxing）");
        Console.WriteLine("    • 手寫迴圈直接讀欄位，JIT 可以完全 inline，零配置");
        Console.WriteLine("    • 或用 Span<T> / ref struct 讓泛型不被 boxing");
        Console.WriteLine();
        Console.WriteLine("  ⚠ 但先量再優化。LINQ 讀起來清楚得多，");
        Console.WriteLine("    除非這段真的在每秒跑幾百萬次，否則不要為了省 boxing 犧牲可讀性。");
        Console.WriteLine();

        await Task.CompletedTask;
    }
}

// ── 示範用型別 ─────────────────────────────────────────────────────────

/// <summary>
/// readonly struct：所有欄位都是唯讀。
///
/// 每個方法裡如果想把欄位指派回去，編譯器會要求你先做防禦性複製
/// （因為 this 可能是一份複本）。
/// </summary>
internal readonly record struct Measurement(double Value, double Limit)
{
    public bool IsOverLimit => Value > Limit;

    public Measurement Normalized()
    {
        // readonly 欄位在非 readonly 方法內要複製才能改。
        // 這裡直接回傳新實例 —— 呼叫端拿到複本，原始物件不動。
        return this with { Value = Math.Clamp(Value, 0, Limit) };
    }
}

/// <summary>一般 struct 示範。欄位可變，方法內修改不影響 caller（值語意）。</summary>
internal struct MutableSample
{
    public int Count;

    public void Increment() => Count++;
}
