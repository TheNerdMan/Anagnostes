using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Anagnostes.Services;

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
    private readonly ArticleService _articles = new();
    private readonly TtsService     _tts      = new();
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
    public bool ModelReady { get => _modelReady; set { _modelReady = value; OnPropertyChanged(); RefreshCommands(); } }

    private TtsState _ttsState = TtsState.Idle;
    public TtsState TtsState
    {
        get => _ttsState;
        private set { _ttsState = value; OnPropertyChanged(); RefreshCommands();
            OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(IsPaused)); }
    }

    public bool IsPlaying => TtsState == TtsState.Speaking;
    public bool IsPaused  => TtsState == TtsState.Paused;

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

    public MainViewModel()
    {
        LoadCommand  = new RelayCommand(OnLoad,  () => !IsBusy && !string.IsNullOrWhiteSpace(Url));
        PlayCommand  = new RelayCommand(OnPlay,  () => ModelReady && !string.IsNullOrWhiteSpace(ArticleText) && TtsState == TtsState.Idle);
        PauseCommand = new RelayCommand(OnPause, () => TtsState == Services.TtsState.Speaking || TtsState == Services.TtsState.Paused);
        StopCommand  = new RelayCommand(OnStop,  () => TtsState != TtsState.Idle);

        _tts.StateChanged     += s => { TtsState = s; };
        _tts.Error            += msg => { StatusText = $"⚠ {msg}"; };
        _tts.DownloadProgress += p  => { DownloadProgress = p; StatusText = $"Downloading model… {p:P0}"; };

        _ = InitModelAsync();
    }

    private async Task InitModelAsync()
    {
        IsBusy = true;
        StatusText = "Loading TTS model…";
        try
        {
            await _tts.InitialiseAsync().ConfigureAwait(false);
            ModelReady = true;
            StatusText = "Ready — paste a URL and press LOAD";
        }
        catch (Exception ex)
        {
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
        IsBusy = true;
        StatusText = "Fetching article…";
        ArticleText = string.Empty;
        try
        {
            var (title, text) = await _articles.FetchAsync(Url.Trim()).ConfigureAwait(false);
            ArticleTitle = title;
            ArticleText  = text;
            StatusText   = $"Loaded: {title}";
        }
        catch (Exception ex)
        {
            StatusText = $"⚠ {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async void OnPlay()
    {
        if (string.IsNullOrWhiteSpace(ArticleText)) return;
        _speakCts = new CancellationTokenSource();
        await _tts.SpeakAsync(ArticleText, _speakCts.Token).ConfigureAwait(false);
    }

    private void OnPause()
    {
        if (TtsState == Services.TtsState.Speaking) _tts.Pause();
        else if (TtsState == Services.TtsState.Paused) _tts.Resume();
    }

    private void OnStop()
    {
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
        _speakCts?.Cancel();
        _speakCts?.Dispose();
        _tts.Dispose();
        _articles.Dispose();
    }
}
