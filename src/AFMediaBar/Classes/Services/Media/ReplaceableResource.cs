namespace AFMediaBar.Classes.Services;

/// <summary>
/// 为可替换的外部资源提供短时租约；替换或关闭后，旧资源要等最后一个使用者退出才会交给回收回调。
/// Provides short leases for a replaceable external resource; replacement or shutdown retires the old resource only after its last user exits.
/// </summary>
internal sealed class ReplaceableResource<T> : IDisposable where T : class
{
    internal sealed class Slot(T value)
    {
        public T Value { get; } = value;
        public int Readers { get; set; }
        public bool IsRetired { get; set; }
        public bool RetirementScheduled { get; set; }
    }

    private readonly object _gate = new();
    private readonly Action<T> _retire;
    private Slot? _current;
    private bool _isDisposed;

    public ReplaceableResource(T initial, Action<T> retire)
    {
        _current = new Slot(initial);
        _retire = retire;
    }

    public Lease? TryAcquire()
    {
        lock (_gate)
        {
            if (_isDisposed || _current is null)
            {
                return null;
            }

            _current.Readers++;
            return new Lease(this, _current);
        }
    }

    public bool TryReplace(T replacement)
    {
        T? retired;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return false;
            }

            var previous = _current!;
            _current = new Slot(replacement);
            previous.IsRetired = true;
            retired = TakeRetired(previous);
        }

        RetireOutsideGate(retired);
        return true;
    }

    public void Dispose()
    {
        T? retired;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            var previous = _current!;
            _current = null;
            previous.IsRetired = true;
            retired = TakeRetired(previous);
        }

        RetireOutsideGate(retired);
    }

    private void Release(Slot slot)
    {
        T? retired;
        lock (_gate)
        {
            slot.Readers--;
            retired = TakeRetired(slot);
        }

        RetireOutsideGate(retired);
    }

    private static T? TakeRetired(Slot slot)
    {
        if (!slot.IsRetired || slot.Readers != 0 || slot.RetirementScheduled)
        {
            return null;
        }

        slot.RetirementScheduled = true;
        return slot.Value;
    }

    private void RetireOutsideGate(T? retired)
    {
        if (retired is not null)
        {
            _retire(retired);
        }
    }

    /// <summary>保持一只资源在使用期间不被回收。/ Keeps one resource alive until the caller finishes using it.</summary>
    internal sealed class Lease : IDisposable
    {
        private ReplaceableResource<T>? _owner;
        private readonly Slot _slot;

        internal Lease(ReplaceableResource<T> owner, Slot slot)
        {
            _owner = owner;
            _slot = slot;
        }

        public T Value => _slot.Value;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_slot);
    }
}
