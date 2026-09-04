using System.Diagnostics;

namespace RoslynTools.Services;

/// <summary>
/// Where one find-unused run spent its time, and how many questions it asked Roslyn to get there.
/// <see cref="SearchedDocuments" /> is the sum of the document sets those questions covered — the
/// total work the searches were asked to do, and the number a narrower search scope moves.
/// The counters matter as much as the milliseconds: the cost of this analysis is understood to be
/// <c>candidate symbols × cost of a reference search</c>, so a change is only an improvement if it
/// moves <see cref="ReferenceQueries" /> or the work inside each one. A timing that improves while
/// the query count is unchanged is a machine having a good day.
/// </summary>
internal sealed record FindUnusedProfile(
    long TotalMs,
    long ReflectionScanMs,
    long ReferenceSearchMs,
    int Documents,
    int DocumentsSkipped,
    int TypeCandidates,
    int MethodCandidates,
    int UniqueCandidates,
    int ReferenceQueries,
    int ExtensionMethodQueries,
    long SearchedDocuments)
{
    /// <summary>
    /// Time spent outside the reflection scan and the reference searches — reading syntax roots,
    /// binding semantic models, and walking declarations. Derived rather than measured, so it
    /// cannot drift out of agreement with the parts it is the remainder of.
    /// </summary>
    internal long CandidateDiscoveryMs => Math.Max(0, TotalMs - ReflectionScanMs - ReferenceSearchMs);
}

/// <summary>
/// Collects a <see cref="FindUnusedProfile" /> while the analysis runs. Counters are interlocked
/// because the analysis is expected to gain bounded parallelism over its candidates: making that
/// safe now costs nothing and removes a race that would otherwise be introduced silently, by a
/// change whose author is thinking about throughput rather than about this class.
/// </summary>
internal sealed class FindUnusedProfiler
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _reflectionScanTicks;
    private long _referenceSearchTicks;
    private int _documents;
    private int _documentsSkipped;
    private int _typeCandidates;
    private int _methodCandidates;
    private int _uniqueCandidates;
    private int _referenceQueries;
    private int _extensionMethodQueries;
    private long _searchedDocuments;

    /// <summary>Reads the clock the durations are measured against.</summary>
    internal static long Timestamp() => Stopwatch.GetTimestamp();

    internal void RecordReflectionScan(long startedAt) =>
        Interlocked.Add(ref _reflectionScanTicks, Timestamp() - startedAt);

    /// <summary>
    /// Records one completed reference search. Extension-method probes are counted separately
    /// because they are a per-member multiplier on a type candidate rather than a candidate of
    /// their own, and folding them into the same number would hide that.
    /// </summary>
    internal void RecordReferenceSearch(
        long startedAt,
        int searchedDocuments,
        bool forExtensionMethod = false)
    {
        Interlocked.Add(ref _referenceSearchTicks, Timestamp() - startedAt);
        Interlocked.Increment(ref _referenceQueries);
        Interlocked.Add(ref _searchedDocuments, searchedDocuments);
        if (forExtensionMethod)
            Interlocked.Increment(ref _extensionMethodQueries);
    }

    internal void CountDocument() => Interlocked.Increment(ref _documents);

    internal void CountSkippedDocument() => Interlocked.Increment(ref _documentsSkipped);

    internal void CountTypeCandidate() => Interlocked.Increment(ref _typeCandidates);

    internal void CountMethodCandidate() => Interlocked.Increment(ref _methodCandidates);

    internal void CountUniqueCandidate() => Interlocked.Increment(ref _uniqueCandidates);

    internal FindUnusedProfile Snapshot() =>
        new(
            ToMilliseconds(Timestamp() - _started),
            ToMilliseconds(Interlocked.Read(ref _reflectionScanTicks)),
            ToMilliseconds(Interlocked.Read(ref _referenceSearchTicks)),
            Volatile.Read(ref _documents),
            Volatile.Read(ref _documentsSkipped),
            Volatile.Read(ref _typeCandidates),
            Volatile.Read(ref _methodCandidates),
            Volatile.Read(ref _uniqueCandidates),
            Volatile.Read(ref _referenceQueries),
            Volatile.Read(ref _extensionMethodQueries),
            Interlocked.Read(ref _searchedDocuments));

    private static long ToMilliseconds(long ticks) => ticks * 1000 / Stopwatch.Frequency;
}
