using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using Microsoft.Extensions.Logging;

namespace Anagnostes.Services;

public enum TtsState { Idle, Loading, Speaking, Paused }

/// <summary>Wraps KokoroSharp to provide TTS playback with play/pause/stop control.</summary>
public class TtsService : IDisposable
{
    private readonly ILogger<TtsService> _logger;
    private KokoroTTS? _tts;
    private KokoroVoice? _voice;
    private CancellationTokenSource? _cts;
    private string? _activeText;
    private string? _pausedText;
    private int _currentSentenceIndex;
    private int _pausedSentenceIndex;
    private int _playbackVersion;

    public TtsState State { get; private set; } = TtsState.Idle;
    public bool IsReady => _tts != null && _voice != null;
    public event Action<TtsState>? StateChanged;
    public event Action<string>? Error;
    public event Action<double>? DownloadProgress;

    public TtsService(ILogger<TtsService> logger) => _logger = logger;

    /// <summary>Loads the Kokoro model asynchronously. Must be called before Speak.</summary>
    public async Task InitialiseAsync()
    {
        if (_tts != null) return;
        _logger.LogInformation("TTS model load started.");
        SetState(TtsState.Loading);
        try
        {
            _tts = await KokoroTTS.LoadModelAsync(
                model: default,
                OnDownloadProgress: p => DownloadProgress?.Invoke(p),
                sessionOptions: null).ConfigureAwait(false);

            _voice = KokoroVoiceManager.GetVoice("af_heart");
            _logger.LogInformation("TTS model load completed.");
            SetState(TtsState.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS model load failed.");
            SetState(TtsState.Idle);
            Error?.Invoke($"Model load failed: {ex.Message}");
        }
    }

    /// <summary>Speaks the supplied text, sentence by sentence, honouring pause/stop.</summary>
    public async Task SpeakAsync(string text, CancellationToken externalCt = default)
    {
        if (_tts == null || _voice == null)
            throw new InvalidOperationException("TTS not initialised. Call InitialiseAsync first.");

        var resumeAt = State == TtsState.Paused && string.Equals(text, _pausedText, StringComparison.Ordinal)
            ? _pausedSentenceIndex
            : 0;

        StopInternal(); // cancel any previous playback
        var playbackVersion = ++_playbackVersion;
        _activeText = text;
        _pausedText = null;
        _pausedSentenceIndex = 0;
        _logger.LogInformation("Speech started. {CharacterCount} characters queued. {StartSentenceIndex}", text.Length, resumeAt);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;
        var config = new KokoroSharp.Processing.KokoroTTSPipelineConfig { Speed = 1.0f };

        SetState(TtsState.Speaking);
        try
        {
            var sentences = SplitSentences(text);
            for (var i = resumeAt; i < sentences.Length; i++)
            {
                if (ct.IsCancellationRequested) break;
                _currentSentenceIndex = i;

                // Wait for current utterance to complete via TaskCompletionSource
                var tcs = new TaskCompletionSource<bool>();
                using var reg = ct.Register(() => tcs.TrySetCanceled());

                void OnCompleted(KokoroSharp.Core.SpeechCompletionPacket _) => tcs.TrySetResult(true);
                _tts.OnSpeechCompleted += OnCompleted;
                try
                {
                    _tts.SpeakFast(sentences[i], _voice, config);
                    await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    _tts.OnSpeechCompleted -= OnCompleted;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Speech cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech failed.");
            Error?.Invoke($"Speech failed: {ex.Message}");
        }
        finally
        {
            if (playbackVersion == _playbackVersion)
            {
                _activeText = null;
                _currentSentenceIndex = 0;
                _logger.LogInformation("Speech ended.");
                SetState(TtsState.Idle);
            }
        }
    }

    public void Pause()
    {
        if (State != TtsState.Speaking || _activeText == null) return;

        _pausedText = _activeText;
        _pausedSentenceIndex = _currentSentenceIndex;
        _playbackVersion++;
        StopInternal();
        _tts?.StopPlayback();
        _logger.LogInformation("Speech paused at sentence {SentenceIndex}.", _pausedSentenceIndex);
        SetState(TtsState.Paused);
    }

    public void SetVoice(string voiceId)
    {
        _voice = KokoroVoiceManager.GetVoice(voiceId);
        _logger.LogInformation("TTS voice changed. {VoiceId}", voiceId);
    }

    public void SetVolume(float volume)
    {
        _tts?.SetVolume(Math.Clamp(volume, 0f, 1f));
    }

    public void Stop()
    {
        _logger.LogInformation("Speech stopped.");
        _playbackVersion++;
        _activeText = null;
        _pausedText = null;
        _currentSentenceIndex = 0;
        _pausedSentenceIndex = 0;
        StopInternal();
        _tts?.StopPlayback();
        SetState(TtsState.Idle);
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void SetState(TtsState s) { State = s; StateChanged?.Invoke(s); }

    /// <summary>Splits text into sentence-sized chunks for streaming playback.</summary>
    private static string[] SplitSentences(string text)
    {
        var parts = Regex.Split(text, @"(?<=[.!?])\s+");
        var result = new System.Collections.Generic.List<string>();
        foreach (var s in parts)
        {
            var trimmed = s.Trim();
            if (trimmed.Length > 0) result.Add(trimmed);
        }
        return result.ToArray();
    }

    public void Dispose()
    {
        StopInternal();
        _tts?.Dispose();
    }
}
