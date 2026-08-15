<h1 align="center">
  <br>
  <img src="https://raw.github.com/strumenta/SmartReader/master/assets/Anagnostes-Logo.png" width="256" alt="Anagnostes">
  <br>
  Anagnostes
  <br>
</h1>
<h5 align="center">ἀναγνώστης - Ancient Greek, “reader, one who reads aloud”</h5>

## Features

- **Paste & Play** — paste any article URL into the URL bar and press **▶ LOAD** to fetch and strip it of ads/navigation using [SmartReader](https://github.com/strumenta/SmartReader)
- **Text-to-Speech** — clean article text is fed into [KokoroSharp](https://github.com/Lyrcaxis/KokoroSharp) (Kokoro 82M TTS) for high-quality neural speech
- **WinAMP-style UI** — dark LCD display, animated equaliser bars, classic bevel-style transport buttons
- **Play / Pause / Stop** — full playback control; the EQ bars animate while speech is active
- **Volume control** — slider wired to KokoroSharp's audio output
- **Cross-platform** — runs on Windows, macOS, and Linux via Avalonia

## Shoutouts
Anagnostes is built on the shoulders of giants, and would not exist without the following open-source projects:

# [Avalonia UI](https://github.com/AvaloniaUI/Avalonia)
The cross-platform UI framework that powers the Anagnostes interface.

# [KokoroSharp](https://github.com/Lyrcaxis/KokoroSharp)
The .NET wrapper for the Kokoro TTS model, enabling high-quality neural speech synthesis.

# [Kokoro](https://github.com/hexgrad/kokoro)
The high-quality neural TTS model used by KokoroSharp.

# [SmartReader](https://github.com/strumenta/SmartReader)
The readability-style article extraction library that removes boilerplate content from web pages.

# Developing

## Requirements

- .NET 10 SDK
- Internet connection on first launch (KokoroSharp downloads the ~320 MB Kokoro model automatically)

## Build & Run

```bash
git clone https://github.com/TheNerdMan/Anagnostes.git
cd Anagnostes
dotnet run
```

## How It Works

1. **Article extraction** — `ArticleService` fetches the page with `HttpClient` and passes it through `SmartReader`, which applies a Readability-style algorithm to strip boilerplate (ads, nav, footers) and return the clean article title + body text.
2. **TTS** — `TtsService` wraps `KokoroTTS` (sentence-streaming mode). Each sentence is queued individually so the first words play back almost immediately. Pause/resume use KokoroSharp's built-in audio-player control (`CrossPlatformHelper.GetAudioPlayer().Pause()`).
3. **UI** — `MainViewModel` (INotifyPropertyChanged MVVM) drives the `MainWindow` via compiled Avalonia bindings. The `EqBarsControl` uses a `DispatcherTimer` with spring-physics to animate the bars while speech is active.

## License

MIT — see [LICENSE](LICENSE).
