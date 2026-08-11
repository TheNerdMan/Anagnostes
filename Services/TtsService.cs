using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using KokoroSharp.Utilities;

namespace Anagnostes.Services;

public enum TtsState { Idle, Loading, Speaking, Paused }

/// <summary>Wraps KokoroSharp to provide TTS playback with play/pause/stop control.</summary>
public class TtsService : IDisposable
{
    private KokoroTTS? _tts;
    private KokoroVoice? _voice;
    private CancellationTokenSource? _cts;

    public TtsState State { get; private set; } = TtsState.Idle;
    public event Action<TtsState>? StateChanged;
    public event Action<string>? Error;
    public event Action<double>? DownloadProgress;

    /// <summary>Loads the Kokoro model asynchronously. Must be called before Speak.</summary>
    public async Task InitialiseAsync()
    {
        if (_tts != null) return;
        SetState(TtsState.Loading);
        try
        {
            _tts = await KokoroTTS.LoadModelAsync(
                model: default,
                OnDownloadProgress: p => DownloadProgress?.Invoke(p),
                sessionOptions: null).ConfigureAwait(false);

            _voice = KokoroVoiceManager.GetVoice("af_heart");
            SetState(TtsState.Idle);
        }
        catch (Exception ex)
        {
            SetState(TtsState.Idle);
            Error?.Invoke($"Model load failed: {ex.Message}");
        }
    }

    /// <summary>Speaks the supplied text, sentence by sentence, honouring pause/stop.</summary>
    public async Task SpeakAsync(string text, CancellationToken externalCt = default)
    {
        if (_tts == null || _voice == null)
            throw new InvalidOperationException("TTS not initialised. Call InitialiseAsync first.");

        StopInternal(); // cancel any previous playback

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _cts.Token;
        var config = new KokoroSharp.Processing.KokoroTTSPipelineConfig { Speed = 1.0f };

        SetState(TtsState.Speaking);
        try
        {
            var sentences = SplitSentences(text);
            foreach (var sentence in sentences)
            {
                if (ct.IsCancellationRequested) break;

                // Wait for current utterance to complete via TaskCompletionSource
                var tcs = new TaskCompletionSource<bool>();
                using var reg = ct.Register(() => tcs.TrySetCanceled());

                void OnCompleted(KokoroSharp.Core.SpeechCompletionPacket _) => tcs.TrySetResult(true);
                _tts.OnSpeechCompleted += OnCompleted;
                try
                {
                    _tts.SpeakFast(sentence, _voice, config);
                    await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    _tts.OnSpeechCompleted -= OnCompleted;
                }
            }
        }
        catch (OperationCanceledException) { /* expected on stop */ }
        finally
        {
            SetState(TtsState.Idle);
        }
    }

    public void Pause()
    {
        if (State != TtsState.Speaking) return;
        CrossPlatformHelper.GetAudioPlayer()?.Pause();
        SetState(TtsState.Paused);
    }

    public void Resume()
    {
        if (State != TtsState.Paused) return;
        CrossPlatformHelper.GetAudioPlayer()?.Play();
        SetState(TtsState.Speaking);
    }

    public void SetVolume(float volume)
    {
        _tts?.SetVolume(Math.Clamp(volume, 0f, 1f));
    }

    public void Stop()
    {
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
