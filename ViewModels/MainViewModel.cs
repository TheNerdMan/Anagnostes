using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Anagnostes.Services;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Anagnostes.ViewModels;

public class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? _) => canExecute?.Invoke() ?? true;
    public void Execute(object? _) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly ArticleService _articles;
    private readonly SettingsService _settings;
    private readonly TtsService _tts;
    private CancellationTokenSource? _speakCts;

    // ── Bindable properties ──────────────────────────────────────────────────

    private string _url = string.Empty;
    public string Url { get => _url; set { _url = value; OnPropertyChanged(); FetchCommand.RaiseCanExecuteChanged(); } }

    private string _fetchedArticleTitle = "";
    public string FetchedArticleTitle { get => _fetchedArticleTitle; set { _fetchedArticleTitle = value; OnPropertyChanged(); } }

    private string _fetchedArticleText = string.Empty;
    public string FetchedArticleText { get => _fetchedArticleText; set { _fetchedArticleText = value; OnPropertyChanged();  } }

    private string _articleTitle = "Ready";
    public string ArticleTitle { get => _articleTitle; set { _articleTitle = value; OnPropertyChanged(); } }

    private string _articleText = string.Empty;
    private bool _suppressAutoLoad;
    public string ArticleText
    {
        get => _articleText;
        set
        {
            var previous = _articleText;
            _articleText = value;
            OnPropertyChanged();
            LoadCommand.RaiseCanExecuteChanged();

            // Detect a paste (or drag-drop/IME bulk insert) as opposed to normal typing:
            // a single keystroke only ever changes the length by one character, whereas
            // pasting drops a whole block of text in at once.
            if (!_suppressAutoLoad && !IsBusy && LooksLikePastedText(previous, value))
            {
                OnLoad();
            }
        }
    }

    private static bool LooksLikePastedText(string previous, string current)
        // A single keystroke inserts at most one character - except Enter, which can
        // insert a 2-character "\r\n" now that the box accepts multi-line text. Require
        // a bigger jump than that so normal typing/newlines never false-trigger this.
        => !string.IsNullOrWhiteSpace(current) && current.Length - previous.Length > 2;

    private string _statusText = "Paste a URL (or any text) and press LOAD";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); RefreshCommands(); } }

    private double _downloadProgress;
    public double DownloadProgress { get => _downloadProgress; set { _downloadProgress = value; OnPropertyChanged(); } }

    private bool _modelReady;
    public bool ModelReady { get => _modelReady; set { _modelReady = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanChangeVoice)); RefreshCommands(); } }

    private TtsState _ttsState = TtsState.Idle;
    public TtsState TtsState
    {
        get => _ttsState;
        private set { _ttsState = value; OnPropertyChanged(); RefreshCommands();
            OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(IsPaused)); OnPropertyChanged(nameof(CanChangeVoice)); }
    }

    public bool IsPlaying => TtsState == TtsState.Speaking;
    public bool IsPaused  => TtsState == TtsState.Paused;
    public bool CanChangeVoice => ModelReady && TtsState == TtsState.Idle;

    public record Voice
    {
        public Voice(string a, string b)
        {
            KokoroName = a;
            VoiceName = b;
        }
        public string KokoroName { get; internal set; }
        public string VoiceName { get; internal set; }
    }

    public static IReadOnlyList<Voice> Voices { get; } =
    [
        new ("af_heart", "US/F - Heart"), 
        new ("af_bella" , "US/F - Bella"), 
        new ("af_nicole", "US/F - Nicole"),
        new ("am_michael", "US/M - Michael"), 
        new ("am_fenrir", "US/M - Fenrir"), 
        new ("bf_emma", "GB/F - Emma"),
        new ("bm_george", "GB/M - George")
    ];

    private Voice _voice = Voices[0];
    public Voice SelectedVoice
    {
        get => _voice;
        set
        {
            _voice = value;
            if (ModelReady) _tts.SetVoice(value.KokoroName);
            _settings.SetVoice(value.KokoroName);
            OnPropertyChanged();
        }
    }

    private bool _shareAnonymousLogs;
    public bool ShareAnonymousLogs
    {
        get => _shareAnonymousLogs;
        set
        {
            if (_shareAnonymousLogs == value) return;
            _shareAnonymousLogs = value;
            _settings.SetShareAnonymousLogs(value);
            _logger.LogInformation("Anonymous log sharing preference changed. {Enabled}", value);
            OnPropertyChanged();
        }
    }

    private bool _autoSpeak = true;
    public bool AutoSpeak
    {
        get => _autoSpeak;
        set
        {
            if (_autoSpeak == value) return;
            _autoSpeak = value;
            _settings.SetAutoSpeak(value);
            OnPropertyChanged();
        }
    }

    private string _modelFolder = string.Empty;
    public string ModelFolder
    {
        get => _modelFolder;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _modelFolder == value) return;
            _modelFolder = value;
            _settings.SetModelFolder(value);
            OnPropertyChanged();
        }
    }

    private bool _isSettingsOpen;
    public bool IsSettingsOpen { get => _isSettingsOpen; private set { _isSettingsOpen = value; OnPropertyChanged(); } }

    private float _volume = 1.0f;
    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); _tts.SetVolume(_volume); }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public RelayCommand FetchCommand  { get; }
    public RelayCommand LoadCommand  { get; }
    public RelayCommand PlayCommand  { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StopCommand  { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ResetModelFolderCommand { get; }

    public MainViewModel(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MainViewModel>();
        _articles = new ArticleService(loggerFactory.CreateLogger<ArticleService>());
        _settings = new SettingsService(loggerFactory.CreateLogger<SettingsService>());
        _tts = new TtsService(loggerFactory.CreateLogger<TtsService>());
        _voice = Voices.FirstOrDefault(c => c.KokoroName == _settings.Voice) ?? Voices.First();
        _shareAnonymousLogs = _settings.ShareAnonymousLogs;
        _autoSpeak = _settings.AutoSpeak;
        _modelFolder = _settings.ModelFolder;

        FetchCommand  = new RelayCommand(OnFetch,  () => !IsBusy && !string.IsNullOrWhiteSpace(Url));
        LoadCommand  = new RelayCommand(OnLoad,  () => !IsBusy && !string.IsNullOrWhiteSpace(ArticleText));
        PlayCommand  = new RelayCommand(OnPlay,  () => ModelReady && !string.IsNullOrWhiteSpace(ArticleText) && TtsState is TtsState.Idle or TtsState.Paused);
        PauseCommand = new RelayCommand(OnPause, () => TtsState == Services.TtsState.Speaking);
        StopCommand  = new RelayCommand(OnStop,  () => TtsState != TtsState.Idle);
        SettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);
        ResetModelFolderCommand = new RelayCommand(() => ModelFolder = SettingsService.DefaultModelFolder);

        _tts.StateChanged     += s => Dispatcher.UIThread.Post(() => TtsState = s);
        _tts.Error            += msg => Dispatcher.UIThread.Post(() => StatusText = $"⚠ {msg}");
        _tts.DownloadProgress += p => Dispatcher.UIThread.Post(() =>
        {
            DownloadProgress = p;
            ArticleTitle = "Loading";
            StatusText = $"Downloading model… {p:P0}";
        });

        _ = InitModelAsync();
    }

    private async Task InitModelAsync()
    {
        _logger.LogInformation("TTS initialization requested.");
        IsBusy = true;
        ArticleTitle = "Loading";
        StatusText = "Loading TTS model…";
        try
        {
            await Task.Run(() => _tts.InitialiseAsync(_modelFolder));
            ModelReady = _tts.IsReady;
            if (ModelReady)
            {
                _tts.SetVoice(SelectedVoice.KokoroName);
                StatusText = "Ready — paste a URL and press LOAD";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS initialization failed.");
            StatusText = $"⚠ Model load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void OnFetch()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        _logger.LogInformation("Article fetch requested.");
        IsBusy = true;
        ArticleTitle = "Loading";
        StatusText = "Fetching article…";
        ArticleText = string.Empty;
        try
        {
            var (title, text) = await _articles.FetchAsync(Url.Trim());
            FetchedArticleTitle = title;
            FetchedArticleText  = text;
            await LoadArticleAsync(title, text, setArticleText: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Article fetch failed.");
            StatusText = $"⚠️ {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    // Fired either by the (unbound-by-default) LoadCommand or automatically when a
    // paste into the article text box is detected. In both cases ArticleText already
    // holds the text to speak, so we only need to set a title and kick off speech.
    private async void OnLoad()
    {
        if (string.IsNullOrWhiteSpace(ArticleText)) return;
        await LoadArticleAsync(title: null, ArticleText, setArticleText: false);
    }

    private async Task LoadArticleAsync(string? title, string text, bool setArticleText)
    {
        IsBusy = true;
        ArticleTitle = "Loading";
        StatusText = "Loading Text...";
        try
        {
            var displayTitle = string.IsNullOrWhiteSpace(title) ? "Custom Text" : title;
            ArticleTitle = displayTitle;

            if (setArticleText)
            {
                // Prevent the paste-detection logic in the ArticleText setter from
                // re-entering OnLoad when we assign fetched text here.
                _suppressAutoLoad = true;
                ArticleText = text;
                _suppressAutoLoad = false;
            }

            StatusText = $"Loaded: {displayTitle}";
            if (AutoSpeak && ModelReady) await SpeakArticleAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Article load failed.");
            StatusText = $"⚠ {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async void OnPlay() => await SpeakArticleAsync();

    private async Task SpeakArticleAsync()
    {
        if (string.IsNullOrWhiteSpace(ArticleText) || !ModelReady) return;
        _logger.LogInformation("Play requested.");
        _speakCts = new CancellationTokenSource();
        await _tts.SpeakAsync(ArticleText, _speakCts.Token);
    }

    private void OnPause()
    {
        _logger.LogInformation("Pause requested.");
        _tts.Pause();
    }

    private void OnStop()
    {
        _logger.LogInformation("Stop requested.");
        _speakCts?.Cancel();
        _tts.Stop();
    }

    private void RefreshCommands()
    {
        LoadCommand.RaiseCanExecuteChanged();
        PlayCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _logger.LogInformation("Main view model disposing.");
        _speakCts?.Cancel();
        _speakCts?.Dispose();
        _tts.Dispose();
        _articles.Dispose();
    }
}
