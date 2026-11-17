# Giấy phép icon ứng dụng

Áp dụng cho:

- `GameRunningDetector/app.ico`, `GameRunningDetector/app.svg` (GameProcessChecker)
- `GameScanner/app.ico`, `GameScanner/app.svg` (GameScanner)

## Nguồn glyph tay cầm chơi game

- Bộ icon: **Maki** của Mapbox
- Gói npm: `@mapbox/maki`, phiên bản **8.2.0** (tải từ https://registry.npmjs.org)
- File gốc: `icons/gaming.svg` (viewBox 15x15, một `<path>`)
- Giấy phép: **CC0 1.0 Universal** (cống hiến cho phạm vi công cộng - public domain)
  - `package.json` của gói ghi: `"license": "CC0-1.0"`
  - `LICENSE.txt` của gói mở đầu bằng `CC0 1.0 Universal`, câu chính (mục 2, Waiver):

    > "Affirmer hereby overtly, fully, permanently, irrevocably and unconditionally
    > waives, abandons, and surrenders all of Affirmer's Copyright and Related Rights
    > and associated claims and causes of action [...] in the Work [...] for any
    > purpose whatsoever, including without limitation commercial, advertising or
    > promotional purposes"

CC0 không yêu cầu ghi công; ghi chú này chỉ để tiện truy nguồn.

## Phần tự vẽ

Path của glyph được giữ nguyên, chỉ phóng to và đặt vào giữa. Các phần còn lại
do dự án tự vẽ bằng SVG, không lấy từ nguồn bên ngoài:

- Nền vuông bo góc tô gradient (xanh teal cho GameProcessChecker, xanh chàm cho GameScanner).
- Kính lúp ở góc dưới phải của icon GameScanner (một hình tròn + một đoạn thẳng), cùng
  mask cắt khoảng hở giữa kính lúp và tay cầm.

## Cách tạo file .ico

Mỗi `app.svg` được render riêng ở từng kích thước 16, 20, 24, 32, 40, 48, 64, 128, 256 px
(nền trong suốt) rồi ghép thành `.ico` đa kích thước: các size dưới 256 lưu dạng BMP 32-bit
có alpha, size 256 lưu dạng PNG.
