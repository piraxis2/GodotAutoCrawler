using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.addons.behaviortree;

public static class Util
{
    public struct BehaviorLog
    {
        public BehaviorTree_Node.BtStatus Status;
        public long Time;
        public BehaviorLog()
        {
            Status = BehaviorTree_Node.BtStatus.Failure;
            Time = 0;
        }
    }
    
    
}