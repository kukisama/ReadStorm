using ReadStorm.Application.Services;
using ReadStorm.Domain.Models;

namespace ReadStorm.Tests;

public class ReaderAutoPrefetchPolicyTests
{
    [Fact]
    public void ShouldQueueWindow_JumpTrigger_AlwaysTrueWhenWindowAvailable()
    {
        var plan = new ReaderAutoDownloadPlan
        {
            ShouldQueueWindow = false,
            WindowStartIndex = 12,
            WindowTakeCount = 8,
            HasGap = true,
            FirstGapIndex = 3,
        };

        var shouldQueue = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, "jump");

        Assert.True(shouldQueue);
    }

    [Fact]
    public void ShouldQueueWindow_ForceCurrentTrigger_AlwaysTrueWhenWindowAvailable()
    {
        var plan = new ReaderAutoDownloadPlan
        {
            ShouldQueueWindow = false,
            WindowStartIndex = 24,
            WindowTakeCount = 6,
            HasGap = true,
            FirstGapIndex = 10,
        };

        var shouldQueue = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, "force-current");

        Assert.True(shouldQueue);
    }

    [Fact]
    public void ShouldQueueWindow_NonJump_RespectsPlanWindowDecision()
    {
        var plan = new ReaderAutoDownloadPlan
        {
            ShouldQueueWindow = false,
            WindowStartIndex = 12,
            WindowTakeCount = 8,
            HasGap = true,
            FirstGapIndex = 3,
        };

        var shouldQueue = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, "open");

        Assert.False(shouldQueue);
    }

    [Fact]
    public void ShouldQueueWindow_NoWindow_NeverQueues()
    {
        var plan = new ReaderAutoDownloadPlan
        {
            ShouldQueueWindow = true,
            WindowStartIndex = 0,
            WindowTakeCount = 0,
        };

        var shouldQueueJump = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, "jump");
        var shouldQueueOpen = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, "open");

        Assert.False(shouldQueueJump);
        Assert.False(shouldQueueOpen);
    }
}