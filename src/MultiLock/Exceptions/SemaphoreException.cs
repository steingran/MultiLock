namespace MultiLock.Exceptions;

/// <summary>
/// The base exception for all distributed semaphore related errors.
/// </summary>
public class SemaphoreException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreException"/> class.
    /// </summary>
    public SemaphoreException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public SemaphoreException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemaphoreException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public SemaphoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

