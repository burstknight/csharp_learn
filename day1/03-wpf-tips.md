# Day 1 — WPF 使用與設計建議

> ⚠ **本 repo 不含 WPF 專案，也沒有可執行的 WPF 範例。**
> 原因：WPF 應用程式**只能在 Windows 上執行**。本教材撰寫與驗證環境是 Linux，
> 就算建立了專案也完全無法驗證 — 那是「看起來能跑、其實沒測過」的程式碼，比沒有更危險。
>
> 這份是**設計與專案設定層面的建議**，等你到了 Windows 機器上再動手。

---

## 1. 產生物件

```bash
dotnet new wpf -o MyApp                  # 產生 WPF 專案
dotnet new wpf -o MyApp --dry-run        # 先看會產生什麼，不實際寫檔
dotnet new list wpf                      # 看有哪些 WPF 相關範本
```

`dotnet new list` 是很有用的指令 — 加上 `--dry-run` 就能安全地探索範本產物。

### 產生的 csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

三個關鍵差異，記住就好：

| 設定 | 為什麼 |
| --- | --- |
| `net10.0-windows` | WPF 需要 Windows 專屬 API，TFM 要帶 `-windows` |
| `UseWPF` | 啟用 XAML 編譯與相依 |
| `WinExe` | GUI 專案不開主控台視窗（用 `Exe` 會多開一個黑窗） |

**若把 WPF 專案放進有 `Directory.Build.props` 的 repo**，那個檔案統一設定的 `TargetFramework` 會被繼承。WPF 專案必須在自己的 csproj **覆寫**（離檔案越近的設定贏）：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <OutputType>WinExe</OutputType>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

---

## 2. 在 Linux 上能不能開發 WPF

**編譯：可以嘗試。執行：不行。**

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <EnableWindowsTargeting>true</EnableWindowsTargeting>
</PropertyGroup>
```

`EnableWindowsTargeting` 讓非 Windows 機器能還原／編譯 Windows 目標的套件。

注意事項：

- **編譯成功不代表能執行。** 產出的 exe 在 Linux 上跑不起來，這是作業系統限制（視窗系統、DirectX、COM）。
- 遇到「找不到 `Microsoft.WindowsDesktop.App` 參考組件」，通常是 SDK 版本或來源設定問題。
- 真正要跑，還是得有 Windows 機器（實體機、VM、或雲端桌面）。
- 順帶一提：跨平台桌面需求越來越多，WPF 之外的選擇是 .NET MAUI 或 Avalonia。看公司技術選型再說。

---

## 3. 專案結構建議

```
MyApp/
├── MyApp.csproj
├── App.xaml                  ← 應用程式層級資源（全域樣式、主題）
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs        ← code-behind
├── Views/
│   ├── MainView.xaml
│   └── SettingsView.xaml
├── ViewModels/
│   ├── MainViewModel.cs
│   └── SettingsViewModel.cs
├── Models/
│   └── DeviceInfo.cs
├── Services/
│   ├── IDeviceService.cs
│   └── DeviceService.cs
└── Converters/
    └── BoolToVisibilityConverter.cs
```

**WPF 專案千萬不要把商業邏輯寫在 `MainWindow.xaml.cs`。** code-behind 只該放純 UI 的事情（開視窗、動畫、事件轉呼叫 ViewModel）。

理由：code-behind 裡的邏輯**沒辦法用一般方式測試**（要先開視窗），而且沒有任何 IDE 工具能幫你檢查對不對。

---

## 4. MVVM：最低限度的心智模型

```
View (XAML)  ←資料綁定→  ViewModel (C#)  ←呼叫→  Service
     ↑                                          │
     └────────────── INotifyPropertyChanged ─────┘
```

**ViewModel 要做的事只有一件：當狀態改變時，發出通知。**

```csharp
public sealed class MainViewModel : INotifyPropertyChanged
{
    private double _temperature;
    private string _status = "未連線";

    public double Temperature
    {
        get => _temperature;
        set
        {
            if (Math.Abs(_temperature - value) < 0.01) return;   // 避免無意義的通知
            _temperature = value;
            OnPropertyChanged();       // 通知 UI：「Temperature 變了」
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

`[CallerMemberName]` 是關鍵 — 讓 `OnPropertyChanged()` 不用手寫字串 `"Temperature"`，改名時不會忘記改。

---

## 5. CommunityToolkit.Mvvm

上面的 `INotifyPropertyChanged` 樣板碼寫起來很煩，所以官方出了原始碼產生器套件。

```bash
dotnet add package CommunityToolkit.Mvvm
```

```csharp
// ObservableObject 已經幫你實作 INotifyPropertyChanged 與 OnPropertyChanged
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]      // 產生 public double Temperature { get; set; }
    private double _temperature;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]     // 改時順便通知另一個屬性
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))] // 改時更新命令的可執行狀態
    private bool _isConnected;
}

// [RelayCommand] 產生 public IRelayCommand RefreshCommand { get; }
// 方法 Refresh() 自動變成命令
[RelayCommand]
private async Task RefreshAsync(CancellationToken ct) { ... }
```

**優點**：消除樣板碼、加欄位或改名字時不會漏掉通知（編譯期產生，不是執行期找字串）。

**缺點**：需要 IDE 支援才能看產生結果，純文字閱讀時要查文件；老闆或同事不熟會需要解釋。

> 本 repo **不引用這個套件**，因為 Day 1 的範例是 console 專案，而且引入原始碼產生器套件會讓「範例零相依」這個原則被打破。真要用時在 WPF 專案裡加。

**命令（Command）vs Click 事件**

MVVM 的重點是 UI 動作不要直接綁到方法，而要綁到 `ICommand`：

```xml
<Button Content="重新整理" Command="{Binding RefreshCommand}" />
```

好處是**同一個 ViewModel 可以在 WPF、WinUI、Avalonia、MAUI 重用**（它們的命令系統雖細節不同，但都實作 `ICommand`）。而且命令可以帶「可不可以執行」的狀態，按鈕會自動 disabled。

---

## 6. 資料綁定的效能

WPF 最常見的效能問題是**在迴圈裡建立太多物件 / 繫結更新太頻繁**。

```csharp
public sealed partial class MainViewModel : ObservableObject
{
    // ObservableCollection：新增項目時通知 UI 自動加上控件
    // List<T> 不會通知，UI 看不到變化
    [ObservableProperty]
    private ObservableCollection<DeviceRow> _rows = [];
}

public sealed partial class DeviceRow : ObservableObject
{
    [ObservableProperty]
    private double _value;
}
```

**重點：資料來源要 `ObservableCollection<T>`，元素也要實作 `INotifyPropertyChanged`。** 只改外層集合、卻讓元素是普通 class，UI 只會在新增/刪除時更新，**屬性改變不會反映到畫面上** — 這是新人最常卡住的地方。

### 高頻更新的處理

如果資料是每秒幾百筆（PLC 畫面就是這種），直接 `ObservableCollection` + 每筆通知會讓 UI thread 塞爆。做法：

1. **在背景執行緒收集，不要在 UI thread** — 用 `Channel`（見 [`day2/02-channels-and-collector.md`](../day2/02-channels-and-collector.md)）
2. **合併更新**：累積後每 100–200 ms 批次更新一次，而不是每筆都通知
3. **用 `IValueConverter` 把資料轉成畫面呈現**，不要在 ViewModel 裡塞字串格式
4. 資料量大時用**虛擬化**（`VirtualizingStackPanel`，預設對 `ItemsControl` 有開，但要確認沒被 `CanContentScroll` 關掉）

---

## 7. 執行緒：UI thread

**WPF 的規則很短：只有 UI thread 能碰畫面上的物件。**

從背景執行緒更新 UI 會丟 `InvalidOperationException`。兩種解法：

```csharp
// 解法一：直接 marshaling
await Task.Run(() => ReadTemperature());   // 在背景讀
Dispatcher.Invoke(() => ViewModel.Temperature = value);   // 切回 UI thread 更新

// 解法二：綁定 + INotifyPropertyChanged（MVVM 的常見做法）
// 背景執行緒改 ViewModel 屬性 → 屬性變更通知會被 WPF 自動 marshal 到 UI thread
// ⚠ 但背景執行緒「建立」或「替換」整個集合是危險的（ObservableCollection 不是執行緒安全）
```

**最容易踩的雷**：在背景執行緒 `ObservableCollection.Add(...)`。集合本身不是執行緒安全的，會造成隨機的崩潰或資料損毀。正確做法是把事件轉成一個佇列，讓 UI thread 定期批次取出來更新。

---

## 8. 進入新專案時先看這些

| 檔案 | 想知道什麼 |
| --- | --- |
| `*.csproj` | TargetFramework、`UseWPF`、有沒有 `EnableWindowsTargeting` |
| `App.xaml` | 全域資源、主題、啟動時做了什麼 |
| `ViewModels/` | MVVM 有沒有真的在用，還是全都塞在 code-behind |
| `Services/` | 業務邏輯有沒有跟 UI 分離 |
| `.editorconfig` | 程式碼風格規矩 |
| `Directory.Build.props` | 有沒有共用設定會影響 WPF 專案 |

---

## 到 Windows 機器上動手時的順序

1. `dotnet new wpf -o Scratch` — 確認 SDK 與範本正常
2. `dotnet run` — 確認視窗開得起來
3. 改 `TargetFramework` 成公司要用的版本
4. 加 `CommunityToolkit.Mvvm`
5. 從「一個 view + 一個 viewmodel + 一個 service」的小功能開始，**不要**試著重構整個舊專案
6. 每加一個畫面就 `dotnet build` 一次，不要累積

---

## 延伸閱讀

- Microsoft Learn：<https://learn.microsoft.com/tw/dotnet/desktop/wpf/>
- CommunityToolkit.Mvvm：<https://learn.microsoft.com/tw/dotnet/communitytoolkit/mvvm/>
- WPF 效能白皮书：<https://learn.microsoft.com/tw/dotnet/desktop/wpf/advanced/optimizing-performance>
