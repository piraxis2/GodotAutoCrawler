---
id: ADR-016
type: decision
status: proposed
date: 2026-06-19
updated: 2026-06-21
system: BehaviorTree
---

# BehaviorTree Remote Debug Channel

## Context

BT-001은 BehaviorTree 실행 상태를 에디터 GraphEdit에 하이라이트하려 한다. 하지만 BehaviorTree는 플레이 세션의 `TurnHelper._PhysicsProcess`에서 tick되고, Godot editor process와 play process는 별도 OS 프로세스다.

따라서 EditorPlugin은 런타임 `BehaviorTree_Node` 인스턴스의 C# event를 직접 구독할 수 없다. 현재 `BehaviorTree_Node.OnLogChanged`도 `#if TOOLS` C# event이며 payload가 `{Status, Time}`뿐이라 노드 식별과 tick 단위 routing에 부족하다. Running node는 매 physics frame emit될 수 있어 raw event를 그대로 UI에 연결하면 spam과 stale highlight 문제가 생긴다.

DialogueTool은 별도 프로세스 디버그 하이라이트를 `EditorDebuggerPlugin._capture`와 런타임 `EngineDebugger.send_message`로 처리한다. BehaviorTree도 같은 프로세스 경계 패턴을 따라야 한다.

대조한 선례:

- `addons/world_core/dialogtool/debugger_plugin/dialogue_debugger_plugin.gd`는 `_has_capture("dialogue")`와 `_capture("dialogue:current_node", data, session_id)`로 메시지를 받는다.
- `addons/world_core/dialogtool/RunTime/dialogue_player.gd`는 `EngineDebugger.is_active()`일 때 `EngineDebugger.send_message("dialogue:current_node", [node_id])` 또는 clear용 `[-1]`을 보낸다.
- BehaviorTree는 DialogueTool보다 node report가 많고 Running node가 매 physics frame 반복될 수 있으므로 단일 current node message가 아니라 tick batch contract가 필요하다.

디버그 대상 BehaviorTree는 play process에만 존재하며, battle_field에서 동적으로 instance될 수 있다. 따라서 에디터는 "어떤 tree가 살아있는지"를 먼저 알아야 하고(discovery), 실제 디버그 대상에 대해서만 tick을 받아야 한다(gating). 그리고 디버그 GraphEdit는 에디터에 열린 `.tscn` 구조에 의존하지 않고 remote payload만으로 자체 구성되어야 dynamically-spawned 캐릭터를 디버깅할 수 있다.

## Options

1. EditorPlugin이 `OnLogChanged`를 직접 구독한다.
2. 런타임에서 `OnLogChanged` 또는 tick 지점의 정보를 모아 `EngineDebugger.send_message`로 보내고, 에디터 `EditorDebuggerPlugin._capture`가 수신한다.
3. 파일/소켓/autoload 같은 별도 IPC 또는 shared state를 만든다.

## Decision

옵션 2(EngineDebugger 양방향 채널)를 채택하되, 다음 4가지를 확정한다.

1. **양방향 채널.** runtime→editor는 `EngineDebugger.SendMessage`, editor→runtime는 `EditorDebuggerSession.SendMessage` + runtime `EngineDebugger.RegisterMessageCapture`.
2. **per-tree gating (default OFF).** 에디터가 특정 tree_path에 `start`를 보내기 전에는 어떤 tree도 tick을 emit하지 않는다. 비디버깅 play의 런타임 비용을 0에 수렴시킨다.
3. **discovery announce.** 각 BehaviorTree는 play 진입/이탈 시 1회 `register`/`unregister`만 ungated로 보낸다(스폰당 1메시지). 에디터는 이 목록으로 디버그 대상을 선택한다.
4. **payload-built debug graph.** 디버그 GraphEdit는 `start` 직후 받은 `structure` 메시지로 노드 집합을 자체 구성한다. 에디터-열린 씬에 매핑하지 않는다. (Steps 1~3 authoring 그래프는 기존대로 scene 기반이며, 디버그 그래프와 분리된 read-only render path다.)

`OnLogChanged`는 public 채널이 아니다. 런타임 내부에서 tick batch를 만드는 hook으로만 쓴다.

읽기 전용 debug emit은 실행 semantics 변경이 아니다. gate가 OFF이면 노드별 path 문자열/딕셔너리 할당이 발생하기 *전에* 단락되어야 한다(아래 Gating 참조).

## Message Contract

namespace는 `behavior_tree`. capture name 충돌(`dialogue`)은 없다.

### runtime → editor (EngineDebugger.SendMessage)

| message | when | payload |
| --- | --- | --- |
| `behavior_tree:register` | BehaviorTree play `_Ready` (ungated) | `{ tree_path, article_name }` |
| `behavior_tree:unregister` | BehaviorTree `_ExitTree` (ungated) | `{ tree_path }` |
| `behavior_tree:structure` | `start` 수신 직후 1회 | `{ tree_path, nodes:[{ node_path, name, type, parent_path, graph_position }] }` |
| `behavior_tree:tick` | gate ON인 tree의 physics tick마다 | `{ tree_path, physics_frame, nodes:[{ node_path, status, elapsed_time }] }` |

### editor → runtime (EditorDebuggerSession.SendMessage)

| message | when | payload |
| --- | --- | --- |
| `behavior_tree:start` | 디버그 탭 open / 대상 선택 | `{ tree_path }` |
| `behavior_tree:stop`  | 디버그 탭 close | `{ tree_path }` |

### 식별자
- `tree_path`: play process 안 BehaviorTree의 절대 NodePath 문자열. **탭 라우팅 키**이자 registry 키. 동명 재소환은 Godot `@N` suffix로 구분된다.
- `node_path`: tree(BehaviorTree) 기준 상대 NodePath(`Tree.GetPathTo(node)`). 디버그 그래프 내 GraphNode 키. structure와 tick에서 동일 키를 쓴다.
- `status`: int (BtStatus). `elapsed_time`: double.

### Gating (런타임 비용 단락)
- BehaviorTree는 `bool DebugEnabled`(default false)를 가진다. `start`에서 true(+structure 1회), `stop`에서 false.
- `BehaviorTree_Node.Behave()`는 **`if (Tree == null || !Tree.DebugEnabled) return;` 로 path 문자열/딕셔너리 생성 이전에 단락**한다. `EngineDebugger.IsActive()`는 export 빌드 마스터 가드로만 남긴다.

### Runtime dispatcher / registry
- editor→runtime 메시지는 **단일** `EngineDebugger.RegisterMessageCapture("behavior_tree", ...)` 로 수신한다. 첫 BehaviorTree가 active-debugger play에서 `_Ready`될 때 1회 등록(static guard).
- static `Dictionary<string, BehaviorTree>` registry(키=tree_path)로 `start`/`stop`을 해당 인스턴스에 라우팅. register/unregister announce와 같은 레지스트리를 공유한다.

### Lifecycle 정책 (확정)
- **freed 시 탭**: 캐릭터가 freed/사망하면 `unregister`로 해당 탭을 **마지막 상태로 정지(stale 표시)**한다. 자동 닫지 않고 사용자가 직접 닫는다(죽기 직전 동작 확인 가능).
- **대상 선택**: register 목록에서 **수동 선택**을 먼저 구현한다. TurnHelper current-actor 자동 추적은 follow-up.
- structure 없이 도착한 tick(순서 꼬임/늦은 도착)은 mapping 실패로 무시한다.

## Rationale

- editor/play 프로세스 경계를 정확히 따른다.
- DialogueTool의 검증된 remote debugger 패턴과 일관된다.
- 런타임 execution semantics를 바꾸지 않고 읽기 전용 관찰만 추가한다.
- tick batch와 frame id를 포함해 Running spam, stale highlight, 다중 캐릭터 routing을 제어할 수 있다.
- node 식별자를 payload에 포함해 GraphEdit node와 status를 매핑할 수 있다.

## Consequences

### Positive

- EditorPlugin이 런타임 객체 lifetime에 직접 묶이지 않는다.
- 캐릭터 freed/scene 종료 후 stale message를 fail-closed로 무시할 수 있다.
- 다중 캐릭터 탭과 현재 턴 캐릭터 highlight로 확장 가능하다.
- GraphEdit highlight는 runtime Node tree source of truth와 독립적인 UI projection으로 유지된다.

### Negative

- EditorDebuggerPlugin과 runtime send_message 양쪽 구현이 필요하다.
- C# editor debugger 자동 테스트 인프라가 부족해 일부 검증은 fixture + 수동 확인이 필요할 수 있다.
- NodePath 기반 식별자는 rename/reparent 시점의 mapping 갱신이 필요하다.
- Running node가 많은 경우 throttle/batch 정책을 잘못 잡으면 debug UI가 과도하게 갱신될 수 있다.
- editor→runtime 역방향(start/stop)은 DialogueTool 선례에 없는 신규 패턴이라 capture 등록·dispatcher·static registry를 새로 만든다.
- 디버그 그래프가 payload 기반이라 authoring 그래프와 별도 render path를 유지한다(중복 약간).
- gate OFF default로 비디버깅 play 비용이 사실상 0이다.
- payload 기반이라 battle 동적 캐릭터도 에디터에 해당 씬을 열지 않고 디버깅 가능하다.

## Follow-ups

- BT-001 Step 4a에서 정확한 message namespace와 payload schema를 구현하며 테스트한다.
- BT-001 Step 4b에서 GraphEdit stale/not ticked 표시 정책을 확정한다.
- TurnHelper current-actor 자동 추적(현재 턴 캐릭터 탭 자동 전환)은 follow-up으로 미룬다. Step 5는 수동 선택까지만 다룬다.
- 장기적으로 execution timeline recorder가 필요하면 이 channel 위에 별도 기록 계층을 추가한다.
