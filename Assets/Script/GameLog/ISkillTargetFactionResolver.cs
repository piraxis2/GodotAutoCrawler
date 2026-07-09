namespace AutoCrawler.Assets.Script.GameLog;

// cast_cancel 같은 raw report의 대상 진영을 판정하는 계약(Step 3 A안). adapter가 주입받아 파싱 시점에
// target node path를 진영으로 해석한다. resolve 실패/freed/진영 불명은 `Unknown`으로 fail-closed한다.
public enum LogFaction
{
    Unknown = 0,
    Ally = 1,
    Enemy = 2,
}

public interface ISkillTargetFactionResolver
{
    LogFaction Resolve(string targetPath);
}
