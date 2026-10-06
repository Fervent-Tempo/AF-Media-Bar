# Third-party notices

## TaskbarLyrics web lyrics presentation engine

The files under `src/AFMediaBar/Web/Lyrics/` are derived from the TaskbarLyrics web lyrics presentation engine.

Copyright (c) 2026 anync

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Third-party NuGet packages

The packages referenced by `src/AFMediaBar/AFMediaBar.csproj` are listed below. The same
list drives the "Open source" section of the About page in Settings through
`OpenSourceLicenseCatalog`, and every version and license can be checked against the
`<PackageReference>` entries and each local package's `.nuspec`.

| Package | Version | License | Home page |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | https://github.com/CommunityToolkit/dotnet |
| Dubya.WindowsMediaController | 2.5.6 | MIT | https://github.com/DubyaDude/WindowsMediaController |
| F23.StringSimilarity | 7.0.1 | MIT | https://github.com/feature23/StringSimilarity.NET |
| Lyricify.Lyrics.Helper | 0.2.0 | Apache-2.0 | https://github.com/WXRIW/Lyricify-Lyrics-Helper |
| MicaWPF | 7.1.0 | MIT | https://github.com/Simnico99/MicaWPF |
| Microsoft.Extensions.Hosting | 10.0.1 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Web.WebView2 | 1.0.4191.47 | BSD-3-Clause | https://aka.ms/webview |
| NAudio.Wasapi | 3.1.0 | MIT | https://github.com/naudio/NAudio |
| OpenccNetLib | 1.7.0 | MIT | https://github.com/laisuk/OpenccNet |
| WPF-UI | 4.2.0 | MIT | https://github.com/lepoco/wpfui |
| WPF-UI.DependencyInjection | 4.2.0 | MIT | https://github.com/lepoco/wpfui |

Notes:

- `OpenccNetLib` powers the Simplified/Traditional conversion of lyric text. Its conversion
  dictionary is embedded inside its own assembly, so nothing is redistributed outside the
  package and the application's single-file publish produces one executable
  (see the `ExcludeAssets="contentFiles"` remark in `AFMediaBar.csproj`).
- License texts of the above packages are available from their home pages; they are not
  reproduced in full here because each is a standard SPDX license (MIT, Apache-2.0,
  BSD-3-Clause) referenced above.
