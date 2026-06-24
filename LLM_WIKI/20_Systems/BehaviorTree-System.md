---
type: system
system: BehaviorTree
status: active
updated: 2026-06-20
---

# BehaviorTree System

## Agent Brief

- 주요 위치: `addons/behaviortree`, `Assets/Script/AutoCrawlerBehaviorTree`
- 책임: 자동 전투 유닛의 조건 평가와 행동 선택
- 상태: Failure, Success, Running
- 주의: 노드 상태 초기화, owner 타입, Tree/Blackboard 연결

## Main Nodes

- Selector: 성공 또는 실행 중인 첫 자식 반환
- Sequence: 실패 또는 실행 중인 첫 자식 반환
- RatingSelector: RatingDecorator 점수가 가장 높은 후보 실행
- Action: 이동 또는 TurnAction 선택
- Decorator: 상대 탐색, 복수 상대 조건 등

## Integration

`CharacterArticle`이 자신의 `BehaviorTree`를 실행한다. 행동 노드는 `BattleFieldScene`과 `BattleFieldTileMapLayer`를 통해 대상과 이동 경로를 찾는다.

## Debugging & Gating (Step 4a)

- **원격 디버그 채널**: `EngineDebugger` 양방향 IPC를 활용하며 `behavior_tree` 네임스페이스를 사용합니다.
- **Discovery (Announce)**: `BehaviorTree` 인스턴스가 `_Ready()` 시 `behavior_tree:register`, `_ExitTree()` 시 `behavior_tree:unregister`를 전송합니다.
- **Gating**: 에디터 측의 `start` 요청 전까지는 `DebugEnabled = false`로 유지되며, `BehaviorTree_Node.Behave()` 게이트에 의해 디버깅 연산 및 딕셔너리/상대 NodePath 문자열 할당이 완전히 단락(Zero allocation)됩니다.
- **Structure & Tick**: `start` 수신 시 `behavior_tree:structure` 페이로드가 1회 전송되고, 매 physics tick마다 최적화된 `behavior_tree:tick` 틱 리포트가 전송됩니다.

## Remote Debug Window Routing (Step 4b P1 fix)

- `BtDebuggerPlugin._Capture`가 받은 `behavior_tree:register|unregister|structure|tick` 메시지는
  `BehaviorTreeEditor.HandleDebugMessage(message, payload)`를 거쳐 `DebuggerWindow.HandleDebugMessage(message, payload)`로 그대로 위임됩니다.
- 디버거 창이 아직 열려 있지 않은 상태에서 원격 메시지가 먼저 도착하면 `BehaviorTreeEditor.EnsureDebuggerWindow()`가
  `DebuggerWindow.tscn`을 인스턴스화하고 `SetEditor(this)` 및 `CloseRequested` 정리를 연결한 뒤 editor base control에 붙입니다.
- 최초 `register` 수신 시 창을 표시해 BehaviorTree target selector에서 살아있는 BehaviorTree 목록을 볼 수 있게 합니다.
- `ShowDebuggerWindow(BehaviorTree)`의 인스펙터 버튼 경로도 같은 `EnsureDebuggerWindow()`를 사용하며, 기존 로컬 `AddTab(tree)` 흐름을 유지합니다.

## Battle Debug Integration (Step 5)

- DebuggerWindow 상단 target selector는 런타임에서 register된 BehaviorTree를 `index. articleName — tree_path`로
  표시하고, 사용자가 수동으로 `Start`/`Stop`을 누르는 방식입니다. 현재 턴 캐릭터 자동 추적은 없습니다.
- `Start`/`Stop` 송신 직전 `BehaviorTreeEditor`가 `BtDebuggerPlugin.RegisterAvailableSessions()`로 현재 debugger
  session을 재수집합니다. `_Capture`에서도 수신한 `sessionId`를 등록해 session setup timing 차이를 흡수합니다.
- 런타임 `BehaviorTree.OnMessageCapture`는 editor→runtime 메시지로 `behavior_tree:start|stop`과 capture-local
  `start|stop`을 모두 처리합니다. start는 해당 `tree_path`의 `DebugEnabled = true`와 structure 1회 송신,
  stop은 `DebugEnabled = false`입니다.
- 원격 탭은 `tree_path` 메타데이터로 라우팅합니다. structure는 remote tab을 생성/갱신하고, tick은 같은
  `tree_path`의 `BehaviorTreeDebugGraphView`에만 반영됩니다. 탭 close는 해당 tree 하나에만 stop을 보냅니다.
- freed/unregister 또는 수동 stop 시 탭은 자동으로 닫히지 않고 `[STALE]`로 정지합니다.
- `DebuggerTree`는 로컬 에디터 scene Node 구조 보기 전용 legacy/local panel로 유지합니다. 원격 payload graph와
  status highlight는 `BehaviorTreeDebugGraphView`가 담당합니다.

## Verification

- Root가 없을 때 Failure인지 확인
- Running 노드가 다음 frame에 정상 재개되는지 확인
- 대상 사망/삭제 후 Blackboard 참조가 남지 않는지 확인
- `DebugEnabled == false`일 때 디버그 관련 메모리/페이로드 할당이 차단(단락)되는지 확인
- `start` / `stop` 신호에 따라 디버그 게이트가 정상적으로 열리고 닫히며 registry에 매핑되는지 확인
- editor -> window 포워딩 회귀: `BehaviorTreeValidationTest` R 케이스가 register discovery, structure 원격 탭,
  tick 하이라이트, unregister stale/removal을 검증합니다.
- Step 5 회귀: S 케이스가 다중 tree_path tick 라우팅 격리를, T 케이스가 탭 close stop 대상 격리를 검증합니다.
