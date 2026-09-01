using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Models;
using Core.Services;

namespace GsiHost.Services;

public sealed class AppStateService : IAppStateService, IDisposable
{
    private const int MaxEvents = 200;
    private readonly object _lock = new();
    private readonly GsiProcessingService _processor;
    private readonly SimpleSubject<StatusSnapshot> _statusSubject = new();
    private readonly SimpleSubject<NormalizedEvent> _eventSubject = new();
    private readonly List<NormalizedEvent> _recentEvents = new();
    private StatusSnapshot _current;

    public AppStateService(GsiProcessingService processor)
    {
        _processor = processor;
        _current = new StatusSnapshot(
            GsiStatus: "Disconnected",
            LastSnapshotAt: null,
            Game: "Unknown",
            LastEvent: null
        );

        _processor.Processed += OnProcessed;
    }

    public IObservable<StatusSnapshot> StatusSnapshot => _statusSubject;

    public IObservable<NormalizedEvent> Events => _eventSubject;

    public Task<StatusSnapshot> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_current);
        }
    }

    public IReadOnlyList<NormalizedEvent> GetRecentEvents()
    {
        lock (_lock)
        {
            return _recentEvents.ToList();
        }
    }

    /// <summary>
    /// Clears the recent-events ring (e.g. after <c>POST /gsi/reset</c>) so
    /// <c>GET /events</c> does not show stale entries from before the reset.
    /// </summary>
    public void ClearRecentEvents()
    {
        lock (_lock)
        {
            _recentEvents.Clear();
        }
    }

    private void OnProcessed(object? sender, GsiProcessedEventArgs args)
    {
        lock (_lock)
        {
            foreach (var normalizedEvent in args.Events)
            {
                _recentEvents.Add(normalizedEvent);
                if (_recentEvents.Count > MaxEvents)
                {
                    _recentEvents.RemoveAt(0);
                }
            }
        }

        var snapshot = args.Snapshot;
        var lastEvent = args.Events.LastOrDefault();
        var status = new StatusSnapshot(
            GsiStatus: "Connected",
            LastSnapshotAt: snapshot.Timestamp,
            Game: snapshot.GameId ?? "Unknown",
            LastEvent: lastEvent
        );

        lock (_lock)
        {
            _current = status;
        }

        foreach (var normalizedEvent in args.Events)
        {
            _eventSubject.OnNext(normalizedEvent);
        }

        _statusSubject.OnNext(status);
    }

    public void Dispose()
    {
        _processor.Processed -= OnProcessed;
    }
}
