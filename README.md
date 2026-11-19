# Game Detector

Bộ hai ứng dụng Windows nhỏ để **biết máy bạn có đang chạy game hay không**, rồi ghi kết quả ra một file đơn giản để chương trình khác (script, app tự động hóa, thiết bị thông minh…) đọc và phản ứng, ví dụ đổi chế độ nguồn, tắt thông báo, đổi đèn phòng, tạm dừng tác vụ nền khi bạn đang chơi.

| App | Vai trò | Chạy như thế nào |
| :--- | :--- | :--- |
| **GameScanner** | *Tìm* game đã cài trên máy và tạo ra danh sách game cần theo dõi (`games.json`). | Mở khi cần, dùng xong tắt. Có giao diện, cũng có chế độ dòng lệnh. |
| **GameProcessChecker** | *Theo dõi* liên tục: cứ vài chục giây kiểm tra xem có game nào trong danh sách đang chạy không. | Chạy ngầm ở khay hệ thống (system tray), nên mở cùng Windows. |

> **Lưu ý:** cả hai chỉ chạy trên Windows. README này mô tả hành vi theo code ở [PR #2](https://github.com/nduc03/game-detector/pull/2) (pipeline mới, Danh sách nhớ, dữ liệu portable). Chi tiết sâu của GameScanner (định dạng packet, cú pháp rules, ví dụ pipeline) nằm ở [`GameScanner/readme.md`](GameScanner/readme.md).

---

## 1. Hai app phối hợp với nhau thế nào

Hai app **không gọi nhau trực tiếp**. Chúng chỉ chia sẻ một file trung gian là `games.json`:

```
┌───────────────┐   xuất danh sách    ┌────────────┐   đọc & tự nạp lại   ┌────────────────────┐
│  GameScanner  │ ──────────────────▶ │ games.json │ ───────────────────▶ │ GameProcessChecker │
│ (tìm game)    │                     └────────────┘                      │ (theo dõi nền)     │
└───────────────┘                                                         └─────────┬──────────┘
                                                                                    │ ghi "1" / "0"
                                                                                    ▼
                                                                  %USERPROFILE%\gamerunning.sensor
                                                                                    │
                                                                                    ▼
                                                              Script / app khác của bạn đọc và phản ứng
```

Mục đích của việc tách đôi:

- **Quét game thì nặng và thỉnh thoảng mới cần** (đọc Registry, thư mục launcher, chấm điểm). Làm một lần, kiểm tra, chỉnh tay rồi thôi.
- **Theo dõi game thì phải nhẹ và chạy suốt**. Checker chỉ so tên process, không quét lại máy, nên gần như không tốn tài nguyên.
- Nhờ file trung gian, bạn có thể **sửa tay `games.json`** hoặc dùng công cụ khác sinh ra danh sách, Checker vẫn hiểu.

Checker **tự nạp lại `games.json` khi file đổi**, nên xuất danh sách mới từ GameScanner là có hiệu lực ngay, không cần khởi động lại.

---

## 2. GameScanner: tìm game và tạo danh sách

GameScanner đọc thông tin cài đặt trên máy (Registry và dữ liệu của Steam, Epic Games, Ubisoft Connect, GOG Galaxy, EA App, Riot Games), đoán đâu là game, rồi cho bạn xem, sửa và xuất danh sách.

### Cách làm việc (mức tổng quan)

Việc quét là một **chuỗi các bước nối tiếp**, kết quả bước trước là đầu vào bước sau:

1. **Thu thập (Collect)**: gom mọi thứ tìm được. Luôn chạy.
2. **Heuristic**: chấm điểm để chọn ra cái nào *có vẻ là game* (loại bớt trình điều khiển, runtime, phần mềm văn phòng…). Có thể tắt.
3. **Các bước tùy chỉnh** (nếu bạn thêm): chương trình hoặc script của riêng bạn, ví dụ lọc thêm bằng AI.
4. **Danh sách nhớ**: những điều bạn đã dạy app, luôn chạy cuối cùng để có quyền quyết định sau cùng.

### Dùng giao diện

1. Mở `GameScanner.exe`.
2. Bấm **Chạy toàn bộ pipeline** (`F5`). Game mới tìm được sẽ hiện trong danh sách, tô nền xanh nhạt.
3. Rà lại danh sách:
   - Game thiếu → **Thêm thủ công…** (`Ctrl+N`), chọn file `.exe`.
   - Game sai hoặc không phải game → chọn rồi **Xóa** (`Del`). App sẽ nhớ để lần quét sau không đưa lại.
   - Exe chính bị nhận nhầm (ví dụ nhận launcher thay vì exe game) → **Sửa…** (`F2`), đổi exe chính hoặc thêm các exe khác của game.
4. **Tệp → Xuất JSON cho GameProcessChecker…** (`Ctrl+E`) để ghi `games.json`.
5. Đặt `games.json` cạnh `GameProcessChecker.exe` (xem phần 3).

Những điều nên biết:

- **Quét lại không làm mất gì**: game cũ không bao giờ bị xóa hay ghi đè khi quét lại. Chỉ game mới được thêm.
- **Pipeline lỗi thì không thay đổi gì**: một bước hỏng giữa chừng sẽ hủy cả lần chạy, danh sách giữ nguyên.
- **Danh sách nhớ** là nơi app ghi lại "game này luôn loại" (do bạn xóa) và "game này luôn chọn" (do bạn thêm tay). Mở từ menu *Pipeline → Danh sách nhớ…* nếu muốn xem hoặc "quên" một mục.
- **Dữ liệu portable**: mọi thứ lưu trong thư mục `GameScanner_Data` nằm cạnh `GameScanner.exe`. Copy cả thư mục chứa exe là mang theo được. Vì vậy **đừng đặt trong `Program Files`** (cần quyền ghi).

### Dùng dòng lệnh

Cùng file `GameScanner.exe` nhưng thêm cờ `--cli` sẽ chạy không giao diện, in kết quả ra `stdout`, hợp để ghép với script:

```powershell
.\GameScanner.exe --cli > scan_full.json
```

Xem thêm ví dụ nối nhiều bước bằng `|` trong [`GameScanner/readme.md`](GameScanner/readme.md#2-chế-độ-dòng-lệnh-unix-filter).

---

## 3. GameProcessChecker: theo dõi game đang chạy

Một app nhỏ chạy ngầm, không có cửa sổ, chỉ có biểu tượng ở khay hệ thống.

### Nó làm gì

- Mỗi **30 giây** kiểm tra các tiến trình đang chạy, đối chiếu với danh sách trong `games.json`.
- Có ít nhất một game đang chạy → ghi **`1`** vào file `%USERPROFILE%\gamerunning.sensor`. Không có game nào → ghi **`0`**.
- Chỉ ghi lại khi trạng thái **đổi** (hoặc file bị xóa), nên gần như không tốn ổ đĩa.
- Rê chuột lên biểu tượng khay để xem trạng thái hiện tại; chuột phải → **Thoát**.
- Khi thoát, nó ghi `0` để không bị kẹt ở "đang có game".
- Chỉ cho chạy **một bản** cùng lúc; mở lần hai sẽ tự đóng.
- Tự đặt mình ở chế độ tiết kiệm năng lượng của Windows (Efficiency mode).

### Cách dùng

1. Đặt `GameProcessChecker.exe` và `games.json` (do GameScanner xuất) **cùng một thư mục**. Hoặc truyền đường dẫn file làm tham số:

   ```powershell
   .\GameProcessChecker.exe D:\Tools\games.json
   ```

2. Mở `GameProcessChecker.exe`. Biểu tượng tay cầm xuất hiện ở khay hệ thống.
3. Chương trình khác chỉ cần đọc file `%USERPROFILE%\gamerunning.sensor`: nội dung `1` là đang chơi, `0` là không.

Cách nhận diện: Checker so **đường dẫn đầy đủ** của exe đang chạy với exe chính và các exe khác (`candidateExes`) trong `games.json`. Danh sách rỗng hoặc không tìm thấy file thì kết quả luôn là `0`.

---

## 4. Khuyến nghị sử dụng

### Cho GameProcessChecker luôn chạy khi khởi động Windows

Checker chỉ có ích khi chạy suốt, nên hãy cho nó tự mở cùng Windows. Có hai cách, chọn một.

**Cách 1: thư mục Startup (đơn giản nhất)**

1. Đặt `GameProcessChecker.exe` và `games.json` ở một thư mục cố định, không dời đi sau này (ví dụ `D:\Tools\GameDetector\`).
2. Nhấn `Win + R`, gõ `shell:startup`, Enter.
3. Trong thư mục vừa mở: chuột phải → *New → Shortcut*, trỏ tới `GameProcessChecker.exe`.
4. Nếu `games.json` nằm chỗ khác, thêm đường dẫn vào ô **Target** của shortcut:

   ```
   "D:\Tools\GameDetector\GameProcessChecker.exe" "D:\Tools\GameDetector\games.json"
   ```

**Cách 2: Task Scheduler (khi cần kiểm soát hơn)**

1. Mở *Task Scheduler* → *Create Task…*
2. Tab **Triggers** → *New…* → *Begin the task:* **At log on**.
3. Tab **Actions** → *New…* → *Start a program*, chọn `GameProcessChecker.exe`; điền tham số nếu cần và **Start in** là thư mục chứa exe.
4. Tab **Settings**: bỏ tick *Stop the task if it runs longer than…* để tác vụ không bị tắt giữa chừng.

Sau khi cấu hình, đăng xuất rồi đăng nhập lại để thử: biểu tượng tay cầm phải tự hiện ở khay.

### Quy trình dùng hằng ngày

- **Quét một lần khi mới cài, rồi quét lại khi cài game mới.** Không cần để GameScanner mở thường trực.
- Sau mỗi lần quét, **rà danh sách bằng mắt** (xóa mục sai, sửa exe chính nếu nhận nhầm launcher) rồi mới xuất. Heuristic là đoán, không phải chắc chắn.
- Đặt `GameScanner.exe` ở thư mục ghi được; nên **sao lưu thư mục `GameScanner_Data`** (chứa danh sách và những gì bạn đã dạy app).
- Luôn **xuất lại `games.json`** sau khi chỉnh danh sách; Checker sẽ tự nhận.

### Hạn chế đã biết

- Một số game nhận **nhầm launcher làm exe chính** (hoặc lẫn launcher vào danh sách exe khác). Khi đó Checker có thể báo "đang chơi" cả khi chỉ mở launcher. Cách xử lý hiện tại: sửa tay trong GameScanner (**Sửa…**) hoặc sửa `games.json`, hoặc tắt launcher sau khi vào game.
- Game không có mục gỡ cài đặt trong Registry (bản giải nén tay, portable, một số game từ Xbox/itch.io) có thể không được tìm thấy, hãy dùng **Thêm thủ công…**.
- Phát hiện theo chu kỳ 30 giây, nên trạng thái có thể trễ đến khoảng đó.
- Phần giao diện của GameScanner và việc quét Registry/launcher thật **chưa được kiểm thử trên Windows** ở thời điểm viết (theo mô tả PR #2).

---

## 5. Mở rộng tính năng

### Thêm một bước quét riêng (không cần sửa code)

GameScanner cho phép nối thêm chương trình của bạn vào chuỗi quét: Python, Node.js, PowerShell hoặc bất kỳ exe nào. Chương trình đó nhận danh sách game ở dạng JSON, chỉnh lại, rồi trả danh sách mới. Ví dụ dùng AI để lọc game, hoặc cứu lại game bị loại nhầm.

- Trong giao diện: *Pipeline → Thêm pipeline tùy chỉnh…*, chọn chương trình, tham số, cách truyền dữ liệu vào/ra và giới hạn thời gian.
- Ngoài giao diện: nối bằng `|` ở chế độ `--cli`.
- Hợp đồng đầu vào/ra, ví dụ mẫu Node.js: xem [`GameScanner/readme.md`](GameScanner/readme.md#3-cấu-trúc-packet-pipelinepacket).

### Dạy app bằng quy tắc

Ngoài việc thêm/xóa tay từng game, bạn có thể luôn chọn hoặc luôn loại theo thư mục, theo mẫu đường dẫn (ví dụ mọi thứ trong `D:\Games\`). Có thể chỉnh trong *Danh sách nhớ* hoặc viết file `rules.json` (định dạng ở [`GameScanner/readme.md`](GameScanner/readme.md#5-file-rules-rulesjson)).

### Đọc kết quả bằng chương trình của bạn

Muốn phản ứng khi bắt đầu/kết thúc chơi game, chỉ cần một script theo dõi file `%USERPROFILE%\gamerunning.sensor` (hoặc đọc định kỳ). Checker lo phần phát hiện, bạn lo phần hành động.

### Mở rộng trong mã nguồn

- **Dạng đầu ra khác** (thay vì file `0/1`, ví dụ gọi HTTP/webhook, ghi log thời gian chơi): chỉnh ở `GameRunningDetector/GameWatcher.cs`, nơi trạng thái "có/không có game" được xác định và ghi ra.
- **Cách nhận diện game khác** (theo thư mục cài đặt, tên cửa sổ…): `GameRunningDetector/ProcessScanner.cs`.
- **Nguồn game mới** (thêm launcher chưa hỗ trợ): `GameScanner/Collection/RegistryAndLauncherCollector.cs`.
- **Luật chấm điểm game/không phải game**: `GameScanner/Stages/HeuristicFilterStage.cs`.
- **Ý tưởng đang lên kế hoạch** (chưa làm): bước tích hợp sẵn "Common missing games" để bắt các game hay bị sót, xem [`GameScanner/docs/plan-bundled-missing-games.md`](GameScanner/docs/plan-bundled-missing-games.md).

---

## 6. Build từ mã nguồn

Cần Windows và .NET 10 SDK. Mỗi app được đóng thành **một file exe độc lập**, máy đích không cần cài .NET.

```powershell
dotnet publish GameScanner/GameScanner.csproj -c Release -o ./publish/scanner
dotnet publish GameRunningDetector/GameProcessChecker.csproj -c Release -o ./publish/checker
```

Cấu trúc repo:

```
GameScanner/           App tìm game và tạo games.json (giao diện + dòng lệnh)
GameRunningDetector/   App GameProcessChecker chạy nền, ghi gamerunning.sensor
ICON-LICENSE.md        Nguồn và giấy phép của icon dùng cho cả hai app
```

---

## Giấy phép icon

Icon của cả hai app dùng glyph từ bộ Maki (Mapbox), giấy phép CC0 1.0. Chi tiết xem [`ICON-LICENSE.md`](ICON-LICENSE.md).

## Credit

README này do **Claude Sonnet 5.5** (Anthropic, mã model `claude-sonnet-5-5`) viết, dựa trên mã nguồn ở nhánh của [PR #2](https://github.com/nduc03/game-detector/pull/2).
