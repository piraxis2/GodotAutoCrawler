﻿using AutoCrawler.addons.behaviortree.node;

namespace AutoCrawler.addons.behaviortree;

public static class Constants
{
    public static class Notifications
    {
        public const int NotificationChildAdded = 10;
    }
    public enum BtStatus { Success, Failure, Running }
}