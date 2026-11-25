# GameScanner: tài liệu kỹ thuật pipeline

Tài liệu này dành cho người muốn **viết pipeline tùy chỉnh** (script hay chương trình riêng nối vào chuỗi quét), dùng **chế độ dòng lệnh**, hoặc **sửa `rules.json` bằng tay**. Nếu chỉ cần quét game và xuất danh sách, xem [hướng dẫn sử dụng](../readme.md).

File này được chép vào thư mục `docs` cạnh `GameScanner.exe` khi build. Trong app, mở nó từ menu *Pipeline → Tài liệu viết pipeline tùy chỉnh* hoặc từ liên kết trong cửa sổ *Thêm / Sửa pipeline tùy chỉnh*.

## 1. Pipeline chạy thế nào

Mỗi lần quét là một chuỗi bước chạy từ trên xuống. Các bước trao đổi với nhau một **packet** JSON (mục 3): kết quả của bước trước là đầu vào của bước sau.

| # | Bước | Vai trò với packet |
| :--- | :--- | :--- |
| 1 | **Thu thập (Collect)** | Luôn chạy đầu tiên, không tắt được. Đọc Registry `Uninstall` và cấu hình của Steam, Epic Games, Ubisoft Connect, GOG Galaxy, EA App, Riot Games. Không phân loại: **mọi mục được đặt vào `excluded`**, kèm `source` và cờ `launcherGame` / `redistributable`. Đây là nguồn dữ liệu nên không dùng packet đầu vào: mỗi lần chạy luôn bắt đầu từ packet rỗng. |
| 2 | **Heuristic** | Tắt được. Chấm điểm các mục trong `excluded`, mục được nhận là game thì chuyển sang `included`. Không đụng tới mục đã ở `included`, nên chạy lại nhiều lần vẫn an toàn. Tắt bước này thì chỉ pipeline tùy chỉnh và Danh sách nhớ quyết định mục nào vào `included`. |
| 3 | **Các pipeline tùy chỉnh** | Chương trình của bạn (mục 2), chạy theo thứ tự trong danh sách. |
| 4 | **Danh sách nhớ** | Luôn ở cuối, chỉ tắt được. Áp `rules.json` (mục 7): chuyển mục giữa `included` / `excluded` và thêm lại game luôn chọn còn trên đĩa. |

Quy tắc xử lý kết quả:

* Sau **mỗi bước**, packet được kiểm tra lại cấu trúc (mục 3). Sai cấu trúc là lỗi.
* **Một bước lỗi vì bất kỳ lý do gì** (không tìm thấy chương trình, mã thoát khác 0, quá thời gian, thiếu file kết quả, JSON sai cấu trúc, người dùng bấm Dừng…) thì pipeline ngắt ngay và **hủy toàn bộ kết quả từ bước đầu tiên**; danh sách game giữ nguyên.
* Khi mọi bước thành công, app lấy **trạng thái cuối cùng**: mục nào trong `included` chưa có trong danh sách (so theo thư mục, mục 8) thì được thêm. `excluded` chỉ để các bước sau tham khảo và cứu lại, không ảnh hưởng tới danh sách. Game đã có trong danh sách không bao giờ bị xóa hay ghi đè khi quét lại.

## 2. Pipeline tùy chỉnh trong giao diện

*Pipeline → Thêm pipeline tùy chỉnh…* mở cửa sổ con:

| Trường | Ý nghĩa |
| :--- | :--- |
| **Tên** | Tên hiện trong danh sách bước. Để trống thì dùng tên chương trình. |
| **Chương trình (exe)** | Nhập tay hoặc bấm *Chọn…*. Hợp lệ khi là đường dẫn tuyệt đối tới file `.exe/.com/.bat/.cmd` đang tồn tại, hoặc tên lệnh có trong `PATH` (`python`, `node`, `pwsh`…). Kết quả kiểm tra hiện ngay dưới ô nhập. |
| **Tham số (args)** | Dòng tham số, có thể chèn biến (bảng dưới). |
| **Truyền dữ liệu đầu vào (packet) qua stdin** | Tick để ghi packet đầu vào vào `stdin` của chương trình, giống nối bằng `\|`. |
| **Lấy kết quả từ** | Một trong ba: `stdout` của chương trình; file tại một đường dẫn cố định; file chương trình ghi ra `$out`. |
| **Giới hạn thời gian (giây)** | Số giây tối đa, 0 = không giới hạn. Quá thời gian thì chương trình bị dừng và cả lần quét bị hủy. |

Biến chèn vào tham số:

| Biến | Thay bằng |
| :--- | :--- |
| `$in` | Đường dẫn file JSON chứa packet đầu vào (trước khi xử lý). |
| `$out` | Đường dẫn file để chương trình ghi packet đã xử lý. |

* Biến nằm ngoài cặp nháy kép được tự bọc nháy; biến đã nằm trong cặp nháy thì chèn nguyên văn. Vì vậy viết `--in $in` hay `--in "$in"` đều được.
* Đường dẫn khác trong tham số (ví dụ file script) nên viết dạng tuyệt đối: thư mục làm việc của chương trình là thư mục chứa exe của nó (ví dụ thư mục cài `python`), không phải thư mục chứa script.
* Không tick stdin và tham số cũng không có `$in` thì chương trình không nhận được packet đầu vào; app sẽ hỏi lại trước khi lưu.
* File kết quả ở đường dẫn cố định phải được ghi lại trong lần chạy đó; file cũ từ lần trước bị coi là lỗi.

Ví dụ:

| Mục đích | Chương trình | Tham số | stdin | Lấy kết quả từ |
| :--- | :--- | :--- | :--- | :--- |
| Script Python kiểu filter | `python` | `D:\tools\ai_filter_stage.py` | ✔ | stdout |
| Script đọc/ghi file | `node` | `D:\tools\rescue.js --in $in --out $out` | ✘ | file `$out` |
| Áp thêm một file rules sau cùng | `C:\Tools\GameScanner.exe` | `--rules D:\configs\post_ai_rules.json` | ✔ | stdout |

## 3. Cấu trúc packet (`PipelinePacket`)

Mọi bước (bước có sẵn, pipeline tùy chỉnh trong GUI, hay chương trình nối bằng `|`) trao đổi cùng một cấu trúc JSON, mã hóa UTF-8:

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

* `included`: các mục được chọn là game.
* `excluded`: các mục bị loại, giữ lại để bước sau có thể cứu.
* `exePath`: exe chính của game. `candidateExes`: các exe khác cũng được tính là game đang chạy (launcher, bản 64-bit…); GameProcessChecker theo dõi cả hai.

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

## 4. Quy ước cho chương trình làm một bước

| Luồng | Quy định |
| :--- | :--- |
| `stdin` | Nhận packet JSON UTF-8 không BOM (nếu bật truyền qua stdin / nối bằng `\|`). |
| `stdout` | Chỉ in packet JSON kết quả, không in log. |
| `stderr` | Mọi log, tiến độ, cảnh báo. Trong GUI, các dòng này hiện ở khung nhật ký. |
| Mã thoát | 0 là thành công; khác 0 là lỗi (cả lần quét bị hủy). |

## 5. Mẫu một bước tùy chỉnh (Node.js)

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

## 6. Chế độ dòng lệnh (Unix filter)

`GameScanner.exe` chạy chế độ dòng lệnh thay vì mở giao diện khi có ít nhất một trong các cờ `--cli`, `--rules <file>` (`-r`), `--no-exclude-output`. **Gọi trần `GameScanner.exe > file.json` sẽ mở giao diện; cần thêm `--cli`.** Việc chuyển hướng `stdin` / `stdout` không tự bật chế độ này, vì nhiều trình khởi chạy (Git Bash, IDE, script) luôn nối pipe vào app.

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

## 7. File rules (`rules.json`)

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

* `includeApps` / `excludeApps`: game được nhớ (tab *Luôn chọn* / *Luôn loại*), so theo thư mục như mục 8. Game trong `includeApps` mà packet không có sẽ được thêm mới nếu còn trên đĩa.
* `includePaths` / `excludePaths`: mẫu đường dẫn (bảng dưới). Đường dẫn tuyệt đối trong `includePaths` (file `.exe` hoặc thư mục) mà chưa mục nào khớp thì được thêm mới vào `included`.
* Thứ tự ưu tiên: `includeApps` → `excludeApps` → `includePaths` → `excludePaths`; không khớp gì thì giữ nguyên vị trí.

Mẫu đường dẫn (không phân biệt hoa thường, so với `installDir` và `exePath`):

| Mẫu | Khớp |
| :--- | :--- |
| `D:\Games\MyGame` (đường dẫn tuyệt đối) | Chính nó và mọi thứ bên trong, đúng ranh giới thư mục (không khớp `D:\Games\MyGame2`). |
| `*\Games\*` (có `*`) | Wildcard, khớp toàn chuỗi. |
| `regex:\\Ubisoft\\.*\.exe$` | Biểu thức chính quy. |
| `Common Files` (còn lại) | Chứa chuỗi con. |

Thay đổi so với bản trước: đường dẫn tuyệt đối không có `*` giờ khớp đúng ranh giới thư mục (trước đây là "chứa chuỗi con", nên `C:\YostarGames\Arknights_EN` từng khớp cả `Arknights_EN_Beta`). Muốn khớp theo tiền tố thì dùng wildcard, ví dụ `C:\YostarGames\Arknights*`.

## 8. Nhận diện "cùng một game"

Mọi chỗ cần biết hai mục có phải cùng một game không (quét lại có thêm trùng không, thêm/sửa thủ công có trùng không, game đã xóa có bị loại không, rules có khớp không) đều so theo **thư mục**, không theo `exePath`, để đổi exe chính không làm game bị thêm lại:

* Thư mục của một mục là `installDir`, không có thì thư mục chứa exe chính.
* Cùng game khi: cùng thư mục; hoặc cùng file exe; hoặc exe của bên này nằm trong thư mục của bên kia **và ngược lại** (một bên ghi thư mục gốc, bên kia ghi thư mục con chứa exe, ví dụ Unreal `...\Binaries\Win64`). Điều kiện hai chiều tránh nhầm thư mục launcher (ví dụ `Steam`) với các game nằm bên trong nó.
* Không phân biệt hoa thường, `/` và `\` coi như nhau, bỏ qua `\` ở cuối. `D:\Games\A` không khớp `D:\Games\AB`.
