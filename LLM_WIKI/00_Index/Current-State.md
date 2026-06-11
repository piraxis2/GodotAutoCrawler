---
type: status
project: AutoCrawler
updated: 2026-06-11
---

# Current State

## Project

- Godot 4.6.3 Mono, C#, .NET 8 기반 자동 턴제 택티컬 RPG 프로토타입이다.
- 전투 핵심은 Article, Status, BehaviorTree, TurnAction, TurnHelper로 구성된다.
- 실제 전투 실험 중심 씬은 `Assets/Scenes/Map/battle_field.tscn`이다.
- `main.tscn`은 UI와 윈도우 실험 성격이 강하다.

## DialogueTool

- Step 1~8 구현 및 리뷰가 완료됐다.
- 에디터 그래프는 `nodes/connections`, 런타임은 `runtime_nodes/runtime_connections`를 사용한다.
- 런타임 실행기는 Start, Say, Choice, Branch, End, Variable, Expression을 처리한다.
- 에디터 UI 구현은 Editor Adapter로 이동했다.
- `current_node_changed`와 원격 디버거 메시지를 통해 실행 노드 하이라이트가 가능하다.
- `DialogueManager.play(resource)`로 게임 코드에서 대화를 실행할 수 있다.
- 연속 대화와 이전 UI의 지연 종료 signal에 대한 재진입 방어가 적용됐다.
- Say 텍스트에 줄바꿈이 있으면 이전 줄을 같은 대화창에 유지하면서 한 줄씩 누적 공개하고,
  마지막 줄 이후에만 다음 Flow로 진행한다([[DT-003-Say-Line-Paging]]).

## Known Gaps

- Portrait는 `Say` 요청의 문자열 필드와 별개로, 독립적인 비대기 UI 상태 명령으로 분리됐다.
  DT-002 Portrait State MVP가 완료됐다([[DT-002-Portrait-Review]]).
  - DialoguePlayer가 `portrait_show/hide/expression`을 비대기 `portrait_state` 요청으로 발행한다(Step 1).
  - 세 노드를 에디터에서 생성·편집하고 Definition/runtime snapshot에 저장·재로드한다
    (공통 `PortraitDef` + `portrait_editor_adapter`, Step 2).
  - DialogueUI가 left/center/right 슬롯에서 Portrait 상태를 소유하고 `texture_path` Texture를
    렌더링한다. Say/Choice 전환에도 유지되고 종료/교체 시 정리된다(Step 3).
  - 반복 실행/교체/재진입 수명주기와 기존 리소스 호환을 통합 검증했다(Step 4).
  - MVP 이후 후속: transition 애니메이션, Portrait Focus/dim, actor database/resolver,
    speaker 기반 자동 선택. [[Open-Tasks]] 참고.
- Autoload와 SceneFunction의 안전한 런타임 평가/부작용 정책은 미완성이다.
- DialogueTool 통합 회귀 테스트 리소스와 자동 테스트가 아직 고정되지 않았다.
- Say 줄 누적 표시는 정적 검토만 완료됐으며 Godot 실제 클릭/headless 회귀 검증이 남아 있다.
- Definition이 Adapter 조회를 중계하는 점진적 호환 계층이 남아 있다.
- 전투 시스템에는 게임오버 후속 처리와 일부 null 방어 과제가 남아 있다.

## Verification Baseline

- Godot 4.6.3 headless editor load가 성공해야 한다.
- Dialogue 리소스는 편집 -> 저장 -> 재로드 후 값과 포트 순서를 보존해야 한다.
- `Start -> Say -> Choice -> Branch -> End` 흐름이 종료까지 실행돼야 한다.

## Related

- [[Open-Tasks]]
- [[DialogueTool-Architecture]]
- [[DialogueTool-Step-1-to-8]]
