// ImplicitUsings 涵蓋 System / System.Collections.Generic / System.Linq。
// System.Numerics（泛型數學的 INumber<T>）不在隱含清單裡，要自己加。
using System.Numerics;

namespace CSharpLearn.Day1;

// Day 1 的第二支範例：物件導向。
// 重點不是「類別怎麼寫」（你用 C++ 已經會了），而是 C# 特有的幾個設計選擇：
// 介面有預設實作、泛型可以限制在數學型別、以及 IDisposable 的正確用法。

public static class OopDemo
{
    public static async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("--- 1. 同一個抽象底下有多種協定實作 ---");

        ISensor[] sensors =
        [
            new SimulatedSensor("Line1.Temp", "°C", 25.0, 0.5),
            new SimulatedSensor("Line1.Humidity", "%", 60.0, 2.0),
            new ModbusSensor("Line2.Pressure", "bar", address: 0x0000),
        ];

        // 呼叫端只認得 ISensor，完全不需要知道背後是模擬值還是 Modbus。
        // 這是多型最實際的用途：替換實作，呼叫端一行都不用改。
        foreach (var sensor in sensors)
        {
            Console.WriteLine($"  {sensor,-18} 單位 {sensor.Unit}");
        }

        Console.WriteLine();

        // 實際讀值的迴圈：try/catch 包起來，單一裝置故障不該讓整個採集迴圈中斷。
        // 這在 PLC 採集系統是硬需求——通訊失敗是日常，不是例外狀況。
        foreach (var sensor in sensors)
        {
            try
            {
                var value = await sensor.ReadAsync(ct);
                Console.WriteLine($"  {sensor.Name,-18} = {value,8:F2} {sensor.Unit}");
            }
            catch (Exception ex) when (ex is NotSupportedException or IOException)
            {
                Console.WriteLine($"  {sensor.Name,-18} ! 讀取失敗：{ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- 2. 泛型 + where 限制 ---");

        // where T : INumber<T> 讓編譯器知道 T 支援 +, / 等運算子，
        // 所以 Averager<double> 和 Averager<int> 都能共用同一份程式碼。
        var doubles = new Averager<double>();
        foreach (var sensor in sensors)
        {
            if (sensor is not SimulatedSensor simulated)
            {
                continue;
            }

            doubles.Add(await simulated.ReadAsync(ct));
        }

        Console.WriteLine($"  double 平均 = {doubles.Average():F2}");

        // 注意：這裡「不能」寫成 new Averager<int> { 10, 20, 30, 40 }。
        // 集合初始化式 { ... } 編譯器會去對應 IEnumerable.Add(T)，
        // 所以型別必須「實作 IEnumerable<T>（或非泛型 IEnumerable）」才會有 Add()。
        // Averager<T> 只是個一般的泛型類別，沒有實作 IEnumerable<T>，因此：
        // ⚠ CS1922 無法使用集合初始設定式來初始設定類型 'Averager<int>'
        //     （因為它未實作 'System.Collections.IEnumerable'）
        //
        // 兩種修法：
        //   (a) 老實呼叫 Add()，像下面這樣。
        //   (b) 讓 Averager<T> 實作 IEnumerable<T>，就能用 { ... }。
        //       這是很好的取捨點：只是「收集樣本算平均」的容器，本來就沒必要
        //       假裝成集合；硬實作 IEnumerable 反而會讓呼叫端以為可以拿它去 foreach。
        var ints = new Averager<int>();
        ints.Add(10);
        ints.Add(20);
        ints.Add(30);
        ints.Add(40);
        Console.WriteLine($"  int 平均    = {ints.Average():F2}");

        // 沒有 where T : INumber<T> 的話，下面這行會編譯失敗（operator + 不存在於未知的 T）：
        //   var invalid = new Averager<string>();   // s + s 沒有定義
        // 限制條件會直接變成編譯期錯誤，而不是執行期才爆。

        Console.WriteLine();
        Console.WriteLine("--- 3. IDisposable 與 using ---");

        var logPath = Path.Combine(Path.GetTempPath(), "csharp-learn-sensors.csv");
        WriteCsvReport(logPath, sensors);

        Console.WriteLine($"  報告已寫到: {logPath}");
        Console.WriteLine("  （using 區塊離開時 Dispose 會被自動呼叫，即使中途丟例外也一樣）");

        Console.WriteLine();
        Console.WriteLine("--- 4. static 成員 vs 實例成員 ---");

        // static 屬於「型別」，不屬於「物件」。不需要（也不該）new 就能用。
        for (var i = 0; i < 3; i++)
        {
            Console.WriteLine($"  產生 tag 名稱: {TagNaming.Next("PLC01")}");
        }

        // 靜態欄位在多執行緒下是不安全的。真的需要時用 Interlocked（見 day4）。
        Console.WriteLine($"  目前已產生 {TagNaming.Created} 個名稱（static 計數器）");

        Console.WriteLine();
        Console.WriteLine("--- 5. 唯讀集合 + LINQ 彙整 ---");

        var hub = new SensorHub(sensors);
        Console.WriteLine($"  感測器數量: {hub.Count}");
        Console.WriteLine($"  全部名稱: {string.Join(", ", hub.Names)}");
        Console.WriteLine($"  依單位分組:");

        // 這裡的 GroupBy 簽章是 GroupBy(Func<ISensor, string> keySelector)，
        // 也就是「把選取器當成一個值傳進去」，所以呼叫端要寫 hub.GroupBy(s => s.Unit)。
        // 對照：Enumerable.GroupBy(sensors, s => s.Unit) 是 LINQ 的靜態擴充方法寫法。
        // 如果簽章誤寫成 GroupBy(string keySelector)，編譯器就會把 lambda 當成
        // 已經算好的字串，報 ⚠ CS1660（無法將 Lambda 運算式轉換成 'string'）。
        foreach (var group in hub.GroupBy(s => s.Unit))
        {
            Console.WriteLine($"    {group.Key}: {group.Count()} 個");
        }
    }

    private static void WriteCsvReport(string path, IReadOnlyList<ISensor> sensors)
    {
        // using 的離開點相當於 finally：一定會執行 Dispose，
        // 所以即使 WriteCsv 丟出例外，檔案句柄也會被釋放。
        using var writer = new CsvFileWriter(path);
        writer.WriteLine("name,unit");

        foreach (var sensor in sensors)
        {
            writer.WriteLine($"{sensor.Name},{sensor.Unit}");
        }
    }
}

// ── 介面：不同品牌／協定沒有共用實作時用這個 ───────────────────────────

/// <summary>所有感測器都要做到的事。</summary>
internal interface ISensor
{
    string Name { get; }

    string Unit { get; }

    Task<double> ReadAsync(CancellationToken ct);
}

// ── 抽象類別：有一堆「共通流程」要共用時用這個 ─────────────────────────

/// <summary>
/// 一組感測器共有的部分：名稱、單位、字串表示。
/// 這裡有實際的共用實作，所以抽象類別比介面更合適。
///
/// 判斷準則：
///   只有「方法簽章」、沒有共用程式碼       → interface
///   有共用欄位／共用實作、需要繼承          → abstract class
/// </summary>
internal abstract class SensorBase(string name, string unit) : ISensor
{
    public string Name { get; } = name;

    public string Unit { get; } = unit;

    public abstract Task<double> ReadAsync(CancellationToken ct);

    public override string ToString() => $"{Name} ({Unit})";
}

/// <summary>用假資料假裝感測器。實際工作上會換成真實協定。</summary>
internal sealed class SimulatedSensor(string name, string unit, double baseValue, double noise)
    : SensorBase(name, unit)
{
    public override async Task<double> ReadAsync(CancellationToken ct)
    {
        // 模擬 I/O 等待。await 不會佔住執行緒，細節見 day2。
        await Task.Delay(20, ct);
        return baseValue + (Random.Shared.NextDouble() * noise);
    }
}

/// <summary>同一個介面、不同的協定實作。切換時呼叫端完全不用改。</summary>
internal sealed class ModbusSensor(string name, string unit, ushort address)
    : SensorBase(name, unit)
{
    public ushort Address { get; } = address;

    public override Task<double> ReadAsync(CancellationToken ct)
    {
        // 真正組封包、讀回應的程式在 day3/ModbusDemo.cs。
        // 這裡的重點是：「換協定」對呼叫端是隱形的。
        throw new NotSupportedException("真正的 Modbus 讀取請看 day3/ModbusDemo.cs");
    }
}

// ── 泛型 + 限制 ─────────────────────────────────────────────────────────

/// <summary>
/// Averager&lt;T&gt; 只能用在「數學型別」上。
/// where T : INumber&lt;T&gt; 是 .NET 7 引入的泛型數學（generic math）介面，
/// 它把每個數值型別的 +, -, *, /, CompareTo 都放進同一個介面，
/// 因此 Averager 只需要寫一次，double / int / decimal 都能用。
///
/// 這叫「靜態抽象成員」（static abstract members，C# 11）。
/// 對照：C++ 沒有這個機制，你得寫一份 template 加上 SFINAE。
/// </summary>
internal sealed class Averager<T> where T : INumber<T>
{
    private readonly List<T> _samples = [];

    public int Count => _samples.Count;

    public void Add(T sample) => _samples.Add(sample);

    public T Average()
    {
        if (_samples.Count == 0)
        {
            throw new InvalidOperationException("還沒有任何樣本，無法取平均值");
        }

        var sum = T.Zero;
        foreach (var sample in _samples)
        {
            sum += sample;
        }

        // 除以樣本數。
        //
        // ⚠ 常見錯誤：T.FromInt32(_samples.Count) 會得到 CS0704
        //   「無法在 'T' 中進行非虛擬的成員查尋，因為其為型別參數」。
        //
        //   原因有兩層：
        //   1. .NET 10 的 INumberBase<T> 已經沒有 FromInt32 這個成員了。
        //      「從某個型別轉換成 T」的入口現在是泛型的 CreateChecked<TOther>(TOther)。
        //   2. 對型別參數 T 做成員查找時，編譯器只認得 where T : ... 列出來的約束。
        //      找不到就是找不到，一律回報 CS0704（就算你寫的是一個根本沒有的名稱，
        //      也會得到同一個錯誤，而不是 CS1061「沒有這個成員」）。
        //
        //   能跨型別呼叫的 static 成員只有約束介面上宣告的那些：
        //   Zero、One、Abs、MultiplyAddEstimate、Parse、TryParse…
        //   泛型數學（.NET 7+）就是靠這個機制，讓數學運算也能 virtual dispatch。
        //
        // 修法：CreateChecked<int>(n) 會在執行期依 T 決定用哪種轉換。
        // int / double / decimal / float 都能用同一份 Averager<T>。
        return sum / T.CreateChecked<int>(_samples.Count);
    }
}

// ── IDisposable ────────────────────────────────────────────────────────

/// <summary>
/// 持有 unmanaged 資源（檔案句柄、Socket）的類別一定要實作 IDisposable。
/// Dispose 可能被重複呼叫（using 之外又手動呼叫），所以自己加防護。
/// </summary>
internal sealed class CsvFileWriter(string path) : IDisposable
{
    private readonly StreamWriter _writer = new(path, append: false);

    private bool _disposed;

    public void WriteLine(string line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Dispose();
    }
}

// ── static 成員 ────────────────────────────────────────────────────────

/// <summary>static 成員屬於型別本身，不需要物件就能存取。</summary>
internal static class TagNaming
{
    private static int _created;

    public static int Created => _created;

    public static string Next(string device) => $"{device}/TAG{++_created:D4}";
}

// ── 唯讀集合 ───────────────────────────────────────────────────────────

/// <summary>把感測器集合包成唯讀。外部拿不到可變的陣列，就無法偷偷改壞內部狀態。</summary>
internal sealed class SensorHub
{
    private readonly IReadOnlyList<ISensor> _sensors;

    public SensorHub(IReadOnlyList<ISensor> sensors) => _sensors = sensors;

    public int Count => _sensors.Count;

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

    /// <summary>
    /// yield return 讓這個方法變成延遲（lazy）求值的 iterator，不必真的建中間集合。
    ///
    /// 參數型別是 Func&lt;ISensor, string&gt;，不是 string —— 這是本節要記住的重點：
    /// 「把選取器當值傳進去」和「把選取好的值傳進去」是兩件事。
    /// 誤寫成 GroupBy(string keySelector) 會得到 ⚠ CS1660（lambda 不能轉成 string），
    /// 誤寫成 GroupBy(Func&lt;ISensor, string&gt; keySelector) => keySelector(s)
    /// 這種「把函式當值呼叫」的寫法則會得到 ⚠ CS0149（必須是方法名稱），
    /// 因為 keySelector 是一個 delegate 欄位，要呼叫它得寫 keySelector(s)。
    /// </summary>
    public IEnumerable<IGrouping<string, ISensor>> GroupBy(Func<ISensor, string> keySelector) =>
        _sensors.GroupBy(keySelector);
}