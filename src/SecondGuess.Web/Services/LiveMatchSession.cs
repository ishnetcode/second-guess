using SecondGuess.Domain;
using SecondGuess.Simulation;
using SecondGuess.Simulation.Live;

namespace SecondGuess.Web.Services;

/// <summary>
/// One viewer's live replay: streams a match's events in match-time order (sped up), feeds them to the
/// live stats engine and raises <see cref="Changed"/> so the page can redraw. Supports pause, speed and jumping.
/// </summary>
public sealed class LiveMatchSession : IDisposable
{
    private const double MaxGapSeconds = 2.5;   // long stoppages are shortened so the replay keeps moving

    private readonly SecondGuessService _service;
    private CancellationTokenSource? _playback;
    private int _next;

    public LiveMatchSession(SecondGuessService service, string matchId)
    {
        _service = service;
        Match = service.GetMatch(matchId);
        Engine = service.CreateLiveEngine(matchId);
        Snapshot = Engine.Snapshot();
    }

    public LoadedMatch Match { get; }
    public LiveStatsEngine Engine { get; private set; }
    public LiveSnapshot Snapshot { get; private set; }
    public MatchEvent? LastAction { get; private set; }
    public bool Playing => _playback is { IsCancellationRequested: false };
    public bool Finished => _next >= Match.Events.Count;
    public double Speed { get; set; } = 30;

    /// <summary>Raised after each processed event, with any new overlay cards.</summary>
    public event Action<IReadOnlyList<OverlayCard>>? Changed;

    public void Play()
    {
        if (Playing || Finished) return;
        _playback = new CancellationTokenSource();
        _ = RunAsync(_playback.Token);
    }

    public void Pause() => _playback?.Cancel();

    /// <summary>Jumps to a match minute by replaying everything before it instantly.</summary>
    public void JumpTo(int minute)
    {
        bool wasPlaying = Playing;
        Pause();

        Engine = _service.CreateLiveEngine(Match.Info.MatchId);
        LastAction = null;
        _next = 0;
        long targetMs = Match.Events.FirstOrDefault(e => Minute(e) >= minute)?.TimestampMs ?? long.MaxValue;
        while (_next < Match.Events.Count && Match.Events[_next].TimestampMs < targetMs) Step();

        Snapshot = Engine.Snapshot();
        Changed?.Invoke([]);
        if (wasPlaying) Play();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _next < Match.Events.Count)
            {
                long previous = _next == 0 ? 0 : Match.Events[_next - 1].TimestampMs;
                double gap = Math.Min((Match.Events[_next].TimestampMs - previous) / 1000.0 / Speed, MaxGapSeconds);
                if (gap > 0) await Task.Delay(TimeSpan.FromSeconds(gap), ct);

                var cards = Step();
                Snapshot = Engine.Snapshot();
                Changed?.Invoke(cards);
            }
        }
        catch (OperationCanceledException)
        {
            // Paused.
        }
        finally
        {
            if (_next >= Match.Events.Count) _playback?.Cancel();
        }
    }

    private IReadOnlyList<OverlayCard> Step()
    {
        var e = Match.Events[_next++];
        if (e.FreezeFrame is not null) LastAction = e;
        return Engine.Process(e);
    }

    private static int Minute(MatchEvent e) => int.Parse(e.MatchClock.Split(':')[0]);

    public void Dispose()
    {
        _playback?.Cancel();
        _playback?.Dispose();
    }
}
