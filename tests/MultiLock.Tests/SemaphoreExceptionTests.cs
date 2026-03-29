using MultiLock.Exceptions;
using Shouldly;
using Xunit;

// Disambiguate from System.Threading.SemaphoreFullException
using SemaphoreFullException = MultiLock.Exceptions.SemaphoreFullException;

namespace MultiLock.Tests;

public class SemaphoreExceptionTests
{
    // SemaphoreException

    [Fact]
    public void SemaphoreException_DefaultCtor_ShouldCreateInstance()
    {
        var ex = new SemaphoreException();
        ex.ShouldBeOfType<SemaphoreException>();
        ex.ShouldBeAssignableTo<Exception>();
    }

    [Fact]
    public void SemaphoreException_MessageCtor_ShouldSetMessage()
    {
        var ex = new SemaphoreException("test message");
        ex.Message.ShouldBe("test message");
    }

    [Fact]
    public void SemaphoreException_InnerExceptionCtor_ShouldSetInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new SemaphoreException("outer", inner);
        ex.Message.ShouldBe("outer");
        ex.InnerException.ShouldBe(inner);
    }

    // SemaphoreProviderException

    [Fact]
    public void SemaphoreProviderException_DefaultCtor_ShouldCreateInstance()
    {
        var ex = new SemaphoreProviderException();
        ex.ShouldBeAssignableTo<SemaphoreException>();
    }

    [Fact]
    public void SemaphoreProviderException_MessageCtor_ShouldSetMessage()
    {
        var ex = new SemaphoreProviderException("provider error");
        ex.Message.ShouldBe("provider error");
    }

    [Fact]
    public void SemaphoreProviderException_InnerExceptionCtor_ShouldSetInnerException()
    {
        var inner = new TimeoutException("timed out");
        var ex = new SemaphoreProviderException("provider failed", inner);
        ex.Message.ShouldBe("provider failed");
        ex.InnerException.ShouldBe(inner);
    }

    // SemaphoreFullException

    [Fact]
    public void SemaphoreFullException_DefaultCtor_ShouldCreateInstance()
    {
        var ex = new SemaphoreFullException();
        ex.ShouldBeAssignableTo<SemaphoreException>();
        ex.SemaphoreName.ShouldBeNull();
        ex.MaxCount.ShouldBeNull();
        ex.CurrentCount.ShouldBeNull();
    }

    [Fact]
    public void SemaphoreFullException_MessageCtor_ShouldSetMessage()
    {
        var ex = new SemaphoreFullException("semaphore is full");
        ex.Message.ShouldBe("semaphore is full");
    }

    [Fact]
    public void SemaphoreFullException_InnerExceptionCtor_ShouldSetInnerException()
    {
        var inner = new Exception("cause");
        var ex = new SemaphoreFullException("full", inner);
        ex.InnerException.ShouldBe(inner);
    }

    [Fact]
    public void SemaphoreFullException_Properties_ShouldBeSettable()
    {
        var ex = new SemaphoreFullException("full")
        {
            SemaphoreName = "my-semaphore",
            MaxCount = 5,
            CurrentCount = 5
        };
        ex.SemaphoreName.ShouldBe("my-semaphore");
        ex.MaxCount.ShouldBe(5);
        ex.CurrentCount.ShouldBe(5);
    }

    // SemaphoreTimeoutException

    [Fact]
    public void SemaphoreTimeoutException_DefaultCtor_ShouldCreateInstance()
    {
        var ex = new SemaphoreTimeoutException();
        ex.ShouldBeAssignableTo<SemaphoreException>();
        ex.Timeout.ShouldBeNull();
        ex.SemaphoreName.ShouldBeNull();
    }

    [Fact]
    public void SemaphoreTimeoutException_MessageCtor_ShouldSetMessage()
    {
        var ex = new SemaphoreTimeoutException("timed out waiting");
        ex.Message.ShouldBe("timed out waiting");
    }

    [Fact]
    public void SemaphoreTimeoutException_InnerExceptionCtor_ShouldSetInnerException()
    {
        var inner = new TimeoutException("underlying");
        var ex = new SemaphoreTimeoutException("timeout", inner);
        ex.InnerException.ShouldBe(inner);
    }

    [Fact]
    public void SemaphoreTimeoutException_Properties_ShouldBeSettable()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var ex = new SemaphoreTimeoutException("timeout")
        {
            Timeout = timeout,
            SemaphoreName = "rate-limiter"
        };
        ex.Timeout.ShouldBe(timeout);
        ex.SemaphoreName.ShouldBe("rate-limiter");
    }
}

