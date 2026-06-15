---
type: system
system: DialogueTool
status: active
updated: 2026-06-15
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
- Portrait가 아닌 Effect 대상과 Effect 순환은 저장을 중단한다.
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

- `state_condition` Dialogue Data 노드는 단일 게임 저장소의
  `Assets/Script/gds/world_state/condition/`에 있는 `ConditionSet`과 `ConditionEvaluator`를 직접 사용한다.
- 따라서 이 노드가 추가된 이후 DialogueTool addon은 World State condition 모듈에 의존한다. 별도 독립
  addon 배포는 현재 목표가 아니며, Dialogue runtime은 여전히 `/root`를 직접 조회하지 않고 주입된 read
  provider만 evaluator에 전달한다([[ADR-009-State-Condition-Dialogue-Consumption]]).

## Related

- [[DialogueTool-User-Guide]]
- [[DialogueTool-Architecture]]
- [[DT-002-Portrait-State]]
- [[DialogueTool-Step-1-to-8]]
