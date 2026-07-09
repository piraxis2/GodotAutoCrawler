using System.Collections.Generic;

namespace AutoCrawler.Assets.Script.GameLog;

// SkillSystem raw report 문자열(`SkillState.Reports`)을 표시용 `GameLogEntry`로 변환하는 경계(D4, Step 3).
// EffectBlock들을 즉시 구조화 SkillReport로 바꾸지 않고, 문자열 계약을 읽어 E-9 정책으로 매핑한다.
//
// 매핑(E-9):
// - damage/miss/stun/stun_miss/bind/chain/knockback/manadrain/selfbuff -> Combat/Detail/Normal.
// - cast_cancel -> resolver로 진영 판정. enemy target = Event/Normal(아군이 적을 끊음),
//   ally target = Event/Warning(아군이 끊김). 진영 불명/unresolved = fail-closed.
// - unknown kind / malformed(토큰 수 불일치·빈 path) = fail-closed(null).
//
// 각 entry는 현지화 준비(`TitleKey` + `Args`)와 v0 표시 fallback(`Title`)을 함께 담는다.
public static class SkillReportToGameLogAdapter
{
    // raw report 하나를 변환한다. 로그를 만들지 않아야 하면 null을 돌려준다(fail-closed).
    // path에는 콜론이 없으므로(Godot 노드명 규칙) ':' split의 마지막 토큰이 항상 target path다.
    public static GameLogEntry Convert(string rawReport, ISkillTargetFactionResolver resolver)
    {
        if (string.IsNullOrEmpty(rawReport)) return null;

        string[] tokens = rawReport.Split(':');
        if (tokens.Length < 2) return null;

        string kind = tokens[0];
        string path = tokens[tokens.Length - 1];
        if (string.IsNullOrEmpty(path)) return null;

        string target = LeafName(path);

        switch (kind)
        {
            case "damage":
                return tokens.Length != 3 ? null : Detail("combat.skill.damage",
                    $"{target} 피해 ({tokens[1]})", Args(("tag", tokens[1]), ("target", target)));
            case "miss":
                return tokens.Length != 3 ? null : Detail("combat.skill.miss",
                    $"{target} 빗나감 ({tokens[1]})", Args(("tag", tokens[1]), ("target", target)));
            case "stun":
                return tokens.Length != 3 ? null : Detail("combat.skill.stun",
                    $"{target} 스턴 {tokens[1]}턴", Args(("turns", tokens[1]), ("target", target)));
            case "stun_miss":
                return tokens.Length != 2 ? null : Detail("combat.skill.stun_miss",
                    $"{target} 스턴 실패", Args(("target", target)));
            case "bind":
                return tokens.Length != 3 ? null : Detail("combat.skill.bind",
                    $"{target} 속박 {tokens[1]}턴", Args(("turns", tokens[1]), ("target", target)));
            case "chain":
                return tokens.Length != 3 ? null : Detail("combat.skill.chain",
                    $"연쇄 {tokens[1]} → {target}", Args(("index", tokens[1]), ("target", target)));
            case "knockback":
                return tokens.Length != 3 ? null : Detail("combat.skill.knockback",
                    $"{target} 넉백 {tokens[1]}칸", Args(("distance", tokens[1]), ("target", target)));
            case "manadrain":
                return tokens.Length != 3 ? null : Detail("combat.skill.manadrain",
                    $"{target} 마나 흡수 {tokens[1]}", Args(("amount", tokens[1]), ("target", target)));
            case "selfbuff":
                return ConvertSelfBuff(tokens, target);
            case "cast_cancel":
                return ConvertCastCancel(tokens, target, resolver);
            default:
                return null; // unknown kind fail-closed.
        }
    }

    // reports를 변환해 sink에 append하고, 실제로 만든 entry 수를 돌려준다. null 결과는 조용히 건너뛴다.
    // 이 메서드가 미래의 전투 hook이 부를 발행 경계다(v0는 live battle에 배선하지 않는다 — Out of Scope).
    public static int PublishReports(
        IEnumerable<string> reports, ISkillTargetFactionResolver resolver, IGameLogSink sink)
    {
        if (reports == null || sink == null) return 0;

        int count = 0;
        foreach (string report in reports)
        {
            GameLogEntry entry = Convert(report, resolver);
            if (entry == null) continue;
            sink.Append(entry);
            count++;
        }

        return count;
    }

    // selfbuff:{Kind}:{DamageMultiplier}x{Turns}:{casterPath} -> 4 토큰. token[2]는 "1.5x2".
    private static GameLogEntry ConvertSelfBuff(string[] tokens, string target)
    {
        if (tokens.Length != 4) return null;

        string kind = tokens[1];
        string mag = tokens[2];
        string mult = mag;
        string turns = "";
        int x = mag.IndexOf('x');
        if (x >= 0)
        {
            mult = mag.Substring(0, x);
            turns = mag.Substring(x + 1);
        }

        return Detail("combat.skill.selfbuff",
            $"{target} 자기 버프 {kind} {mult}x{turns}",
            Args(("kind", kind), ("multiplier", mult), ("turns", turns), ("target", target)));
    }

    // cast_cancel:{targetPath} -> 2 토큰. 진영으로 severity/importance를 나눈다.
    private static GameLogEntry ConvertCastCancel(string[] tokens, string target, ISkillTargetFactionResolver resolver)
    {
        if (tokens.Length != 2) return null;

        LogFaction faction = resolver?.Resolve(tokens[1]) ?? LogFaction.Unknown;
        switch (faction)
        {
            case LogFaction.Enemy:
                return Event("combat.skill.cast_cancel.enemy", GameLogImportance.Normal,
                    $"적 {target} 영창 취소", Args(("target", target)));
            case LogFaction.Ally:
                return Event("combat.skill.cast_cancel.ally", GameLogImportance.Warning,
                    $"아군 {target} 영창 취소됨", Args(("target", target)));
            default:
                return null; // 진영 불명/unresolved fail-closed.
        }
    }

    private static GameLogEntry Detail(string key, string fallback, IReadOnlyDictionary<string, string> args)
        => new(GameLogChannel.Combat, GameLogSeverity.Detail, GameLogImportance.Normal,
            fallback, titleKey: key, args: args);

    private static GameLogEntry Event(string key, GameLogImportance importance,
        string fallback, IReadOnlyDictionary<string, string> args)
        => new(GameLogChannel.Combat, GameLogSeverity.Event, importance,
            fallback, titleKey: key, args: args);

    private static IReadOnlyDictionary<string, string> Args(params (string Name, string Value)[] pairs)
    {
        var dict = new Dictionary<string, string>();
        foreach ((string name, string value) in pairs) dict[name] = value;
        return dict;
    }

    private static string LeafName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash >= 0 ? path.Substring(slash + 1) : path;
    }
}
