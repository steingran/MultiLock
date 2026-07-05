using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class SemaphoreOptionsValidationTests
{
    private static SemaphoreOptions ValidOptions() => new()
    {
        SemaphoreName = "test-semaphore",
        MaxCount = 3,
        HeartbeatInterval = TimeSpan.FromSeconds(5),
        HeartbeatTimeout = TimeSpan.FromSeconds(30),
        AcquisitionInterval = TimeSpan.FromSeconds(5),
        MaxRetryAttempts = 3,
        RetryBaseDelay = TimeSpan.FromMilliseconds(100),
        RetryMaxDelay = TimeSpan.FromSeconds(5)
    };

    [Fact]
    public void Validate_WithValidOptions_ShouldNotThrow()
    {
        Should.NotThrow(() => ValidOptions().Validate());
    }

    [Fact]
    public void Validate_WithEmptySemaphoreName_ShouldThrow()
    {
        var options = ValidOptions();
        options.SemaphoreName = "";
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("SemaphoreName");
    }

    [Fact]
    public void Validate_WithWhitespaceSemaphoreName_ShouldThrow()
    {
        var options = ValidOptions();
        options.SemaphoreName = "   ";
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("SemaphoreName");
    }

    [Fact]
    public void Validate_WithMaxCountZero_ShouldThrow()
    {
        var options = ValidOptions();
        options.MaxCount = 0;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("MaxCount");
    }

    [Fact]
    public void Validate_WithMaxCountNegative_ShouldThrow()
    {
        var options = ValidOptions();
        options.MaxCount = -1;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("MaxCount");
    }

    [Fact]
    public void Validate_WithZeroHeartbeatInterval_ShouldThrow()
    {
        var options = ValidOptions();
        options.HeartbeatInterval = TimeSpan.Zero;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("HeartbeatInterval");
    }

    [Fact]
    public void Validate_WithZeroHeartbeatTimeout_ShouldThrow()
    {
        var options = ValidOptions();
        options.HeartbeatTimeout = TimeSpan.Zero;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("HeartbeatTimeout");
    }

    [Fact]
    public void Validate_WithHeartbeatTimeoutEqualToInterval_ShouldThrow()
    {
        var options = ValidOptions();
        options.HeartbeatInterval = TimeSpan.FromSeconds(10);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(10);
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("HeartbeatTimeout");
    }

    [Fact]
    public void Validate_WithHeartbeatTimeoutLessThanInterval_ShouldThrow()
    {
        var options = ValidOptions();
        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
        options.HeartbeatTimeout = TimeSpan.FromSeconds(10);
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("HeartbeatTimeout");
    }

    [Fact]
    public void Validate_WithZeroAcquisitionInterval_ShouldThrow()
    {
        var options = ValidOptions();
        options.AcquisitionInterval = TimeSpan.Zero;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("AcquisitionInterval");
    }

    [Fact]
    public void Validate_WithNegativeMaxRetryAttempts_ShouldThrow()
    {
        var options = ValidOptions();
        options.MaxRetryAttempts = -1;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("MaxRetryAttempts");
    }

    [Fact]
    public void Validate_WithZeroRetryBaseDelay_ShouldThrow()
    {
        var options = ValidOptions();
        options.RetryBaseDelay = TimeSpan.Zero;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("RetryBaseDelay");
    }

    [Fact]
    public void Validate_WithZeroRetryMaxDelay_ShouldThrow()
    {
        var options = ValidOptions();
        options.RetryMaxDelay = TimeSpan.Zero;
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("RetryMaxDelay");
    }

    [Fact]
    public void Validate_WithRetryMaxDelayLessThanBase_ShouldThrow()
    {
        var options = ValidOptions();
        options.RetryBaseDelay = TimeSpan.FromSeconds(5);
        options.RetryMaxDelay = TimeSpan.FromSeconds(1);
        Should.Throw<ArgumentException>(() => options.Validate())
            .ParamName.ShouldBe("RetryMaxDelay");
    }

    [Fact]
    public void Validate_WithMaxRetryAttemptsZero_ShouldNotThrow()
    {
        var options = ValidOptions();
        options.MaxRetryAttempts = 0;
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void Validate_WithRetryBaseDelayEqualsMaxDelay_ShouldNotThrow()
    {
        var options = ValidOptions();
        options.RetryBaseDelay = TimeSpan.FromSeconds(1);
        options.RetryMaxDelay = TimeSpan.FromSeconds(1);
        Should.NotThrow(() => options.Validate());
    }
}

