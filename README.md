# AutoCrawler

Godot 4.6.3 Mono와 C#/.NET 8 기반의 자동 턴제 택티컬 RPG 프로토타입입니다.
전투 실험과 함께, 대화 저작 도구, 타입 안전 World State, SaveGame 코어, BehaviorTree 에디터/디버거를 재사용 가능한 애드온 형태로 개발하고 있습니다.

## 현재 초점

- `Assets/Scenes/Map/battle_field.tscn`: 실제 전투/AI 실험 중심 씬
- `main.tscn`: UI와 윈도우 동작 실험 성격의 씬
- `addons/world_core/`: DialogueTool, WorldState, SaveGame 계열 재사용 모듈
- `addons/behaviortree/`: BehaviorTree 런타임, 에디터 그래프, 원격 디버거

## 개발 환경

- Godot `4.6.3` Mono
- C# / .NET `8`
- GDScript 기반 에디터 도구 및 런타임 보조 시스템
- 주요 플러그인 활성화:
  - `res://addons/behaviortree/plugin.cfg`
  - `res://addons/devconsole/plugin.cfg`
  - `res://addons/world_core/dialogtool/plugin.cfg`
  - `res://addons/godot-vim/plugin.cfg`

## 빠른 실행

1. Godot Mono 4.6.3으로 프로젝트를 엽니다.
2. 최초 실행 또는 경로 이동 후에는 import를 갱신합니다.

```powershell
godot --headless --path . --import
```

3. 전투 시스템을 확인하려면 `Assets/Scenes/Map/battle_field.tscn`을 실행합니다.
4. 대화 도구는 Godot 상단/플러그인 UI의 DialogueTool 진입점에서 열 수 있습니다.
5. BehaviorTree는 Inspector에서 BehaviorTree 또는 BT node를 선택한 뒤 Behavior Tree Editor를 열어 확인합니다.

## 핵심 시스템

### 전투와 Article

`ArticleBase`는 캐릭터, 장애물, 아이템처럼 게임 월드에 놓이는 상호작용 객체의 기반입니다.
각 Article은 `ArticleStatus`로 체력/능력치/상태 효과를 관리하고, `TilePosition`, `OnMove`, `OnDead` 등을 통해 전투 흐름과 연결됩니다.

### Status

`ArticleStatus`는 고정 스탯과 지속 효과를 함께 관리합니다.
`StatusAffect`는 버프, 디버프, 지속 피해 같은 효과를 표현하며, 즉시 적용 효과와 턴 기반 지속 효과를 나누어 처리합니다.

### Turn System

`TurnHelper`가 현재 턴 Article을 선택하고, 매 physics frame마다 해당 Article의 `TurnPlay()`를 호출합니다.
BehaviorTree 실행 결과가 `Success` 또는 `Failure`가 되면 다음 Article로 턴을 넘깁니다.

### BehaviorTree

캐릭터 AI는 Godot Node 계층 기반 BehaviorTree로 작성합니다.
`Selector`, `Sequence`, `RatingSelector`, `Decorator`, `Action` 노드를 조합해 행동을 결정하며, `CharacterArticle`이 자신의 `BehaviorTree` child를 실행합니다.

최근 추가된 BehaviorTree 도구:

- GraphEdit 기반 read-only viewer와 authoring UI
- 노드 생성/삭제, 연결/해제, sibling order 조정
- 구조 validation: root 부재, decorator 자식 초과, action 자식 존재, cycle, multiple parent 등
- Inspector 연동과 `bt_graph_position` 메타데이터 저장
- play process와 editor process를 잇는 원격 debug channel
- runtime discovery, per-tree start/stop gating, payload-built debug graph
- tick 상태 하이라이트, stale clear, unregister 처리
- `battle_field.tscn` F5 smoke에서 remote graph 생성과 stop stale 확인

## Custom Addons

| Addon | 위치 | 역할 |
| --- | --- | --- |
| BehaviorTree | `addons/behaviortree/` | 전투 AI 런타임, GraphEdit 기반 에디터, 원격 디버거 |
| WorldCore / DialogueTool | `addons/world_core/dialogtool/` | 노드 기반 대화 그래프 에디터와 런타임 |
| WorldCore / WorldState | `addons/world_core/world_state/` | 타입 안전 상태 schema/store, condition evaluation, runtime lifecycle |
| WorldCore / SaveGame | `addons/world_core/save_game/` | domain-free save section/manager/slot/backup/facade |
| WorldCore / SaveGame WorldState | `addons/world_core/save_game_world_state/` | WorldState snapshot을 SaveGame section으로 연결하는 adapter |
| DevConsole | `addons/devconsole/` | 런타임 개발자 콘솔과 cheat command 연동 |
| godot-vim | `addons/godot-vim/` | 에디터 보조 플러그인 |

### DialogueTool

DialogueTool은 게임 내 대화와 이벤트 흐름을 노드 그래프로 작성하는 에디터 플러그인입니다.
작성된 그래프는 `.tres` 리소스로 저장되고, 런타임에서는 `DialogueManager.play(resource, read_provider, mutation_provider)`로 실행합니다.

지원하는 주요 흐름:

- `Start`, `Say`, `Choice`, `Branch`, `End`
- `Variable`, `Expression`, `Autoload`, `SceneFunction`
- Portrait show/hide/expression effect
- 비대기 Effect 연결과 validation
- Say 줄 누적 표시 및 실제 UI 클릭 경로 회귀 테스트
- 조건부 Branch와 조건부 Choice
- Choice 항목별 Effect
- State Set/Add Effect
- WorldState 단일 key 읽기 Data node
- 에디터 Play 시 preview WorldState provider 자동 주입
- `DialogueManager` 반복 실행, 교체, same-frame latest-wins, stale signal 방어

### WorldState

WorldState는 게임 상태를 schema로 선언하고 strict type으로 읽고 쓰는 런타임 상태 저장소입니다.
상태 key, 타입, 기본값, lifetime, writable 여부를 `StateSchema`/`StateDefinition`으로 관리합니다.

주요 기능:

- 허용 타입: `bool`, `int`, `float`, `String`, `StringName`
- SAVE/SESSION lifetime 분리
- JSON-safe snapshot export/import
- atomic batch apply
- `add_state` 기반 보고형 numeric mutation
- `ConditionSet`, `ConditionGroup`, `StateCondition` 기반 조건 평가
- `WorldStateRuntime.start_new_game()`, `capture_world_state()`, `restore_world_state()`
- DialogueTool의 condition/read/mutation provider로 주입 가능

### SaveGame

SaveGame은 특정 도메인에 묶이지 않는 저장 프레임워크입니다.
저장 단위는 `SaveSection`으로 확장하고, `SaveGameManager`가 envelope, validation, slot file, `.tmp`, `.bak` 한 세대 백업을 관리합니다.

추가된 흐름:

- `save_slot`, `load_slot`, `list_slots`, `delete_slot`
- `WorldStateSaveSection` adapter
- `SaveFlow` facade: metadata provider, save gate, raw report preservation
- host-owned save slot UI contract 문서화
- test-only fake host controller로 slot list/save/load/delete 소비 규칙 검증

SaveGame core는 production save/load UI를 제공하지 않습니다. 실제 메뉴, theme, input focus, overwrite confirmation은 host 게임이 소유합니다.

### DevConsole

`F1` 키로 여는 개발자 콘솔입니다.
`CheatManager.cs`와 GDScript cheat manager에 정의한 명령을 런타임 디버깅용으로 연결합니다.

## Autoload

현재 프로젝트의 주요 autoload:

| 이름 | 경로 |
| --- | --- |
| `DialogueManager` | `res://addons/world_core/dialogtool/RunTime/dialogue_manager.gd` |
| `WorldState` | `res://addons/world_core/world_state/world_state_store.tscn` |
| `WorldStateRuntime` | `res://addons/world_core/world_state/world_state_runtime.gd` |
| `DialogueToolUtil` | `uid://bg2wpsw3ggue7` |
| `CheatManager` | `res://Assets/Script/CheatManager.cs` |
| `GdsCheatManager` | `res://Assets/Script/gds/gds_cheat_manager.gd` |
| `SoundManager` | `res://Assets/Script/gds/sound_manager.gd` |

SaveGame은 addon 코드와 테스트가 준비되어 있지만, 현재 `project.godot`에는 `SaveGame` autoload로 등록되어 있지 않습니다.
호스트에서 사용할 때 `SaveGameManager`를 `SaveGame` 같은 class_name과 다른 이름으로 등록합니다.

## 검증 자산

저장소에는 기능별 headless 테스트가 포함되어 있습니다.

- DialogueTool: `addons/world_core/dialogtool/RunTime/tests/`
- WorldState: `addons/world_core/world_state/tests/`
- WorldState condition: `addons/world_core/world_state/condition/tests/`
- SaveGame: `addons/world_core/save_game/tests/`
- SaveGame WorldState adapter: `addons/world_core/save_game_world_state/tests/`
- BehaviorTree: `addons/behaviortree/tests/BehaviorTreeValidationTest.cs`

Godot/GDScript 변경은 가능한 경우 headless editor load와 관련 test scene으로 확인합니다.
BehaviorTree C# 변경은 `dotnet build`와 `bt_validation_test.tscn` 기반 검증을 함께 사용합니다.

## 문서

상세 설계와 작업 이력은 `LLM_WIKI/`에 정리되어 있습니다.

- `LLM_WIKI/00_Index/Current-State.md`: 현재 구현 상태
- `LLM_WIKI/00_Index/Open-Tasks.md`: 남은 작업과 최근 완료 항목
- `LLM_WIKI/20_Systems/DialogueTool.md`
- `LLM_WIKI/20_Systems/World-State-System.md`
- `LLM_WIKI/20_Systems/SaveGame-System.md`
- `LLM_WIKI/20_Systems/BehaviorTree-System.md`
- `LLM_WIKI/20_Systems/*-User-Guide.md`: 시스템별 사용 가이드

Wiki와 코드가 다르면 실제 코드와 실행 결과를 최종 사실로 봅니다.
