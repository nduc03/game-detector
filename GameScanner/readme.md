# GameScanner: quét game & đường dẫn `.exe` trên Windows (.NET 10)

`GameScanner.exe` có hai cách dùng trong cùng một file exe:

* **Giao diện WinForms** (mặc định khi mở bằng double-click): quét, lưu danh sách game, thêm thủ công, tự định nghĩa pipeline quét, xuất `games.json` cho GameProcessChecker.
* **Dòng lệnh kiểu Unix filter** (`stdin` / `stdout` / `stderr`), bật bằng cờ `--cli`: để ghép nối bằng `|`, và để chính `GameScanner.exe` làm một mắt xích trong pipeline.

## 1. Giao diện

### Danh sách game

* Mỗi game hiện icon lấy từ file exe, tên, nhà phát hành, đường dẫn exe, nguồn (Steam, Registry…, hoặc Thủ công) và nút **Mở thư mục** (mở Explorer, chọn sẵn file exe).
* **Thêm thủ công…**: chọn file `.exe` → hộp thoại điền các thuộc tính không bắt buộc. Tên và nhà phát hành được điền sẵn từ thông tin phiên bản của file. Game thêm thủ công được ghi vào **Danh sách nhớ → Luôn chọn**.
* **Sửa…** (hoặc double-click, `F2`). Mỗi exe khác của game (`candidateExes`) là một dòng riêng, có nút **…** (chọn file), **⬆** (đưa lên làm exe chính) và **✕** (xóa); dòng cuối có nút **+ Thêm exe khác**. Khi promote, app hỏi xác nhận, kèm ô tick (mặc định tắt) để xóa luôn mọi exe khác (kể cả exe chính cũ).
* **Xóa** (`Del`): bỏ khỏi danh sách (không đụng tới file trên đĩa) và ghi game vào **Danh sách nhớ → Luôn loại** để các lần quét sau tự động loại.
* Ô **Tìm** lọc theo tên, nhà phát hành, đường dẫn.

### Nhận diện "cùng một game"

Mọi chỗ cần biết hai mục có phải cùng một game không (quét lại có thêm trùng không, thêm/sửa thủ công có trùng không, game đã xóa có bị loại không) đều so theo **thư mục**, không theo `exePath`, để đổi exe chính không làm game bị thêm lại:

* Thư mục của một mục là `installDir`, không có thì thư mục chứa exe chính.
* Cùng game khi: cùng thư mục; hoặc cùng file exe; hoặc exe của bên này nằm trong thư mục của bên kia **và ngược lại** (một bên ghi thư mục gốc, bên kia ghi thư mục con chứa exe, ví dụ Unreal `...\Binaries\Win64`). Điều kiện hai chiều tránh nhầm thư mục launcher (ví dụ `Steam`) với các game nằm bên trong nó.
* Không phân biệt hoa thường, `/` và `\` coi như nhau, bỏ qua `\` ở cuối. `D:\Games\A` không khớp `D:\Games\AB`.

### Dữ liệu của app (portable)

Lưu dạng JSON trong thư mục **`GameScanner_Data` cạnh `GameScanner.exe`** (menu *Tệp → Mở thư mục dữ liệu*). Chép cả thư mục chứa exe sang máy khác là mang theo dữ liệu; vì thế thư mục đó phải ghi được (đừng đặt trong `Program Files`).

| File | Nội dung |
| :--- | :--- |
| `library.json` | Danh sách game đã quét / thêm thủ công. |
| `settings.json` | Các bước pipeline (bật/tắt, pipeline tùy chỉnh), tùy chọn xuất. |
| `rules.json` | Danh sách nhớ (bước cuối pipeline). Cùng định dạng với `--rules` của chế độ dòng lệnh (mục 5). |

Chọn JSON thay vì SQLite vì dữ liệu chỉ vài trăm mục, không cần truy vấn, sửa tay được, và không phải đóng gói thêm thư viện native vào file exe duy nhất. File hỏng không bị ghi đè: app đổi tên nó thành `*.hong-<thời gian>` và bắt đầu lại từ rỗng.

### Pipeline quét

Khung **Pipeline quét** liệt kê các bước theo thứ tự chạy từ trên xuống:

1. **Thu thập (Collect)** – có sẵn, luôn chạy, không tắt được. Đọc Registry `Uninstall` và cấu hình của Steam, Epic Games, Ubisoft Connect, GOG Galaxy, EA App, Riot Games. Không phân loại: **mọi mục được đặt vào `excluded`**, kèm `source` và cờ `launcherGame` / `redistributable` để bước sau dùng. Đây là nguồn dữ liệu nên không dùng packet đầu vào.
2. **Heuristic** – có sẵn, tắt được. Chấm điểm các mục trong `excluded`, mục được nhận là game thì chuyển sang `included`. Không đụng tới mục đã ở `included`, nên chạy lại nhiều lần vẫn an toàn. Tắt bước này thì chỉ pipeline tùy chỉnh và danh sách nhớ quyết định game nào được chọn.
3. **Các pipeline tùy chỉnh** – do bạn định nghĩa, sắp thứ tự bằng nút ▲ ▼, tắt được hoặc xóa hẳn.
4. **Danh sách nhớ (include / exclude)** – có sẵn, **luôn ở cuối** (pipeline mới thêm vào vẫn nằm trên nó), không xóa được, chỉ tắt được. Bấm **Sửa…** để xem và chỉnh (mục bên dưới).

Bỏ tick ở một bước, hoặc chọn bước rồi bấm **Tắt**, để bỏ qua bước đó khi chạy mà vẫn giữ cấu hình (bước tắt hiện chữ mờ). Bấm **Bật** để dùng lại. Nút **Tắt tất cả / Bật tất cả** (cạnh **Chạy toàn bộ**, hoặc menu *Pipeline → Bật / tắt tất cả các bước*) tắt hoặc bật cùng lúc mọi bước trừ bước 1 (Collect luôn chạy): đang bật hết thì tắt hết, còn lại thì bật hết.

Cách chạy:

* **Chạy toàn bộ** (`F5`): chạy các bước đang bật từ trên xuống, bắt đầu từ Collect; kết quả bước trước là đầu vào bước sau. Không có chế độ chạy lẻ một bước: muốn thử riêng một bước, tắt các bước khác (bước 1 vẫn chạy để có dữ liệu).

Quy tắc xử lý kết quả:

* Sau **mỗi bước**, kết quả được kiểm tra lại cấu trúc packet (mục 3). Sai cấu trúc là lỗi.
* **Một bước lỗi vì bất kỳ lý do gì** (không tìm thấy chương trình, mã thoát khác 0, quá thời gian, thiếu file kết quả, JSON sai cấu trúc, bấm Dừng…) thì pipeline ngắt ngay và **hủy toàn bộ kết quả từ bước đầu tiên**; danh sách game giữ nguyên.
* Khi mọi bước thành công, app lấy **trạng thái cuối cùng** của pipeline: mục nào trong `included` chưa có trong danh sách (so theo thư mục) thì được thêm (tô nền xanh nhạt). **Game cũ không bao giờ bị xóa hay ghi đè khi quét lại**, kể cả khi lần quét này không còn thấy nó.

### Danh sách nhớ

Thay cho việc viết `rules.json` bằng tay. Cửa sổ có hai tab:

* **Luôn loại (exclude)**: game bạn đã xóa khỏi danh sách (tự thêm khi xóa), cùng các mẫu đường dẫn luôn loại.
* **Luôn chọn (include)**: game bạn đã thêm thủ công (tự thêm), cùng các mẫu đường dẫn luôn chọn. Game luôn chọn mà lần quét không thấy sẽ được thêm lại nếu thư mục/exe còn trên đĩa.

Mỗi tab có: **Thêm thư mục game…**, **Thêm file exe…**, **Thêm mẫu đường dẫn…**, **Sửa…**, **Xóa (quên)**, **Chuyển sang tab kia**. Một game không thể nằm ở cả hai tab: thêm vào tab này sẽ tự bỏ khỏi tab kia.

Mẫu đường dẫn (không phân biệt hoa thường, so với `installDir` và `exePath`):

| Mẫu | Khớp |
| :--- | :--- |
| `D:\Games\MyGame` (đường dẫn tuyệt đối) | Chính nó và mọi thứ bên trong, đúng ranh giới thư mục (không khớp `D:\Games\MyGame2`). |
| `*\Games\*` (có `*`) | Wildcard, khớp toàn chuỗi. |
| `regex:\\Ubisoft\\.*\.exe$` | Biểu thức chính quy. |
| `Common Files` (còn lại) | Chứa chuỗi con. |

Thứ tự ưu tiên khi quyết định một mục: game luôn chọn → game luôn loại → mẫu luôn chọn → mẫu luôn loại; không khớp gì thì giữ nguyên vị trí.

### Tự định nghĩa pipeline

*Pipeline → Thêm pipeline tùy chỉnh…* mở cửa sổ con:

| Trường | Ý nghĩa |
| :--- | :--- |
| **Chương trình (exe)** | Nhập tay hoặc bấm *Chọn…*. Hợp lệ khi là đường dẫn tuyệt đối tới file `.exe/.com/.bat/.cmd` đang tồn tại, hoặc tên lệnh có trong `PATH` (`python`, `node`, `pwsh`…). Kết quả kiểm tra hiện ngay dưới ô nhập. |
| **Tham số (args)** | Dòng tham số, có thể chèn biến (bảng dưới). |
| **Truyền packet vào stdin** | Tick để ghi packet đầu vào vào `stdin` của chương trình, giống nối bằng `\|`. |
| **Lấy kết quả từ** | Một trong ba: `stdout` của chương trình; file tại một đường dẫn cố định; file chương trình ghi ra `$out`. |
| **Giới hạn thời gian** | Số giây tối đa (0 = không giới hạn). |

Biến chèn vào tham số:

| Biến | Thay bằng |
| :--- | :--- |
| `$in` | Đường dẫn file JSON chứa packet đầu vào (trước khi xử lý). |
| `$out` | Đường dẫn file để chương trình ghi packet đã xử lý. |

Biến nằm ngoài cặp nháy kép được tự bọc nháy; biến đã nằm trong cặp nháy thì chèn nguyên văn. Đường dẫn khác trong tham số (ví dụ file script) nên viết dạng tuyệt đối.

Ví dụ:

| Mục đích | Chương trình | Tham số | stdin | Lấy kết quả từ |
| :--- | :--- | :--- | :--- | :--- |
| Script Python kiểu filter | `python` | `D:\tools\ai_filter_stage.py` | ✔ | stdout |
| Script đọc/ghi file | `node` | `D:\tools\rescue.js --in $in --out $out` | ✘ | file `$out` |
| Áp thêm một file rules sau cùng | `C:\Tools\GameScanner.exe` | `--rules D:\configs\post_ai_rules.json` | ✔ | stdout |

File kết quả ở đường dẫn cố định phải được ghi lại trong lần chạy đó; file cũ từ lần trước bị coi là lỗi.

### Xuất cho GameProcessChecker

Nút **Xuất JSON cho GameProcessChecker…** ghi file `games.json` đã tối giản: mảng ở gốc, không thụt dòng, mỗi mục chỉ còn những trường checker thực sự đọc, bỏ các đường dẫn trùng nhau:

```json
[{"exePath":"D:\\Games\\A\\a.exe","candidateExes":["D:\\Games\\A\\launcher.exe"]},{"exePath":"D:\\Games\\B\\b.exe"}]
```

Bỏ tick *Tệp → Xuất kèm exe khác của game (candidateExes)* nếu chỉ muốn theo dõi exe chính. Đặt file cạnh `GameProcessChecker.exe` với tên `games.json`, hoặc truyền đường dẫn file làm tham số khi chạy checker.

## 2. Chế độ dòng lệnh (Unix filter)

`GameScanner.exe` chạy chế độ dòng lệnh thay vì mở giao diện khi có ít nhất một trong các cờ `--cli`, `--rules <file>` (`-r`), `--no-exclude-output`. **Khác với bản console trước đây, gọi trần `GameScanner.exe > file.json` sẽ mở giao diện; cần thêm `--cli`.** Việc chuyển hướng `stdin` / `stdout` không tự bật chế độ này, vì nhiều trình khởi chạy (Git Bash, IDE, script) luôn nối pipe vào app.

```powershell
# Quét máy: Collect -> Heuristic -> Rules (nếu có rules.json), xuất cả included lẫn excluded
.\GameScanner.exe --cli > scan_full.json

# Chỉ xuất danh sách đã chọn
.\GameScanner.exe --no-exclude-output > games_clean.json

# Chỉ định file rules
.\GameScanner.exe --rules D:\configs\my_rules.json > scan_with_rules.json

# Nối với bước lọc khác, rồi dùng lại GameScanner ở cuối để cắt phần excluded
.\GameScanner.exe --cli | python ai_filter_stage.py | .\GameScanner.exe --no-exclude-output > games.json

# Nhận packet từ stdin và áp thêm một file rules
.\GameScanner.exe --cli | python ai_filter_stage.py | .\GameScanner.exe --rules post_ai_rules.json --no-exclude-output > games.json
```

Ghi chú:

* Khi nhận packet từ `stdin`, rules chỉ được áp nếu truyền `--rules` tường minh.
* Packet từ `stdin` sai cấu trúc, hoặc bất kỳ lỗi nào khác, làm chương trình thoát với mã 1 (lỗi ghi ra `stderr`).
* App là chương trình Windows (không có console riêng). PowerShell đợi nó chạy xong khi đầu ra được chuyển hướng hoặc nối pipe như các ví dụ trên. Ở dấu nhắc `cmd` gõ tay, `cmd` trả dấu nhắc ngay mà không đợi và không cập nhật `%ERRORLEVEL%`; dùng `start /wait GameScanner.exe --cli ...` nếu cần đợi hoặc cần mã thoát.

## 3. Cấu trúc packet (`PipelinePacket`)

Mọi bước trong pipeline (bước có sẵn, pipeline tùy chỉnh trong GUI, hay chương trình nối bằng `|`) trao đổi cùng một cấu trúc JSON, mã hóa UTF-8:

```json
{
  "included": [
    {
      "name": "Black Myth: Wukong",
      "publisher": "Game Science",
      "exePath": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1\\Binaries\\Win64\\b1-Win64-Shipping.exe",
      "installDir": "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong",
      "candidateExes": [
        "D:\\SteamLibrary\\steamapps\\common\\BlackMythWukong\\b1.exe"
      ],
      "source": "Steam",
      "launcherGame": true
    }
  ],
  "excluded": [
    {
      "name": "Discord",
      "publisher": "Discord Inc.",
      "exePath": "C:\\Users\\User\\AppData\\Local\\Discord\\app-1.0.9015\\Discord.exe",
      "installDir": "C:\\Users\\User\\AppData\\Local\\Discord",
      "candidateExes": []
    }
  ]
}
```

Quy tắc được kiểm tra sau mỗi bước:

| Thành phần | Yêu cầu |
| :--- | :--- |
| Gốc | Phải là object. |
| `included` | Bắt buộc, là mảng các mục. |
| `excluded` | Không bắt buộc; là mảng các mục, `null`, hoặc vắng mặt (khi dùng `--no-exclude-output`). |
| `name`, `publisher`, `exePath`, `installDir`, `source` | Chuỗi hoặc `null`. |
| `candidateExes` | Mảng chuỗi, `null`, hoặc vắng mặt. |
| `launcherGame`, `redistributable` | `true` / `false` / `null`, hoặc vắng mặt. Collect đặt `launcherGame: true` cho mục lấy từ launcher và `redistributable: true` cho gói redistributable; Heuristic dùng hai cờ này. |
| Mục trong `included` | Phải có `exePath` hoặc `installDir`. |
| Trường lạ | Được chấp nhận và giữ nguyên khi chuyển cho bước sau (ví dụ điểm số do bước AI thêm vào). |

Tên trường không phân biệt hoa thường. Một bước có thể chuyển mục từ `excluded` sang `included` (cứu mục bị lọc nhầm), chuyển ngược lại, hoặc đổi `exePath` với một đường dẫn trong `candidateExes`.

Quy ước cho chương trình làm một bước:

| Luồng | Quy định |
| :--- | :--- |
| `stdin` | Nhận packet JSON UTF-8 không BOM (nếu bật truyền qua stdin / nối bằng `\|`). |
| `stdout` | Chỉ in packet JSON kết quả, không in log. |
| `stderr` | Mọi log, tiến độ, cảnh báo. Trong GUI, các dòng này hiện ở khung nhật ký. |
| Mã thoát | 0 là thành công; khác 0 là lỗi. |

## 4. Mẫu một bước tùy chỉnh (Node.js)

```javascript
// Dòng lệnh: .\GameScanner.exe --cli | node rescue_stage.js | .\GameScanner.exe --no-exclude-output
// Trong GUI: Chương trình = node, Tham số = D:\tools\rescue_stage.js, tick stdin, lấy kết quả từ stdout
const fs = require("fs");

const packet = JSON.parse(fs.readFileSync(0, "utf-8"));
const included = packet.included || [];
const remainingExcluded = [];

// 1. Cứu lại các mục bị lọc nhầm
for (const item of packet.excluded || []) {
  if (item.installDir && item.installDir.toLowerCase().includes("indiegames")) {
    included.push(item);
  } else {
    remainingExcluded.push(item);
  }
}

// 2. Ưu tiên chọn file *-Win64-Shipping.exe từ candidateExes làm exe chính
for (const item of included) {
  const candidates = item.candidateExes || [];
  const idx = candidates.findIndex((exe) => exe.toLowerCase().endsWith("-win64-shipping.exe"));
  if (idx !== -1 && !(item.exePath || "").toLowerCase().endsWith("-win64-shipping.exe")) {
    const chosen = candidates.splice(idx, 1)[0];
    if (item.exePath) candidates.unshift(item.exePath);
    item.exePath = chosen;
    item.candidateExes = candidates;
  }
}

packet.included = included;
packet.excluded = remainingExcluded;

console.error(`[rescue] ${included.length} included, ${remainingExcluded.length} excluded.`);
process.stdout.write(JSON.stringify(packet));
```

## 5. File rules (`rules.json`)

GUI lưu Danh sách nhớ vào `GameScanner_Data\rules.json`; chế độ dòng lệnh đọc cùng định dạng qua `--rules <file>`. Không truyền `--rules` thì chế độ dòng lệnh tự tìm `rules.json` ở thư mục hiện tại, cạnh `GameScanner.exe`, rồi tới `GameScanner_Data\rules.json` (khi quét máy; khi nhận packet từ `stdin` thì chỉ áp rules nếu có `--rules`).

```json
{
  "includeApps": [
    { "name": "Arknights", "installDir": "C:\\YostarGames\\Arknights_EN", "exePath": "C:\\YostarGames\\Arknights_EN\\Arknights.exe", "candidateExes": [] }
  ],
  "excludeApps": [
    { "name": "Discord", "installDir": "C:\\Users\\User\\AppData\\Local\\Discord" }
  ],
  "includePaths": ["*\\Games\\*", "D:\\Portable\\MyGame"],
  "excludePaths": ["*\\Program Files\\Common Files\\*"]
}
```

* `includeApps` / `excludeApps`: game được nhớ, so theo thư mục như mục "Nhận diện cùng một game". Game trong `includeApps` mà packet không có sẽ được thêm mới nếu còn trên đĩa.
* `includePaths` / `excludePaths`: mẫu đường dẫn như bảng ở mục "Danh sách nhớ". Đường dẫn tuyệt đối trong `includePaths` (file `.exe` hoặc thư mục) mà chưa mục nào khớp thì được thêm mới vào `included`.
* Thứ tự ưu tiên: `includeApps` → `excludeApps` → `includePaths` → `excludePaths`; không khớp gì thì giữ nguyên vị trí.
* Thay đổi so với trước: đường dẫn tuyệt đối không có `*` giờ khớp đúng ranh giới thư mục (trước đây là "chứa chuỗi con", nên `C:\YostarGames\Arknights_EN` từng khớp cả `Arknights_EN_Beta`). Muốn khớp theo tiền tố thì dùng wildcard, ví dụ `C:\YostarGames\Arknights*`.

## 6. Đóng gói thành một file `.exe`

Trên máy Windows có .NET 10 SDK:

```powershell
dotnet publish GameScanner/GameScanner.csproj -c Release -o ./publish
```

Các thiết lập self-contained, single-file, `win-x64` đã nằm sẵn trong `GameScanner.csproj`. Icon ứng dụng lấy từ `app.ico` (nguồn và giấy phép: xem `ICON-LICENSE.md` ở gốc repo).
