using Shouldly;
using Xunit;

namespace MultiLock.Tests;

/// <summary>
/// Tests for semaphore-related parameter validation methods.
/// </summary>
public class SemaphoreParameterValidationTests
{
    // ValidateSemaphoreName tests

    [Fact]
    public void ValidateSemaphoreName_WithValidValue_ShouldNotThrow()
    {
        // Arrange
        const string validSemaphoreName = "valid-semaphore_name.123";

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateSemaphoreName(validSemaphoreName));
    }

    [Fact]
    public void ValidateSemaphoreName_WithNull_ShouldThrowArgumentException()
    {
        // Arrange
        string? semaphoreName = null;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSemaphoreName(semaphoreName!));
        exception.ParamName.ShouldBe("semaphoreName");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateSemaphoreName_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        const string semaphoreName = "";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSemaphoreName(semaphoreName));
        exception.ParamName.ShouldBe("semaphoreName");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateSemaphoreName_WithWhitespace_ShouldThrowArgumentException()
    {
        // Arrange
        const string semaphoreName = "   ";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSemaphoreName(semaphoreName));
        exception.ParamName.ShouldBe("semaphoreName");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateSemaphoreName_WithTooLongValue_ShouldThrowArgumentException()
    {
        // Arrange
        string semaphoreName = new('a', 256);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSemaphoreName(semaphoreName));
        exception.ParamName.ShouldBe("semaphoreName");
        exception.Message.ShouldContain("cannot exceed 255 characters");
    }

    [Theory]
    [InlineData("semaphore with spaces")]
    [InlineData("semaphore@invalid")]
    [InlineData("semaphore#invalid")]
    public void ValidateSemaphoreName_WithInvalidCharacters_ShouldThrowArgumentException(string invalidName)
    {
        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSemaphoreName(invalidName));
        exception.ParamName.ShouldBe("semaphoreName");
        exception.Message.ShouldContain("invalid characters");
    }

    // ValidateHolderId tests

    [Fact]
    public void ValidateHolderId_WithValidValue_ShouldNotThrow()
    {
        // Arrange
        const string validHolderId = "valid-holder_id.123";

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateHolderId(validHolderId));
    }

    [Fact]
    public void ValidateHolderId_WithNull_ShouldThrowArgumentException()
    {
        // Arrange
        string? holderId = null;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateHolderId(holderId!));
        exception.ParamName.ShouldBe("holderId");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateHolderId_WithEmptyString_ShouldThrowArgumentException()
    {
        // Arrange
        const string holderId = "";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateHolderId(holderId));
        exception.ParamName.ShouldBe("holderId");
        exception.Message.ShouldContain("cannot be null, empty, or whitespace");
    }

    [Fact]
    public void ValidateHolderId_WithTooLongValue_ShouldThrowArgumentException()
    {
        // Arrange
        string holderId = new('a', 256);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateHolderId(holderId));
        exception.ParamName.ShouldBe("holderId");
        exception.Message.ShouldContain("cannot exceed 255 characters");
    }

    [Fact]
    public void ValidateHolderId_WithInvalidCharacters_ShouldThrowArgumentException()
    {
        // Arrange
        const string holderId = "holder with spaces";

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateHolderId(holderId));
        exception.ParamName.ShouldBe("holderId");
        exception.Message.ShouldContain("invalid characters");
    }

    // ValidateMaxCount tests

    [Fact]
    public void ValidateMaxCount_WithValidValue_ShouldNotThrow()
    {
        // Arrange
        const int maxCount = 5;

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMaxCount(maxCount));
    }

    [Fact]
    public void ValidateMaxCount_WithOne_ShouldNotThrow()
    {
        // Arrange
        const int maxCount = 1;

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMaxCount(maxCount));
    }

    [Fact]
    public void ValidateMaxCount_WithMaxValue_ShouldNotThrow()
    {
        // Arrange
        const int maxCount = 10000;

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateMaxCount(maxCount));
    }

    [Fact]
    public void ValidateMaxCount_WithZero_ShouldThrowArgumentException()
    {
        // Arrange
        const int maxCount = 0;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMaxCount(maxCount));
        exception.ParamName.ShouldBe("maxCount");
        exception.Message.ShouldContain("must be at least 1");
    }

    [Fact]
    public void ValidateMaxCount_WithNegative_ShouldThrowArgumentException()
    {
        // Arrange
        const int maxCount = -1;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMaxCount(maxCount));
        exception.ParamName.ShouldBe("maxCount");
        exception.Message.ShouldContain("must be at least 1");
    }

    [Fact]
    public void ValidateMaxCount_WithTooLargeValue_ShouldThrowArgumentException()
    {
        // Arrange
        const int maxCount = 10001;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateMaxCount(maxCount));
        exception.ParamName.ShouldBe("maxCount");
        exception.Message.ShouldContain("cannot exceed 10000");
    }

    // ValidateSlotTimeout tests

    [Fact]
    public void ValidateSlotTimeout_WithValidTimeout_ShouldNotThrow()
    {
        // Arrange
        var slotTimeout = TimeSpan.FromMinutes(5);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateSlotTimeout(slotTimeout));
    }

    [Fact]
    public void ValidateSlotTimeout_WithZero_ShouldThrowArgumentException()
    {
        // Arrange
        TimeSpan slotTimeout = TimeSpan.Zero;

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSlotTimeout(slotTimeout));
        exception.ParamName.ShouldBe("slotTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public void ValidateSlotTimeout_WithNegative_ShouldThrowArgumentException()
    {
        // Arrange
        var slotTimeout = TimeSpan.FromSeconds(-1);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSlotTimeout(slotTimeout));
        exception.ParamName.ShouldBe("slotTimeout");
        exception.Message.ShouldContain("must be positive");
    }

    [Fact]
    public void ValidateSlotTimeout_WithMoreThanOneDay_ShouldThrowArgumentException()
    {
        // Arrange
        TimeSpan slotTimeout = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1);

        // Act & Assert
        ArgumentException exception = Should.Throw<ArgumentException>(() =>
            ParameterValidation.ValidateSlotTimeout(slotTimeout));
        exception.ParamName.ShouldBe("slotTimeout");
        exception.Message.ShouldContain("cannot exceed 1 day");
    }

    [Fact]
    public void ValidateSlotTimeout_WithExactlyOneDay_ShouldNotThrow()
    {
        // Arrange
        var slotTimeout = TimeSpan.FromDays(1);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateSlotTimeout(slotTimeout));
    }

    [Fact]
    public void ValidateSlotTimeout_WithOneMillisecond_ShouldNotThrow()
    {
        // Arrange
        var slotTimeout = TimeSpan.FromMilliseconds(1);

        // Act & Assert
        Should.NotThrow(() => ParameterValidation.ValidateSlotTimeout(slotTimeout));
    }
}

