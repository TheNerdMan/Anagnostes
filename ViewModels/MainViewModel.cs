using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    public string Url { get => _url; set { _url = value; OnPropertyChanged(); LoadCommand.RaiseCanExecuteChanged(); } }

    private string _articleTitle = "ANAGNOSTES";
    public string ArticleTitle { get => _articleTitle; set { _articleTitle = value; OnPropertyChanged(); } }

    private string _articleText = string.Empty;
    public string ArticleText { get => _articleText; set { _articleText = value; OnPropertyChanged(); } }

    private string _statusText = "Paste a URL and press LOAD";
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

    public IReadOnlyList<string> Voices { get; } =
    ["af_heart", "af_bella", "af_nicole", "am_michael", "am_fenrir", "bf_emma", "bm_george"];

    private string _voice = "af_heart";
    public string Voice
    {
        get => _voice;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _voice == value) return;
            _voice = value;
            if (ModelReady) _tts.SetVoice(value);
            _settings.SetVoice(value);
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

    private bool _isSettingsOpen;
    public bool IsSettingsOpen { get => _isSettingsOpen; private set { _isSettingsOpen = value; OnPropertyChanged(); } }

    private float _volume = 1.0f;
    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); _tts.SetVolume(_volume); }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public RelayCommand LoadCommand  { get; }
    public RelayCommand PlayCommand  { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StopCommand  { get; }
    public RelayCommand SettingsCommand { get; }

    public MainViewModel(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<MainViewModel>();
        _articles = new ArticleService(loggerFactory.CreateLogger<ArticleService>());
        _settings = new SettingsService(loggerFactory.CreateLogger<SettingsService>());
        _tts = new TtsService(loggerFactory.CreateLogger<TtsService>());
        _voice = _settings.Voice;
        _shareAnonymousLogs = _settings.ShareAnonymousLogs;

        LoadCommand  = new RelayCommand(OnLoad,  () => !IsBusy && !string.IsNullOrWhiteSpace(Url));
        PlayCommand  = new RelayCommand(OnPlay,  () => ModelReady && !string.IsNullOrWhiteSpace(ArticleText) && TtsState is TtsState.Idle or TtsState.Paused);
        PauseCommand = new RelayCommand(OnPause, () => TtsState == Services.TtsState.Speaking);
        StopCommand  = new RelayCommand(OnStop,  () => TtsState != TtsState.Idle);
        SettingsCommand = new RelayCommand(() => IsSettingsOpen = !IsSettingsOpen);

        _tts.StateChanged     += s => Dispatcher.UIThread.Post(() => TtsState = s);
        _tts.Error            += msg => Dispatcher.UIThread.Post(() => StatusText = $"⚠ {msg}");
        _tts.DownloadProgress += p => Dispatcher.UIThread.Post(() =>
        {
            DownloadProgress = p;
            StatusText = $"Downloading model… {p:P0}";
        });

        _ = InitModelAsync();
    }

    private async Task InitModelAsync()
    {
        _logger.LogInformation("TTS initialization requested.");
        IsBusy = true;
        StatusText = "Loading TTS model…";
        try
        {
            await Task.Run(_tts.InitialiseAsync);
            ModelReady = _tts.IsReady;
            if (ModelReady)
            {
                _tts.SetVoice(Voice);
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

    private async void OnLoad()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        _logger.LogInformation("Article load requested.");
        IsBusy = true;
        StatusText = "Fetching article…";
        ArticleText = string.Empty;
        try
        {
            var (title, text) = await _articles.FetchAsync(Url.Trim());
            ArticleTitle = title;
            ArticleText  = text;
            StatusText   = $"Loaded: {title}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Article load failed.");
            StatusText = $"⚠ {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async void OnPlay()
    {
        if (string.IsNullOrWhiteSpace(ArticleText)) return;
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
