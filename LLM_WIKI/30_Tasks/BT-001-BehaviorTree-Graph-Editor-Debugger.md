---
id: BT-001
type: task
status: in-progress
system: BehaviorTree
created: 2026-06-19
updated: 2026-06-20
tags: [task, behavior-tree, editor, debugger]
---

# BehaviorTree Graph Editor and Debugger

## Goal

현재 캐릭터 씬 안에 Godot `Node` 계층으로 작성하는 BehaviorTree를 GraphEdit 기반 도구에서 시각화, 설정, 저장, 디버깅할 수 있게 만든다.

첫 목표는 런타임 BehaviorTree 구조를 바꾸지 않고, 기존 `BehaviorTree`/`BehaviorTree_Node` 자식 구조를 source of truth로 유지하는 것이다. GraphEdit는 그 구조를 읽고 편집하는 editor adapter 역할을 맡는다.

## Context

- 현재 런타임은 `addons/behaviortree/BehaviorTree.cs`와 `addons/behaviortree/node/*` C# Node 계층이다.
- `CharacterArticle`은 자신의 `"BehaviorTree"` child를 찾아 `BehaviorTree.Behave(delta, this)`로 실행한다.
- BehaviorTree는 플레이 세션의 `TurnHelper._PhysicsProcess`에서 tick된다. 에디터와 플레이 세션은 별도 OS 프로세스이므로 EditorPlugin은 런타임 노드의 in-process C# event를 직접 구독할 수 없다.
- 기존 캐릭터 씬(`PrincessKnight.tscn`, `TempArticle*.tscn`)은 BehaviorTree를 씬의 child Node 구조로 직렬화한다.
- 현재 씬에서 실제로 쓰는 BT node는 Selector + Decorator + Action 계열이다. Sequence/RatingSelector/RatingDecorator를 쓰는 `.tscn`은 없다. 단, 코드에는 해당 타입이 있으므로 fixture로 검증한다.
- `BehaviorTreeEditor.cs`와 `DebuggerWindow`/`DebuggerTree`가 있으나, Inspector 버튼은 주석 처리되어 있고 현재 디버거는 Tree 위젯 기반 구조 표시 수준이다.
- `BehaviorTree_Node.OnLogChanged`는 `#if TOOLS` C# event이며 payload가 `{Status, Time}`뿐이다. 노드 식별자가 없고 Running node는 매 physics frame emit된다.
- DialogueTool은 별도 프로세스 실행 노드 하이라이트를 `EditorDebuggerPlugin._capture` + 런타임 `EngineDebugger.send_message`로 처리한다. BT debug도 같은 remote debug 패턴을 따른다.

## Code Evidence

2026-06-20 설계 rework에서 아래 파일을 직접 대조했다.

- `BehaviorTree.Root`는 `GetChild(0) as BehaviorTree_Node`다. 0번 child가 BT node가 아니면 root는 `null`이고, `Behave()`는 `Root?.Behave(...) ?? BtStatus.Failure`로 빈 트리를 Failure 처리한다.
- `BehaviorTree.SetTree()`는 root에서 raw `GetChildren()`를 따라 `Tree` reference를 주입한다. 이 경로는 runtime cache가 아니라 실제 Node child 구조를 순회한다.
- `BehaviorTree_Node.Behave()`는 `_status`가 Success/Failure일 때 `OnInit()`과 elapsed reset을 수행하고, `OnBehave()` 결과를 `_status`에 저장한다. `OnLogChanged` payload는 `Util.BehaviorLog { Status, Time }`뿐이다.
- `BehaviorTree_Composite.OnTreeChanged()`는 raw `GetChildren()`에서 BT child를 `_children` cache로 복사한다.
- `BehaviorTree_Decorator.OnTreeChanged()`는 첫 BT child만 `Child`로 보존하고 이후 child는 `RemoveChild(child)`로 떼어낸다. `QueueFree()`가 없으므로 orphan 위험이 있다.
- `BehaviorTree_Action.OnTreeChanged()`는 모든 child를 `RemoveChild(child)`로 떼어낸다. `QueueFree()`가 없으므로 orphan 위험이 있다.
- `BehaviorTree_RatingSelector.TakeDecorator()`는 `TreeChildren.OfType<BehaviorTree_RatingDecorator>()`만 평가한다. non-rating child는 runtime에서 무시될 수 있으므로 첫 authoring policy는 warn이 맞다.
- `TurnHelper._PhysicsProcess()`가 현재 turn article의 `TurnPlay(delta * Speed)`를 호출한다. `CharacterArticle.TurnPlay()`는 `CurrentTurnAction == null`일 때만 `BehaviorTree.Behave(delta, this)`를 호출한다.
- `BehaviorInspectorPlugin._ParseBegin()`의 editor button 생성 코드는 현재 주석 처리되어 있다.
- `DebuggerTree`는 editor process 안의 `BehaviorTree.OnUpdateTree`를 구독해 Tree 위젯 구조를 다시 그린다. 별도 play process의 runtime tick 상태를 받을 수 있는 remote debugger가 아니다.
- DialogueTool 선례는 `DialogueDebuggerPlugin._has_capture("dialogue")`, `_capture("dialogue:current_node", data, session_id)`, runtime `EngineDebugger.send_message("dialogue:current_node", [...])` 조합이다.

## Design Direction

### 선택안 A: Node Tree Source of Truth

이번 Task는 **A안**을 채택한다.

- 실제 저장 데이터는 기존 Godot scene Node tree다.
- GraphEdit의 노드와 연결은 `BehaviorTree_Node` parent-child 관계의 editor-only projection이다.
- GraphEdit에서 연결을 수정하면 실제 Node parent/child와 sibling order를 갱신한다.
- 기존 캐릭터 씬, C# node class, export property, runtime 실행 경로와 호환성을 유지한다.

### 보류안 B: Resource Graph Source of Truth

별도 `.tres` graph resource를 만들고 런타임 Node tree를 생성하는 방식은 이번 범위에서 제외한다. 공유 AI asset, prefab-style 재사용, 버전 migration, runtime instantiate가 필요해질 때 별도 Task/ADR로 검토한다.

## Scope

- BehaviorTree용 editor dock 또는 window 추가.
- 선택한 `BehaviorTree`를 GraphEdit로 읽기 전용 표시.
- 주석 처리된 `BehaviorInspectorPlugin` 진입 버튼을 복구하거나 동등한 editor 진입점을 제공.
- GraphEdit에서 노드 추가, 연결 변경, 삭제, 순서 변경을 실제 Node tree에 반영.
- 기존 export property 편집은 새 property editor를 만들기보다 Godot Inspector 선택 연동을 우선한다.
- 런타임 프로세스에서 `EngineDebugger.send_message`로 전달한 BehaviorTree tick batch를 에디터 `EditorDebuggerPlugin`이 받아 GraphEdit에 debug highlight로 표시.
- 최소한의 validation: root 누락, Decorator 자식 수, Action 자식 금지, cycle/부모 중복 방지, 저장 전 손상 구조 감지.
- 기존 in-editor `DebuggerTree`와 새 remote Graph debugger의 책임을 분리하거나 새 도구로 통합한다.

## Out of Scope

- BehaviorTree 실행 semantics 변경.
- 읽기 전용 debug emit 추가는 런타임 semantics 변경으로 보지 않는다. 단, debug payload 생성은 게임 결과와 상태 변경에 영향을 주지 않아야 한다.
- BehaviorTree를 별도 Resource asset으로 분리.
- 캐릭터 AI 설계 자체 변경.
- 전투 밸런스, TurnAction 동작 변경.
- 복잡한 Blackboard 편집기.
- visual scripting 수준의 custom condition/action node generator.
- production 전투 UI 변경.

## Data Model

### Runtime Model

Source of truth는 기존 Node tree다.

```text
BehaviorTree
  BehaviorTree_Selector_Root
    BehaviorTree_FindOpponent
      BehaviorTree_MultipleMove
    BehaviorTree_TurnAction(Attack)
```

### Editor Projection

GraphEdit projection은 `TreeChildren` runtime cache가 아니라 **raw `GetChildren()` 필터링**을 source로 삼는다.

이유:
- `BehaviorTree_Decorator.TreeChildren`는 첫 번째 child만 노출한다.
- `BehaviorTree_Action.TreeChildren`는 항상 빈 목록이다.
- 잘못된 구조(Decorator 2자식, Action 자식)를 표시하고 차단하려면 raw child를 보아야 한다.

GraphEdit는 다음 mapping을 가진다.

- node identity: `BehaviorTree_Node.GetPath()`를 기본 식별자로 사용한다. rename/reparent 중에는 instance id 또는 editor-local mapping으로 보조한다.
- Graph node title: `node.GetType().Name` + `node.Name`.
- Graph connection: parent output port -> child input port.
- sibling order: 같은 parent의 raw child order를 실행 순서로 표시한다. authoring 시 GraphEdit 연결 순서 또는 명시 move command를 Node child order로 반영한다.
- Graph position: `BehaviorTree_Node`에 `set_meta("bt_graph_position", Vector2)` / `get_meta("bt_graph_position")`로 저장한다.

Graph position 저장 결정:

- 채택: node metadata(`set_meta/get_meta`).
- 장점: `.tscn` 직렬화 유지, Inspector/export 부채 없음, runtime base class export 오염 없음.
- 비용: metadata key 계약이 숨겨져 있으므로 상수화와 migration 문서화가 필요하다.
- 보류: base class exported `Vector2 GraphPosition`은 Inspector 노출과 런타임 base class 오염 때문에 사용하지 않는다.
- 보류: 매번 auto layout은 초기 viewer fallback으로는 가능하지만 authoring 위치 보존을 만족하지 못한다.

근거:
- Graph position은 editor projection 상태이며 BehaviorTree 실행에 필요하지 않다.
- base class export로 만들면 모든 runtime node inspector에 authoring 좌표가 노출되고, 런타임 모델에 editor-only 필드가 섞인다.
- node metadata는 Godot scene 직렬화 대상이므로 A안(Node tree source of truth)을 유지하면서도 기존 export API를 오염시키지 않는다.

## Node Rules

- root는 현재 코드와 동일하게 `BehaviorTree.GetChild(0) as BehaviorTree_Node`다. 0번 child가 `BehaviorTree_Node`가 아니면 root는 `null`이다.
- Composite(`BehaviorTree_Composite`)는 0개 이상의 BehaviorTree child를 허용하고 sibling order를 실행 순서로 사용한다.
- Decorator(`BehaviorTree_Decorator`)는 정확히 1개의 BehaviorTree child만 유효한 구조로 본다.
- Action(`BehaviorTree_Action`)은 child를 갖지 않는다.
- RatingSelector의 RatingDecorator-only 규칙은 현재 scene 사용례가 없으므로 hard-block하지 않고 warning으로 시작한다. fixture로 실제 동작을 확인한 뒤 별도 Step에서 강화할 수 있다.
- 연결은 tree여야 하며 cycle을 만들 수 없다.
- 하나의 child는 하나의 parent만 가져야 한다.

### Validation Severity

| Case | Severity | Policy |
| --- | --- | --- |
| Decorator 2개 이상 child 연결 시도 | block | connect 시점에 거부해 데이터 보존 |
| Action child 연결 시도 | block | connect 시점에 거부 |
| cycle 생성 | block | connect 시점에 거부 |
| 같은 child를 여러 parent에 연결 | block | reparent 의도가 명시되지 않으면 거부 |
| root가 없음 | warn | `Behave()`는 Failure로 fail-closed, editor에 empty/invalid 표시 |
| 0번 child가 BT node가 아님 | warn | 코드 기준 root null, editor에 root warning 표시 |
| RatingSelector child가 RatingDecorator가 아님 | warn | 초기 authoring에서는 경고, runtime 동작은 현 코드 우선 |
| unknown BT subclass | warn | 표시와 Inspector 선택은 허용, palette 생성은 보류 |

### Self-prune Policy

현재 `BehaviorTree_Decorator.OnTreeChanged()`는 두 번째 이후 child를 `RemoveChild`하고, `BehaviorTree_Action.OnTreeChanged()`는 모든 child를 조용히 제거한다. 둘 다 `[Tool]` 경로에서 에디터에서도 실행될 수 있고, `QueueFree` 없이 orphan을 만들 수 있다.

BT-001 기본 정책:

- authoring GraphEdit는 잘못된 연결을 **connect 시점에 사전 거부**한다.
- 기존 손상 씬을 projection할 때는 raw `GetChildren()`로 invalid child를 표시하고 block/warn reason을 보여준다.
- 런타임/기존 node의 auto-prune 동작은 이번 Task에서 바꾸지 않는다. 단, Step 2 이후 editor 경로에서는 auto-prune에 의존하지 않도록 한다.
- auto-prune 결과로 orphan이 발생하는지 fixture로 확인하고, 필요하면 별도 cleanup Task로 분리한다.

근거:
- 현재 self-prune은 `[Tool]` node의 `OnTreeChanged()`에서 실행되므로 editor authoring 중에도 데이터가 조용히 떨어질 수 있다.
- GraphEdit connect 시점 사전 거부는 잘못된 child를 실제 Node tree에 넣지 않으므로 데이터 보존에 유리하다.
- 기존 runtime auto-prune을 같은 Step에서 바꾸면 런타임 semantics와 기존 씬 호환성 검증 범위가 커진다.

## Debug Contract

Debug는 editor process와 play process가 분리되어 있으므로 remote channel을 사용한다. 상세 결정은 [[ADR-016-BehaviorTree-Remote-Debug-Channel]]에 둔다.

- 런타임 프로세스: BehaviorTree tick 중 읽기 전용 payload를 만들고 `EngineDebugger.send_message`로 전송한다.
- 에디터 프로세스: `EditorDebuggerPlugin._capture`에서 BT debug message를 수신하고 열린 GraphEdit debugger에 전달한다.
- `OnLogChanged`는 EditorPlugin이 직접 구독하지 않는다. 필요하면 런타임 측에서 send_message batch를 만드는 in-process hook으로만 사용한다.
- payload는 최소한 tree 식별자, owner/Article 식별자, tick/frame id, node 식별자(NodePath 또는 안정 id), status, elapsed time을 포함한다.
- Running node는 매 physics frame emit될 수 있으므로 tick 단위 batch 또는 throttle 정책을 둔다.
- stale/not ticked 구분을 위해 tick/frame timestamp를 payload에 포함하고, GraphEdit는 새 tick을 받으면 이전 tick highlight를 clear하거나 timestamp가 다른 node를 stale로 낮춘다.
- tree/node가 freed 되었거나 scene이 바뀐 뒤 늦게 도착한 message는 식별자 불일치로 무시한다.
- 현재 턴 캐릭터를 자동 선택하려면 TurnHelper/전투 runtime 쪽도 remote channel로 현재 actor 정보를 보내야 한다. editor process에서 runtime Node를 직접 참조하지 않는다.

## Steps

## Step 0: Design Review

목표:
- A안(Node tree source of truth)의 API, 저장 정책, validation 범위, debug remote channel, self-prune 대응 정책을 확정한다.

작업 범위:
- 이 Task 문서와 관련 BehaviorTree 코드 검토.
- [[ADR-016-BehaviorTree-Remote-Debug-Channel]] 검토.
- 제품 코드와 `.tscn`/`.tres` 리소스는 수정하지 않는다.

제외 범위:
- GraphEdit 구현.
- runtime behavior 변경.

완료 조건:
- 설계 리뷰 판정이 `Approved` 또는 `Approved after design fixes`다.
- Graph position 저장 방식이 metadata로 확정되거나 대안이 명시된다.
- debug 채널 결정이 승인된다.
- Decorator/Action self-prune 대응 정책이 승인된다.
- Step 1~5의 완료 조건이 구현 가능한 수준으로 조정된다.

검증 방법:
- 코드 경로 대조: `BehaviorTree.cs`, `BehaviorTree_Node.cs`, Composite/Decorator/Action/Rating node, existing debugger, DialogueTool remote debug 선례.
- 대표 씬의 현재 Node tree 구조 확인.

## Step 1: Read-only Graph Viewer (완료)

목표:
- 선택한 `BehaviorTree`를 GraphEdit에 raw child 구조 그대로 표시한다.

작업 범위:
- editor plugin에서 BehaviorTree graph window/dock 진입점 추가.
- 주석 처리된 `BehaviorInspectorPlugin` 버튼 복구 또는 동등한 메뉴/dock 진입점 제공.
- raw `GetChildren()` 필터링 기반 Node tree -> GraphNode/connection projection.
- 타입별 기본 스타일과 라벨 표시.
- root null/0번 child non-BT/Decorator extra child/Action child 같은 invalid 구조를 표시한다.
- 구조가 바뀌면 `OnUpdateTree` 또는 editor refresh로 GraphEdit 재구성.

제외 범위:
- GraphEdit에서 구조 편집.
- node property editing.
- debug status highlight.

완료 조건:
- 기존 캐릭터 씬의 BehaviorTree가 GraphEdit에서 parent-child 구조와 sibling order를 보존해 표시된다.
- Root가 없거나 BehaviorTree가 비어 있어도 editor가 crash 없이 empty/warn state를 표시한다.
- Decorator/Composite/Action 타입 구분이 시각적으로 가능하다.
- raw child projection으로 Decorator 2자식/Action 자식 fixture를 표시하고 invalid reason을 낸다.

검증 방법:
- C# editor tool headless 테스트 인프라가 없으면 projection 로직을 순수 함수로 분리해 가능한 범위에서 테스트하거나 수동 검증으로 명시한다.
- Godot editor import/load.
- 대표 scene 수동 확인 절차 문서화.

## Step 2: Basic Authoring (완료)

목표:
- GraphEdit에서 BehaviorTree 구조를 추가/연결/삭제하고 실제 Node tree에 반영한다.

작업 범위:
- node type palette 또는 context menu.
- Graph connection create/delete -> `AddChild`/`RemoveChild`/reparent 반영.
- sibling order 보존.
- Decorator child 1개 제한과 Action child 금지를 connect 시점에 사전 거부한다.
- cycle/다중 parent를 block한다.
- RatingSelector child type은 warn으로 표시한다.
- scene 저장 후 재로드 구조 보존.

제외 범위:
- 고급 auto layout.
- Blackboard editor.
- 별도 Resource export/import.
- 런타임 auto-prune 동작 수정.

완료 조건:
- 새 Selector/Sequence/Decorator/Action 노드를 만들고 연결한 뒤 scene save/reload에서 구조가 보존된다.
- Decorator 2자식/Action 자식 연결 시도가 Node tree를 변경하기 전에 거부된다.
- 잘못된 연결 reason이 사용자에게 표시된다.
- 기존 캐릭터 씬을 열고 저장해도 기존 BT 구조가 사라지지 않는다.
- PrincessKnight fixture save round-trip에서 BT 구조가 보존된다.

검증 방법:
- editor API 기반 round-trip test 또는 수동 검증.
- test-only fixture scene으로 save/reload 검증.
- Godot headless editor load.
- 빈 트리 `Behave()`가 Failure를 반환하는 기존 동작 회귀 확인.

## Step 3: Inspector and Settings UX (완료)

목표:
- GraphEdit에서 선택한 BT node의 설정을 기존 Godot Inspector로 편집할 수 있게 한다.

작업 범위:
- GraphNode selection -> 실제 `BehaviorTree_Node` selection 연동.
- node rename과 Graph title refresh.
- rename 후 NodePath mapping 갱신.
- 기존 exported property 편집 경로 보존.
- `bt_graph_position` metadata 저장/로드 적용.

제외 범위:
- 모든 node type별 custom inspector.
- custom TurnAction picker.

완료 조건:
- Graph에서 노드를 선택하면 Inspector가 해당 Node를 보여준다.
- export 값을 수정하고 저장/재로드하면 값과 graph 위치 metadata가 보존된다.
- 노드 이름 변경이 Graph title과 runtime Node name에 반영되고 debug/editor mapping이 갱신된다.

검증 방법:
- editor round-trip test 또는 수동 검증.
- rename 후 projection/debug 식별자 재매핑 검증.
- 수동 editor 확인 절차 문서화.

> Step 4a/4b/5는 2026-06-21 설계 리뷰 Rework로 재작성됨. 상세 프로토콜은 [[ADR-016-BehaviorTree-Remote-Debug-Channel]].

## Step 4a: Remote Debug Channel + Gating + Discovery (완료)

목표:
- play→editor tick 채널, editor→play start/stop 게이팅, register/unregister discovery를 만든다. UI 없음.

구현 완료 내역:
- **Registry 및 Dispatcher**: `BehaviorTree.cs`에 static `_registry` 구성 및 `OnMessageCapture` 디스패처 1회 등록 가드 설계 완료.
- **Announcement**: `_Ready()` 시 `register` 송신 및 registry 추가, `_ExitTree()` 시 `unregister` 송신 및 registry 제거 구현 완료.
- **Gating**: `start` / `stop` 메시지 수신 시 `DebugEnabled` 토글 및 `SendStructure()` 1회 호출 구현 완료.
- **Zero Allocation 게이트 단락**: `BehaviorTree_Node.Behave()` 게이트가 꺼져있을 때 어떠한 메모리 할당 없이 조기 반환(단락) 구현 완료.
- **에디터 세션**: `BtDebuggerPlugin.cs`에서 `_SetupSession`을 통해 세션 목록을 확보하고, `_Capture`에서 4가지 프로토콜 메시지 수신 후 `BehaviorTreeEditor.HandleDebugMessage`로 `CallDeferred` 위임 완료.
- **제어 헬퍼**: `BehaviorTreeEditor.cs`에서 `StartDebugging` / `StopDebugging` 메시지 송출 헬퍼 작성 완료.

검증 완료:
- 헤드리스 C# 단위 테스트(`BehaviorTreeValidationTest.cs` 내 K~N 사례) 구현 및 ALL PASS 확인.
  - registry 라이프사이클 격리 검증 PASS.
  - `DebugEnabled == false`일 때의 Behave 단락 검증 PASS.
  - 가짜 start/stop 메시지 디스패치 라우팅 및 게이트 토글 검증 PASS.
  - structure / tick payload 빌드 및 타입/필드 일치 라운드트립 검증 PASS.

## Step 4b: Payload-built Debug Graph + Highlight (완료)

목표:
- `structure`로 디버그 GraphEdit를 자체 구성하고 `tick`으로 status를 표시한다(read-only).

구현 완료 내역:
- **원격 전용 디버그 그래프 뷰 구축**: `BehaviorTreeDebugGraphView.cs`를 구현하여 에디터 씬 의존성 없이 structure 페이로드에서 노드 및 연결을 동적으로 복원하고, BFS 기반 auto-layout 배치를 제공함. 노드 타입별 기본 색상은 최초 1회만 계산 및 캐싱함.
- **실시간 틱 하이라이트 및 Stale Clear**: 틱 수신 시 `Success`/`Failure`/`Running` 상태 색상을 변경하고 경과 시간을 표출하며, 틱 갱신 시작 전에 미보고 노드의 기본 색상을 원복(`ResetHighlights()`)함으로써 하이라이트 잔상 누적을 완벽히 차단함.
- **탭 라이프사이클 및 세션 정지**: `DebuggerWindow.cs`를 `tree_path` 기반 탭 라우팅으로 전환하고 탭 클로즈 시 `StopDebugging` 메시지를 자동 송출하여 누수 및 오버헤드를 제어함. unregister 수신 시 탭을 회색조 `SetStaleState()`로 전환해 상태 관찰을 지원함.
- **수동 스모크 테스트 트리거**: 윈도우 상단에 register된 캐릭터를 선택하고 수동으로 start/stop 시킬 수 있는 임시 Discovery OptionButton UI 패널을 탑재하여 스모크 테스트 연동을 완수함.

검증 완료:
- 헤드리스 C# 단위 테스트(`BehaviorTreeValidationTest.cs` 내 O~Q 케이스) ALL PASS.
  - 가짜 structure 페이로드 기반 노드 개수/키/연결/배치 구성 검증 PASS.
  - 가짜 tick 페이로드 기반 상태별 하이라이트 및 미보고 노드 기본색 원복 검증 PASS.
  - structure 미수신 틱 무시 검증 PASS.
- 실제 F5 플레이 스모크 테스트를 통해 IPC 양방향 메시지(register -> start -> structure -> tick -> stop) 및 실시간 색상 깜빡임, 사망 시 Stale 회색 잠금이 안정적으로 동작함을 확인 완료.

2026-06-20 P1 수정:
- 리뷰에서 `BehaviorTreeEditor.HandleDebugMessage`가 4a stub 상태로 `behavior_tree:tick`만 창에 넘기고
  `register`/`unregister`/`structure`를 버리는 결함이 확인되어 수정했다.
- `ShowDebuggerWindow(BehaviorTree)`의 창 생성 배선을 `EnsureDebuggerWindow()`로 추출하고, 원격 메시지 수신 시
  창이 없으면 자동 생성한 뒤 최초 `register`에서 표시하도록 했다.
- `HandleDebugMessage`는 이제 `DebuggerWindow.HandleDebugMessage(message, payload)`로 4종 메시지를 그대로 위임한다.
- 회귀 테스트 `BehaviorTreeValidationTest` R 케이스를 추가해 editor -> window 포워딩 결과를
  `register` discovery, `structure` 원격 탭 생성, `tick` 하이라이트, `unregister` stale/removal로 검증한다.
- 검증: `dotnet build AutoCrawler.sln -c Debug` 경고/오류 0, `bt_validation_test.tscn` A~R ALL PASS(exit 0).
- 실제 F5 스모크는 에디터에서 F5 후 플레이 프로세스 생성 및 `register` 수신에 따른 `🌵Behavior Tree Editor`
  창 자동 표시까지 확인했다. `Start Debugging` 이후 원격 탭/색상/stale 시각 확인은 현재 자동 GUI 관찰 한계로
  미확인([[../../walkthrough|walkthrough]]).

## Step 5: Battle Debug Integration

목표:
- battle에서 선택 캐릭터의 BehaviorTree를 discovery 목록으로 열고 디버깅한다.

작업 범위:
- register 목록 기반 디버그 대상 **수동 선택** UI.
- 탭 라우팅 키 = `tree_path`. 다중 인스턴스 격리.
- freed(unregister/사망) 시 탭을 **stale 표시로 정지**(자동 닫지 않음, 사용자가 닫음).
- 기존 in-editor `DebuggerTree(OnUpdateTree)`는 legacy 구조 보기로 한정.

제외 범위:
- 전투 UI redesign.
- AI behavior tuning.
- current-turn 자동 추적(follow-up).

완료 조건:
- `battle_field.tscn`에서 살아있는 캐릭터의 tree를 목록에서 열어 status를 본다.
- 사망/삭제 시 stale 정지, crash 없음.
- 반복 턴/다중 탭에서 라우팅·cleanup 안전.

검증 방법:
- battle scene smoke test.
- 캐릭터 삭제/전투 종료 lifecycle test.
- 다중 캐릭터 탭 라우팅 수동/자동 검증.
- 수동 editor debug 절차 문서화.

구현 결과(2026-06-20, 리뷰 대기):
- **Discovery UI 정식화**: `DebuggerWindow`의 TEMP naming을 제거하고 정식 target selection bar로 승격했다.
  라벨은 `BehaviorTree:`로 정리했고, 버튼은 `Start`/`Stop`으로 단순화했다. 목록 항목은
  `index. articleName — tree_path` 형태로 표시해 같은 article 이름의 다중 인스턴스를 구분한다.
- **세션 재수집 보강**: `BehaviorTreeEditor.StartDebugging/StopDebugging`이 송신 직전
  `BtDebuggerPlugin.RegisterAvailableSessions()`로 현재 `EditorDebuggerSession` 목록을 재수집한다.
  `_Capture`에서도 수신 `sessionId`의 세션을 등록해 setup timing에 덜 민감하게 했다.
- **Runtime dispatcher 호환성**: Godot runtime capture가 namespace를 제거해 `start`/`stop`만 넘기는 경우도
  처리하도록 `BehaviorTree.OnMessageCapture`가 `behavior_tree:start|stop`과 `start|stop`을 모두 허용한다.
  message contract는 기존 full message를 유지하되 실제 Godot callback 변형을 fail-closed로 흡수한다.
- **다중 라우팅 및 탭 close 보강**: `DebuggerWindow.CloseTab(int)`가 해당 탭의 `tree_path`에만 stop 요청을 보내고
  탭을 제거한다. 테스트 관찰용 C# event(`DebugStartRequested`/`DebugStopRequested`)를 추가하되 실제 editor 송신
  경로는 유지했다. stale title 중복 추가도 방지한다.
- **레거시 책임 결정**: `DebuggerTree`는 삭제하지 않고 로컬 에디터 scene Node 구조 보기 전용으로 유지한다.
  원격 payload structure/tick 디버깅은 `BehaviorTreeDebugGraphView`/remote tab이 담당한다.

검증 결과:
- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- `bt_validation_test.tscn`: A~T ALL PASS(exit 0).
  - S: 다중 `tree_path` tick payload가 각 원격 탭에만 반영됨.
  - T: 탭 close가 해당 `tree_path` 하나에만 stop 요청을 냄.
- 실제 F5 battle smoke([[../../walkthrough|walkthrough]]):
  - `battle_field.tscn` F5 후 `behavior_tree:register`로 디버그 창 자동 표시 확인.
  - discovery 목록에 `Character — /root/BattleField/Articles/Ally/Character/BehaviorTree` 표시 확인.
  - `Start` 후 remote tab 생성 및 payload graph 표시 확인.
  - `Stop` 후 탭이 닫히지 않고 `[STALE]` 상태로 정지 확인.
  - 현재 `battle_field.tscn` smoke에서는 discovery에 live BehaviorTree가 1개만 올라와 둘째 캐릭터 Start 및
    수동 multi-tab 시각 확인은 불가했다. 색상/elapsed tick 변화와 natural death unregister stale도 육안 확인하지 못했고,
    자동 테스트(P/R/S/T)로 deterministic payload 경로를 보강했다.

## Completion Criteria

- 기존 Node tree 기반 BehaviorTree runtime이 유지된다.
- GraphEdit로 기존 BT를 읽고, 편집하고, 저장/재로드할 수 있다.
- GraphEdit에서 별도 플레이 프로세스의 실행 상태를 remote debug channel로 볼 수 있다.
- 대표 캐릭터 씬과 battle scene에서 crash 없이 동작한다.
- root 누락, 0번 non-BT child, decorator child 누락/초과, action child 연결, cycle, stale debug message가 fail-closed 처리된다.
- 관련 시스템 문서와 Open Tasks가 최신 상태다.

## Changes

- 예정:
  - `addons/behaviortree/BehaviorTreeEditor.cs`
  - `addons/behaviortree/BehaviorInspectorPlugin.cs`
  - `addons/behaviortree/debugger/*`
  - 신규 `addons/behaviortree/graph_editor/*`
  - 신규/수정 `EditorDebuggerPlugin` 구현 파일
  - 필요 시 `addons/behaviortree/node/BehaviorTree_Node.cs` debug emit 보조
  - 필요 시 `TurnHelper` 현재 actor debug emit 보조
  - test-only fixture scenes/scripts
  - `LLM_WIKI/20_Systems/BehaviorTree-System.md`
  - `LLM_WIKI/50_Reviews/BT-001-BehaviorTree-Graph-Editor-Debugger-Review.md`

## Verification

- Step별 검증에서 확정한다.
- 기본 baseline:
  - Godot 4.6.3 headless editor load.
  - BehaviorTree fixture scene save/reload round-trip.
  - 기존 representative character scene load.
  - runtime `BehaviorTree.Behave()` smoke test.
  - 빈 트리 `Behave()` = Failure 회귀.
  - Decorator 2자식/Action 자식 auto-prune 결과 확인.
  - remote debug message shape와 stale/freed handling.
  - 반복 턴 highlight clear.
  - 다중 캐릭터 탭 routing.
  - rename 후 mapping 갱신.
  - Sequence/Rating fixture scene 신규 작성 후 projection/validation 확인.
  - `PrincessKnight.tscn` save round-trip 구조 보존.
- C# editor tool 자동 테스트 인프라가 부족하면 projection/validation을 순수 함수로 분리해 테스트하고, EditorDebuggerPlugin/GraphEdit 시각 확인은 수동 검증으로 명시한다.

## Decisions

- A안(Node tree source of truth)을 채택한다.
- 별도 Resource graph는 이번 Task 범위에서 제외한다.
- 기존 export property 편집은 Godot Inspector 연동을 우선하고, node별 custom settings UI는 후속으로 둔다.
- Graph position은 node metadata(`bt_graph_position`)로 저장한다.
- debug highlight는 `EditorDebuggerPlugin` + `EngineDebugger.send_message` remote channel을 사용한다([[ADR-016-BehaviorTree-Remote-Debug-Channel]]).
- authoring에서는 Decorator/Action self-prune에 의존하지 않고 잘못된 연결을 사전 거부한다.
- validation severity는 block/warn 2단계로 시작한다. 데이터 손실 또는 tree invariant 파괴 가능성이 있는 연결은 block, 현재 runtime이 fail-closed 또는 무시할 수 있는 구조는 warn으로 둔다.
- (2026-06-21 확정) debug 채널은 양방향 + per-tree gating(default OFF) + register/unregister discovery + payload-built debug graph로 한다([[ADR-016-BehaviorTree-Remote-Debug-Channel]]).
- (2026-06-21 확정) message namespace = `behavior_tree`. node identity = tree 상대 `node_path`, 탭 라우팅/registry 키 = 절대 `tree_path`.
- (2026-06-21 확정) freed 시 탭은 stale 표시로 정지(자동 닫지 않음). 디버그 대상은 수동 선택 우선.
- (2026-06-21 확정) 디버그 그래프는 payload 기반 read-only render path로, authoring(Steps 1~3) scene 기반 그래프와 분리한다.

## Open Decisions

- (해소됨 → Decisions로 이동) debug API/namespace, node identity, graph source, freed/선택 정책은 모두 확정.
- 검증 캐시 무효화 트리거를 `RebuildGraph`/`OnUpdateTree`에 묶는 구체 구현은 Step 4b에서 확정한다.
- 기존 `DebuggerTree`를 삭제할지 legacy 구조 보기로 유지할지는 Step 5에서 결정한다. 기본값은 legacy 구조 보기로 한정.

## Follow-ups

- BehaviorTree Resource asset화 및 공유 AI preset.
- Blackboard inspector/editor.
- execution timeline recorder.
- node별 custom authoring UI.
- AI behavior library/palette 정리.
- graph auto layout 고도화.
- 런타임 auto-prune orphan cleanup 정책 재검토.
- TurnHelper current-actor 자동 추적(현재 턴 캐릭터 디버그 탭 자동 전환).

## Related

- [[BehaviorTree-System]]
- [[Turn-System]]
- [[Project-Overview]]
- [[ADR-016-BehaviorTree-Remote-Debug-Channel]]
- [[STEP_REVIEW_WORKFLOW]]
