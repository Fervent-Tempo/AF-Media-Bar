<p align="center">
  <img src="docs/assets/readme/hero-vi.svg" width="100%" alt="Minh họa AF Media Bar: thanh media ở góc dưới bên trái thanh tác vụ Windows, bảng điều khiển đầy đủ phía trên, biểu tượng và giới thiệu dự án bên phải">
</p>

<p align="center">
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=phi%C3%AAn%20b%E1%BA%A3n" alt="phiên bản"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square&amp;label=l%C6%B0%E1%BB%A3t%20t%E1%BA%A3i" alt="lượt tải"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=gi%E1%BA%A5y%20ph%C3%A9p" alt="giấy phép"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Fervent-Tempo/AF-Media-Bar/build.yml?style=flat-square&amp;label=build&amp;branch=main" alt="build"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/stargazers"><img src="https://img.shields.io/github/stars/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=Stars&amp;color=gold" alt="Stars"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/forks"><img src="https://img.shields.io/github/forks/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=Forks" alt="Forks"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/graphs/contributors"><img src="https://img.shields.io/github/contributors/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=ng%C6%B0%E1%BB%9Di%20%C4%91%C3%B3ng%20g%C3%B3p" alt="người đóng góp"></a>
  <a href="docs/localization.md"><img src="https://img.shields.io/badge/ng%C3%B4n%20ng%E1%BB%AF%20giao%20di%E1%BB%87n-4-0078d4?style=flat-square" alt="ngôn ngữ giao diện"></a>
  <br>
  <a href="README.md">简体中文</a> · <a href="README.en-US.md">English</a> · Tiếng Việt
  <br>
  <a href="#tải-về-và-khởi-chạy">Tải về</a> · <a href="#xem-trước-trực-tiếp">Xem trước</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">Báo cáo lỗi</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">Đề xuất tính năng</a> · <a href="docs/localization.md">Đóng góp bản dịch</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/discussions">Thảo luận và trợ giúp</a>
</p>

AF Media Bar là bộ điều khiển media trên thanh tác vụ Windows 10/11. Ứng dụng đọc nội dung đang phát từ phiên media của hệ thống, giúp thông tin media, lời bài hát, điều khiển phát nhạc và chuyển thiết bị trở nên trực quan, dễ sử dụng hơn trên desktop.

## Xem trước trực tiếp

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="80%" alt="AF Media Bar chạy trên thanh tác vụ Windows với ảnh bìa, điều khiển phát nhạc và thông tin bài hát">
</p>

[Xem video giới thiệu trên Bilibili](https://www.bilibili.com/video/BV17yhh6aEgK)

## Tải về và khởi chạy

Truy cập [GitHub Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases) và chọn một gói:

1. **Bộ cài đặt (khuyên dùng):** Tải `AFMediaBar-Setup-vX.Y.Z-win-x64.exe` và làm theo hướng dẫn cài đặt.
2. **Bản portable:** Tải `AFMediaBar-vX.Y.Z-win-x64.zip`, giải nén vào thư mục có quyền ghi và chạy `AFMediaBar.exe`.

**Yêu cầu hệ thống:** Windows 10 1809 trở lên, x64, cùng Microsoft Edge WebView2 Runtime. Nếu thiếu, hãy cài Evergreen Runtime. Các gói phát hành đã tích hợp .NET runtime.

## Cập nhật và gỡ cài đặt

- **Cập nhật:** Ứng dụng thông báo khi có phiên bản mới; bạn có thể kiểm tra trong trang Ứng dụng. Bản cài đặt hỗ trợ tải và cài cập nhật trong ứng dụng; bản portable cần thay thế tệp thủ công.
- **Cài đặt:** Lưu tại `%LOCALAPPDATA%\AFMediaBar\settings.json`; có thể mở thư mục cài đặt từ trang Ứng dụng.
- **Gỡ cài đặt:** Gỡ bản cài đặt qua mục Ứng dụng đã cài đặt của Windows hoặc menu Start. Với bản portable, xóa thư mục chương trình. Để xóa cả cài đặt và bộ nhớ đệm, xóa thêm `%LOCALAPPDATA%\AFMediaBar`.

## Quyền riêng tư và bảo mật

- Không có telemetry, quảng cáo hay tài khoản. Điều khiển media và âm lượng được xử lý trên máy.
- Kiểm tra cập nhật và tìm lời bài hát trực tuyến sử dụng mạng. Tìm lời sử dụng tên bài hát và nghệ sĩ; có thể tắt các nguồn lời trong Cài đặt.
- Ứng dụng chạy với quyền của người dùng hiện tại. Báo cáo vấn đề bảo mật riêng tư theo [SECURITY.md](SECURITY.md).

## Lịch sử Star

<a href="https://www.star-history.com/?repos=Fervent-Tempo%2FAF-Media-Bar&amp;type=date&amp;legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;theme=dark&amp;legend=top-left">
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left">
    <img src="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left" width="100%" alt="Lịch sử số Star của AF Media Bar trên GitHub">
  </picture>
</a>

### Hoạt động dự án

![Thống kê hoạt động và đóng góp của kho AF Media Bar](https://repobeats.axiom.co/api/embed/5c84aada847e73d1f944fc22151a821d0fb2a9a9.svg "Repobeats analytics image")

## Cấu trúc dự án

Mã nguồn ứng dụng nằm trong `src/`, kiểm thử trong `tests/` và công cụ phát triển trong `tools/`. Xem [Tài liệu kiến trúc](docs/architecture.md#仓库目录) để biết cấu trúc thư mục và trách nhiệm, và [Hướng dẫn đóng góp](CONTRIBUTING.md#development-and-validation) để biết cách build và kiểm thử.

## Đóng góp

Đọc [Hướng dẫn đóng góp](CONTRIBUTING.md) trước khi đóng góp; xem [Tài liệu kiến trúc](docs/architecture.md) để biết ranh giới kiến trúc.

Báo cáo lỗi nên kèm phiên bản Windows, phiên bản ứng dụng, trình phát và các bước tái hiện. Xem [CHANGELOG.en-US.md](CHANGELOG.en-US.md) để biết lịch sử phiên bản.

## Bản địa hóa và dịch thuật

Giao diện hiện hỗ trợ **简体中文, 繁體中文, English và Tiếng Việt**. Chọn ngôn ngữ trong trang Ứng dụng. README có phiên bản tiếng Trung, tiếng Anh và tiếng Việt.

Chúng tôi hoan nghênh đóng góp bản dịch ở mọi ngôn ngữ, bao gồm ngôn ngữ mới và cải thiện bản dịch hiện có. Xem [Hướng dẫn đóng góp bản dịch](docs/localization.md#translation-guide). Dùng [Phản hồi bản dịch](https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=localization.yml) để báo cáo bản dịch sai, thiếu, chữ bị cắt hoặc đóng góp ngôn ngữ mới.

## Lời cảm ơn

### Người đóng góp

Cảm ơn tất cả các nhà phát triển đã đóng góp cho AF Media Bar.

[![Người đóng góp cho AF Media Bar](https://contrib.nn.ci/api?repo=Fervent-Tempo/AF-Media-Bar&cols=12&radius=24&no_bot=true)](https://github.com/Fervent-Tempo/AF-Media-Bar/graphs/contributors)

### Dự án mã nguồn mở

Cảm ơn các dự án mã nguồn mở sau:

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout): Bảng điều khiển âm lượng Windows đơn giản và hiện đại.
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper): Thư viện lời bài hát Lyricify để phân tích, tạo, tìm kiếm, giải mã và xử lý lời.
- [TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics): Công cụ lời bài hát trên thanh tác vụ Windows.
- [NAudio](https://github.com/naudio/NAudio): Thu và xử lý âm thanh Windows.
- [WPF UI](https://github.com/lepoco/wpfui): Điều khiển WPF theo phong cách Fluent.

## Tài trợ

<div align="center">

Nếu AF Media Bar hữu ích với bạn, hãy tặng dự án một Star ❤️

</div>

Ủng hộ tác giả một tách cà phê. **Khoản tài trợ trên 10 CNY có thể được ghi vào danh sách nhà tài trợ; vui lòng để lại ID trong ghi chú thanh toán.**

<div align="center">

| WeChat | Alipay |
| :---: | :---: |
| <img src="src/AFMediaBar/Assets/Sponsor/wechat-pay.png" alt="Mã thanh toán WeChat" width="220"> | <img src="src/AFMediaBar/Assets/Sponsor/alipay-pay.png" alt="Mã thanh toán Alipay" width="220"> |

</div>
