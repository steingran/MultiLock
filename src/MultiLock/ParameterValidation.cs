using System.Text.RegularExpressions;

namespace MultiLock;

/// <summary>
/// Provides validation methods for common parameters used across the leader election framework.
/// </summary>
public static partial class ParameterValidation
{
    private const int maxIdentifierLength = 255;
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
    /// Checks if a string is a valid identifier (alphanumeric, underscore, hyphen, period).
    /// </summary>
    /// <param name="value">The value to check.</param>
    /// <returns>True if the value is a valid identifier; otherwise, false.</returns>
    private static bool IsValidIdentifier(string value)
    {
        return IdentifierRegex().IsMatch(value);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled)]
    private static partial Regex IdentifierRegex();
}

