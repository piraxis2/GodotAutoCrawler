---
id: DT-004
type: task
status: done
system: DialogueTool
created: 2026-06-11
updated: 2026-06-11
tags: [task, dialogue-tool, flow, effect, portrait]
---

# Nonblocking Effect Flow

## Goal

Portrait 같은 비대기 연출 명령을 주 Dialogue Flow와 같은 실행 시점에 여러 개 적용한 뒤,
Say 또는 Choice로 자연스럽게 진행할 수 있게 한다.

```text
Start
  effect -> PortraitShow(left)
  effect -> PortraitShow(right)
  flow   -> Say
```

일반 Flow를 병렬화하지 않고, 단일 실행 커서와 wait state 규칙을 유지한다.

## Context

- 현재 `DialogueGraphResource.get_runtime_next_node_id()`는 첫 연결 하나만 반환한다.
- 같은 Flow 출력에 여러 연결을 만들어도 하나만 실행되므로 이미지와 같은 그래프가 작동하지 않는다.
- Say/Choice까지 fan-out하는 일반 병렬 실행은 현재 Player 수명 주기와 맞지 않는다.
- 설계 방향은 [[ADR-005-Nonblocking-Effect-Connections]]을 따른다.

## Scope

- Effect 연결/포트 계약 도입.
- Portrait Show/Hide/Expression을 Effect로 실행.
- Effect 여러 개 실행 후 주 Flow 하나로 진행.
- 기존 Portrait 직렬 Flow와 기존 Dialogue 리소스 호환 유지.
- 저장/재로드, validation, 디버그 하이라이트와 재진입 검증.

## Out of Scope

- Say, Choice, Branch의 일반 병렬 실행.
- 스레드, coroutine 또는 동시 wait state.
- Effect 실행 완료를 기다리는 애니메이션/Wait.
- Sound, Emit Event 등 신규 Effect 노드 자체 구현.
- Portrait transition, Focus와 actor resolver.

## Step 1: Effect 연결 데이터와 런타임 계약

목표:
- 에디터 UI 없이 수작업 runtime snapshot에서 Effect 여러 개와 주 Flow 하나를 결정적으로 실행한다.

작업 범위:
- Effect 연결을 식별하는 저장 계약을 확정한다.
- `DialogueGraphResource`에 주 Flow와 Effect 연결을 각각 조회하는 API를 추가한다.
- DialoguePlayer가 현재 실행 지점의 Effect들을 먼저 실행한 뒤 주 Flow로 이동하게 한다.
- Portrait type만 Effect 실행 대상으로 허용한다.
- 연결 저장 순서를 Effect 실행 순서로 사용하거나 별도 명시 순서를 도입한다.
- Effect 순환과 잘못된 대상에 대한 런타임 방어를 추가한다.

제외 범위:
- GraphNode의 새 포트와 에디터 연결 UX.
- 기존 리소스 자동 변환.
- Portrait Definition/Adapter UI 변경.

완료 조건:
- 수작업 snapshot에서 두 Portrait Effect가 순서대로 발행된 뒤 Say가 실행된다.
- Effect 실행은 `waiting_for`를 만들지 않는다.
- 주 Flow 대상이 없으면 기존 종료 정책을 따른다.
- 잘못된 Effect 대상과 순환에서 크래시하거나 무한 루프가 발생하지 않는다.
- 기존 `PortraitShow -> Say` 직렬 그래프와 Portrait 없는 그래프가 동일하게 실행된다.
- Effect callback 대화 교체 시 stale 요청 source guard가 유지된다.

검증 방법:
- Godot headless editor load.
- runtime snapshot 단위 실행 순서 검증.
- 다중 Portrait, 빈 Effect, 잘못된 대상, 순환, 재진입 검증.
- 기존 DT-002와 Say/Choice 회귀 검증.

## Step 2: 에디터 Effect 포트와 저장/재로드

목표:
- 사용자가 GraphEdit에서 Flow와 Effect 연결을 명확히 구분해 작성할 수 있게 한다.

작업 범위:
- `DialogueNode.port_type`에 Effect 타입과 전용 색상을 추가한다.
- Effect를 발행할 수 있는 Flow 노드의 포트 UX를 확정한다.
- Portrait 노드에 Effect 입력 계약을 적용한다.
- 필요하면 Portrait의 기존 Flow 입력/출력은 하위 호환 모드로 유지한다.
- editor capture와 runtime snapshot에 Effect 연결 종류를 보존한다.
- 로드 후 포트 종류, index와 연결이 동일하게 복원되게 한다.

설계 쟁점:
- 모든 Flow 노드에 Effect 출력 포트를 둘지, Start/Say 등 명시된 노드에만 둘지 결정한다.
- Effect 노드를 단일 명령 leaf로 제한할지, Effect-to-Effect 체인을 허용할지 결정한다.
- 새 포트 추가가 기존 노드의 Flow port index를 바꾸지 않도록 배치한다.

제외 범위:
- 런타임 Effect 종류 추가.
- 기존 리소스의 강제 마이그레이션.

완료 조건:
- Start에서 Portrait Effect 여러 개와 Say 주 Flow를 동시에 연결할 수 있다.
- Flow와 Effect를 잘못 연결하면 에디터가 거부하거나 저장을 중단한다.
- 생성 -> 연결 -> 저장 -> 재로드 후 연결 종류와 순서가 보존된다.
- 기존 `.tres`의 Flow/Data 연결과 포트 index가 보존된다.

검증 방법:
- Godot headless editor load.
- GraphEdit 포트 개수/type 검사.
- 저장/재로드 round-trip.
- 기존 대화 리소스 무편집 재저장 비교.

## Step 3: Validation과 편집 UX

목표:
- 모호하거나 실행 불가능한 fan-out을 저장 전에 명확히 차단한다.

작업 범위:
- 한 실행 지점의 주 Flow 대상은 최대 하나로 제한한다.
- Effect 대상 type whitelist를 검사한다.
- Effect 순환과 주 Flow로의 잘못된 재합류를 검사한다.
- 오류 메시지에 node id, type과 port를 포함한다.
- Effect 포트의 색상, 라벨과 tooltip로 일반 Flow와 구분한다.
- 기존 Flow 포트 여러 연결이 있을 때 조용히 첫 연결만 실행하는 현재 위험을 validation한다.

완료 조건:
- Flow 대상 2개, Effect->Say, Effect 순환 등 잘못된 그래프가 저장되지 않는다.
- 유효한 다중 Portrait Effect + 단일 Say 그래프는 저장된다.
- 기존 정상 그래프는 새 validation을 통과한다.

검증 방법:
- 유효/무효 그래프 validation 행렬.
- 저장 차단과 경고 메시지 확인.
- 포트 시각 구분 수동 확인.

## Step 4: 통합 회귀와 완료 판정

목표:
- Effect 실행 모델을 저장, 런타임, UI와 수명 주기 전체에서 검증한다.

통합 시나리오:

```text
Start
  effects -> PortraitShow(left), PortraitShow(right)
  flow    -> Say
Say
  effects -> PortraitExpression(left), PortraitHide(right)
  flow    -> Choice
```

작업 범위:
- 다중 Portrait 적용 순서와 최종 UI 상태 확인.
- Say 줄 누적 표시와 Effect가 서로 간섭하지 않는지 확인.
- 반복 실행, 종료 callback 재진입과 실행 중 교체.
- 기존 직렬 Portrait 그래프와 Portrait 없는 리소스 회귀.
- 최종 리뷰 문서와 Wiki 갱신.

완료 조건:
- Effect가 모두 적용된 뒤 주 Flow가 정확히 한 번 실행된다.
- Say/Choice가 중복 표시되거나 wait state가 충돌하지 않는다.
- 반복/교체/재진입에서 이전 Effect 요청이나 Portrait 상태가 남지 않는다.
- 기존 리소스가 데이터 손실 없이 실행된다.
- P0/P1 문제가 없고 남은 제한이 문서화된다.

검증 방법:
- Godot headless editor load.
- 에디터 저장/재로드와 통합 런타임 실행.
- DialogueManager 수명 주기 회귀.
- [[STEP_REVIEW_WORKFLOW]]에 따른 최종 리뷰와 재검증.

## Step 1 구현 결과 (2026-06-11)

변경 파일:
- `addons/dialogtool/Resource/dialogue_graph_resource.gd`: Effect 연결 계약과 조회 API.
- `addons/dialogtool/RunTime/dialogue_player.gd`: Effect 실행과 방어 로직.
- `addons/dialogtool/RunTime/tests/dt004_step1_headless_test.{gd,tscn}`: 헤드리스 회귀 테스트.

구현 내용:
- 연결 계약: connection 딕셔너리에 선택적 `kind: "effect"` 필드를 둔다. 없거나 다른 값이면
  기존 Flow/Data 규칙으로 해석한다(ADR-005 호환). 기존 필드와 포트 index는 바꾸지 않는다.
- `DialogueGraphResource`:
  - `get_runtime_next_node_id()`가 Effect 연결(`kind=="effect"`)을 건너뛰어 주 Flow 하나만 반환한다.
    같은 포트에 Effect와 Flow가 함께 있어도 Flow만 따라간다.
  - `get_runtime_effect_node_ids(from, port)` 추가: Effect 대상들을 저장 순서대로 반환한다(=실행 순서).
- `DialoguePlayer`:
  - `_go_to_next_node(port)`와 `select_choice(index)`가 주 Flow 이동 직전에 `_run_effects()`를 호출한다.
  - `_run_effects()`는 Effect들을 저장 순서대로 발행하고 `waiting_for`를 만들지 않는다.
    visited 셋으로 Effect 순환을 차단하고, Portrait 외 타입(`EFFECT_NODE_TYPES` 화이트리스트)·
    누락 노드는 경고 후 건너뛴다. Effect-to-Effect 체인도 따라간다.
  - 기존 `_build_portrait_request()`를 재사용해 동일한 `portrait_state` 요청 형식을 유지한다.
- 비대기 요청은 기존 `ui_request` 경로로 발행되어 DialogueManager의 `source_ui` stale guard를 그대로 받는다(변경 없음).

검증:
- 헤드리스 씬 부팅 테스트 6개 시나리오 ALL PASS:
  A 다중 Portrait(left,right) 후 Say, B 기존 직렬 Portrait->Say, C Effect 없는 그래프,
  D 잘못된 Effect 대상(Say) skip, E Effect 순환 차단(무한 루프 없음), F 반복 실행 일관성.
- Godot 4.6.3(mono) headless editor load 성공, 스크립트 파싱/컴파일 오류 없음.
- 테스트는 autoload 의존(DialogueToolUtil) 때문에 `--script`가 아닌 씬 부팅으로 실행한다.

남은 위험 / 다음 Step:
- 에디터 포트/연결 UX와 `kind` 저장 캡처는 미구현(Step 2). 현재는 수작업 snapshot으로만 Effect를 만든다.
- 저장 validation(주 Flow 2개 차단, Effect 화이트리스트 저장 거부 등)은 Step 3.
- `_build_portrait_request()`의 빈 texture 경고가 Effect 실행 시 source 노드 id를 참조한다(P3, 메시지 한정).
- 실제 DialogueUI 렌더링/클릭 통합 회귀는 Step 4.

## Step 2 구현 결과 (2026-06-11)

변경 파일:
- `addons/dialogtool/Node/dialogue_node.gd`: `port_type`에 `effect` 추가(마지막 값=3, 기존 값 유지) + effect 색상.
- `addons/dialogtool/Editor/Adapter/start_editor_adapter.gd`: Start에 Effect 출력 포트(port 1) 추가.
- `addons/dialogtool/Editor/Adapter/say_editor_adapter.gd`: Say에 Effect 출력 포트(port 1) 추가.
- `addons/dialogtool/Editor/Adapter/portrait_editor_adapter.gd`: Portrait에 Effect 입력 포트(port 1) 추가.
- `addons/dialogtool/Editor/editor.gd`: capture가 출력 포트 타입에서 `kind` 파생, validation 카테고리 검사 + Effect 도달성, **load 시 Effect 연결을 Effect 포트로 정규화**(리뷰 P1).
- `addons/dialogtool/Resource/dialogue_graph_resource.gd`: `get_runtime_effect_node_ids`를 port-agnostic(kind 기준)으로 변경.
- `addons/dialogtool/RunTime/dialogue_player.gd`: `_run_effects`를 노드 단위로 변경(Effect 전용 포트 index 대응).
- `addons/dialogtool/RunTime/tests/dt004_step2_editor_test.{gd,tscn}`: 헤드리스 에디터 왕복 테스트(신규).

설계 결정:
- 포트 타입 카테고리: flow / value(data·boolean) / effect. 같은 카테고리끼리만 연결(Godot 기본 same-type 규칙 + 저장 validation 백스톱). `effect`는 enum 마지막에 추가해 기존 flow/data/boolean index를 보존한다.
- Effect 출력 포트는 **Start와 Say에만** 둔다(통합 시나리오 범위). Choice/Branch/End는 제외.
- Portrait는 Effect **입력** 포트만 갖는 leaf다(Effect 출력 없음 → Effect-to-Effect 체인은 에디터에서 생성 불가). Portrait의 기존 Flow 입력/출력은 직렬 호환용으로 유지.
- 새 Effect 포트는 항상 기존 Flow/Data 포트 **아래 row**에 배치해 기존 flow/data port index가 바뀌지 않게 한다(Start flow=port0/effect=port1, Say flow_out=port0/effect_out=port1, Portrait flow_in=port0/effect_in=port1).
- `kind`는 capture 시 출력 포트 타입에서 파생한다. 저장값과 재캡처값이 항상 일치하고, Effect 포트가 없는 기존 리소스는 `kind`가 붙지 않는다.
- Step 1의 port-shared 가정을 폐기하고 Effect를 `kind`만으로 식별(노드 단위)하도록 런타임을 조정했다. Effect는 노드를 떠나는 시점(`_go_to_next_node`/`select_choice`)에 한 번 실행된다.

검증:
- 헤드리스 에디터 왕복 테스트(`dt004_step2_editor_test.tscn`) **30/30 PASS**:
  - 포트 계약: Start(flow=0,effect=1), Portrait(flow_in=0,effect_in=1,flow_out=0), Say(flow_in=0,flow_out=0,effect_out=1).
  - capture: Effect 연결에 `kind="effect"` 파생, Flow 연결엔 없음. 유효 그래프 validation 통과.
  - 런타임: 캡처 리소스 실행 시 Portrait 2개 발행 후 Say.
  - 저장→재로드→재캡처 후 연결 종류/개수와 Effect 포트 계약 보존.
  - Flow↔Effect 잘못된 연결을 validation이 거부(fatal).
  - Flow 전용(레거시 형태) 그래프는 `kind`가 붙지 않고 통과(무편집 동일성).
- Step 1 런타임 테스트 6/6 회귀 PASS(port-agnostic 변경 후에도).
- Godot 4.6.3(mono) headless editor load 성공.

남은 위험 / 다음 Step:
- 저장 validation의 강화(주 Flow 대상 2개 차단, Effect 화이트리스트 위반 거부, Effect 순환 차단, 오류 메시지에 node/type/port 명시)는 Step 3.
- Effect 포트 tooltip/라벨 등 시각 구분 보강은 Step 3.
- `_build_portrait_request`의 빈 texture 경고가 Effect 실행 시 source 노드 id를 참조(P3, 메시지 한정) — 여전히 잔존.
- `clear_graph`의 deferred queue_free로 재로드 시 노드 이름이 일시적으로 바뀔 수 있음(기존 동작, id 기준 조회로 무해). 데이터/연결 보존에는 영향 없음.

### Step 2 리뷰 대응 (2026-06-11)

- **[P1] 로드 시 Effect 연결 의미 손실 — 수정.**
  `editor.gd.load_resource`가 `kind` 를 무시하고 저장된 포트(예: Step 1 형태 0→0)로 그대로 재연결해
  Effect가 직렬 Flow로 둔갑하고 주 Flow 선택을 방해할 수 있었다. 이제 `kind=="effect"`면 노드의
  Effect 출력/입력 포트(`_find_effect_port`)로 정규화해 연결한다. 매핑할 Effect 포트가 없으면 조용히
  Flow로 바꾸지 않고 `push_error` 후 해당 연결을 건너뛴다. 회귀 테스트(시나리오 D):
  kind=effect·port 0→0 리소스를 로드→재캡처→실행해 effect kind 보존(`D.portrait_effect_preserved`),
  포트 정규화(`D.effect_port_normalized -> 1`), Effect 실행(`["show:left","say"]`) 확인.
- **[P2] 저장·재로드 후 Effect 실행 순서 미검증 — 보강.**
  재캡처된 Effect 대상 ID 순서 비교(`A.reload.effect_order == [1,2]`)와 재캡처 리소스 재실행
  (`A.reload.runtime.order == ["show:left","show:right","say"]`)을 추가했다. left/right 슬롯을 구분해 순서를 증명한다.
- **[P3] Task 상태 불일치 — 수정.** frontmatter `status: proposed -> in-progress`.

검증: 에디터 왕복 테스트 38/38 PASS, Step 1 런타임 6/6 PASS, headless editor load 성공.

## Step 3 구현 결과 (2026-06-11)

변경 파일:
- `addons/dialogtool/Resource/dialogue_graph_resource.gd`: Effect 대상 화이트리스트 `EFFECT_TARGET_TYPES` + `is_effect_target_type()` 단일 정의(에디터·런타임 공유).
- `addons/dialogtool/RunTime/dialogue_player.gd`: 로컬 `EFFECT_NODE_TYPES` 제거, 공유 화이트리스트 사용.
- `addons/dialogtool/Editor/editor.gd`: validation에 주 Flow 단일성·Effect 화이트리스트·Effect 순환 검사 추가, 오류 메시지에 node/type/port 포함, 순환 검사 헬퍼.
- `addons/dialogtool/Editor/Adapter/{start,say,portrait}_editor_adapter.gd`: Effect 포트 라벨에 tooltip 추가.
- `addons/dialogtool/RunTime/tests/dt004_step3_validation_test.{gd,tscn}`: validation 행렬 테스트(신규).

구현 내용(validation 강화, 모두 fatal=저장 중단):
- (A) **주 Flow 단일성**: 한 Flow 출력 포트(`from_id:from_port`)에 Flow 대상이 2개 이상이면 거부.
  "조용히 첫 연결만 실행하는 현재 위험"을 저장 전에 차단한다.
- (B) **Effect 대상 화이트리스트**: Effect 출력 연결의 대상 타입이 Portrait가 아니면 거부.
- (C) **Effect 순환**: Effect 간선의 순환(자기 자신 포함)을 DFS로 검사해 거부.
- 카테고리 불일치(Step 2) 메시지에 out/in 포트 타입과 port index를 포함하도록 보강.
- **Effect→Say 차단**: 에디터 포트 설계상 Effect 출력→Say는 effect↔flow 카테고리 불일치로 (B) 이전에 거부된다.
- **편집 UX**: Effect 포트는 주황색 + "effect" 라벨 + tooltip으로 일반 Flow와 구분.

설계 메모:
- 에디터 포트 설계(Portrait는 Effect 입력 leaf, Start/Say만 Effect 출력)상 Effect→비Portrait와 Effect 순환은
  UI로 만들 수 없다. (B)(C)는 수작업/레거시 리소스 방어용 백스톱이며, 순수 함수 단위 테스트로 검증한다.
- "주 Flow로의 잘못된 재합류"는 (A) 주 Flow 단일성 + (B) 화이트리스트(Effect는 wait 노드로 갈 수 없음)로 커버한다.

검증:
- validation 행렬 테스트(`dt004_step3_validation_test.tscn`) **12/12 PASS**:
  - 단위: 화이트리스트(portrait 허용, say/branch 거부), 순환 검사(순환/자기루프 true, 비순환/빈 false).
  - 유효: 다중 Portrait Effect+단일 Say 통과, 기존 직렬 Portrait→Say 통과.
  - 무효: 주 Flow 대상 2개 거부, Effect→Say 거부.
- 회귀: Step 1 런타임 6/6, Step 2 에디터 왕복 38/38, headless editor load 성공.

남은 위험 / 다음 Step:
- `_build_portrait_request`의 빈 texture 경고가 Effect 실행 시 source 노드 id 참조(P3, 메시지 한정) — 잔존.
- DialogueUI 실제 렌더링/클릭 통합 회귀와 수명주기 검증은 Step 4.

### Step 3 리뷰 대응 (2026-06-11)

- **[P2] 오류 메시지 진단 정보 보강 — 수정.**
  세 validation 오류에 source/target의 node id·type·port를 모두 포함하도록 수정했다. Effect 순환은
  bool 대신 순환 경로를 반환하는 `_find_effect_cycle()`로 바꿔 경로(node id+type)를 메시지에 싣는다.
  실제 출력 예: `주 Flow 대상이 둘 이상입니다 — 출력 node 0(type start) out-port 0 → [node 1(type say)
  in-port 0, node 2(type say) in-port 0]`, `Effect 순환 — 경로 node 1(type portrait_show) → node 2(...) → node 1(...)`.
- **[P2] 화이트리스트·순환의 validation 전체 경로 미테스트 — 보강.**
  테스트 전용 Effect 포트를 단 노드로 `_validate_runtime_snapshot()`이 실제 false를 반환함을 검증:
  I3(Say에 Effect 입력을 달아 비-Portrait 대상 → (B)만 fatal), I4(Portrait 2개에 Effect 출력을 달아
  순환 → (C)만 fatal). 단위 헬퍼 테스트(U1/U2)와 별개로 통합 경로를 증명한다.
- **[P3] ADR-005 status `proposed -> accepted`** — Effect 계약이 Step 1~3에 적용된 현재 사실 반영.

검증(리뷰 대응 후): validation 행렬 16/16 PASS(U1·U2·V1·V2·I1·I2·I3·I4), Step 1 6/6, Step 2 38/38,
headless editor load 성공.

### Step 3 2차 리뷰 대응 (2026-06-11)

- **[P2] Effect 순환 메시지에 port 누락 — 수정.**
  `_find_effect_cycle()`를 node id 배열이 아니라 간선 배열(`{from_id, from_port, to_id, to_port}`)을
  반환하도록 바꿔, 순환 경로의 각 간선에 out-port/in-port를 표시한다. 모든 validation 오류가 공유하는
  포맷 헬퍼 `_format_port_edge()`로 통일했다. 실제 출력 예:
  `Effect 순환 — 경로: node 1(type portrait_show) out-port 1 → node 2(...) in-port 1 → node 2(...) out-port 1 → node 1(...) in-port 1`.
- **[P3] 오류 메시지 내용 미검증 — 보강.**
  메시지 포맷 헬퍼 `_format_port_edge()`를 분리하고, 단위 테스트 U3에서 출력 문자열이
  node id·type·out-port·in-port를 모두 포함하는지 검사한다. 순환 헬퍼 U2도 간선의 from/to id와 port를 확인한다.

검증(2차 대응 후): validation 행렬 **24/24 PASS**(U1·U2·U3·V1·V2·I1·I2·I3·I4), Step 1 6/6, Step 2 38/38,
headless editor load 성공.

## Step 4 구현 결과 (2026-06-11)

변경 파일(테스트만, 제품 코드 변경 없음):
- `addons/dialogtool/RunTime/tests/dt004_step4_pipeline_test.{gd,tscn}`: 두 Effect 지점 에디터 저장/재로드 + 런타임.
- `addons/dialogtool/RunTime/tests/dt004_step4_integration_test.{gd,tscn}`: DialogueUI/DialogueManager 통합·수명주기·회귀.

구현 내용:
- Step 4는 신규 제품 로직이 아니라 Step 1~3 산출물을 통합 시나리오로 검증하는 단계다. Effect는
  기존 `ui_request`(`portrait_state`) 경로로 발행되므로 DialogueUI의 Portrait 소유/렌더가 그대로 적용된다.
- 통합 시나리오(`Start[effect: show left/right; flow: Say] → Say[effect: expression left/hide right; flow: Choice] → Choice → End`)를
  실제 `DialogueUI`로 실행해 검증.

검증(헤드리스, 5개 테스트 파일):
- **Pipeline 13/13 PASS**: 두 Effect 지점 그래프를 에디터에서 구성→저장→재로드→재캡처해 4개 Effect 연결의
  종류·순서(`[1,2,4,5]`)가 보존되고, 재로드 리소스의 런타임 순서가
  `show:left → show:right → say → (advance) expression:left → hide:right`로 결정적임을 확인.
- **Integration 30/30 PASS**:
  - 통합(B): Start 두 Effect 적용 후 Say 표시(Portrait 미간섭) → advance 시 Say 두 Effect(왼쪽 expression 갱신,
    오른쪽 hide) 적용 후 Choice. `ui_request` 로그가 `show:left, show:right, say, expression:left, hide:right, choice`로
    각 1회·중복 없음. 종료 시 Portrait 정리.
  - 회귀(D1/D2): 기존 직렬 `Portrait→Say`, Portrait 없는 그래프가 동일하게 동작.
  - 수명주기(C1 반복/C2 교체/C3 재진입): 반복 실행 시 이전 Portrait 잔존 없음, DialogueManager로 실행 중 교체 시
    새 UI에 이전 Portrait 상태 미잔류(source guard 유지), 종료 callback 재진입에서 깨끗하게 새 대화 시작.
- Godot 4.6.3(mono) headless editor load 성공.

남은 위험:
- `_build_portrait_request`의 빈 texture 경고가 Effect 실행 시 source 노드 id 참조(P3, 메시지 한정) — 잔존.
- Say 줄 누적(타이핑/페이지) UI는 player.advance 직접 호출로 우회 검증했다(클릭 기반 paging 경로는 DT-003 범위).
- 실제 화면 픽셀 렌더링은 headless라 검증 대상이 아니다(논리 상태 `_portrait_state`/visible로 검증).

### Step 4 리뷰 대응 (2026-06-11)

- **[P2] Effect 콜백 중 교체 stale guard 미회귀 — 보강.**
  C2는 Effect 발행 후 외부에서 교체해 DT-002 P1 조건(콜백 한가운데 교체)을 재현하지 못했다.
  C4 추가: `DialogueManager.ui_request`의 `portrait_state` 콜백 안에서 즉시 `play()`로 교체하고,
  OLD의 후속 요청(`show:right`, stale `say:hello`)이 source guard로 차단되고 NEW(`say:NEW`)만 전달됨을 검증.
  (guard 제거 시 `C4.old_later_effect_blocked`/`C4.old_stale_say_blocked`가 실패한다.)
- **[P2] 실제 기존 .tres 호환 미검증 — 보강.**
  D3 추가: 실제 `res://pride_and_prejudice.tres`(start/say×8/end)를 직접 로드·실행. 노드 구성과 legacy
  Say 필드(`speaker="엘리자베스"`, `portrait` 필드)가 보존되고, UI에 speaker가 렌더되며, 종료까지 진행하고
  Effect(`portrait_state`) 요청이 발생하지 않음(legacy 무 Effect)을 확인.
- **[P3] Dialogue_UI.tscn 변경과 보고 불일치 — 해명.**
  `Dialogue_UI.tscn`의 `unique_id`/`anchors_preset` 변경은 **세션 시작 시점의 기존 작업본**(시작 git status에 이미 `M`)으로
  DT-004 변경이 아니다. Godot 에디터의 재직렬화 churn으로 보이며 기능에 영향 없다(테스트는 노드를 이름으로 조회).
  사용자 변경 비복원 원칙에 따라 손대지 않았다. DT-004 제품 코드 변경은 Step 1~3 파일에 한정된다.

검증(Step 4 대응 후): Integration **42/42 PASS**(B·D1·D2·D3·C1·C2·C3·C4), Pipeline 13/13, Step 1~3 회귀 유지,
headless editor load 성공.

## Completion Criteria

- Portrait Effect 여러 개와 주 Flow 하나를 같은 실행 지점에 연결할 수 있다.
- 실행 순서는 결정적이며 Effect 전체 처리 후 주 Flow가 한 번 실행된다.
- 일반 Flow의 다중 대기 노드 병렬 실행은 허용하지 않는다.
- 기존 직렬 Portrait와 기존 Dialogue 리소스가 호환된다.
- 저장/재로드 후 연결 의미와 포트 순서가 보존된다.
- 잘못된 그래프는 저장 전에 차단된다.

## Related

- [[ADR-005-Nonblocking-Effect-Connections]]
- [[DT-002-Portrait-State]]
- [[DialogueTool]]
- [[Runtime-Data-Flow]]

