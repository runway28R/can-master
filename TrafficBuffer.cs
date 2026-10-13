namespace PcanCanMonitor;

public sealed class TrafficBuffer
{
    private readonly object _sync = new();
    private readonly Queue<CanFrame> _frames;
    private int _capacity;

    public TrafficBuffer(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _frames = new Queue<CanFrame>(capacity);
    }

    public int Capacity
    {
        get { lock (_sync) return _capacity; }
    }

    public int Count
    {
        get { lock (_sync) return _frames.Count; }
    }

    public void Add(CanFrame frame)
    {
        lock (_sync)
        {
            if (_frames.Count >= _capacity)
                _frames.Dequeue();
            _frames.Enqueue(frame);
        }
    }

    public void SetCapacity(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        lock (_sync)
        {
            _capacity = capacity;
            while (_frames.Count > _capacity)
                _frames.Dequeue();
        }
    }

    public void AddRange(IEnumerable<CanFrame> frames)
    {
        foreach (var frame in frames)
            Add(frame);
    }

    public IReadOnlyList<CanFrame> Snapshot()
    {
        lock (_sync)
            return _frames.ToArray();
    }

    public void Clear()
    {
        lock (_sync)
            _frames.Clear();
    }
}
