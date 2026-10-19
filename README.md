# ScreenWallpaper 動態桌布

把影片或 GIF 設成 Windows 桌布，顯示在桌面圖示的後面。支援多螢幕。

## 下載

到 [Releases](../../releases) 下載最新的 `ScreenWallpaper-standalone.zip`，解壓縮後雙擊 `ScreenWallpaper.exe` 就能用，不用另外安裝 .NET。

- 第一次執行如果出現「Windows 已保護您的電腦」，按「其他資訊」→「仍要執行」。
- 程式沒有數位簽章。開了「智慧型應用程式控制」的電腦可能會擋下它（是否會擋由微軟的雲端判斷決定），這時可以改用下方「從原始碼建置」的方式自己編譯。

系統需求：Windows 10 1809 以上（64 位元）。主要在 Windows 11 25H2 上測試。

## 支援格式

- **影片**：`.mp4`、`.m4v`、`.mov`、`.wmv`、`.avi`、`.mkv`、`.webm`
  影片透過 Windows 內建的 Media Foundation 播放，能播什麼跟「電影與電視」App 一樣。`.webm`（VP9）、AV1、HEVC 需要 Microsoft Store 的「Web 媒體延伸模組」、「VP9 視訊延伸模組」等解碼器，多數 Windows 11 電腦已內建。
- **圖片**：`.gif`（動畫）、`.png`、`.jpg`、`.bmp`

## 使用方式

1. 第一次啟動會請你選一個影片或 GIF 檔。
2. 之後都從右下角系統匣的 ▶ 圖示操作（點右鍵開選單，雙擊可以換檔案）：
   - **選擇桌布檔案…**：換一個影片或 GIF
   - **暫停 / 繼續播放**
   - **靜音**：影片預設靜音
   - **顯示模式**：填滿（裁切邊緣）、符合（保留黑邊）、延展
   - **全螢幕程式執行時自動暫停**：玩遊戲或全螢幕看影片時自動暫停，節省效能
   - **開機時自動啟動**
   - **移除動態桌布**：恢復原本的靜態桌布
   - **結束**：關閉程式並恢復原本的桌布

也可以把影片或 GIF 檔直接拖到 `ScreenWallpaper.exe` 上，用那個檔案當桌布。

設定存在 `%APPDATA%\ScreenWallpaper\settings.json`，紀錄檔在同一個資料夾的 `log.txt`。重新啟動 Explorer 或接上、拔掉螢幕時，會自動重新套用桌布。

## 從原始碼建置

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

**一般版本**（需要安裝 .NET 10 Desktop Runtime）：

```bash
dotnet publish -c Release -o publish
```

如果電腦開了「智慧型應用程式控制」而擋下 `ScreenWallpaper.exe`，可以改雙擊 `publish\ScreenWallpaper.vbs`。它會透過有微軟簽章的 `dotnet` 在背景啟動程式，不會跳出黑色視窗。

**獨立版本**（單一 exe，內含 .NET，約 78 MB）：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish-standalone
```

發布 Release 時，把 `publish-standalone\ScreenWallpaper.exe` 和 [`packaging/使用說明.txt`](packaging/使用說明.txt) 一起壓縮成 `ScreenWallpaper-standalone.zip`。

## 運作原理

- 對 Explorer 的 `Progman` 視窗送出未公開的 `0x052C` 訊息，讓桌面分出「圖示層」和「桌布層」，再把桌布視窗放進圖示層下方。Windows 11 24H2 之後，桌布視窗必須是 layered window 才會顯示。
- 影片用 `Windows.Media.Playback.MediaPlayer`（Media Foundation）解碼，透過 `Windows.UI.Composition` 畫到視窗上，多台螢幕共用同一個解碼器。
- GIF 和圖片用 WinForms `PictureBox` 顯示。

## 授權

[MIT](LICENSE)
