namespace MultiLock.Exceptions;

/// <summary>
/// Exception thrown when a semaphore is at full capacity and cannot accept more holders.
/// </summary>
public class SemaphoreFullException : SemaphoreException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreFullException"/> class.
    /// </summary>
    public SemaphoreFullException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreFullException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SemaphoreFullException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreFullException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SemaphoreFullException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the name of the semaphore that is full.
    /// </summary>
    public string? SemaphoreName { get; init; }

    /// <summary>
    /// Gets or sets the maximum count of the semaphore.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Gets or sets the current count of holders.
    /// </summary>
    public int? CurrentCount { get; init; }
}

