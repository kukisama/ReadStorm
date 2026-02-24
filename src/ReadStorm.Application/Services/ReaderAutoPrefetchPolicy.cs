using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Services;

/// <summary>
/// 阅读自动预取策略：将“触发原因”与“规划结果”组合成最终执行分支。
/// </summary>
public static class ReaderAutoPrefetchPolicy
{
    /// <summary>
    /// 是否应优先下发“当前窗口”下载任务。
    /// 规则：
    /// 1) jump/force-current（用户主动跳章或当前章强制兜底）始终窗口优先；
    /// 2) 非高优先触发保持原有窗口判定；
    /// 3) 无有效窗口（WindowTakeCount <= 0）时不下发窗口任务。
    /// </summary>
    public static bool ShouldQueueWindow(ReaderAutoDownloadPlan plan, string trigger)
    {
        if (plan.WindowTakeCount <= 0)
        {
            return false;
        }

        if (string.Equals(trigger, "jump", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trigger, "force-current", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return plan.ShouldQueueWindow;
    }
}