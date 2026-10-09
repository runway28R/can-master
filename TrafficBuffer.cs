namespace PcanCanMonitor;

public sealed class TrafficBuffer
{
    private const int Capacity = 5000;
    private readonly object _sync = new();
    private readonly Queue<CanFrame> _frames = new(Capacity);

    public int Count
    {
        get { lock (_sync) return _frames.Count; }
    }

    public void Add(CanFrame frame)
    {
        lock (_sync)
        {
            if (_frames.Count >= Capacity)
                _frames.Dequeue();
            _frames.Enqueue(frame);
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
