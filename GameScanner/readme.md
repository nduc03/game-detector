# GameScanner: hướng dẫn sử dụng

GameScanner tìm các game đã cài trên máy Windows, cho bạn xem và chỉnh lại danh sách, rồi xuất file `games.json` để **GameProcessChecker** biết cần theo dõi những game nào.

> Muốn viết script riêng để lọc game, dùng dòng lệnh, hoặc sửa `rules.json` bằng tay? Xem [tài liệu kỹ thuật pipeline](docs/pipeline.md).

## 1. Bắt đầu nhanh

1. Đặt `GameScanner.exe` vào một thư mục bạn có quyền ghi (ví dụ `D:\Tools\GameScanner`), **đừng đặt trong `Program Files`**. App lưu dữ liệu ngay cạnh nó.
2. Mở `GameScanner.exe`, bấm **▶ Chạy toàn bộ pipeline** (`F5`). Game tìm được hiện trong danh sách bên phải, game mới tô nền xanh nhạt.
3. Rà lại danh sách:
   * Thiếu game → **Thêm thủ công…** (`Ctrl+N`).
   * Có thứ không phải game → chọn rồi **Xóa** (`Del`).
   * Exe chính bị nhận nhầm (ví dụ nhận launcher thay vì game) → **Sửa…** (`F2`).
4. Bấm **Xuất JSON cho GameProcessChecker…** (`Ctrl+E`) và lưu thành `games.json` cạnh `GameProcessChecker.exe`.

Khi cài thêm game mới, chỉ cần mở lại GameScanner, quét lại và xuất lại.

## 2. Danh sách game

Mỗi dòng hiện icon, tên, nhà phát hành, file exe, nguồn (Steam, Registry…, hoặc Thủ công) và nút **Mở thư mục** (mở Explorer tại thư mục game). Ô **Tìm** lọc theo tên, nhà phát hành hoặc đường dẫn.

### Thêm game thủ công

Bấm **Thêm thủ công…** rồi chọn file `.exe` của game. Tên và nhà phát hành được điền sẵn từ thông tin của file; các trường còn lại không bắt buộc. App sẽ nhớ game này (**Danh sách nhớ → Luôn chọn**) nên các lần quét sau luôn giữ nó.

### Sửa thông tin game

Chọn game rồi bấm **Sửa…** (hoặc double-click, `F2`):

* **File exe chính**: exe mà GameProcessChecker dùng để biết game đang chạy.
* **Exe khác của game**: các file `.exe` phụ cũng được tính là game đang chạy (launcher riêng của game, bản 64-bit…). Mỗi exe là một dòng, có nút **…** (chọn file), **⬆** (đưa lên làm exe chính) và **✕** (xóa dòng). Bấm **+ Thêm exe khác** để thêm dòng.
* Cần ít nhất file exe chính hoặc thư mục cài đặt.

Đổi exe chính không làm game bị thêm trùng ở lần quét sau: app nhận ra game theo thư mục cài đặt chứ không theo tên file exe.

### Xóa game

Chọn một hoặc nhiều game rồi bấm **Xóa** (`Del`). Game chỉ bị bỏ khỏi danh sách, **file trên đĩa không bị đụng tới**. App ghi game đó vào **Danh sách nhớ → Luôn loại** để lần quét sau không đưa lại. Lỡ xóa nhầm thì mở Danh sách nhớ và bỏ game khỏi tab Luôn loại (mục 4), hoặc thêm thủ công lại.

## 3. Quét game

Khung **Pipeline quét** bên trái liệt kê các bước của một lần quét, chạy lần lượt từ trên xuống:

1. **Thu thập**: gom các ứng dụng đã cài từ Registry và từ Steam, Epic Games, Ubisoft Connect, GOG Galaxy, EA App, Riot Games. Luôn chạy.
2. **Heuristic**: tự đoán ứng dụng nào là game (loại bớt trình điều khiển, runtime, phần mềm văn phòng…).
3. **Pipeline tùy chỉnh** (nếu bạn thêm, mục 5).
4. **Danh sách nhớ**: áp những gì bạn đã dạy app (mục 4). Luôn ở cuối.

Di chuột lên một bước để xem mô tả ngắn.

* **Bật / tắt một bước**: bỏ tick, hoặc chọn bước rồi bấm **Tắt**. Bước tắt hiện chữ mờ và được bỏ qua khi quét, nhưng cấu hình vẫn giữ. Bấm **Bật** để dùng lại.
* **Tắt tất cả / Bật tất cả** (cạnh **Chạy toàn bộ**): tắt hoặc bật cùng lúc mọi bước trừ bước Thu thập.
* **Chạy toàn bộ** (`F5`) để quét; **Dừng** (`Shift+F5`) để hủy giữa chừng. Nhật ký quét hiện ở khung dưới.

Những điều nên biết:

* **Quét lại không làm mất gì.** Quét xong app chỉ thêm game mới vào danh sách; game đã có không bao giờ bị xóa hay ghi đè, kể cả khi lần này không còn thấy nó.
* **Lỗi thì không có gì thay đổi.** Nếu một bước lỗi (hoặc bạn bấm Dừng), cả lần quét bị hủy và danh sách giữ nguyên. Xem khung nhật ký để biết bước nào lỗi.
* Nếu tắt Heuristic, chỉ pipeline tùy chỉnh và Danh sách nhớ quyết định game nào được chọn, nên lần quét có thể không thêm được game nào.

## 4. Danh sách nhớ

Mở bằng menu *Pipeline → Danh sách nhớ…*, hoặc chọn bước **Danh sách nhớ** rồi bấm **Sửa…**. Đây là nơi app ghi lại những gì bạn đã dạy nó, có hai tab:

* **Luôn loại**: game bạn đã xóa khỏi danh sách (app tự thêm khi bạn xóa). Lần quét sau sẽ không đưa chúng vào lại.
* **Luôn chọn**: game bạn đã thêm thủ công (app tự thêm). Nếu lần quét không thấy một game luôn chọn nhưng nó vẫn còn trên đĩa, app thêm nó lại.

Ở mỗi tab bạn có thể **Thêm thư mục game…**, **Thêm file exe…**, **Thêm mẫu đường dẫn…**, **Sửa…**, **Xóa (quên)** hoặc **Chuyển sang tab kia**. Một game chỉ nằm ở một trong hai tab: thêm vào tab này thì tự bỏ khỏi tab kia.

### Mẫu đường dẫn

Dùng khi muốn luôn chọn hoặc luôn loại cả một nhóm, ví dụ mọi thứ trong `D:\Games`:

| Viết | Khớp với |
| :--- | :--- |
| `D:\Games\MyGame` | Thư mục đó và mọi thứ bên trong (không khớp `D:\Games\MyGame2`). |
| `*\Games\*` | Có `*` là ký tự đại diện: khớp mọi đường dẫn có thư mục `Games`. |
| `regex:\\Ubisoft\\.*\.exe$` | Biểu thức chính quy (cho người đã quen regex). |
| `Common Files` | Mọi đường dẫn có chứa chuỗi này. |

Khi một game khớp nhiều quy tắc, quy tắc theo từng game (thêm bằng thư mục / file exe, hoặc do thêm thủ công / xóa) luôn thắng mẫu đường dẫn; ở cùng loại thì *Luôn chọn* thắng *Luôn loại*.

## 5. Pipeline tùy chỉnh

Nếu muốn lọc thêm theo cách riêng (ví dụ dùng script Python hoặc AI để cứu game bị loại nhầm), bạn có thể nối chương trình của mình vào chuỗi quét: *Pipeline → Thêm pipeline tùy chỉnh…* (hoặc nút **Thêm…** trong khung Pipeline quét).

Pipeline tùy chỉnh nằm giữa Heuristic và Danh sách nhớ; sắp thứ tự bằng nút ▲ ▼, tắt được hoặc **Xóa** hẳn.

Cách viết chương trình, định dạng dữ liệu vào/ra và ví dụ mẫu nằm trong [tài liệu kỹ thuật pipeline](docs/pipeline.md). Trong app, mở tài liệu này bằng liên kết ở cửa sổ *Thêm pipeline tùy chỉnh* hoặc menu *Pipeline → Tài liệu viết pipeline tùy chỉnh*.

## 6. Xuất cho GameProcessChecker

Bấm **Xuất JSON cho GameProcessChecker…** (`Ctrl+E`) để lưu file `games.json`. Đặt file cạnh `GameProcessChecker.exe` với đúng tên `games.json`, hoặc truyền đường dẫn file làm tham số khi chạy checker. Checker tự nạp lại khi file thay đổi.

Mặc định file xuất gồm cả **exe khác của game**. Bỏ tick *Tệp → Xuất kèm exe khác của game* nếu chỉ muốn theo dõi exe chính.

## 7. Dữ liệu của app

Mọi thứ được lưu trong thư mục **`GameScanner_Data` cạnh `GameScanner.exe`** (menu *Tệp → Mở thư mục dữ liệu*). Chép cả thư mục chứa exe sang máy khác là mang theo được toàn bộ; nên sao lưu thư mục này.

| File | Nội dung |
| :--- | :--- |
| `library.json` | Danh sách game. |
| `settings.json` | Các bước quét (bật/tắt, pipeline tùy chỉnh) và tùy chọn xuất. |
| `rules.json` | Danh sách nhớ. |

Nếu một file bị hỏng, app không ghi đè lên nó: file cũ được đổi tên thành `*.hong-<thời gian>` và app bắt đầu lại từ rỗng cho phần đó.

## 8. Dòng lệnh

Cùng file `GameScanner.exe`, thêm cờ `--cli` sẽ quét không mở giao diện và in kết quả ra màn hình, hợp để dùng trong script:

```powershell
.\GameScanner.exe --no-exclude-output > games.json
```

Các cờ khác và cách nối nhiều bước bằng `|`: xem [tài liệu kỹ thuật pipeline](docs/pipeline.md#6-chế-độ-dòng-lệnh-unix-filter).

## 9. Đóng gói thành một file `.exe` (dành cho người build từ mã nguồn)

Trên máy Windows có .NET 10 SDK:

```powershell
dotnet publish GameScanner/GameScanner.csproj -c Release -o ./publish
```

Các thiết lập self-contained, single-file, `win-x64` đã nằm sẵn trong `GameScanner.csproj`. Tài liệu `docs\pipeline.md` được chép cạnh exe trong thư mục `publish\docs`; nên phát hành kèm thư mục này để liên kết tài liệu trong app mở được khi không có mạng (thiếu file thì app mở bản online). Icon ứng dụng lấy từ `app.ico` (nguồn và giấy phép: xem `ICON-LICENSE.md` ở gốc repo).
