# GameScanner: quét game & đường dẫn `.exe` trên Windows (.NET 10)

`GameScanner.exe` có hai cách dùng trong cùng một file exe:

* **Giao diện WinForms** (mặc định khi mở bằng double-click): quét, lưu danh sách game, thêm thủ công, tự định nghĩa pipeline quét, xuất `games.json` cho GameProcessChecker.
* **Dòng lệnh kiểu Unix filter** (`stdin` / `stdout` / `stderr`): giữ nguyên như trước để ghép nối bằng `|`, và để chính `GameScanner.exe` làm một mắt xích trong pipeline.

## 1. Giao diện

### Danh sách game

* Mỗi game hiện icon lấy từ file exe, tên, nhà phát hành, đường dẫn exe, nguồn (Quét / Thủ công) và nút **Mở thư mục** (mở Explorer, chọn sẵn file exe).
* **Thêm thủ công…**: chọn file `.exe` → hộp thoại điền các thuộc tính không bắt buộc (tên, nhà phát hành, thư mục cài đặt, các exe khác của game). Tên và nhà phát hành được điền sẵn từ thông tin phiên bản của file.
* **Sửa…** (hoặc double-click, `F2`) và **Xóa** (`Del`) áp dụng cho game đang chọn. Xóa chỉ bỏ khỏi danh sách, không đụng tới file trên đĩa.
* Ô **Tìm** lọc theo tên, nhà phát hành, đường dẫn.

### Dữ liệu của app

Lưu dạng JSON trong `%LOCALAPPDATA%\GameScanner\` (menu *Tệp → Mở thư mục dữ liệu*):

| File | Nội dung |
| :--- | :--- |
| `library.json` | Danh sách game đã quét / thêm thủ công. |
| `settings.json` | Các bước pipeline, file rules đã chọn, tùy chọn xuất. |
| `last-packet.json` | Packet cuối của lần chạy pipeline thành công gần nhất (đầu vào khi chạy riêng một bước). |

Chọn JSON thay vì SQLite vì dữ liệu chỉ vài trăm mục, không cần truy vấn, sửa tay được, và không phải đóng gói thêm thư viện native vào file exe duy nhất.

### Pipeline quét

Khung **Pipeline quét** liệt kê các bước theo thứ tự chạy từ trên xuống:

1. **Quét hệ thống (Collect + Heuristic)** – có sẵn. Đọc Registry `Uninstall` và cấu hình của Steam, Epic Games, Ubisoft Connect, GOG Galaxy, EA App, Riot Games rồi phân loại bằng heuristic. Đây là nguồn dữ liệu nên không dùng packet đầu vào.
2. **Rules** – có sẵn. Áp `includePaths` / `excludePaths`. Mặc định tự tìm `rules.json` ở thư mục hiện tại rồi cạnh `GameScanner.exe` (không có thì bỏ qua); hoặc chọn file cụ thể bằng *Pipeline → Chọn file rules…* (khi đã chọn mà file thiếu/hỏng thì bước này lỗi).
3. **Các pipeline tùy chỉnh** – do bạn định nghĩa, sắp thứ tự bằng nút ▲ ▼.

Cách chạy:

* **Chạy toàn bộ**: chạy các bước đang được tick, kết quả bước trước là đầu vào bước sau.
* **Chạy riêng bước đã chọn**: chỉ chạy một bước. Bước "Quét hệ thống" tự sinh dữ liệu; các bước khác nhận đầu vào là kết quả của lần chạy pipeline thành công gần nhất (gồm cả `included` lẫn `excluded`), chưa có thì lấy danh sách game hiện tại.

Quy tắc xử lý kết quả:

* Sau **mỗi bước**, kết quả được kiểm tra lại cấu trúc packet (mục 3). Sai cấu trúc là lỗi.
* **Một bước lỗi vì bất kỳ lý do gì** (không tìm thấy chương trình, mã thoát khác 0, quá thời gian, thiếu file kết quả, JSON sai cấu trúc, bấm Dừng…) thì pipeline ngắt ngay và **hủy toàn bộ kết quả từ bước đầu tiên**; danh sách game giữ nguyên.
* Khi mọi bước thành công, app lấy **trạng thái cuối cùng** của pipeline: mục nào trong `included` chưa có trong danh sách thì được thêm (tô nền xanh nhạt). **Game cũ không bao giờ bị xóa hay ghi đè khi quét lại**, kể cả khi lần quét này không còn thấy nó.
* Hai mục được coi là cùng một game khi trùng exe chính, hoặc cùng thư mục cài đặt và exe chính của bên này nằm trong `candidateExes` của bên kia.

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

**`$json` (chèn thẳng nội dung packet vào tham số) không được hỗ trợ**: dòng lệnh Windows giới hạn khoảng 32.767 ký tự trong khi packet của một máy bình thường dài hơn nhiều, và JSON đầy dấu nháy rất dễ làm hỏng tham số. Dùng stdin hoặc `$in` thay thế.

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

`GameScanner.exe` chạy chế độ dòng lệnh thay vì mở giao diện khi có một trong các cờ `--cli`, `--rules <file>` (`-r`), `--no-exclude-output`, hoặc khi `stdin` / `stdout` đang được nối với pipe hay file. Thêm `--gui` để luôn mở giao diện.

```powershell
# Quét máy: Collect -> Heuristic -> Rules (nếu có rules.json), xuất cả included lẫn excluded
.\GameScanner.exe > scan_full.json

# Chỉ xuất danh sách đã chọn
.\GameScanner.exe --no-exclude-output > games_clean.json

# Chỉ định file rules
.\GameScanner.exe --rules D:\configs\my_rules.json > scan_with_rules.json

# Nối với bước lọc khác, rồi dùng lại GameScanner ở cuối để cắt phần excluded
.\GameScanner.exe | python ai_filter_stage.py | .\GameScanner.exe --no-exclude-output > games.json

# Nhận packet từ stdin và áp thêm một file rules
.\GameScanner.exe | python ai_filter_stage.py | .\GameScanner.exe --rules post_ai_rules.json --no-exclude-output > games.json
```

Ghi chú:

* Khi nhận packet từ `stdin`, rules chỉ được áp nếu truyền `--rules` tường minh.
* Packet từ `stdin` sai cấu trúc, hoặc bất kỳ lỗi nào khác, làm chương trình thoát với mã 1 (lỗi ghi ra `stderr`).
* App là chương trình Windows (không có console riêng) nên `cmd` không đợi nó chạy xong khi gọi trần không chuyển hướng; hãy chuyển hướng ra file/pipe như các ví dụ trên, hoặc dùng `start /wait`.

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
      ]
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
| `name`, `publisher`, `exePath`, `installDir` | Chuỗi hoặc `null`. |
| `candidateExes` | Mảng chuỗi, `null`, hoặc vắng mặt. |
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
// Dòng lệnh: .\GameScanner.exe | node rescue_stage.js | .\GameScanner.exe --no-exclude-output
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

## 5. File rules

```json
{
  "includePaths": ["*\\Games\\*", "D:\\Portable\\MyGame\\"],
  "excludePaths": ["*\\Program Files\\Common Files\\*"]
}
```

Mỗi pattern được so với `installDir` và `exePath` của từng mục, không phân biệt hoa thường: `regex:...` là biểu thức chính quy, có `*` là wildcard khớp toàn chuỗi, còn lại là chứa chuỗi con. Khớp `includePaths` thì vào `included` (ưu tiên hơn `excludePaths`), khớp `excludePaths` thì vào `excluded`, không khớp thì giữ nguyên. `includePaths` là đường dẫn tuyệt đối (file `.exe` hoặc thư mục) mà chưa mục nào khớp thì được thêm mới vào `included`.

## 6. Đóng gói thành một file `.exe`

Trên máy Windows có .NET 10 SDK:

```powershell
dotnet publish GameScanner/GameScanner.csproj -c Release -o ./publish
```

Các thiết lập self-contained, single-file, `win-x64` đã nằm sẵn trong `GameScanner.csproj`. Icon ứng dụng lấy từ `app.ico` (nguồn và giấy phép: xem `ICON-LICENSE.md` ở gốc repo).
