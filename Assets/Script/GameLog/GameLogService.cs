using Godot;

namespace AutoCrawler.Assets.Script.GameLog;

// 전역 GameLog 수명 소유자(ADR-020). autoload Node로 `GameLogModel`을 생성/보관하고 `IGameLogSink`를 노출한다.
// 전투(`battle_field.tscn`)·아웃게임·대화·시스템이 서로 다른 씬에서도 같은 sink로 entry를 보낼 수 있다.
//
// 이 서비스는 sink/보관만 책임진다 — `GameLogEntry` 생성 정책은 adapter/producer가 소유한다(ADR-020).
// `LogWindowController`는 이 서비스의 모델을 구독해 표시하며, 서비스가 없으면 자체 로컬 모델로 fail-closed한다.
public partial class GameLogService : Node, IGameLogSink
{
    public GameLogModel Model { get; } = new();

    public void Append(GameLogEntry entry) => Model.Append(entry);
}
