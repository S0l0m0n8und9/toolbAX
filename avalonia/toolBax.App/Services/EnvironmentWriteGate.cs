using System;

namespace ToolBax.App.Services;

/// <summary>
/// Shell-scoped admission gate between profile/default/secret commits and live environment writes.
/// Profile commits are exclusive; existing simultaneous live writes remain allowed.
/// </summary>
public sealed class EnvironmentWriteGate
{
    private readonly object _sync = new();
    private bool _profileCommit;
    private int _liveWrites;

    public event Action? StateChanged;

    public bool ProfileCommitInProgress
    {
        get { lock (_sync) return _profileCommit; }
    }

    public bool LiveWriteInProgress
    {
        get { lock (_sync) return _liveWrites != 0; }
    }

    public bool TryAcquireProfileCommit(out IDisposable? lease)
    {
        lock (_sync)
        {
            if (_profileCommit || _liveWrites != 0)
            {
                lease = null;
                return false;
            }
            _profileCommit = true;
            lease = new Lease(this, profile: true);
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool TryAcquireLiveWrite(out IDisposable? lease)
    {
        lock (_sync)
        {
            if (_profileCommit)
            {
                lease = null;
                return false;
            }
            _liveWrites++;
            lease = new Lease(this, profile: false);
        }
        StateChanged?.Invoke();
        return true;
    }

    private void Release(bool profile)
    {
        lock (_sync)
        {
            if (profile) _profileCommit = false;
            else if (_liveWrites > 0) _liveWrites--;
        }
        StateChanged?.Invoke();
    }

    private sealed class Lease : IDisposable
    {
        private EnvironmentWriteGate? _owner;
        private readonly bool _profile;

        public Lease(EnvironmentWriteGate owner, bool profile)
        {
            _owner = owner;
            _profile = profile;
        }

        public void Dispose()
        {
            var owner = System.Threading.Interlocked.Exchange(ref _owner, null);
            owner?.Release(_profile);
        }
    }
}
