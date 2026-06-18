---
type: system
system: DialogueTool
status: active
updated: 2026-06-16
---

# DialogueTool

## Responsibilities

- GraphEdit 기반 대화 작성
- `.tres` 저장 및 재로드
- Flow/Data 포트 validation
- 런타임 대화 실행
- 텍스트와 선택지 UI
- 현재 실행 노드 디버그 하이라이트
- 게임 코드용 전역 실행 API

## Supported Runtime Nodes

| Type | Role |
| --- | --- |
| `start` | 시작 후 첫 Flow로 자동 이동 |
| `say` | 텍스트 UI 요청 후 advance 대기 |
| `choice` | 선택지 UI 요청 후 index 대기 |
| `branch` | Data 입력을 bool로 평가해 분기 |
| `portrait_show` | Portrait 표시 요청(비대기) 발행 후 다음 Flow로 진행 |
| `portrait_hide` | Portrait 숨김 요청(비대기) 발행 후 다음 Flow로 진행 |
| `portrait_expression` | Portrait 표정 변경 요청(비대기) 발행 후 다음 Flow로 진행 |
| `state_set` | 명시적 mutation provider로 World State key를 절대값 변경(비대기 Effect) |
| `state_add` | 명시적 mutation provider로 INT/FLOAT World State key에 delta 더하기(비대기 Effect) |
| `end` | 대화 종료 |
| `variable` | 정적 값 또는 random range 제공 |
| `expression` | 연결된 Data 입력으로 Expression 평가 |
| `state_condition` | 주입된 read provider로 `ConditionSet`을 평가해 boolean Data 제공(DT-008) |

## Portrait Nodes (DT-002 MVP)

- `portrait_show`/`portrait_hide`/`portrait_expression`은 Say와 독립된 비대기 Flow 명령이다.
- 에디터에서 생성·편집·저장/재로드된다. Definition은 공통 베이스 `PortraitDef`(Abstract/)를
  상속하고 에디터 UI는 공통 `portrait_editor_adapter`에 위임한다.
- DialoguePlayer가 비대기 `portrait_state` 요청으로 정규화해 발행하고, DialogueUI가
  left/center/right 슬롯에서 상태를 소유하며 `texture_path` Texture를 렌더링한다.
- 상태는 Say/Choice 전환에도 유지되고 대화 종료/교체 시 정리된다.
- Portrait Show/Expression의 `texture_path` 필드는 문자열 직접 입력과 FileSystem의
  Texture2D 리소스 드롭을 지원한다. 유효한 단일 Texture만 `res://` 경로로 반영한다.
- MVP 이후: transition 애니메이션, Portrait Focus/dim, actor/expression resolver. [[DT-002-Portrait-State]]

## State Condition (DT-008)

- `state_condition` Data 노드(`WorldStateConditionDef`)는 `ConditionSet` 하나를 보유하고 boolean Data를
  낸다. 에디터 노드는 boolean output 포트 + ConditionSet `.tres` 드롭 picker를 갖는다.
- (DT-012 완료, [[DT-012-Condition-Authoring-UX-Review]]) 에디터 노드는 picker path 아래에 provider-free
  `ConditionSummary.summarize()` 결과를
  `SummaryLabel`로 표시한다. leaf `key 기호 literal` / group `ALL·ANY·NOT(...)` 자동 요약, structural
  valid일 때만 `description` 우선, null/invalid는 `No ConditionSet`/`Invalid: <code>`로 빨강 계열 구분,
  full 요약·path·오류는 tooltip. `ConditionSummary`는 `ConditionValidator.validate`를 먼저 호출하는
  validate-first helper로 provider를 읽지 않으며 표시 문자열은 ADR-008 trace 안정 계약과 분리된다.
  요약 갱신은 drop/clear/load·재로드 시점(live external edit 구독 없음).
- 런타임 평가는 `DialoguePlayer._eval_data`가 주입된 **원본** read provider를 `ConditionEvaluator.evaluate`에
  그대로 전달해 수행한다(facade 재포장 금지). 내부 Data 평가는 `{value, errored}`로 전파되어, 조건/구조
  오류(invalid report·중첩 Expression 입력 오류·순환·parse/execute 실패)는 단순 false와 구분된 errored로
  지배한다(ADR-008 error-dominance). 소비자(Branch/Choice)는 errored면 무조건 false/숨김으로 fail-closed한다.
- **Branch**: Data 입력(port 0)을 평가해 true→port 0 / false·errored→port 1.
- **조건부 Choice**: 항목 i의 Data 입력(port i+1)을 Choice 진입 시 1회 평가해 true 항목만 표시한다. Data
  입력이 없는 항목은 항상 표시(레거시 호환). DialoguePlayer가 `visible_index → 원래 항목 index(=flow 출력
  port i)` mapping을 대기 동안 보관하고, `select_choice(visible_index)`가 범위 검증 후 원래 Flow로 진행한다.
  대기 중 외부 상태 변화는 현재 목록을 바꾸지 않고 재진입에서만 재평가한다. 모든 항목이 숨겨지면 명시적 종료.
- `condition_evaluated(condition_node_id, consumer_node_id, report)` signal을 평가 1회당 발행한다(consumer는
  입력 포트를 직접 소유한 Branch/Choice/Expression id). report는 evaluator의 detached deep copy다.
- 자세한 결정은 [[ADR-009-State-Condition-Dialogue-Consumption]], 검증은 [[DT-008-Choice-Integration-Review]].

## State Mutation Effects (DT-009)

- `state_set`/`state_add`는 비대기 Effect 노드다. Flow를 기다리지 않고 현재 실행 지점의 Effect 순서대로
  mutation provider를 호출한 뒤 주 Flow가 진행된다.
- 실행 경로는 `DialogueManager.play(resource, read_provider, mutation_provider)` ->
  `DialogueUI.play(...)` -> `DialoguePlayer.set_mutation_state_provider(...)`다. read provider는 mutation 권한으로
  자동 승격되지 않는다.
- `state_set`은 provider의 `apply_state_batch([{key, value}])`를 사용하고, `state_add`는
  `add_state(key, delta)`를 사용한다. Set은 bool/int/float/String/StringName, Add는 INT/FLOAT strict 타입만
  지원한다.
- `state_mutation_evaluated(effect_node_id, report)` signal을 Effect 평가 1회당 발행한다. report는 commit 후
  deep copy이며 `{applied, changed, operation, key, old_value, new_value, error}` 형태다.
- Choice는 항목별 Effect 포트와 전용 공통 Effect 포트를 갖는다. 선택 시 해당 항목의 Effect와 공통 Effect만
  실행한다. 공통 연결은 `choice_index`가 없고, 손상된 `choice_index` 타입은 fail-closed로 건너뛴다.
- provider 누락/계약 위반/Store 오류는 구조화 report로 남기고 Flow는 계속한다. 값은 Store 계약에 따라 불변이다.
  검증과 판정은 [[DT-009-State-Mutation-Review]].

## Debug WorldState Preview (DT-010)

- DialogueTool 에디터 Play/debug 실행의 `DialoguePlayer._ready()` debug 분기는 저장된 dialogue resource를
  self-start하기 전에 `DialogueDebugPreviewProvider`로 preview 전용 `WorldStateStore`를 구성해 read/mutation
  provider 양쪽에 주입한다([[DT-010-Dialogue-Debug-WorldState-Preview-Review]]).
- provider source는 addon 동봉 `examples/world_state_schema_example.tres`다. `/root/WorldState` autoload를
  bare 식별자로 참조하지 않아 fresh-project parse-safety를 유지하고, 실제 game/save state를 오염시키지 않는다.
- Play마다 별도 Godot 프로세스가 뜨므로 preview state는 매 실행 example default에서 시작하고, 한 run 안의
  mutation은 누적되어 이후 Branch/Condition이 읽는다.
- 고정 example schema에 없는 게임 schema key는 preview에서 `state_missing`/`unknown_key`로 fail-closed한다.
  게임 schema 경로를 debug 설정으로 주입하는 옵션 C는 후속 범위다.

## Say Line Paging

- Say 텍스트의 줄바꿈은 같은 Say 노드 안의 페이지 경계로 처리한다.
- 첫 줄부터 타이핑하고 이전 줄은 같은 대화창에 유지한다. 클릭 시 현재 줄 완성 -> 다음 줄 누적 표시
  -> 마지막 줄 이후 다음 Flow 순서로 진행한다.
- 줄바꿈이 없는 기존 Say는 기존과 동일하게 현재 문장 완성 후 다음 클릭에 Flow를 진행한다.

## Editor-Only or Incomplete Nodes

- Description: 그래프 메모와 시각 구분
- Test: 에디터 실험용
- Autoload: 정의와 UI는 있으나 런타임 정책 미완성
- SceneFunction: 정의와 UI는 있으나 실행 정책 미완성

## Validation

- Start 노드는 정확히 하나여야 한다.
- 연결 endpoint가 존재해야 한다.
- Flow, Value(Data/Boolean), Effect 포트 카테고리가 다른 연결은 저장을 중단한다.
- 한 Flow 출력 포트에 주 Flow 대상이 둘 이상이면 저장을 중단한다.
- Portrait/State mutation이 아닌 Effect 대상과 Effect 순환은 저장을 중단한다.
- 치명적 연결 오류에는 node id, runtime type, output/input port를 표시한다.
- 도달 불가능한 Flow와 Start의 연결 누락은 warning이다.

## Extension Rules

새 노드는 최소한 다음을 정의한다.

1. runtime type
2. 직렬화 가능한 runtime params
3. 에디터 Adapter 또는 고정 `.tscn` UI
4. 포트 계약
5. DialoguePlayer 실행 또는 data 평가 규칙
6. 저장/재로드 왕복 검증

### Project Integration Dependency

- `state_condition` Dialogue Data 노드는 현재 `addons/dialogtool/world_state/condition/`에 있는
  `ConditionSet`과 `ConditionEvaluator`를 직접 사용한다(`class_name` 참조 — 경로 독립).
- `state_set`/`state_add` Dialogue Effect 노드는 `WorldStateStore` mutation provider 계약
  (`apply_state_batch`, `add_state`)을 소비한다.
- 따라서 이 노드들이 추가된 이후 DialogueTool addon은 World State condition/mutation 모듈에 의존한다.
  Dialogue runtime은 여전히 `/root`를 직접 조회하지 않고 주입된 read/mutation provider만 사용한다
  ([[ADR-009-State-Condition-Dialogue-Consumption]], [[ADR-010-State-Mutation-Dialogue-Effects]]).
- **DT-011 패키징(완료, [[ADR-011-DialogueWorldState-Addon-Packaging]],
  [[DT-011-DialogueWorldState-Addon-Packaging-Review]]):** 이 의존을 깨지지 않게
  하려고 World State 폐쇄집합(코어/condition/store/runtime)을 `addons/dialogtool/world_state/`
  하위모듈로 **이동 완료**했다(후보 B). 이제 `addons/dialogtool/`만 복사하면 World State 코드가 함께 따라온다.
  DialogueTool과 World State를 **별도** 독립 addon으로 쪼개 배포하는 것은 목표가 아니다(둘을 한 폴더로 함께
  배포). 게임 schema/save는 호스트 소유이며, addon은 `examples/world_state_schema_example.tres`,
  `examples/affinity_ge_10.tres`, sample dialogue와 설치/마이그레이션 README를 포함한다.

## Related

- [[DialogueTool-User-Guide]]
- [[DialogueTool-Architecture]]
- [[DT-002-Portrait-State]]
- [[DT-009-State-Mutation-Review]]
- [[DialogueTool-Step-1-to-8]]
