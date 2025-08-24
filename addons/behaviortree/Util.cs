using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.addons.behaviortree;

public static class Util
{
    public struct BehaviorLog
    {
        public BtStatus Status;
        public long Time;
        public BehaviorLog()
        {
            Status = BtStatus.Failure;
            Time = 0;
        }
    }
}