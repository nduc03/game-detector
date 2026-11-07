# GameScanner: Kiến trúc Unix Pipeline phát hiện Game & Đường dẫn `.exe` trên Windows (.NET 10)

Tài liệu này mô tả thiết kế của `GameScanner.exe`, chuẩn giao tiếp **Unix Filter Pipeline** (`stdin` / `stdout` / `stderr`) với cấu trúc JSON phân tách rõ ràng giữa dữ liệu **đã chọn (`included`)** và dữ liệu **đã bị lọc (`excluded`)**, cùng hướng dẫn tạo các bước lọc mở rộng (như AI / `jev`) ghép nối qua đường ống hệ điều hành (`|`).

## 1. Triết lý thiết kế

1. **`GameScanner.exe` gọn nhẹ, tập trung đúng nhiệm vụ cốt lõi**:
   * **Quét từ Registry & cấu hình động của Launcher**: Không fix cứng đường dẫn ổ đĩa; tự động đọc nhánh Registry `Uninstall` (64-bit, `WOW6432Node` 32-bit, `CurrentUser`) và cấu hình gốc của **Steam** (`libraryfolders.vdf` -> `appmanifest_*.acf`), **Epic Games** (`*.item`), **Ubisoft Connect**, **GOG Galaxy**, **EA App**, **Riot Games**.
   * **Chỉ hỗ trợ 2 bộ lọc nội tại (`Heuristic` + `Rules`)**:
     * Bộ lọc **Heuristic** luôn chạy để chấm điểm, nhận diện cấu trúc Game Engine (Unity, Unreal `Win64-Shipping`, SteamAPI, EOS, DLSS...) và loại bỏ các file `.exe` phụ trợ (crash handler, uninstaller, redist).
     * Bộ lọc **Rules (`Include/Exclude`)** **tự động kích hoạt** nếu tìm thấy file `rules.json` (nằm cạnh `GameScanner.exe`, tại thư mục hiện tại, hoặc truyền qua `--rules <file>`), và **tự động bỏ qua** nếu không có file cấu hình nào.
   * **Mọi bộ lọc nâng cao (như gọi AI / `jev`) là Custom Pipeline độc lập**: Không nhồi nhét cờ `--no-ai`, `--no-rules`, `--no-heuristic` vào `GameScanner.exe`; bất kỳ bước xử lý nâng cao nào đều là một tiến trình độc lập nối qua dấu `|`.
2. **Mặc định xuất cả phần ĐÃ CHỌN và phần ĐÃ BỊ LỌC ở 2 nơi tách biệt trong JSON**:
   * Để các bước phía sau trong pipeline có thể kiểm tra lại, cứu (promote) các game bị Heuristic lọc nhầm hoặc chọn lại file `.exe` chính xác hơn, JSON đầu ra mặc định luôn giữ lại đầy đủ cả phần được chọn lẫn phần bị loại ở **2 cấp độ**:
     * **Cấp gói dữ liệu (`PipelinePacket`)**: Tách thành 2 mảng `"included"` (các mục được chọn) và `"excluded"` (các mục bị lọc loại bỏ).
     * **Cấp từng ứng dụng (`AppCandidate`)**: Tách đường dẫn thực thi thành `"exePath"` (1 đường dẫn `.exe` chính đã được chọn), `"candidateExes"` (các đường dẫn `.exe` hợp lệ khác trong cùng thư mục cài đặt) và `"excludedExes"` (các đường dẫn `.exe` đã bị lọc bỏ như `UnityCrashHandler64.exe`, `unins000.exe`).
   * **Cờ `--no-exclude-output`**: Chỉ khi truyền cờ `--no-exclude-output` thì chương trình mới lược bỏ hoàn toàn `"excluded"` và `"excludedExes"` khỏi JSON đầu ra.
3. **Tương thích 100% với Single-File `.exe` trên .NET 10**:
   * Sử dụng `AppContext.BaseDirectory` và `System.Text.Json` Source Generator (`[JsonSerializable]`), đóng gói toàn bộ thành 1 file `.exe` độc lập duy nhất.

## 2. Chuẩn giao tiếp Pipeline (Pipeline Protocol Specification)

Mọi chương trình `.exe` hoặc script chạy qua trình thông dịch (`python`, `node`, `pwsh`, `jev`...) trong chuỗi pipeline tuân thủ 5 quy tắc sau:

| Thành phần | Quy định chuẩn trong Pipeline |
| :--- | :--- |
| **Luồng đầu vào (`stdin`)** | Đọc chuỗi JSON mã hóa **UTF-8 (không BOM)** theo cấu trúc `PipelinePacket`. |
| **Luồng đầu ra (`stdout`)** | **Chỉ** in ra chuỗi JSON hợp lệ của `PipelinePacket`. Tuyệt đối không in log tiến độ ra `stdout`. |
| **Luồng nhật ký (`stderr`)** | Mọi thông báo tiến độ, cảnh báo hoặc lỗi phải ghi ra `stderr` (`Console.Error` trong C# hoặc `sys.stderr` trong Python). |
| **Luân chuyển 2 chiều giữa `included` và `excluded`** | Một stage trong pipeline có thể chuyển một mục từ `"excluded"` sang `"included"` (cứu lại mục bị lọc nhầm), chuyển từ `"included"` sang `"excluded"` (loại bỏ thêm), hoặc hoán đổi `"exePath"` với một đường dẫn nằm trong `"candidateExes"` / `"excludedExes"`. |
| **Lược bỏ dữ liệu thừa ở cuối chuỗi (`--no-exclude-output`)** | Giữa các bước trung gian, luôn giữ cả `"excluded"` và `"excludedExes"`. Ở mắt xích cuối cùng của pipeline (hoặc khi chạy đơn lẻ cần kết quả gọn), truyền thêm cờ `--no-exclude-output` để xóa sạch `"excluded"` và `"excludedExes"` khỏi JSON. |

### Cấu trúc JSON chuẩn (`PipelinePacket`) (đã thay đổi cấu trúc chưa cập nhật readme)

Khi chạy mặc định (`GameScanner.exe`), JSON đầu ra tách biệt rõ phần đã chọn và phần đã bị lọc ở cả cấp danh sách lẫn cấp đường dẫn `.exe`:

```json
{
  "schemaVersion": "2.0",
  "generatedAtUtc": "2026-11-07T07:30:00Z",
  "executedStages": [
    "collect",
    "heuristic",
    "rules"
  ],
  "included": [
    {
      "id": "steam:2358720",
      "name": "Black Myth: Wukong",
      "source": "Steam",
      "publisher": "Game Science",
      "installDir": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong",
      "exePath": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1\\Binaries\\Win64\\b1-Win64-Shipping.exe",
      "candidateExes": [
        "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1.exe"
      ],
      "excludedExes": [
        "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\Engine\\Extras\\Redist\\en-us\\UEPrereqSetup_x64.exe"
      ],
      "confidenceScore": 100,
      "classification": "game",
      "reasons": [
        "Collect: Phát hiện từ Steam libraryfolders.vdf & appmanifest.acf",
        "Heuristic: Phát hiện thư viện Game Engine/Graphics (Unity/Unreal/SteamAPI/EOS/DLSS)",
        "Rules: Khớp danh sách Include (giữ lại trong included)"
      ],
      "metadata": {
        "steamAppId": "2358720"
      }
    }
  ],
  "excluded": [
    {
      "id": "reg:Discord",
      "name": "Discord",
      "source": "RegistryUninstall",
      "publisher": "Discord Inc.",
      "installDir": "C:\\Users\\User\\AppData\\Local\\Discord",
      "exePath": "C:\\Users\\User\\AppData\\Local\\Discord\\app-1.0.9015\\Discord.exe",
      "candidateExes": [],
      "excludedExes": [
        "C:\\Users\\User\\AppData\\Local\\Discord\\Update.exe"
      ],
      "confidenceScore": 0,
      "classification": "non_game",
      "reasons": [
        "Collect: Phát hiện từ Registry Uninstall (CurrentUser\\Registry64)",
        "Heuristic: Tên khớp từ khóa phần mềm hệ thống/công cụ/launcher",
        "Rules: Khớp danh sách Exclude (chuyển sang excluded)"
      ],
      "metadata": {
        "registryKey": "Discord"
      }
    }
  ]
}
```

Khi thêm cờ `--no-exclude-output` (`GameScanner.exe --no-exclude-output`), cả mảng `"excluded"` lẫn trường `"excludedExes"` bên trong từng mục đều được loại bỏ khỏi JSON:

```json
{
  "schemaVersion": "2.0",
  "generatedAtUtc": "2026-11-07T07:30:00Z",
  "executedStages": [
    "collect",
    "heuristic",
    "rules"
  ],
  "included": [
    {
      "id": "steam:2358720",
      "name": "Black Myth: Wukong",
      "source": "Steam",
      "publisher": "Game Science",
      "installDir": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong",
      "exePath": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1\\Binaries\\Win64\\b1-Win64-Shipping.exe",
      "candidateExes": [
        "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1.exe"
      ],
      "confidenceScore": 100,
      "classification": "game",
      "reasons": [
        "Collect: Phát hiện từ Steam libraryfolders.vdf & appmanifest.acf",
        "Heuristic: Phát hiện thư viện Game Engine/Graphics (Unity/Unreal/SteamAPI/EOS/DLSS)",
        "Rules: Khớp danh sách Include (giữ lại trong included)"
      ],
      "metadata": {
        "steamAppId": "2358720"
      }
    }
  ]
}
```

## 3. Cách sử dụng `GameScanner.exe` và ghép nối Custom Pipeline

### Chạy độc lập
* **Khi chưa có file `rules.json`**: Chương trình tự động chạy `Collect -> Heuristic` và bỏ qua `Rules`:
  ```powershell
  # Xuất đầy đủ cả included, excluded và excludedExes
  .\GameScanner.exe > scan_full.json

  # Chỉ xuất danh sách đã chọn (bỏ excluded và excludedExes khỏi JSON)
  .\GameScanner.exe --no-exclude-output > games_clean.json
  ```
* **Khi đặt file `rules.json` cùng thư mục (hoặc chỉ định `--rules <đường_dẫn>`)**: Chương trình tự động kích hoạt thêm bước `Rules`:
  ```powershell
  .\GameScanner.exe --rules D:\configs\my_rules.json > scan_with_rules.json
  ```

### Nối chuỗi với các Custom Pipeline (AI / `jev` / Script)
Vì `GameScanner.exe` mặc định xuất cả `"included"` và `"excluded"`, các công cụ phía sau nhận được bức tranh toàn cảnh để lọc lại, và ở bước cuối cùng bạn có thể dùng chính `GameScanner.exe --no-exclude-output` (hoặc cờ `--no-exclude-output` của script) để làm sạch JSON:

```powershell
# Nối qua script Python gọi AI/jev, sau đó lọc sạch excluded ở bước cuối:
.\GameScanner.exe | python ai_filter_stage.py --no-exclude-output > games.json

# Hoặc dùng chính GameScanner.exe ở cuối đường ống để cắt bỏ phần excluded:
.\GameScanner.exe | python ai_filter_stage.py | .\GameScanner.exe --no-exclude-output > games.json

# Áp dụng thêm một file rules thứ hai sau khi AI đã phân loại xong:
.\GameScanner.exe | python ai_filter_stage.py | .\GameScanner.exe --rules post_ai_rules.json --no-exclude-output > games.json
```

## 4. Hướng dẫn tự tạo một Custom Pipeline Stage mới

Bất kỳ chương trình hay script nào đọc JSON từ `stdin` và ghi JSON ra `stdout` đều có thể làm một mắt xích trong pipeline. Dưới đây là mẫu chuẩn bằng **Node.js** (`rescue_stage.js`) minh họa cách duyệt qua `"excluded"` để cứu lại game và chọn lại `"exePath"` từ `"candidateExes"`:

```javascript
// Chạy trong pipeline: .\GameScanner.exe | node rescue_stage.js | .\GameScanner.exe --no-exclude-output
const fs = require("fs");

const rawInput = fs.readFileSync(0, "utf-8").trim();
if (!rawInput) process.exit(0);

const packet = JSON.parse(rawInput);
const included = packet.included || [];
const excluded = packet.excluded || [];

const remainingExcluded = [];

// 1. Duyệt mảng excluded để cứu lại các mục bị lọc nhầm
for (const item of excluded) {
  if (item.installDir && item.installDir.toLowerCase().includes("indiegames")) {
    item.classification = "game";
    item.confidenceScore = 90;
    item.reasons = item.reasons || [];
    item.reasons.push("CustomNodeStage: Cứu từ excluded vì nằm trong thư mục IndieGames");
    included.push(item);
  } else {
    remainingExcluded.push(item);
  }
}

// 2. Duyệt mảng included để ưu tiên chọn file *-Win64-Shipping.exe từ candidateExes
for (const item of included) {
  const candidates = item.candidateExes || [];
  const shippingIdx = candidates.findIndex((exe) =>
    exe.toLowerCase().endsWith("-win64-shipping.exe")
  );
  if (shippingIdx !== -1 && !item.exePath.toLowerCase().endsWith("-win64-shipping.exe")) {
    const chosen = candidates.splice(shippingIdx, 1)[0];
    if (item.exePath) candidates.unshift(item.exePath);
    item.exePath = chosen;
    item.reasons.push("CustomNodeStage: Đổi exePath sang binary Win64-Shipping");
  }
}

packet.included = included;
packet.excluded = remainingExcluded;
packet.executedStages = packet.executedStages || [];
packet.executedStages.push("node-rescue-stage");

// Ghi log ra stderr, xuất JSON ra stdout
console.error(`[node-rescue-stage] Hiện có ${included.length} included, ${remainingExcluded.length} excluded.`);
process.stdout.write(JSON.stringify(packet, null, 2));
```

## 5. Lệnh đóng gói toàn bộ thành 1 file `.exe` duy nhất (.NET 10)

Đặt `GameScanner.csproj` và `GameScanner.cs` trong cùng một thư mục trên máy Windows có cài đặt .NET 10 SDK và chạy:

```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```