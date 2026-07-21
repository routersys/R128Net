namespace R128Net;

[Flags]
public enum LoudnessModes
{
    None = 0,
    Momentary = 1 << 0,
    ShortTerm = (1 << 1) | Momentary,
    Integrated = (1 << 2) | Momentary,
    LoudnessRange = (1 << 3) | ShortTerm,
    SamplePeak = (1 << 4) | Momentary,
    TruePeak = (1 << 5) | SamplePeak | Momentary,
    Histogram = 1 << 6,
    All = ShortTerm | Integrated | LoudnessRange | TruePeak,
}
