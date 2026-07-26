namespace R128Net;

public readonly record struct LoudnessMeterOptions
{
    public const long UpstreamMaxHistoryMilliseconds = 4294967295L;

    public long MaxWindowMilliseconds { get; init; }

    public long MaxHistoryMilliseconds { get; init; } = UpstreamMaxHistoryMilliseconds;

    public bool PreallocateHistory { get; init; }

    public bool UseUpstreamWindowOverflow { get; init; }

    public LoudnessMeterOptions()
    {
    }
}
