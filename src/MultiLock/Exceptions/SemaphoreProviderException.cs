namespace MultiLock.Exceptions;

/// <summary>
/// Exception thrown when a semaphore provider encounters an error.
/// </summary>
public class SemaphoreProviderException : SemaphoreException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreProviderException"/> class.
    /// </summary>
    public SemaphoreProviderException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreProviderException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SemaphoreProviderException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreProviderException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SemaphoreProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

