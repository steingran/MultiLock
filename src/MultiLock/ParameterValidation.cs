using System.Text.RegularExpressions;

namespace MultiLock;

/// <summary>
/// Provides validation methods for common parameters used across the leader election and semaphore frameworks.
/// </summary>
public static partial class ParameterValidation
{
    private const int maxIdentifierLength = 255;
    private const int maxSqlIdentifierLength = 128;
    private const int maxMetadataKeyLength = 255;
    private const int maxMetadataValueLength = 4000;
    private const int maxMetadataCount = 100;

    /// <summary>
    /// Validates an election group parameter.
    /// </summary>
    /// <param name="electionGroup">The election group to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the election group is invalid.</exception>
    public static void ValidateElectionGroup(string electionGroup, string paramName = "electionGroup")
    {
        if (string.IsNullOrWhiteSpace(electionGroup))
            throw new ArgumentException("Election group cannot be null, empty, or whitespace.", paramName);

        if (electionGroup.Length > maxIdentifierLength)
            throw new ArgumentException($"Election group cannot exceed {maxIdentifierLength} characters.", paramName);

        if (!IsValidIdentifier(electionGroup))
            throw new ArgumentException(
                "Election group contains invalid characters. Only alphanumeric characters, underscores, hyphens, and periods are allowed.",
                paramName);
    }

    /// <summary>
    /// Validates a participant ID parameter.
    /// </summary>
    /// <param name="participantId">The participant ID to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the participant ID is invalid.</exception>
    public static void ValidateParticipantId(string participantId, string paramName = "participantId")
    {
        if (string.IsNullOrWhiteSpace(participantId))
            throw new ArgumentException("Participant ID cannot be null, empty, or whitespace.", paramName);

        if (participantId.Length > maxIdentifierLength)
            throw new ArgumentException($"Participant ID cannot exceed {maxIdentifierLength} characters.", paramName);

        if (!IsValidIdentifier(participantId))
            throw new ArgumentException(
                "Participant ID contains invalid characters. Only alphanumeric characters, underscores, hyphens, and periods are allowed.",
                paramName);
    }

    /// <summary>
    /// Validates a metadata dictionary parameter.
    /// </summary>
    /// <param name="metadata">The metadata dictionary to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentNullException">Thrown when metadata is null.</exception>
    /// <exception cref="ArgumentException">Thrown when metadata contains invalid entries.</exception>
    public static void ValidateMetadata(IReadOnlyDictionary<string, string> metadata, string paramName = "metadata")
    {
        if (metadata == null)
            throw new ArgumentNullException(paramName, "Metadata cannot be null.");

        if (metadata.Count > maxMetadataCount)
            throw new ArgumentException($"Metadata cannot contain more than {maxMetadataCount} entries.", paramName);

        foreach (KeyValuePair<string, string> kvp in metadata)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key))
                throw new ArgumentException("Metadata keys cannot be null, empty, or whitespace.", paramName);

            if (kvp.Key.Length > maxMetadataKeyLength)
                throw new ArgumentException($"Metadata keys cannot exceed {maxMetadataKeyLength} characters.", paramName);

            if (kvp.Value is { Length: > maxMetadataValueLength })
                throw new ArgumentException($"Metadata values cannot exceed {maxMetadataValueLength} characters.", paramName);
        }
    }

    /// <summary>
    /// Validates a lock timeout parameter.
    /// </summary>
    /// <param name="lockTimeout">The lock timeout to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the lock timeout is invalid.</exception>
    public static void ValidateLockTimeout(TimeSpan lockTimeout, string paramName = "lockTimeout")
    {
        if (lockTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Lock timeout must be positive.", paramName);

        if (lockTimeout > TimeSpan.FromDays(1))
            throw new ArgumentException("Lock timeout cannot exceed 1 day.", paramName);
    }

    /// <summary>
    /// Validates a semaphore name parameter.
    /// </summary>
    /// <param name="semaphoreName">The semaphore name to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the semaphore name is invalid.</exception>
    public static void ValidateSemaphoreName(string semaphoreName, string paramName = "semaphoreName")
    {
        if (string.IsNullOrWhiteSpace(semaphoreName))
            throw new ArgumentException("Semaphore name cannot be null, empty, or whitespace.", paramName);

        if (semaphoreName.Length > maxIdentifierLength)
            throw new ArgumentException($"Semaphore name cannot exceed {maxIdentifierLength} characters.", paramName);

        if (!IsValidIdentifier(semaphoreName))
            throw new ArgumentException(
                "Semaphore name contains invalid characters. Only alphanumeric characters, underscores, hyphens, and periods are allowed.",
                paramName);
    }

    /// <summary>
    /// Validates a holder ID parameter.
    /// </summary>
    /// <param name="holderId">The holder ID to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the holder ID is invalid.</exception>
    public static void ValidateHolderId(string holderId, string paramName = "holderId")
    {
        if (string.IsNullOrWhiteSpace(holderId))
            throw new ArgumentException("Holder ID cannot be null, empty, or whitespace.", paramName);

        if (holderId.Length > maxIdentifierLength)
            throw new ArgumentException($"Holder ID cannot exceed {maxIdentifierLength} characters.", paramName);

        if (!IsValidIdentifier(holderId))
            throw new ArgumentException(
                "Holder ID contains invalid characters. Only alphanumeric characters, underscores, hyphens, and periods are allowed.",
                paramName);
    }

    /// <summary>
    /// Validates a max count parameter for semaphores.
    /// </summary>
    /// <param name="maxCount">The max count to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the max count is invalid.</exception>
    public static void ValidateMaxCount(int maxCount, string paramName = "maxCount")
    {
        if (maxCount < 1)
            throw new ArgumentException("Max count must be at least 1.", paramName);

        if (maxCount > 10000)
            throw new ArgumentException("Max count cannot exceed 10000.", paramName);
    }

    /// <summary>
    /// Validates a slot timeout parameter.
    /// </summary>
    /// <param name="slotTimeout">The slot timeout to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the slot timeout is invalid.</exception>
    public static void ValidateSlotTimeout(TimeSpan slotTimeout, string paramName = "slotTimeout")
    {
        if (slotTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Slot timeout must be positive.", paramName);

        if (slotTimeout > TimeSpan.FromDays(1))
            throw new ArgumentException("Slot timeout cannot exceed 1 day.", paramName);
    }

    /// <summary>
    /// Validates that a string is a safe SQL identifier (schema or table name) that can be
    /// interpolated directly into a SQL statement without quoting injection risk.
    /// Only letters, digits, and underscores are permitted; the name must start with a letter or underscore.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="paramName">The parameter name for error messages.</param>
    /// <exception cref="ArgumentException">Thrown when the value is not a safe SQL identifier.</exception>
    public static void ValidateSqlIdentifier(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("SQL identifier cannot be null, empty, or whitespace.", paramName);

        if (value.Length > maxSqlIdentifierLength)
            throw new ArgumentException(
                $"SQL identifier cannot exceed {maxSqlIdentifierLength} characters.", paramName);

        if (!IsSafeSqlIdentifier(value))
            throw new ArgumentException(
                "SQL identifier contains invalid characters. " +
                "Only letters, digits, and underscores are allowed, and the name must start with a letter or underscore.",
                paramName);
    }

    /// <summary>
    /// Checks if a string is a valid identifier (alphanumeric, underscore, hyphen, period).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is a valid identifier; otherwise, false.</returns>
    private static bool IsValidIdentifier(string value)
    {
        return IdentifierRegex().IsMatch(value);
    }

    private static bool IsSafeSqlIdentifier(string value)
    {
        return SqlIdentifierRegex().IsMatch(value);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex SqlIdentifierRegex();
}

