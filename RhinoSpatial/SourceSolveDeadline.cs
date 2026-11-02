using System;
using System.Threading;

namespace RhinoSpatial;

internal static class SourceSolveDeadline
{
    internal static T Run<T>(CancellationToken caller, Func<CancellationToken, T> compute)
    {
        caller.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            var result = compute(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException ex) when (!caller.IsCancellationRequested)
        { throw new TimeoutException("Loading exceeded its two-minute operation limit. Reduce the study area or retry later.", ex); }
    }
}
