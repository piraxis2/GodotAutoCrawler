namespace AutoCrawler.Assets.Script.UI.Window;

// LogWindow 상단 필터 탭(Step 2). E-9 채널 4종을 UI 4탭으로 매핑하되, Reward/System은
// "획득·시스템" 한 탭으로 묶는다. `All`은 저장 채널이 아니라 전체 필터다.
public enum LogChannelTab
{
    All,
    Combat,
    Story,
    RewardSystem,
}
