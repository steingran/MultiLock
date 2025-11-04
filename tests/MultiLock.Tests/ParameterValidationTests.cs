using System.Collections.ObjectModel;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for the ParameterValidation helper class.
/// </summary>
public class ParameterValidationTests
{
    // ValidateElectionGroup tests

    [Fact]
    public void ValidateElectionGroup_WithValidValue_ShouldNotThrow()
    {
        // Arrange
        const string validElectionGroup = "valid-election_group.123";

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateElectionGroup(validElectionGroup));
    }

    [Fact]
    public void ValidateElectionGroup_WithNull_ShouldThrowArgumentException()
    {
        // Arrange
        string? electionGroup = null;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(electionGroup!));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateElectionGroup_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        const string electionGroup = "";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(electionGroup));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateElectionGroup_WithWhitespace_ShouldThrowArgumentException()
    {
        // Arrange
        const string electionGroup = "   ";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(electionGroup));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateElectionGroup_WithTooLongValue_ShouldThrowArgumentException()
    {
        // Arrange
        string electionGroup = new('a', 256); // 256 characters, exceeds 255 limit

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(electionGroup));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("cannot exceed 255 characters");
    }

    [Fact]
    public void ValidateElectionGroup_WithExactlyMaxLength_ShouldNotThrow()
    {
        // Arrange
        string electionGroup = new('a', 255); // Exactly 255 characters

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateElectionGroup(electionGroup));
    }

    [Theory]
    [InlineData("group with spaces")]
    [InlineData("group@invalid")]
    [InlineData("group#invalid")]
    [InlineData("group$invalid")]
    [InlineData("group%invalid")]
    [InlineData("group&invalid")]
    [InlineData("group*invalid")]
    [InlineData("group(invalid")]
    [InlineData("group)invalid")]
    [InlineData("group+invalid")]
    [InlineData("group=invalid")]
    [InlineData("group[invalid")]
    [InlineData("group]invalid")]
    [InlineData("group{invalid")]
    [InlineData("group}invalid")]
    [InlineData("group|invalid")]
    [InlineData("group\\invalid")]
    [InlineData("group/invalid")]
    [InlineData("group:invalid")]
    [InlineData("group;invalid")]
    [InlineData("group'invalid")]
    [InlineData("group\"invalid")]
    [InlineData("group<invalid")]
    [InlineData("group>invalid")]
    [InlineData("group,invalid")]
    [InlineData("group?invalid")]
    [InlineData("group!invalid")]
    [InlineData("group~invalid")]
    [InlineData("group`invalid")]
    public void ValidateElectionGroup_WithInvalidCharacters_ShouldThrowArgumentException(string invalidGroup)
    {
        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(invalidGroup));
        exception.ParamName.ShouldBe("electionGroup");
        exception.Message.ShouldContain("invalid characters");
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("valid-group")]
    [InlineData("valid_group")]
    [InlineData("valid.group")]
    [InlineData("valid123")]
    [InlineData("123valid")]
    [InlineData("UPPERCASE")]
    [InlineData("lowercase")]
    [InlineData("MixedCase")]
    [InlineData("a")]
    [InlineData("1")]
    [InlineData("group-name_with.multiple-separators123")]
    public void ValidateElectionGroup_WithValidCharacters_ShouldNotThrow(string validGroup)
    {
        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateElectionGroup(validGroup));
    }

    [Fact]
    public void ValidateElectionGroup_WithCustomParamName_ShouldUseCustomName()
    {
        // Arrange
        const string electionGroup = "";
        const string customParamName = "customParam";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateElectionGroup(electionGroup, customParamName));
        exception.ParamName.ShouldBe(customParamName);
    }

    // ValidateParticipantId tests

    [Fact]
    public void ValidateParticipantId_WithValidValue_ShouldNotThrow()
    {
        // Arrange
        const string validParticipantId = "valid-participant_id.123";

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateParticipantId(validParticipantId));
    }

    [Fact]
    public void ValidateParticipantId_WithNull_ShouldThrowArgumentException()
    {
        // Arrange
        string? participantId = null;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateParticipantId(participantId!));
        exception.ParamName.ShouldBe("participantId");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateParticipantId_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        const string participantId = "";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateParticipantId(participantId));
        exception.ParamName.ShouldBe("participantId");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateParticipantId_WithTooLongValue_ShouldThrowArgumentException()
    {
        // Arrange
        string participantId = new('a', 256);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateParticipantId(participantId));
        exception.ParamName.ShouldBe("participantId");
        exception.Message.ShouldContain("cannot exceed 255 characters");
    }

    [Fact]
    public void ValidateParticipantId_WithInvalidCharacters_ShouldThrowArgumentException()
    {
        // Arrange
        const string participantId = "participant with spaces";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateParticipantId(participantId));
        exception.ParamName.ShouldBe("participantId");
        exception.Message.ShouldContain("invalid characters");
    }

    // ValidateMetadata tests

    [Fact]
    public void ValidateMetadata_WithValidMetadata_ShouldNotThrow()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMetadata(metadata));
    }

    [Fact]
    public void ValidateMetadata_WithEmptyMetadata_ShouldNotThrow()
    {
        // Arrange
        var metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMetadata(metadata));
    }

    [Fact]
    public void ValidateMetadata_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        IReadOnlyDictionary<string, string>? metadata = null;

        // Act & Assert
        ArgumentNullException exception = Should.Throw<ArgumentNullException>(() =>
            ParameterValidation.ValidateMetadata(metadata!));
        exception.ParamName.ShouldBe("metadata");
    }

    [Fact]
    public void ValidateMetadata_WithTooManyEntries_ShouldThrowArgumentException()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 101; i++)
        {
            metadata[$"key{i}"] = $"value{i}";
        }

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMetadata(metadata));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("cannot contain more than 100 entries");
    }

    [Fact]
    public void ValidateMetadata_WithExactlyMaxEntries_ShouldNotThrow()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 100; i++)
        {
            metadata[$"key{i}"] = $"value{i}";
        }

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMetadata(metadata));
    }

    // Note: We don't test null keys because Dictionary<string, string> doesn't allow null keys
    // The runtime will throw ArgumentNullException before our validation code runs
    // Empty string keys are tested below, which is the realistic edge case

    [Fact]
    public void ValidateMetadata_WithEmptyKey_ShouldThrowArgumentException()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "", "value" }
        };

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMetadata(metadata));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("keys cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateMetadata_WithWhitespaceKey_ShouldThrowArgumentException()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "   ", "value" }
        };

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMetadata(metadata));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("keys cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateMetadata_WithTooLongKey_ShouldThrowArgumentException()
    {
        // Arrange
        string longKey = new('a', 256);
        var metadata = new Dictionary<string, string>
        {
            { longKey, "value" }
        };

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMetadata(metadata));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("keys cannot exceed 255 characters");
    }

    [Fact]
    public void ValidateMetadata_WithTooLongValue_ShouldThrowArgumentException()
    {
        // Arrange
        string longValue = new('a', 4001);
        var metadata = new Dictionary<string, string>
        {
            { "key", longValue }
        };

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMetadata(metadata));
        exception.ParamName.ShouldBe("metadata");
        exception.Message.ShouldContain("values cannot exceed 4000 characters");
    }

    [Fact]
    public void ValidateMetadata_WithMaxLengthValue_ShouldNotThrow()
    {
        // Arrange
        string maxValue = new('a', 4000);
        var metadata = new Dictionary<string, string>
        {
            { "key", maxValue }
        };

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMetadata(metadata));
    }

    [Fact]
    public void ValidateMetadata_WithNullValue_ShouldNotThrow()
    {
        // Arrange
        var metadata = new Dictionary<string, string>
        {
            { "key", null! }
        };

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMetadata(metadata));
    }

    // ValidateLockTimeout tests

    [Fact]
    public void ValidateLockTimeout_WithValidTimeout_ShouldNotThrow()
    {
        // Arrange
        var lockTimeout = TimeSpan.FromMinutes(5);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateLockTimeout(lockTimeout));
    }

    [Fact]
    public void ValidateLockTimeout_WithZero_ShouldThrowArgumentException()
    {
        // Arrange
        TimeSpan lockTimeout = TimeSpan.Zero;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateLockTimeout(lockTimeout));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public void ValidateLockTimeout_WithNegative_ShouldThrowArgumentException()
    {
        // Arrange
        var lockTimeout = TimeSpan.FromSeconds(-1);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateLockTimeout(lockTimeout));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public void ValidateLockTimeout_WithMoreThanOneDay_ShouldThrowArgumentException()
    {
        // Arrange
        TimeSpan lockTimeout = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateLockTimeout(lockTimeout));
        exception.ParamName.ShouldBe("lockTimeout");
        exception.Message.ShouldContain("cannot exceed 1 day");
    }

    [Fact]
    public void ValidateLockTimeout_WithExactlyOneDay_ShouldNotThrow()
    {
        // Arrange
        var lockTimeout = TimeSpan.FromDays(1);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateLockTimeout(lockTimeout));
    }

    [Fact]
    public void ValidateLockTimeout_WithOneMillisecond_ShouldNotThrow()
    {
        // Arrange
        var lockTimeout = TimeSpan.FromMilliseconds(1);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateLockTimeout(lockTimeout));
    }
}

