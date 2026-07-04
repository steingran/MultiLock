using MultiLock.ZooKeeper;
using Shouldly;
using Xunit;

namespace MultiLock.Tests;

public class ZooKeeperSemaphoreNodeOrderingTests
{
    [Theory]
    [InlineData("holder1-0000000000", 0)]
    [InlineData("holder1-0000000042", 42)]
    [InlineData("zzz-0000000001", 1)]
    [InlineData("my-holder.01-0000000005", 5)]
    public void GetSequenceNumber_WithSequentialNodeName_ReturnsSequenceSuffix(string nodeName, long expected)
    {
        ZooKeeperSemaphoreProvider.GetSequenceNumber(nodeName).ShouldBe(expected);
    }

    [Theory]
    [InlineData("no-sequence-suffix")]
    [InlineData("trailing-hyphen-")]
    [InlineData("nohyphen")]
    [InlineData("")]
    [InlineData("holder-+42")]
    [InlineData("holder- 42")]
    [InlineData("holder-42 ")]
    public void GetSequenceNumber_WithUnparsableName_SortsLast(string nodeName)
    {
        ZooKeeperSemaphoreProvider.GetSequenceNumber(nodeName).ShouldBe(long.MaxValue);
    }

    [Fact]
    public void OrderingBySequence_ReflectsCreationOrder_NotHolderIdSort()
    {
        // "zzz" acquired first (sequence 1), "aaa" second (sequence 2). A lexicographic sort of
        // the full node names would put "aaa" first and let a later creator rank itself within
        // maxCount ahead of an established holder; sequence order must win.
        var children = new List<string> { "aaa-0000000002", "zzz-0000000001" };

        List<string> sorted = children.OrderBy(ZooKeeperSemaphoreProvider.GetSequenceNumber).ToList();

        sorted.ShouldBe(new[] { "zzz-0000000001", "aaa-0000000002" });
    }

    [Fact]
    public void OrderingBySequence_WithHyphenatedHolderIds_UsesLastHyphenSuffix()
    {
        var children = new List<string>
        {
            "web-worker-2-0000000003",
            "api-node-1-0000000001",
            "web-worker-1-0000000002"
        };

        List<string> sorted = children.OrderBy(ZooKeeperSemaphoreProvider.GetSequenceNumber).ToList();

        sorted.ShouldBe(new[] { "api-node-1-0000000001", "web-worker-1-0000000002", "web-worker-2-0000000003" });
    }
}
