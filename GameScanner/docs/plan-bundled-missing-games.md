# Kế hoạch: bước có sẵn "Common missing games" (bắt game hay bị bỏ sót)

> **Trạng thái: chỉ là kế hoạch, chưa code, không thuộc ưu tiên đợt này.** Tài liệu ghi lại phân tích và thiết kế để làm ở đợt sau. Đường dẫn trong mục 6 đã được tra web (có link); chỗ nào ghi **chưa xác minh** thì phải kiểm trên máy Windows thật trước khi đưa vào DB.

---

## 1. Mục tiêu và phạm vi

**Mục tiêu:** thêm một bước có sẵn trong app (tên tạm **"Common missing games"**) để bắt những game mà bước Collect + Heuristic hiện tại bỏ sót, rồi đưa chúng vào `included` với exe đúng để GameProcessChecker theo dõi được.

**Trong phạm vi:**

* Một **DB tích hợp** mô tả cách tìm các game hay bị sót (thư mục gốc theo biến môi trường, glob đường dẫn exe, khóa registry, tên process).
* Một **Heuristic lần 2** chạy trên `excluded` và trên vài vị trí quét thêm (ngoài Registry/launcher).
* Phân tích việc cần sửa ở GameProcessChecker. Phần này **tách thành việc riêng**.

**Ngoài phạm vi:** tải DB từ mạng, tự cập nhật DB, quét toàn bộ ổ đĩa, nhận diện game đang chạy bằng cửa sổ hoặc GPU.

## 2. Vì sao bộ quét hiện tại bỏ sót

Rút ra từ code hiện có:

| Nguyên nhân | Chỗ trong code | Hệ quả |
| :--- | :--- | :--- |
| Collect chỉ đọc **Registry Uninstall** (HKLM/HKCU, 32/64-bit) và manifest/registry của Steam, Epic, Ubisoft, GOG, EA, Riot. | `Collection/RegistryAndLauncherCollector.cs` (`Collect()`) | Game không có mục Uninstall (giải nén tay, portable, emulator, game itch.io, game GDK trong `XboxGames`, gói MSIX) **không bao giờ vào packet**, kể cả vào `excluded`. |
| Mục có trong Registry nhưng thiếu `InstallLocation` và `DisplayIcon`/`UninstallString` không trỏ tới file có thật thì bị bỏ (`continue`). | `CollectUninstallRegistry` | Mất luôn những game có uninstaller nằm chỗ khác. |
| `IsLauncher` chỉ bật theo vài marker cứng: tiền tố key `Steam App `/`Uplay Install `/`Riot Game `, đường dẫn chứa `\steamapps\common\`, `\Epic Games\`, `\HoYoPlay\`, `\Battle.net\`, publisher `Cognosphere`/`miHoYo`/`Blizzard`. | `LauncherKeyPrefixes`, `LauncherPathMarkers`, `LauncherPublishers` | Launcher nhỏ (Kuro, Garena, VNG, Rockstar, Amazon…) đi đường "Registry thường" và phải qua ngưỡng điểm. |
| Mục Registry thường bắt đầu từ 0 điểm, cần đạt 45. Chỉ có dấu hiệu engine (+50) là đủ một mình. Dấu hiệu engine chỉ tìm **ngay trong `installDir`** hoặc **cạnh exe ứng viên**: `UnityPlayer.dll`, `steam_api*.dll`, `*-Win64-Shipping.exe`, `Engine\Binaries`, `<exe>_Data`… | `Stages/HeuristicFilterStage.cs` (`ScoreRegistryApp`, `HasGameEngineSignature`) | Game dùng engine không có trong danh sách (Java, Godot, GameMaker, Ren'Py, RPG Maker, engine riêng) gần như chắc chắn bị loại. |
| Tên hoặc publisher trùng danh sách "phần mềm thường" bị trừ 40–45 điểm (ví dụ `Microsoft Corporation`, từ khóa `runtime`, `update`, `helper`, `launcher` của launcher lớn). | `NonGamePublishers`, `NonGameNameKeywords` | Game do Microsoft/Xbox phát hành có mục Uninstall vẫn bị trừ điểm. |
| ExecutableResolver chỉ đệ quy `MaxScanDepth = 3` cấp thư mục con dưới `installDir`. | `Collection/ExecutableResolver.cs` | Exe nằm sâu bị bỏ qua. Ví dụ Wuthering Waves: `<gốc>\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe` sâu 4 cấp, nên exe chính dễ thành `launcher.exe` ở gốc. *(Suy luận từ code, cần kiểm trên máy thật.)* |
| Không có khái niệm "exe dùng chung" hay "đường dẫn đổi theo version". | `Models/AppEntry.cs` | Minecraft (`javaw.exe`) và Roblox (`Versions\version-<hash>\`) không biểu diễn đúng được. |

## 3. Thiết kế đề xuất

### 3.1 Vị trí trong pipeline

```
Collect ─► Heuristic ─► [Common missing games] ─► (pipeline tùy chỉnh…) ─► Danh sách nhớ
                         (mới, có sẵn, bật/tắt)                            (include/exclude của người dùng)
```

* Đây là **bước có sẵn**, chạy trong process, không phải chương trình ngoài. Cách làm: thêm `BundledMissing` vào `StepKind`, enum hiện tại trong code refactor là `Collect`, `Heuristic`, `Custom`, `Remember`. Bước này có checkbox bật/tắt như các bước khác và lưu trạng thái trong `settings.json`.
* Đặt **sau Heuristic** để dùng được `excluded`, và **trước pipeline tùy chỉnh** để script ngoài thấy kết quả và sửa được.
* **Danh sách nhớ chạy cuối**, nên exclude của người dùng luôn thắng, kể cả với mục do DB thêm vào.

Bước gồm hai pha con, mỗi pha có thể tắt riêng trong settings:

1. **Pha A (DB)**: khớp DB tích hợp. Pha này tin cậy cao.
2. **Pha B (Heuristic lần 2)**: chấm lại `excluded` và quét thêm vị trí, dùng ngưỡng riêng (xem mục 4). Pha này tin cậy thấp hơn và nên tắt mặc định ở bản đầu.

### 3.2 Định dạng DB

* **Nhúng trong exe**: `GameScanner/BundledGames/bundled-games.json` khai báo là `EmbeddedResource`. App vẫn là một file exe duy nhất.
* **File override**: `GameScanner_Data/bundled-games.json` cạnh exe, cùng chỗ với `rules.json`. Quy tắc gộp theo `id`:
  * Trùng `id` thì mục trong file override **thay thế hoàn toàn** mục nhúng.
  * Có `"disabled": true` thì tắt mục nhúng cùng `id`.
  * `id` mới thì thêm vào.
* File override hỏng JSON hoặc sai schema thì bước **lỗi** (giống rules), không âm thầm bỏ qua.
* Gốc file có `"schemaVersion": 1` để đổi schema về sau.

### 3.3 Schema một mục DB

| Trường | Bắt buộc | Ý nghĩa |
| :--- | :--- | :--- |
| `id` | có | Khóa ổn định, ví dụ `minecraft-java`. Được ghi vào packet (`bundledDbId`) để khử trùng giữa các lần quét. |
| `name`, `publisher` | có / không | Tên hiển thị. |
| `detect` | không | Điều kiện để coi là "có cài": `anyPathExists` (danh sách đường dẫn), `registry` (`{hive, view, key, value?}`). Không có `detect` thì chỉ dựa vào `exeGlobs`. |
| `installDirs` | không | Danh sách thư mục gốc (có biến môi trường). Lấy thư mục đầu tiên tồn tại làm `installDir`. Có thể lấy từ registry: `{"fromRegistry": {...}}`. |
| `exeGlobs` | có | Glob tới exe cần theo dõi. Chỉ hỗ trợ `*` (một đoạn đường dẫn) và `**` (nhiều đoạn, có giới hạn độ sâu). Biến `%VAR%` được mở rộng trước. |
| `exePick` | không | Cách chọn `exePath` khi có nhiều exe khớp: `newest` (mặc định, theo LastWriteTime), `all` (exe mới nhất làm `exePath`, còn lại vào `candidateExes`). |
| `excludeExes` | không | Tên exe **không** được theo dõi (launcher, updater, crash handler…). |
| `processName` | không | Tên process mong đợi, để kiểm chéo và làm tài liệu. |
| `sharedExe` | không | `true` khi exe dùng chung với app khác (ví dụ `javaw.exe`). |
| `versionedPath` | không | `true` khi đường dẫn exe đổi sau khi update. Khi đó phải quét lại sau update. |
| `commandLineHints` | không | Chuỗi phải có trong command line process. Chỉ có tác dụng khi GameProcessChecker hỗ trợ (mục 5). |
| `rescueFromExcluded` | không | Mặc định `true`: được phép chuyển mục khớp từ `excluded` sang `included`. |
| `notes`, `sources` | không | Ghi chú và link nguồn, để người bảo trì đọc. Code không dùng. |

**Ví dụ: Minecraft Java Edition**

```json
{
  "id": "minecraft-java",
  "name": "Minecraft: Java Edition",
  "publisher": "Mojang Studios",
  "detect": {
    "anyPathExists": [
      "%APPDATA%\\.minecraft\\versions",
      "%LOCALAPPDATA%\\Packages\\Microsoft.4297127D64EC6_8wekyb3d8bbwe\\LocalCache\\Local"
    ]
  },
  "installDirs": ["%APPDATA%\\.minecraft"],
  "exeGlobs": [
    "%APPDATA%\\.minecraft\\runtime\\*\\windows-x64\\*\\bin\\javaw.exe",
    "%LOCALAPPDATA%\\Packages\\Microsoft.4297127D64EC6_8wekyb3d8bbwe\\LocalCache\\Local\\runtime\\*\\windows-x64\\*\\bin\\javaw.exe",
    "%ProgramFiles(x86)%\\Minecraft Launcher\\runtime\\*\\windows-x64\\*\\bin\\javaw.exe"
  ],
  "exePick": "all",
  "excludeExes": ["MinecraftLauncher.exe", "Minecraft.exe"],
  "processName": "javaw.exe",
  "sharedExe": true,
  "versionedPath": true,
  "commandLineHints": ["net.minecraft.client.main.Main", "\\.minecraft"],
  "notes": "javaw.exe trong runtime đi kèm launcher; thư mục thành phần (java-runtime-gamma/delta/...) đổi theo phiên bản game."
}
```

**Ví dụ: Roblox**

```json
{
  "id": "roblox-player",
  "name": "Roblox",
  "publisher": "Roblox Corporation",
  "installDirs": ["%LOCALAPPDATA%\\Roblox", "%ProgramFiles(x86)%\\Roblox"],
  "exeGlobs": [
    "%LOCALAPPDATA%\\Roblox\\Versions\\version-*\\RobloxPlayerBeta.exe",
    "%ProgramFiles(x86)%\\Roblox\\Versions\\version-*\\RobloxPlayerBeta.exe",
    "%LOCALAPPDATA%\\Bloxstrap\\Versions\\version-*\\RobloxPlayerBeta.exe",
    "%LOCALAPPDATA%\\Bloxstrap\\Roblox\\Player\\RobloxPlayerBeta.exe"
  ],
  "exePick": "newest",
  "excludeExes": ["RobloxPlayerLauncher.exe", "RobloxStudioBeta.exe", "RobloxCrashHandler.exe"],
  "processName": "RobloxPlayerBeta.exe",
  "versionedPath": true
}
```

> Glob Bloxstrap thứ tư dựa trên ghi chú phát hành Bloxstrap v2.8.0, phiên bản chuyển sang thư mục cố định `Roblox\Player\`. **Đường dẫn chính xác chưa xác minh.**

### 3.4 Thuật toán khớp (Pha A)

```
với mỗi mục DB (đã gộp nhúng + override, bỏ mục disabled):
  1. Mở rộng %VAR% bằng Environment.ExpandEnvironmentVariables.
     Biến không mở rộng được (còn nguyên "%X%") thì bỏ glob đó.
  2. Nếu có `detect` mà không điều kiện nào đúng thì bỏ qua mục.
  3. Mở rộng exeGlobs bằng bộ glob riêng:
     - đi từng đoạn đường dẫn, '*' khớp tên trong một thư mục, '**' giới hạn 4 cấp;
     - IgnoreInaccessible, không theo reparse point/junction (tránh vòng lặp, ổ mạng);
     - bỏ file có tên nằm trong excludeExes.
  4. Không còn exe nào thì ghi log "[bundled] <id>: không thấy" và bỏ qua.
  5. Chọn exePath và candidateExes theo exePick.
  6. Gộp vào packet (3.5).
```

* Chi phí: mỗi glob chỉ liệt kê vài thư mục cụ thể, nên tổng thời gian ở mức mili giây, không quét cả ổ.
* Bộ glob dùng chung được cho Pha B và có thể tái dùng `PathPatternMatcher` để so khớp chuỗi. Riêng phần **liệt kê** thư mục là code mới.

### 3.5 Ghi kết quả vào packet

Tìm mục đã có trong packet theo thứ tự sau: (a) trùng `bundledDbId`; (b) `exePath` hoặc một `candidateExes` nằm trong tập exe vừa tìm được; (c) `installDir` trùng hoặc là cha/con của `installDirs`.

| Mục đã có ở | Hành động |
| :--- | :--- |
| `excluded` (và `rescueFromExcluded`) | Chuyển sang `included`. Gán `exePath`/`candidateExes` từ DB; exe cũ nằm trong `excludeExes` thì bỏ. Thêm các trường bên dưới. |
| `included` | Giữ nguyên `exePath` nếu exe đó đã khớp glob, nếu không thì thay bằng exe của DB (exe cũ thường là launcher). Hợp nhất `candidateExes`. Thêm các trường bên dưới. |
| Không có | Thêm mục mới vào `included`. |

Trường thêm vào mục. Đây là trường lạ, `AppEntry.Extra` giữ nguyên chúng khi chuyển qua các bước:

```json
{
  "source": "bundled-db",
  "bundledDbId": "minecraft-java",
  "sharedExe": true,
  "versionedPath": true,
  "commandLineHints": ["net.minecraft.client.main.Main", "\\.minecraft"]
}
```

> Trong code đang refactor, `AppEntry` đã có trường chính thức `Source` (các giá trị hiện có: tên launcher, `"rules"`, `"remembered"`) và `LauncherGame`. Bước này gán `Source = "bundled-db"`. Các trường còn lại (`bundledDbId`, `sharedExe`…) để trong `Extra` cho tới khi checker cần.

### 3.6 Thư mục đổi theo version

* Glob được mở rộng **lúc quét**, nên `games.json` chỉ chứa đường dẫn **tại thời điểm quét**. Sau khi Roblox tự update (thư mục `version-<hash>` mới) hoặc Minecraft tải runtime mới (ví dụ chuyển từ `java-runtime-gamma` sang `java-runtime-delta`), checker sẽ **không còn thấy game** cho tới khi quét lại và xuất lại.
* Giảm thiểu khi chưa sửa checker:
  * Minecraft dùng `exePick: "all"` để liệt kê **mọi** runtime đang có. Runtime cũ thường vẫn còn trên đĩa, nên chỉ hụt khi xuất hiện thành phần mới.
  * GUI hiển thị nhãn "đường dẫn đổi theo version, cần quét lại sau khi game cập nhật" cho mục có `versionedPath`.
  * **Khử trùng trong Danh sách game:** README hiện ghi "game cũ không bao giờ bị xóa hay ghi đè khi quét lại", nên mỗi lần Roblox đổi version sẽ sinh **thêm một mục mới**. Đề xuất: mục có `bundledDbId` thì **cập nhật tại chỗ** (thay `exePath`/`candidateExes`) thay vì thêm mục mới. Câu hỏi này cần người dùng quyết (mục 8).
* Cách xử lý triệt để là cho checker so theo thư mục/pattern (mục 5).

## 4. Heuristic lần 2 (Pha B)

**Đầu vào:** (1) mọi mục trong `excluded`; (2) thư mục con cấp 1 của một số **vị trí quét thêm**, mỗi thư mục con coi là một ứng viên với `installDir` là chính nó.

**Vị trí quét thêm (đề xuất):**

| Vị trí | Ghi chú |
| :--- | :--- |
| `%USERPROFILE%\Games`, `<ổ>:\Games`, `<ổ>:\Game` với mọi ổ cố định | Nơi người dùng hay giải nén game portable. |
| `<ổ>:\XboxGames` | Thư mục mặc định của game GDK cài qua Xbox app (đã xác minh, mục 6). |
| `%APPDATA%\itch\apps` | Thư mục cài mặc định của itch app. |
| Danh sách thư mục do người dùng thêm trong settings | Không đoán thêm. |

Không quét `Downloads`, `Desktop` hay cả ổ đĩa vì tốn thời gian và dễ báo nhầm.

**Tín hiệu bổ sung (cộng vào điểm của `ScoreRegistryApp`, ngưỡng riêng, ví dụ ≥ 60):**

| Tín hiệu | Điểm gợi ý | Rủi ro báo nhầm |
| :--- | :--- | :--- |
| Chữ ký engine hiện có (`UnityPlayer.dll`, `*-Win64-Shipping.exe`, `<exe>_Data`…) nhưng tìm **sâu hơn**, tới 5 cấp. | +50 | Thấp. Unity Hub và Unreal Editor phải loại theo tên. |
| `GameAssembly.dll` (Unity IL2CPP), `*_Data\globalgamemanagers` | +50 | Thấp. |
| Thư mục `Content\Paks\*.pak` hoặc `*.utoc/*.ucas` (Unreal) | +40 | **Cao nếu chỉ tìm `*.pak` trần**: mọi app Electron/Chromium có `resources.pak`, `chrome_100_percent.pak`. Bắt buộc phải nằm dưới `Content\Paks`. |
| Godot: `*.pck` cạnh exe cùng tên | +40 | Thấp–trung bình. |
| GameMaker: `data.win`; Ren'Py: thư mục `renpy\` + `game\`; RPG Maker: `RGSS*.dll` hoặc `www\js\rpg_core.js` | +40 | Thấp. |
| `MicrosoftGame.config` cạnh exe | +50 | Thấp. Tài liệu GDK yêu cầu file này nằm cùng thư mục exe chính. |
| `steam_appid.txt` hoặc `steam_api*.dll` ở thư mục con | +30 | Trung bình: bản crack, tool mod. |
| Thư mục nằm trong vị trí quét thêm ở trên | +20 | Trung bình. |
| Kích thước thư mục > 2 GB (chỉ tính khi đã có tín hiệu khác, tính có giới hạn số file) | +10 | Cao nếu đứng một mình (IDE, SDK, máy ảo). Tốn I/O. |
| `FileVersionInfo.FileDescription`/`ProductName` của exe chứa "game" | +10 | Rất yếu, chỉ để phá hòa. |
| Exe có `Comments`/`CompanyName` thuộc danh sách phần mềm thường | trừ như hiện tại | — |

**Rủi ro chung:** ứng dụng dùng Unity hoặc Unreal nhưng không phải game (công cụ VR, phần mềm 3D, launcher), bản cài game bị hỏng, thư mục mod. Vì vậy Pha B **nên tắt mặc định**, và mục nó cứu được gắn `"source": "heuristic-2"` cùng `"score": <n>` để GUI tô màu khác, người dùng xác nhận hoặc đưa vào exclude trong Danh sách nhớ.

**Sửa kèm (nhỏ):** khi mục đã là launcher hoặc có chữ ký engine mà exe chọn được là launcher/updater, cho ExecutableResolver quét lại sâu hơn (6 cấp) để bắt trường hợp Wuthering Waves.

## 5. Thay đổi ở GameProcessChecker (việc riêng, không làm cùng đợt)

Hiện tại `TargetSet` gom `Paths` (đường dẫn đầy đủ) và `Names` (tên file không đuôi). Mỗi 30 giây, `ProcessScanner.IsAnyTargetRunning` liệt kê process, **lọc theo tên trước** (rẻ), rồi mới `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageNameW` và so **khớp chính xác** đường dẫn (không phân biệt hoa thường). Checker không hỗ trợ wildcard.

Hai vấn đề với Minecraft:

1. **Đường dẫn đổi theo version runtime**: báo sót (false negative) cho tới khi quét lại.
2. **`javaw.exe` dùng chung**: nếu người chơi dùng JRE hệ thống (profile tự chỉnh, launcher bên thứ ba), mọi app Java (IDE, tool) chạy cùng `javaw.exe` đều bị coi là "game đang chạy", tức báo nhầm (false positive). Khi dùng runtime đi kèm launcher thì rủi ro thấp, vì gần như chỉ game dùng runtime đó.

| Phương án | Mô tả | Ưu | Nhược / chi phí |
| :--- | :--- | :--- | :--- |
| **A. Không sửa checker** | Scanner liệt kê mọi `javaw.exe` dưới `runtime\` (`exePick: "all"`). Roblox lấy version hiện tại. | Không đụng checker. `games.json` giữ nguyên định dạng. | Hụt sau update cho tới khi quét lại. Không giải quyết JRE dùng chung (chỉ có thể **không** đưa JRE hệ thống vào). |
| **B. Thêm khớp theo thư mục + tên** | Mục trong `games.json` có thêm `exeDirs` (tiền tố thư mục) + `exeNames`. Ví dụ: `{"exeDirs":["C:\\Users\\U\\AppData\\Roaming\\.minecraft\\runtime\\"],"exeNames":["javaw.exe"]}`. Checker thêm `exeNames` vào `Names` (giữ nguyên lọc rẻ theo tên), rồi so `path.StartsWith(dir)`. | Rẻ: vẫn chỉ mở process trùng tên. Xử lý triệt để chuyện đổi version của Roblox và Minecraft. Tương thích ngược (trường mới là tùy chọn). | Phải đổi cả exporter lẫn checker. Tiền tố quá rộng (ví dụ `C:\`) sẽ gây báo nhầm, nên exporter phải từ chối tiền tố quá ngắn. |
| **B'. Pattern/glob đầy đủ** | Như B nhưng dùng glob (`...\Versions\version-*\RobloxPlayerBeta.exe`). | Chính xác hơn B. | Phức tạp hơn, phải tự viết matcher (checker không có `PathPatternMatcher`). B thường đã đủ. |
| **C. Kiểm tra command line** | Mục có `commandLineHints`: sau khi khớp tên + đường dẫn (hoặc thư mục), đọc command line process và yêu cầu chứa một hint (`net.minecraft.client.main.Main`, `\.minecraft`). Cho phép đưa cả JRE hệ thống vào mà không báo nhầm. | Giải quyết `javaw.exe` dùng chung. | Đọc command line process khác **tốn hơn và cần quyền hơn**: (1) `NtQueryInformationProcess(ProcessCommandLineInformation = 60)`. Đây là API nội bộ, Microsoft cảnh báo có thể đổi giữa các bản Windows, theo thư viện sysinfo thì dùng được từ Windows 8.1. Quyền truy cập tối thiểu cho class 60 **chưa xác minh**. (2) Đọc PEB bằng `ReadProcessMemory`: cần `PROCESS_VM_READ`, hay thất bại với process chạy quyền cao hơn checker. (3) WMI `Win32_Process.CommandLine`: chậm (hàng trăm ms), không hợp với vòng 30 giây chạy nền. Cần cache theo PID + StartTime. Chỉ đọc command line cho process **đã trùng tên**, nên số lần đọc rất ít. |

**Đề xuất:** làm **B trước** (rẻ, gỡ được vấn đề version cho cả Roblox lẫn Minecraft), sau đó **C chỉ cho mục có `commandLineHints`**, kèm fallback: không đọc được command line thì coi là khớp nếu đường dẫn nằm trong runtime của launcher, và coi là không khớp nếu là JRE hệ thống. Trong lúc chưa sửa checker thì dùng A.

## 6. Bảng game ví dụ

Ghi chú cột "Xác minh": **có** = nguồn chính thức hoặc nguồn kỹ thuật đáng tin; **một phần** = chỉ có nguồn cộng đồng/bên thứ ba, hoặc chỉ xác minh được một phần đường dẫn; **chưa** = không tìm được nguồn.

| Game / nhóm | Vị trí mặc định | Exe cần theo dõi | Đặc điểm | Xác minh |
| :--- | :--- | :--- | :--- | :--- |
| **Minecraft Java** (launcher Microsoft Store / bản mới) | `%LOCALAPPDATA%\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\<comp>\windows-x64\<comp>\bin\javaw.exe` | `javaw.exe` | Đổi theo thành phần runtime. Exe dùng chung tên với mọi app Java. Launcher nằm trong `WindowsApps` (bị khóa quyền). | **một phần**: bố cục lồng `<comp>\windows-x64\<comp>\bin` thấy trong bug report Mojang ([MC-261958](https://bugs-legacy.mojang.com/browse/MC-261958), [MCL-23035](https://bugs.mojang.com/browse/MCL-23035)). Không mở được trang để đọc trực tiếp (host bị chặn), chỉ thấy qua kết quả tìm kiếm. |
| **Minecraft Java** (launcher cũ / cài vào `%APPDATA%`) | `%APPDATA%\.minecraft\runtime\<comp>\windows-x64\<comp>\bin\javaw.exe`; bản rất cũ: `C:\Program Files (x86)\Minecraft Launcher\runtime\...` | `javaw.exe` | Như trên. `.minecraft` mặc định ở `%APPDATA%`. | **một phần**: bố cục ở `Program Files (x86)` có trong [gist](https://gist.github.com/Densamisten/7a0ab5b8c1ee49c3deb493f542384f41). Đường dẫn chính xác dưới `%APPDATA%\.minecraft\runtime` **chưa xác minh** (không tìm được nguồn trích nguyên văn). |
| Minecraft: tên thành phần runtime | `jre-legacy` (Java 8), `java-runtime-alpha` (16), `-beta`/`-gamma` (17), `-delta` (21, cho 1.20.5+), `-epsilon` (25) | — | Lý do phải dùng glob `*`. | **một phần**: [GDLauncher](https://gdlauncher.com/guides/manage-java-versions), [minecraft-kit](https://loontail.github.io/minecraft-kit/guides/runtime) |
| Minecraft: command line | main class `net.minecraft.client.main.Main` (mọi bản sau 1.5.2) | — | Hint cho phương án C. | **có**: [wiki.vg](https://wiki.vg/Launching_the_game) |
| Minecraft qua launcher bên thứ ba (Prism…) | Runtime riêng, ví dụ `…/PrismLauncher/java/java-runtime-delta/bin/java` | `javaw.exe` | Đường dẫn tùy launcher. Có thể là JRE hệ thống. | **một phần** (log trên Linux, [mclo.gs](https://mclo.gs/pCsQjWS)). Đường dẫn Windows **chưa xác minh**. |
| **Roblox** | `%LOCALAPPDATA%\Roblox\Versions\version-<hash>\RobloxPlayerBeta.exe`. Nếu cho UAC lúc cài thì có thể ở `Program Files (x86)` | `RobloxPlayerBeta.exe` | Thư mục version đổi sau mỗi lần update. | **có**: [Bloxstrap wiki](https://github.com/pizzaboxer/bloxstrap/wiki/A-deep-dive-on-how-the-Roblox-bootstrapper-works), [DevForum](https://devforum.roblox.com/t/how-to-use-the-roblox-app-beta-mac-windows/1145255/58) |
| Roblox qua Bloxstrap | `%LOCALAPPDATA%\Bloxstrap\Versions\version-*\`. Từ v2.8.0 dùng thư mục cố định `…\Roblox\Player\` | `RobloxPlayerBeta.exe` | Từ v2.8.0 đường dẫn không còn đổi theo version. | **một phần**: [release v2.8.0](https://bloxstraplabs.com/versions/2.8.0). Đường dẫn đầy đủ **chưa xác minh**. |
| **osu! (stable)** | `%LOCALAPPDATA%\osu!\osu!.exe` | `osu!.exe` | Đường dẫn cố định. Người dùng có thể đổi lúc cài. | **có**: [osu! wiki](https://osu.ppy.sh/wiki/en/Client/Program_files) |
| **osu!(lazer)** | `%LOCALAPPDATA%\osulazer\current\osu!.exe` (`Update.exe` ở `osulazer\`) | `osu!.exe` | Cài bằng Velopack, thư mục `current` bị thay khi update (đường dẫn không đổi). **Trùng tên exe với bản stable** nhưng khác thư mục. | **một phần**: thư mục `osulazer` có trên trang gỡ cài đặt bên thứ ba ([advanceduninstaller](https://www.advanceduninstaller.com/osu-lazer-f0ab237a9b017d4b5fc744281e950ee9-application.htm)), bố cục `current\` theo [docs Velopack](https://docs.velopack.io/packaging/operating-systems/windows). Đường dẫn đầy đủ trên máy thật **chưa xác minh**. |
| **Game Xbox app / GDK** | `C:\XboxGames\<game>\...`. Exe chính nằm cùng thư mục với `MicrosoftGame.config` | exe khai báo trong `MicrosoftGame.config` | Người dùng đổi được thư mục trong Xbox app. Thư mục con `Content\` chỉ thấy trong báo cáo người dùng. | **có**: [Flat File Install](https://devdocs.xbox.com/build/core-features/common/packaging/packaging-flatfileinstall) (thư mục `XboxGames`, `MicrosoftGame.config` cạnh exe). Thư mục `Content\` **chưa xác minh**. |
| Game Microsoft Store dạng MSIX/UWP cũ | `C:\Program Files\WindowsApps\...` hoặc `<ổ>:\WindowsApps` | — | Thư mục bị khóa quyền, liệt kê không được khi không có quyền admin/TrustedInstaller. Cần `PackageManager`/`Get-AppxPackage` để lấy `InstallLocation`. | **có** (vị trí): [PCWorld](https://www.pcworld.com/article/623123/how-to-install-or-move-your-xbox-pc-games-to-any-folder.html). Khả năng đọc thư mục **chưa thử**. |
| **itch.io app** | `%APPDATA%\itch\apps\<game>\` | tùy game (cần Pha B để chọn exe) | Thêm được vị trí cài khác. Metadata ở `%APPDATA%\itch` (cấu trúc DB nội bộ **chưa xác minh**). | **một phần**: [docs itch](https://itch.io/docs/itch/using/install-locations.html) (không nêu đường dẫn), đường dẫn theo [diễn đàn itch](https://itch.io/t/826257/install-path) |
| **Battle.net** | Game thường ở `C:\Program Files (x86)\<game>`. Trạng thái cài ở `C:\ProgramData\Battle.net\Agent\product.db` (protobuf) | exe game, không phải `Battle.net.exe` | Code đã có marker `\Battle.net\` và publisher `Blizzard`. Đọc `product.db` để chắc hơn. | **một phần**: `product.db` theo [WineHQ bug](https://list.winehq.org/archives/list/wine-bugs@list.winehq.org/thread/G3OSFCAPXXOKMK5B6HNSX4MSXQPQN7IS), [Blizzard support](https://us.support.blizzard.com/en/article/6504). Thư mục game mặc định **chưa xác minh**. |
| **Amazon Games** | Danh sách cài: `%LOCALAPPDATA%\Amazon Games\Data\Games\Sql\GameInstallInfo.sqlite` | exe game | Phải đọc SQLite (có thể cần thêm thư viện, hoặc parse tối giản). | **một phần**: [code bên thứ ba](https://gitverse.ru/thedayg0ne/ProjectArcade/content/main/emulatorLauncher/EmulatorLauncher.Common/Launchers/AmazonLibrary.cs). Thư mục game mặc định (`C:\Amazon Games\Library`) **chưa xác minh**. |
| **Rockstar Games Launcher** | `C:\Program Files\Rockstar Games\Grand Theft Auto V\`; launcher ở `…\Rockstar Games\Launcher`. Registry `HKLM\SOFTWARE\WOW6432Node\Rockstar Games\<game>` giá trị `InstallFolder` | `GTA5.exe` (không phải `PlayGTAV.exe`/launcher) | Đường dẫn cố định. | **có** (đường dẫn): [Rockstar support](https://support.rockstargames.com/en-US/articles/3NbgU5VkopOuPjbRH1wT8p/how-to-perform-a-clean-install-of-grand-theft-auto-v-on-pc). Khóa registry chỉ có nguồn cộng đồng ([gta5-mods](https://www.gta5-mods.com/tools/gta-v-installpath-tool)). Tên exe **chưa xác minh**. |
| **Wuthering Waves** (Kuro launcher) | `<gốc>\Wuthering Waves Game\Client\Binaries\Win64\Client-Win64-Shipping.exe`. Thư mục gốc tùy người dùng | `Client-Win64-Shipping.exe` | Exe sâu 4 cấp, vượt `MaxScanDepth = 3`. Bản Steam/Epic cũng chạy qua launcher Kuro. | **một phần**: đường dẫn con nhất quán giữa các nguồn ([gurugamer](https://gurugamer.com/pc-console/best-lag-fix-setting-for-wuthering-waves-2-4-pc-no-download-24935/amp)). Thư mục gốc **chưa xác minh**. |
| **Genshin Impact** (HoYoPlay) | `C:\Program Files\HoYoPlay\games\Genshin Impact game\GenshinImpact.exe` | `GenshinImpact.exe` | Code đã có marker `\HoYoPlay\`. Cần chắc exe chính không phải `launcher.exe`. | **một phần**: [advanceduninstaller](https://www.advanceduninstaller.com/HoYoPlay-762b079abaf283a437176716fad32aed-application.htm) |
| **RetroArch** | `C:\RetroArch-Win64\retroarch.exe` (installer từ v1.9.1). Bản portable ở bất kỳ đâu | `retroarch.exe` | Emulator. Game là ROM, chỉ theo dõi được emulator. | **một phần**: [Chocolatey](https://community.chocolatey.org/packages/RetroArch), [silentinstallhq](https://silentinstallhq.com/?p=16715) |
| **Ryujinx** (và các bản fork) | Zip portable, không có installer. Dữ liệu ở `%APPDATA%\Ryujinx` | `Ryujinx.exe` | Vị trí exe tùy người dùng. Chỉ có thể dựa vào `%APPDATA%\Ryujinx` để biết đã cài, rồi Pha B hoặc người dùng chỉ exe. | **một phần**: [hướng dẫn cài](https://www.fdaytalk.com/install-ryujinx-canary-windows/), [LaunchBox forum](https://forums.launchbox-app.com/topic/70574-ryujinx-portable-automatic-updates-wrong-folder) |
| Yuzu | — | — | Đã ngừng phát triển năm 2024, không nên đưa vào DB mặc định. | **chưa** (không tra nguồn) |
| **Garena** (FC Online, …) | — | — | Chỉ có thông tin cho Garena Path of Exile (`C:\Program Files (x86)\GarenaPoE`). | **chưa** (không có nguồn cho launcher Garena hiện tại) |
| **VNG** (game VN) | — | — | — | **chưa** (không tìm được nguồn đáng tin) |
| Game portable / giải nén tay, game Java khác | Bất kỳ | — | Không thể đưa vào DB, chỉ Pha B hoặc thêm thủ công xử lý được. | — |

## 7. Kế hoạch triển khai (bước nhỏ)

| # | Việc | Kiểm thử |
| :--- | :--- | :--- |
| 1 | Tách **logic thuần** ra lớp không phụ thuộc Registry/WinForms: `BundledDbLoader` (đọc nhúng + override, gộp theo `id`), `PathGlob` (mở rộng glob trên một `rootResolver`), `BundledMerge` (gộp vào `PipelinePacket`). Biến môi trường được đưa vào qua `Func<string,string?>`. | Unit test chạy được **trên Linux**: project test `net10.0` link file nguồn (`<Compile Include="..\GameScanner\BundledGames\*.cs"/>`), dựng cây thư mục giả trong thư mục tạm, ví dụ `AppData/Roaming/.minecraft/runtime/java-runtime-delta/windows-x64/java-runtime-delta/bin/javaw.exe`, `AppData/Local/Roblox/Versions/version-abc/RobloxPlayerBeta.exe`. Glob phải chuẩn hóa dấu phân cách đường dẫn. |
| 2 | `bundled-games.json` với 3–4 mục đã xác minh tốt nhất (Roblox, osu! stable, Minecraft, Rockstar/GTA V) + JSON schema kiểm tra lúc build/test. | Test: mọi mục nhúng parse được, `id` không trùng, glob không chứa ký tự lạ. |
| 3 | Thêm `StepKind.BundledMissing` vào `PipelineRunner` + checkbox trong GUI + `settings.json`. Bước đi qua `PacketValidator` như các bước có sẵn. | Test merge: mục trong `excluded` được cứu, mục trong `included` không bị nhân đôi, `Extra` của mục giữ nguyên. |
| 4 | Khử trùng theo `bundledDbId` trong Danh sách game (sau khi người dùng chốt câu hỏi ở mục 8). | Test: quét hai lần với `version-abc` rồi `version-def` chỉ còn một mục, `exePath` mới. |
| 5 | Kiểm thật trên Windows: máy có Minecraft (launcher mới), Roblox, osu!. Chạy pipeline, xuất `games.json`, chạy GameProcessChecker, mở game, kiểm sensor bật. Cập nhật cột "Xác minh" trong bảng mục 6 theo kết quả. | Checklist thủ công. Ghi đường dẫn thật thấy được vào `notes` của mục DB. |
| 6 | Pha B (tắt mặc định): vị trí quét thêm + tín hiệu mới, ngưỡng riêng, `source: "heuristic-2"`. | Cây giả trên Linux: thư mục Unity, Godot, Electron (`resources.pak`), dùng làm **ca âm tính**. Trên Windows: đếm số mục báo nhầm trên 2–3 máy thật. |
| 7 | (Việc riêng) GameProcessChecker phương án B, sau đó C. | Test `TargetSet` khớp tiền tố. Trên Windows: chạy một app Java khác bằng cùng `javaw.exe`, checker không được báo game đang chạy (sau khi có C). |

## 8. Rủi ro và câu hỏi mở cho người dùng

1. **Khử trùng mục có đường dẫn đổi theo version**: có cho phép bước này **cập nhật tại chỗ** mục trong Danh sách game (trái với quy tắc hiện tại "không bao giờ ghi đè") cho riêng mục có `bundledDbId` không?
2. **Minecraft dùng JRE hệ thống**: có đưa `javaw.exe` hệ thống vào `games.json` không? Nếu chưa có phương án C thì đề xuất **không**, chỉ lấy runtime của launcher.
3. **Có theo dõi launcher không?** (ví dụ `MinecraftLauncher.exe`, `RobloxPlayerLauncher.exe`). Comment trong `TargetConfig.cs` cho thấy launcher lẫn vào candidate đang gây phiền, nên đề xuất mặc định **không**.
4. **Pha B bật hay tắt mặc định?** Đề xuất tắt và chỉ gợi ý (tô màu) cho người dùng xác nhận.
5. **Đọc DB của launcher nhỏ** (SQLite của Amazon, protobuf `product.db` của Battle.net): đáng làm parser riêng hay chỉ dùng glob thư mục? Thêm thư viện SQLite native sẽ ảnh hưởng mục tiêu single-file exe.
6. **Thư mục `WindowsApps`**: chấp nhận bỏ qua game MSIX cũ, hay gọi `Windows.Management.Deployment.PackageManager` (đã có TFM `windows10.0.22000.0`) để lấy `InstallLocation`? Gọi được không hay vẫn bị chặn quyền thì cần thử.
7. **Bảo trì DB**: ai cập nhật khi launcher đổi bố cục (như Bloxstrap v2.8.0)? File override trong `GameScanner_Data` cho phép người dùng tự sửa mà không cần build lại.
8. **Quyền riêng tư và hiệu năng**: Pha B liệt kê thư mục người dùng. Cần giới hạn độ sâu và thời gian (ví dụ tối đa 5 giây cho cả pha, hủy được bằng nút Dừng).
