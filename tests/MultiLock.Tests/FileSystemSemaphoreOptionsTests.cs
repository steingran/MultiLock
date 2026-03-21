using MultiLock.FileSystem;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class FileSystemSemaphoreOptionsTests
{
    [Fact]
    public void Validate_WithDefaultOptions_ShouldNotThrow()
    {
        var options = new FileSystemSemaphoreOptions();
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void Validate_WithValidCustomOptions_ShouldNotThrow()
    {
        var options = new FileSystemSemaphoreOptions
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "my-semaphores"),
            FileExtension = ".lock",
            AutoCreateDirectory = false
        };
        Should.NotThrow(() => options.Validate());
    }

    [Fact]
    public void Validate_WithEmptyDirectoryPath_ShouldThrow()
    {
        var options = new FileSystemSemaphoreOptions { DirectoryPath = "" };
        ArgumentException ex = Should.Throw<ArgumentException>(() => options.Validate());
        ex.ParamName.ShouldBe("DirectoryPath");
    }

    [Fact]
    public void Validate_WithWhitespaceDirectoryPath_ShouldThrow()
    {
        var options = new FileSystemSemaphoreOptions { DirectoryPath = "   " };
        ArgumentException ex = Should.Throw<ArgumentException>(() => options.Validate());
        ex.ParamName.ShouldBe("DirectoryPath");
    }

    [Fact]
    public void Validate_WithEmptyFileExtension_ShouldThrow()
    {
        var options = new FileSystemSemaphoreOptions { FileExtension = "" };
        ArgumentException ex = Should.Throw<ArgumentException>(() => options.Validate());
        ex.ParamName.ShouldBe("FileExtension");
    }

    [Fact]
    public void Validate_WithWhitespaceFileExtension_ShouldThrow()
    {
        var options = new FileSystemSemaphoreOptions { FileExtension = "  " };
        ArgumentException ex = Should.Throw<ArgumentException>(() => options.Validate());
        ex.ParamName.ShouldBe("FileExtension");
    }
}

