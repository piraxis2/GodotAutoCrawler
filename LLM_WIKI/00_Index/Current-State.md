---
type: status
project: AutoCrawler
updated: 2026-06-14
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

## World State

- 타입 안전 World State 기반 DT-005 Step 1~6이 완료됐다([[DT-005-WorldState-Review]], 판정: 완료).
- `Assets/Script/gds/world_state/`: `StateDefinition`/`StateSchema`(선언·validation·lookup),
  `WorldStateStore`(read/write/reset/`value_changed`, SAVE/SESSION lifetime + `reset_lifetime`,
  JSON snapshot export/import replace-load, atomic `apply_batch`, read/mutation provider facade).
- `DialoguePlayer`는 read 상태 provider를 주입받는다(`DialogueManager`→`DialogueUI`→`DialoguePlayer`).
  `/root`/PlayerData/save를 직접 조회하지 않고 주입 provider로만 상태를 읽는다.
- 허용 타입은 bool/int/float/String/StringName, snapshot INT는 JSON-safe `±(2^53-1)`로 제한.
- 헤드리스 테스트 `Assets/Script/gds/world_state/tests/dt005_step1~6_*`로 검증(ALL PASS).
- 실제 Schema 작성과 Store API 사용법은 [[World-State-User-Guide]]에 정리돼 있다.
- 런타임 통합 DT-006 Step 0~5 완료([[DT-006-WorldState-Runtime-Review]], 판정: 완료).
  - `WorldState` autoload(`/root/WorldState`): 유효 6-key bootstrap Schema(`world_state_schema.tres`)로
    부팅 시 ready.
  - `WorldStateRuntime` autoload(`/root/WorldStateRuntime`): new game/load lifecycle coordinator.
    `start_new_game()`/`restore_game()`(=`restore_world_state()`)는 transactional(envelope pre-validation,
    실패 시 기존 상태 보존), `capture_world_state()`는 SAVE-only. SESSION은 새 게임/load에서만 default.
  - 결정: [[ADR-007-WorldState-Runtime-Lifecycle]]. autoload 이름은 class_name 충돌 회피로 `WorldState`.
- 미구현(후속): 실제 SaveGame file/slot 시스템(DT-006 adapter 소비), State Read/Set Dialogue 노드,
  DT-007 Step 4 완료 판정, full int64.
- DT-007 Step 1~4 구현·검증 완료(in-progress, 최종 완료 판정 대기 — [[DT-007-Condition-Review]]).
  Step 1~3 코드 리뷰 완료.
  - `Assets/Script/gds/world_state/condition/`: `ConditionClause`(@abstract base),
    `StateCondition`(leaf), `ConditionGroup`(ALL/ANY/NOT, recursive `Array[ConditionClause]`),
    `ConditionSet`(top-level asset), `ConditionValidator`(구조 검증), `ConditionEvaluator`(pure-read 평가).
  - Step 1 validator는 iterative DFS로 null/unknown/empty/NOT arity/cycle/alias/depth(64)/node(4096)/key/
    operator/expected/ordered 타입을 검사하고 `{valid, errors[{code,path,key,message}], error_codes,
    node_count}` deep copy를 반환한다. provider read 없음. spike로 `@abstract` 인식 + 재귀 `.tres` 왕복 확인.
    추가 코드 `logic_invalid` 코드 리뷰 비준됨. 판정: Step 1 완료.
  - Step 2 evaluator는 2단계 평가다: validator(read 0)를 먼저 통과해야 주입 provider의 `has_state`/
    `read_state`만으로 트리를 재귀 평가한다. strict typeof 비교(암시적 변환 없음), evaluation-local key
    cache(miss 포함 1회 read), non-short-circuit 전체 trace, fail-closed errored 전파(NOT/ANY가 errored
    child를 pass로 안 바꿈), `{passed, valid, errors, trace, read_count}` deep copy. mutation/autoload 미접근.
  - Step 3은 실제 `WorldStateStore`를 read provider로 주입하는 통합 검증(제품 코드 변경 없음). Step 4는
    end-to-end 완료 검증: 대표 RPG `.tres`를 재로드해 in-memory set과 **동일한 report**(passed/valid/
    errors/trace/read_count 문자열 표현 일치)를 실제 Store에서 내고, load lifecycle(restore_world_state로
    SESSION reset 직접 단언)·성능 sanity(node 4096, 같은 key read 1)·fail-closed Store 불변을 확인했다.
  - 검증: `condition/tests` `dt007_step1`(24)/`step2`(23)/`step3`(11)/`step4`(e2e)/`spike` ALL PASS.
    전체 회귀 DT-004(5)+DT-005(6)+DT-006(5) ALL PASS, editor `--import` 0 오류 — 합계 21 headless.
  - 후속 State Condition Dialogue node Task 입력 계약은 [[DT-007-Condition-Review]]에 문서화. P0/P1 없음
    (구현자 자가평가), 최종 완료 판정 리뷰 대기.

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
- DialogueTool 헤드리스 자동 테스트가 고정됐다(`dt004_step1~4`+integration). World State는
  `dt005_step1~6`로 통합 매트릭스까지 검증된다. 별도의 에디터-저장 기반 통합 회귀 .tres 샘플은 아직 없다.
- Say 줄 누적 표시는 정적 검토만 완료됐으며 Godot 실제 클릭/headless 회귀 검증이 남아 있다.
- Portrait와 주 Flow를 같은 실행 지점에 연결하는 비대기 Effect 모델이 완료됐다
  ([[DT-004-Nonblocking-Effect-Flow]], [[ADR-005-Nonblocking-Effect-Connections]], [[DT-004-Effect-Flow-Review]]).
  한 Flow 출력의 다중 주 Flow 대상은 저장 validation으로 차단된다(런타임은 여전히 첫 주 Flow만 실행).
  - Step 1(런타임 계약) 완료: connection의 `kind: "effect"`로 Effect 연결을 식별한다.
    `get_runtime_next_node_id`는 Effect를 건너뛰어 주 Flow만 반환하고, `get_runtime_effect_node_ids`가
    Effect 대상을 저장 순서대로 반환한다. DialoguePlayer가 노드를 떠나는 시점에 Effect들을 비대기로
    발행하며, 순환·잘못된 대상·누락을 경고 후 건너뛴다. Portrait 타입만 Effect 대상으로 허용한다.
  - Step 2(에디터 포트/저장·재로드) 완료: `port_type`에 `effect`(전용 색상) 추가.
    Start/Say에 Effect 출력 포트(port 1), Portrait에 Effect 입력 포트(port 1)를 두되 기존 Flow/Data
    port index는 보존한다. capture가 출력 포트 타입에서 `kind`를 파생해 저장/재로드/재캡처에 보존한다.
    load 시 `kind=="effect"` 연결은 노드의 Effect 포트로 정규화한다(레거시 0→0 리소스 호환).
    런타임 Effect 식별은 port-agnostic(`kind` 기준)으로 조정했다.
  - Step 3(validation·편집 UX) 완료: 저장 validation이 (A) 한 Flow 포트의 주 Flow 대상 2개 이상,
    (B) Portrait 아닌 Effect 대상, (C) Effect 순환을 거부하고, 오류 메시지에 node/type/port를 포함한다.
    Effect→Say는 effect↔flow 카테고리 불일치로 거부된다. Effect 포트는 주황색+라벨+tooltip로 구분한다.
    Effect 대상 화이트리스트는 `DialogueGraphResource.EFFECT_TARGET_TYPES` 단일 정의로 런타임과 공유한다.
    헤드리스 테스트로 런타임(`dt004_step1_*`), 에디터 왕복(`dt004_step2_*`), validation 행렬(`dt004_step3_*`)을 검증.
  - Step 4(통합 회귀·완료 판정) 완료: 두 Effect 지점 시나리오를 DialogueUI/DialogueManager로 실행해
    Effect와 Say/Choice 미간섭·중복 없음, 저장/재로드 왕복, 반복·교체·재진입 수명주기, 직렬/무 Portrait 회귀를
    헤드리스 5개 테스트(`dt004_step1~4_*`)로 검증했다. P0/P1 없음([[DT-004-Effect-Flow-Review]]).
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
