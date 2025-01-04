using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.addons.behaviortree;

public static class Util
{
    public struct BehaviorLog
    {
        public Constants.BtStatus Status;
        public long Time;
        public BehaviorLog()
        {
            Status = Constants.BtStatus.Failure;
            Time = 0;
        }
    }
    
    
}