---
id: ADR-020
type: decision
status: accepted
date: 2026-07-09
tags: [adr, gamelog, autoload, service, ui]
---

# ADR-020: GameLog Service Lifetime

## Context

GL-001 Step 1~2는 `GameLogModel`을 순수 C# 모델로 만들고, `LogWindowController`가 자기 로컬 모델을 소유하는 형태로 검증했다. 이 구조는 UI 단독 검증에는 좋지만 Step 3부터 전투/스킬 adapter가 로그를 발행해야 하므로 수명 경계가 바뀐다.

전투는 현재 `Assets/Scenes/Map/battle_field.tscn`이 `run/main_scene`이며, Workspace/LogWindow는 `Assets/Scenes/workspace.tscn`의 한 창이다. 따라서 LogWindow가 모델을 단독 소유하면 전투 계층이 로그 sink에 안정적으로 접근할 수 없다.

## Decision

`GameLogService` autoload가 `GameLogModel`을 소유한다.

- `GameLogService`는 Godot autoload Node다.
- 서비스는 `GameLogModel`을 생성하고 보관한다.
- 서비스는 `IGameLogSink` 진입점을 제공한다.
- `LogWindowController`는 더 이상 자체 모델을 권위로 만들지 않고, `GameLogService`의 모델을 구독해 표시한다.
- 테스트에서는 service를 통하지 않고 순수 `GameLogModel`을 직접 만들 수 있어야 한다.
- SaveGame `SaveSection` 통합은 GL-001 후속이다. 이 ADR은 수명과 접근 경로만 결정한다.

## Consequences

장점:

- 전투, 아웃게임, 대화, 시스템 알림이 같은 GameLog sink로 entry를 보낼 수 있다.
- LogWindow는 GameLog의 소유자가 아니라 표시자가 된다.
- Step 3 adapter가 전투 계층에서 로그를 발행할 수 있다.
- 후속 SaveGame 통합 시 최근 200개 entry 저장 대상이 한 곳에 모인다.

비용/주의:

- Step 3에서 `project.godot` autoload 등록이 필요하다.
- Step 2 테스트는 controller가 service 없이도 테스트 가능한 주입 경로를 유지해야 한다.
- Autoload 이름은 기존 `WindowManager`/`WorkspaceWindowManager`와 충돌하지 않게 `GameLogService`로 둔다.
- 전역 서비스는 편리하지만, entry 생성 정책까지 소유하지 않는다. adapter/producer가 `GameLogEntry`를 만들고 service는 sink/보관 책임만 가진다.

## Implementation Notes

Step 3에서 수행한다.

- `Assets/Script/GameLog/GameLogService.cs` 추가.
- `project.godot` autoload에 `GameLogService` 등록.
- `LogWindowController`는 기본적으로 autoload service의 모델을 사용한다.
- headless/unit 테스트는 순수 모델 또는 test service 주입을 통해 autoload 의존을 피한다.
- service unavailable 경로는 fail-closed로 둔다. 로그 누락은 허용하지만 gameplay를 깨지 않는다.

## Related

- [[GL-001-GameLog-Foundation]]
- [[GL-001-GameLog-Foundation-Review]]
- [[GL-001-GameLog-Foundation-Step2-Review]]
- [[Workspace-Window-System]]
- [[Skill-System]]
