namespace R128Net;

public readonly record struct LoudnessMeterOptions
{
    public const long UpstreamMaxHistoryMilliseconds = 4294967295L;

    private readonly long _maxHistoryOffset;

    public long MaxWindowMilliseconds { get; init; }

    public long MaxHistoryMilliseconds
    {
        get => unchecked(_maxHistoryOffset + UpstreamMaxHistoryMilliseconds);
        init => _maxHistoryOffset = unchecked(value - UpstreamMaxHistoryMilliseconds);
    }

    public bool PreallocateHistory { get; init; }

    public bool UseUpstreamWindowOverflow { get; init; }

    public LoudnessMeterOptions()
    {
    }
}
