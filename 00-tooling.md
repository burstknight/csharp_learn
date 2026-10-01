# 00 — 工具、專案與套件管理

這份是動手寫程式前先讀的。目標不是背下所有指令，而是知道**一個 .NET 專案由什麼組成、哪些東西該跟著 repo 走、哪些不該**。

> 本文提到的 SDK／套件版本與 .NET 支援期限都會變動，一律以 <https://dotnet.microsoft.com/> 與 <https://dotnet.microsoft.com/download/dotnet> 的官方文件為準。

---

## 目錄

1. [SDK 與 global.json](#sdk-與-globaljson)
2. [Solution 與專案檔](#solution-與專案檔)
3. [TargetFramework 與 LangVersion](#targetframework-與-langversion)
4. [可空參考型別](#可空參考型別)
5. [ImplicitUsings](#implicitusings)
6. [共用設定：Directory.Build.props](#共用設定directorybuildprops)
7. [套件管理：NuGet](#套件管理nuget)
8. [Central Package Management](#central-package-management)
9. [重現與安全：lock file、NuGetAudit](#重現與安全lock-filenugetaudit)
10. [診斷、除錯與格式化](#診斷除錯與格式化)
11. [發佈](#發佈)
12. [全域工具](#全域工具)
13. [WPF 專案需要的額外設定](#wpf-專案需要的額外設定)
14. [在 Linux 上開發 Windows 專案](#在-linux-上開發-windows-專案)

---

## SDK 與 global.json

安裝 SDK 之後，`.NET` 的工具鏈是一組「SDK」。**`global.json` 用來把專案釘在某個 SDK 版別**，避免同事用新 SDK 產生出你編譯不了的東西。

```jsonc
// 放在 repo 根目錄
{
  "sdk": {
    // 只允許 10.0.2xx 系列
    "version": "10.0.200",
    // 沒安裝就自動抓安裝（會需要網路與額外權限，CI 上要留意）
    "rollForward": "latestFeature"
  }
}
```

| `rollForward` | 行為 |
| --- | --- |
| `patch` | 只允許同 feature band 內的 patch 版 |
| `feature` | 允許同 major.minor 的較新 feature band |
| `latestPatch` / `latestFeature` / `latestMinor` / `latestMajor` | 逐級放寬 |
| `disable` | 不放寬，找不到就直接報錯 |

`dotnet --list-sdks` 可以看本機裝了哪些，`dotnet --version` 看目前生效的（在 `global.json` 影響下會跟 `latest` 不同）。

> 實務上 `rollForward` 設寬一點（`latestFeature`）比較不會卡住別人。

---

## Solution 與專案檔

| 檔案 | 角色 |
| --- | --- |
| `CSharpLearn.sln` | 專案集合。編輯器（VS、Rider、OmniSharp、Roslyn）靠它決定要對哪些專案提供補全 |
| `day1/day1.csproj` | 單一專案的設定與相依套件 |
| `Directory.Build.props` | 套用到底下**所有**專案的共用 MSBuild 屬性 |
| `Directory.Build.targets` | 同上，但時機在專案評估之後（一般用得少） |

`.csproj` 用的是 **MSBuild**（一種 XML 設定格式）。本 repo 的 `day1.csproj` 只有這麼多：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>CSharpLearn.Day1</RootNamespace>
  </PropertyGroup>
</Project>
```

`TargetFramework`、`Nullable`、`ImplicitUsings` 都不在裡面，因為它們在 `Directory.Build.props` 統一設定了。**共用設定往上抽**是 .NET 專案的第一守則——四個專案各寫一遍，改一次要動四個地方。

### 常見的 `OutputType`

| 值 | 產出 |
| --- | --- |
| `Exe` | 主控台應用程式（`dotnet run` 可跑） |
| `WinExe` | Windows GUI 應用程式（**沒有**主控台視窗） |
| `Library` | 類別庫，可被別的專案參考 |
| `None` | 不產生組件（MSBuild 只跑流程時用） |

### 專案之間互相參考

```xml
<ItemGroup>
  <ProjectReference Include="..\day1\day1.csproj" />
</ItemGroup>
```

注意是「專案參考」，不是「檔案參考」。專案參考會帶著相依性一起 build；如果你只是要重用一段程式碼，抽成獨立 class library 通常比直接參考 console 專案乾淨。

---

## TargetFramework 與 LangVersion

這兩個常常被搞混，**它們是獨立的**。

| | 決定什麼 | 影響範圍 |
| --- | --- | --- |
| `TargetFramework` | 用哪個 .NET 執行平台的 API、預設哪個 C# 語言版本 | 整個專案 |
| `LangVersion` | 只調整 C# **語言**版本 | 整個專案 |

```xml
<!-- 用 net8.0 的執行平台，但允許使用 C# 14 的語法寫法 -->
<TargetFramework>net8.0</TargetFramework>
<LangVersion>14</LangVersion>
```

重點在於：**C# 版本不綁定 TargetFramework**。你可以用 .NET 8 的執行平台寫 C# 14 的語法，只是不能用到只有 .NET 10 才有的 API（例如 `field` 關鍵字搭配某些只在 .NET 10 才有的 attribute）。

`TargetFramework` 可以用 `;` 多目標（multi-target）：

```xml
<TargetFramework>net8.0;net10.0</TargetFramework>
```

這會讓 `dotnet build` 產生兩份組件，好處是相容性，壞處是 build 時間加倍、而且**只能用條件編譯**寫分歧：

```csharp
#if NET10_0_OR_GREATER
    // 只有 .NET 10 才編譯這段
#else
    // .NET 8 的作法
#endif
```

`NET8_0_OR_GREATER` 這些符號由 SDK 自動定義，不需要自己宣告。

本 repo 刻意**不**寫 `LangVersion`，讓它跟著 TFM 預設走，避免多一個會過期的設定。

---

## 可空參考型別

C++ 有 `nullptr`、Python 有 `None`，但兩者都不會在編譯期告訴你「這裡可能已經是 null 了」。C# 的 `Nullable=enable` 就是補這個洞。

```csharp
// Nullable=enable 時，下面會有兩個警告

string tagName = FindTag("temperature");   // ⚠ CS8600: 可能將 null 轉換成非可空型別
Console.WriteLine(tagName.Length);         // ⚠ CS8602: 對可能為 null 的參考取值

// 修法：宣告時就說清楚它到底可不可空

string? tagName2 = FindTag("temperature"); // 明說「可能為 null」
if (tagName2 is not null)                   // 先檢查，警告就消失
    Console.WriteLine(tagName2.Length);

string known = "PLC-01";                    // 明說「不會是 null」
```

### 你會遇到的警告代碼

| 代碼 | 意義 | 典型修法 |
| --- | --- | --- |
| `CS8600` | 把 null 或可能為 null 的值轉成非可空型別 | 改成 `string?` |
| `CS8602` | 對可能為 null 的參考取成員（`.Length`、`.X`） | 先 `if (x is not null)` 或用 `?.` |
| `CS8603` | 可能回傳 null，但宣告的回傳型別不可空 | 回傳型別加 `?` |
| `CS8604` | 把可能為 null 的參數傳給不可空參數 | 檢查後再傳，或參數加 `?` |
| `CS8618` | 非可空欄位／屬性在結束建構式時仍是 null | 給預設值，或在 ctor 賦值 |

`CS8618` 最容易出現，因為它是在說「你宣告了 `public string Name { get; set; }` 但沒給初值」。

### 其他常用手段

```csharp
// null 合併：左邊是 null 就用右邊
var port = settings.Port ?? 502;

// 空字串也算「沒有值」
var name = settings.DeviceName ?? "";

// null 傳播：整串短路，過程中任何一格是 null 結果就是 null
Console.WriteLine(config?.Plc?.Station?.Name ?? "<未設定>");

// 空值傳播：呼叫時把 null 傳下去（適合只為了呼叫副作用的方法）
serializer?.Serialize(data);
```

`??=` 是「若為 null 才賦值」：`settings.Name ??= "default";`

### 本 repo 的立場

`Nullable=enable` 開著，而且**警告是教材的一部分**。`day1/SyntaxDemo.cs` 裡刻意留了會產生警告的反例，並在該行註解標明警告代碼。請先讀警告、把警告修掉，再往下走。

不要用 `NoWarn` 或把 `Nullable` 關掉來讓輸出乾淨——那等於放棄 C# 最重要的安全機制，而且你的新工作專案幾乎都開著這個。

---

## ImplicitUsings

`<ImplicitUsings>enable</ImplicitUsings>` 會讓 SDK 自動在每個檔案加上隱含的 `using`。對 **console 專案**（`OutputType=Exe`）來說是這些：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
```

所以程式碼裡 `Console`、`List<T>`、`Task`、`CancellationToken`、`Enumerable` 都不用寫 `using System;`。

**注意兩件事：**

1. 這份清單**只適用於 console／Library 那幾種預設組合**。換成 `Microsoft.NET.Sdk.Web` 或 WPF，預設清單會不同。
2. 清單裡沒有的還是要自己 using。`System.Text.Encoding`、`System.Net.Sockets`、`System.Text.Json`、`MQTTnet`… 本 repo 的範例都會明寫出來，就是為了讓你看清楚。

---

## 共用設定：Directory.Build.props

`Directory.Build.props` 放在 repo 根目錄（或任何上層資料夾），MSBuild 會**自動**匯入它到該目錄底下所有專案。這是 .NET 裡最重要的「一次改、全套生效」機制。

本 repo 的版本：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

| 屬性 | 說明 |
| --- | --- |
| `Nullable` | `enable` / `warnings` / `disable` / `annotations` |
| `ImplicitUsings` | `enable` / `disable` |
| `AnalysisLevel` | 分析器規則等級；`latest` 會跟著 SDK 升級 |
| `EnforceCodeStyleInBuild` | `true` 會把 `.editorconfig` 裡的風格建議升級成 build 警告（IDE 開頭的代碼） |
| `TreatWarningsAsErrors` | `true` 把警告當錯誤。**CI 上很有用，本地會很煩**，本 repo 沒開 |
| `LangVersion` | 覆寫語言版本 |
| `Version` / `Company` / `Copyright` | 組件資訊 |

### 條件式設定

也可以針對特定專案或特定條件設定：

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- 正式建置時才把警告當錯誤 -->
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

也可以對單一專案關閉繼承（例如 WPF 必須覆寫 TFM），見文末〈WPF 專案需要的額外設定〉。

---

## 套件管理：NuGet

### 常用指令

```bash
# 加套件（會自動寫進 csproj，並挑一個「當下最新」的版本）
dotnet add day3/day3.csproj package MQTTnet

# 指定版本
dotnet add day3/day3.csproj package MQTTnet --version 5.2.0.1603

# 看專案目前有哪些套件
dotnet list day3/day3.csproj package

# 看套件有哪些版本可升級
dotnet list day3/day3.csproj package --outdated

# 移除套件
dotnet remove day3/day3.csproj package MQTTnet

# 只還原，不建置
dotnet restore
```

### 版本寫法

```xml
<!-- ✅ 釘死：本 repo 的做法 -->
<PackageReference Include="MQTTnet" Version="5.2.0.1603" />

<!-- ❌ 浮動版本：今天和下週 restore 到的可能不是同一份程式碼 -->
<PackageReference Include="MQTTnet" Version="5.*" />
```

浮動版本會讓「我這裡可以跑」變成不可重現的問題，是 CI／部署失敗的常見來源。實務上要更新版本就明確改 csproj，讓 diff 看得出來。

也可以用範圍語法，但要理解它的代價：

| 寫法 | 意義 |
| --- | --- |
| `Version="5.2.0.1603"` | 只接受這個版本 |
| `Version="[5.2.0, 6.0.0)"` | 5.2.0 以上、6.0.0 以前 |
| `Version="5.*"` | 任何 5.x 的最新版 |

### 套件的載入方式

`PackageReference` 會自動帶入相依套件，你**不需要**（也不應該）手動把 `Newtonsoft.Json` 之類的相依項加進 csproj。

要看相依樹：

```bash
dotnet list day3/day3.csproj package --include-transitive
```

---

## Central Package Management

> 本 repo **刻意不採用** CPM（版本寫在各專案 csproj 裡），這樣 Day 1 的環境最單純、最好驗證。
> 但你新工作的專案很可能有 CPM，這一節看懂會省很多時間。

CPM 的作用是**把所有套件版本集中到一個檔案**，各專案只寫套件名稱。

**第一步：建立根目錄 `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="MQTTnet" Version="5.2.0.1603" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.0" />
  </ItemGroup>
</Project>
```

**第二步：csproj 裡不再寫版本**

```xml
<ItemGroup>
  <PackageReference Include="MQTTnet" />
</ItemGroup>
```

有了 CPM卻在 csproj 寫 `Version=`，會得到 `NU1008: Projects that use central package version management should not define the version on the PackageReference items`。

### 容易踩到的坑

| 坑 | 說明 |
| --- | --- |
| 只會往上找到**最近**一個 `Directory.Packages.props` | repo 根目錄有、某個子資料夾又有，該子資料夾底下的專案只用那個 |
| `NU1008` | 開了 CPM 卻在 csproj 寫版本 |
| `NU1010` | 用了沒宣告在 `Directory.Packages.props` 的套件 |
| 套件沒有版本可用 | 中央檔案要列出**所有**用到的套件，包含只用於 analyzer／build tool 的 |
| `PrivateAssets` 要照設定 | 例如 source generator 套件常配 `<PrivateAssets>all</PrivateAssets>` |

CPM 的收益是「全 repo 統一升版、diff 集中、好稽核」；成本是多一個必須理解的機制。兩邊都很常見，看到 `Directory.Packages.props` 就知道對方在用 CPM。

---

## 重現與安全：lock file、NuGetAudit

### packages.lock.json

```bash
dotnet restore --use-lock-file
```

會產生 `packages.lock.json`，把**相依套件的完整解析結果**（包含間接相依）記錄下來。後續 CI 用：

```bash
dotnet restore --locked-mode
```

`--locked-mode` 意思是「必須跟 lock 檔完全一致，不一致就直接失敗」。這是防止供應鏈攻擊與意外升版最有效的一招。

要升版就明確跑：

```bash
dotnet restore --force-evaluate
```

並把 `packages.lock.json` 一起進版控。

### NuGetAudit

.NET 8 以後，`dotnet restore` 會順便檢查已知漏洞並在 build 時警告（`NU1901`～`NU1904` 等）。相關設定：

```xml
<PropertyGroup>
  <NuGetAudit>true</NuGetAudit>          <!-- 預設就是 true -->
  <NuGetAuditMode>all</NuGetAuditMode>   <!-- direct | all，all 連間接相依也掃 -->
  <NuGetAuditLevel>low</NuGetAuditLevel>
  <NuGetAuditSuppress>NU1902;NU1903</NuGetAuditSuppress>
</PropertyGroup>
```

漏洞報告查 <https://github.com/advisories>。**不要**為了讓 CI 變綠就 `Suppress` 掉警告，除非已經評估過影響。

---

## 診斷、除錯與格式化

```bash
# 基本資訊
dotnet --info                     # SDK、runtime、OS 架構
dotnet --list-sdks
dotnet --list-runtimes

# 建置
dotnet build                     # Debug
dotnet build -c Release
dotnet build -c Debug --no-restore   # 跳過還原，迭代時更快
dotnet build -p:WarningLevel=9999   # 把隱藏的警告也全打開（追查問題用）

# 除錯
dotnet run --project day3 -- modbus
dotnet watch run --project day3     # 存檔自動重新執行（Hot Reload）

# 測試（本 repo 目前沒有測試專案）
dotnet test

# 格式化 / 分析
dotnet format                      # 套用 .editorconfig 的規則
dotnet format --verify-no-changes  # 只檢查不改（CI 用）
dotnet format analyzers            # 套用 analyzer 的自動修正
```

`dotnet build -p:WarningLevel=9999` 是除錯利器：把「預設隱藏的低優先序警告」全部叫出來，當你懷疑有問題卻看不到提示時很好用。

---

## 發佈

```bash
# 基本發佈（framework-dependent：需要目標機器有安裝對應 runtime）
dotnet publish day3 -c Release -o out

# self-contained：連 runtime 一起打包，目標機器不用裝 .NET（檔案大很多）
dotnet publish day3 -c Release -o out -r linux-x64 --self-contained

# 單一檔案
dotnet publish day3 -c Release -o out -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true

# 精簡大小（會影響 reflection 相關功能，見下）
dotnet publish day3 -c Release -o out -r linux-x64 --self-contained -p:PublishTrimmed=true
```

### 自我單檔執行檔（AOT）

```bash
dotnet publish -c Release -r linux-arm64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:InvariantGlobalization=true
```

對邊緣裝置、樹莓派、PLC 旁邊的小盒子很有用：沒有 JIT、啟動快、記憶體省。

**但限制很多**，不能想當然耳：

- 只支援少數語言（**C#、C++ 為主**）；VB、F# 官方支援有限
- **不能**依賴 reflection 動態產生程式碼。`System.Text.Json` 若沒用 source generator、`dynamic`、部分 DI 容器、部分序列化套件會失效
- `PublishTrimmed` 也一樣：被裁掉的型別若之後才用到，執行期才炸

所以 AOT／Trimmed 專案通常要先把 `System.Text.Json` 改成 source generator 模式（見 `day3/04-webapi.md`）。

### 診斷

```bash
# 產生 crash dump
dotnet-dump collect -p <pid>
dotnet-dump analyze

# 追蹤效能（method-level）
dotnet-trace collect --profile cpu-sampling -o trace.nettrace
dotnet-trace report trace.nettrace topN

# 檢查套件漏洞
dotnet list day3/day3.csproj package --vulnerable
```

這些工具是 `dotnet tool install -g` 裝的（見下一節）。

---

## 全域工具

```bash
dotnet tool install -g dotnet-ef          # EF Core CLI
dotnet tool install -g dotnet-counters    # 效能計數器
dotnet tool install -g dotnet-trace
dotnet tool install -g dotnet-dump
dotnet tool update -g dotnet-ef
dotnet tool list -g
dotnet tool uninstall -g dotnet-ef
```

不想全機安裝，就用「本機工具清單」——在 repo 根目錄：

```jsonc
// .config/dotnet-tools.json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": { "version": "8.0.0", "commands": ["dotnet-ef"] }
  }
}
```

```bash
dotnet tool restore     # 依清單裝到本地，會自動在 .config/dotnet-tools.json 產生 manifest
dotnet tool run dotnet-ef -- --help     # 用本機那份
```

本機工具的好處是版本跟著 repo 走，CI 上不用另外安裝。**新 repo 看到 `.config/dotnet-tools.json` 就知道要跑 `dotnet tool restore`。**

---

## WPF 專案需要的額外設定

本 repo 不含 WPF 專案（理由與完整說明見 `day1/03-wpf-tips.md`），但你工作上會用到，所以先記住三件事。

```bash
dotnet new wpf -o MyApp        # 產生 WPF 專案
dotnet new wpf -o MyApp --dry-run   # 先看會產生什麼，不實際寫檔
```

產生的 csproj 長這樣：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <!-- 注意：不是 net10.0，是帶 -windows 的平台 moniker -->
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

三個關鍵差異：

1. **`net10.0-windows`**：WPF 需要 Windows 專屬 API，所以 TFM 多了 `-windows`。
2. **`UseWPF=true`**：啟用 WPF 的 XAML 編譯與相依。
3. **`OutputType=WinExe`**：GUI 專案不開主控台視窗。

**若 WPF 專案放在本 repo 底下**，會踩到本 repo 的 `Directory.Build.props`（它統一設定了 `TargetFramework=net10.0`）。該專案必須在自己的 csproj 裡**覆寫**：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>   <!-- 覆寫上層的值 -->
  <OutputType>WinExe</OutputType>
  <UseWPF>true</UseWPF>
</PropertyGroup>
```

覆寫規則是「離檔案越近的設定贏」，所以專案層可以蓋掉 `Directory.Build.props`。

---

## 在 Linux 上開發 Windows 專案

WPF **只能在 Windows 上執行**，這是作業系統限制（DirectX／視窗系統相依），不是設定問題。但你可以在 Linux 上**嘗試編譯**：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <EnableWindowsTargeting>true</EnableWindowsTargeting>
</PropertyGroup>
```

`EnableWindowsTargeting=true` 讓非 Windows 機器可以還原／編譯 Windows 目標的套件。

注意事項：

- **編譯成功 ≠ 能執行。** 產出的 exe 在 Linux 上跑不起來。
- 遇到「找不到 Microsoft.WindowsDesktop.App 參考組件」時，通常是 SDK 版本不支援，或需要 `--source` 指向正確的來源。
- 真正要執行，還是得有 Windows 機器（實體或 VM）。

---

## 新到一個 .NET repo，先看這 10 個檔案

1. `global.json` — 規定 SDK 版本
2. `CSharpLearn.sln` / `*.sln` — 有哪些專案
3. `Directory.Build.props` — 全域設定（**最常被忽略**）
4. `Directory.Build.targets` — 全域建置步驟
5. `Directory.Packages.props` — 有沒有在用 CPM
6. `NuGet.config` / `nuget.config` — 有沒有私有 feed、憑證
7. `.editorconfig` — 程式碼風格
8. `.config/dotnet-tools.json` — 要不要先 `dotnet tool restore`
9. `packages.lock.json` — CI 是否用 `--locked-mode`
10. `global.json` + `Directory.Build.props` + `README.md` 的 build 步驟 — 確認過編譯指令

看完這 10 個，別人問你「這 repo 怎麼建置的」你就有答案了。