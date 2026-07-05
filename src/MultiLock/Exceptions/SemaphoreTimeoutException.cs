namespace MultiLock.Exceptions;

/// <summary>
/// Exception thrown when a semaphore operation times out.
/// </summary>
public class SemaphoreTimeoutException : SemaphoreException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreTimeoutException"/> class.
    /// </summary>
    public SemaphoreTimeoutException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreTimeoutException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SemaphoreTimeoutException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreTimeoutException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SemaphoreTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the timeout duration that was exceeded.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets or sets the name of the semaphore that timed out.
    /// </summary>
    public string? SemaphoreName { get; init; }
}

