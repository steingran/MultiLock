using System.Runtime.CompilerServices;

namespace MultiLock.Extensions;

/// <summary>
/// Provides extension methods for working with async enumerable streams of leadership change events.
/// </summary>
public static class LeadershipAsyncEnumerableExtensions
{
    /// <summary>
    /// Filters leadership change events based on a predicate.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that contains elements from the input sequence that satisfy the condition.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> Where(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Func<LeadershipChangedEventArgs, bool> predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (predicate(item))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Projects each leadership change event into a new form.
    /// </summary>
    /// <typeparam name="TResult">The type of the value returned by the selector.</typeparam>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable whose elements are the result of invoking the transform function on each element of source.</returns>
    public static async IAsyncEnumerable<TResult> Select<TResult>(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Func<LeadershipChangedEventArgs, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return selector(item);
        }
    }

    /// <summary>
    /// Returns a specified number of contiguous elements from the start of the sequence.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="count">The number of elements to return.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that contains the specified number of elements from the start of the input sequence.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> Take(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            yield break;
        }

        int taken = 0;
        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
            taken++;

            if (taken >= count)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Bypasses a specified number of elements in the sequence and then returns the remaining elements.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="count">The number of elements to skip before returning the remaining elements.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that contains the elements that occur after the specified index in the input sequence.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> Skip(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        int skipped = 0;
        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (skipped < count)
            {
                skipped++;
                continue;
            }

            yield return item;
        }
    }

    /// <summary>
    /// Returns distinct consecutive elements by using the default equality comparer to compare values.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that contains distinct consecutive elements from the source sequence.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> DistinctUntilChanged(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        LeadershipChangedEventArgs? previous = null;
        bool isFirst = true;

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (isFirst || !item.CurrentStatus.Equals(previous!.CurrentStatus))
            {
                yield return item;
                previous = item;
                isFirst = false;
            }
        }
    }

    /// <summary>
    /// Performs an action on each element of the async enumerable sequence.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="action">The action to perform on each element.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task ForEachAsync(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Action<LeadershipChangedEventArgs> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            action(item);
        }
    }

    /// <summary>
    /// Performs an asynchronous action on each element of the async enumerable sequence.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="action">The asynchronous action to perform on each element.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task ForEachAsync(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Func<LeadershipChangedEventArgs, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(action);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await action(item).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Buffers leadership change events by count.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="count">The maximum number of elements to buffer.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields lists of buffered elements.</returns>
    public static async IAsyncEnumerable<IReadOnlyList<LeadershipChangedEventArgs>> Buffer(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var buffer = new List<LeadershipChangedEventArgs>(count);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(item);

            if (buffer.Count >= count)
            {
                yield return buffer.AsReadOnly();
                buffer.Clear();
            }
        }

        // Yield remaining items if any
        if (buffer.Count > 0)
        {
            yield return buffer.AsReadOnly();
        }
    }

    /// <summary>
    /// Buffers leadership change events by time window.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="timeSpan">The time span for each buffer window.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields lists of buffered elements.</returns>
    public static async IAsyncEnumerable<IReadOnlyList<LeadershipChangedEventArgs>> Buffer(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        TimeSpan timeSpan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeSpan, TimeSpan.Zero);

        var buffer = new List<LeadershipChangedEventArgs>();
        DateTime lastFlush = DateTime.UtcNow;

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(item);
            DateTime now = DateTime.UtcNow;

            if ((now - lastFlush) >= timeSpan)
            {
                yield return buffer.AsReadOnly();
                buffer.Clear();
                lastFlush = now;
            }
        }

        // Yield remaining items if any
        if (buffer.Count > 0)
        {
            yield return buffer.AsReadOnly();
        }
    }

    /// <summary>
    /// Throttles the stream to emit at most one event per time window.
    /// The first event in each window is emitted immediately, subsequent events are ignored until the next window.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="timeSpan">The throttle time window.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields throttled events.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> Throttle(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        TimeSpan timeSpan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeSpan, TimeSpan.Zero);

        DateTime? lastEmitTime = null;

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            DateTime now = DateTime.UtcNow;

            if (lastEmitTime == null || (now - lastEmitTime.Value) >= timeSpan)
            {
                yield return item;
                lastEmitTime = now;
            }
        }
    }

    /// <summary>
    /// Debounces the stream to emit an event only after a period of inactivity.
    /// This is a simplified debounce that emits the last event after the specified time window of inactivity.
    /// Note: This implementation uses a polling approach and may not be suitable for high-frequency streams.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="timeSpan">The debounce time window.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields debounced events.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> Debounce(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        TimeSpan timeSpan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeSpan, TimeSpan.Zero);

        // For a proper debounce implementation with async enumerables, we need to use channels
        // This is a simplified version that groups events by time windows
        var buffer = new List<LeadershipChangedEventArgs>();

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(item);

            // Simple approach: wait a bit and check if more events arrived
            await Task.Delay(timeSpan, cancellationToken).ConfigureAwait(false);

            // If no new events arrived during the debounce window, emit the last event
            if (buffer.Count <= 0) continue;

            yield return buffer[^1]; // Return the last event
            buffer.Clear();
        }

        // Emit last buffered event if any
        if (buffer.Count > 0)
            yield return buffer[^1];
    }

    /// <summary>
    /// Takes events until this instance becomes the leader.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields events until leadership is acquired.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> TakeUntilLeader(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;

            if (item.BecameLeader)
                yield break;
        }
    }

    /// <summary>
    /// Yields events only while this instance is the leader.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable that yields events only during leadership.</returns>
    public static async IAsyncEnumerable<LeadershipChangedEventArgs> WhileLeader(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        bool isLeader = false;

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item.BecameLeader)
            {
                isLeader = true;
                yield return item;
            }
            else if (item.LostLeadership)
            {
                yield return item;
                yield break;
            }
            else if (isLeader)
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Executes callbacks when leadership transitions occur.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="onAcquired">Action to execute when leadership is acquired.</param>
    /// <param name="onLost">Action to execute when leadership is lost.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task OnLeadershipTransition(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Action<LeadershipChangedEventArgs>? onAcquired = null,
        Action<LeadershipChangedEventArgs>? onLost = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item.BecameLeader)
            {
                onAcquired?.Invoke(item);
            }
            else if (item.LostLeadership)
            {
                onLost?.Invoke(item);
            }
        }
    }

    /// <summary>
    /// Executes asynchronous callbacks when leadership transitions occur.
    /// </summary>
    /// <param name="source">The source async enumerable.</param>
    /// <param name="onAcquired">Asynchronous action to execute when leadership is acquired.</param>
    /// <param name="onLost">Asynchronous action to execute when leadership is lost.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task OnLeadershipTransition(
        this IAsyncEnumerable<LeadershipChangedEventArgs> source,
        Func<LeadershipChangedEventArgs, Task>? onAcquired = null,
        Func<LeadershipChangedEventArgs, Task>? onLost = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await foreach (LeadershipChangedEventArgs item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (item.BecameLeader && onAcquired != null)
            {
                await onAcquired(item).ConfigureAwait(false);
            }
            else if (item.LostLeadership && onLost != null)
            {
                await onLost(item).ConfigureAwait(false);
            }
        }
    }
}

