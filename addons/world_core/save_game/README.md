# SaveGame Core Addon

Godot 4.6.x (Mono) 용 재사용 가능한 SaveGame framework. 저장 가능한 최소 단위(`SaveSection`)를 상속해
저장 대상을 늘리고, `SaveGameManager`가 등록/검증/envelope/파일 slot/백업을 담당한다.

SaveGame **core**는 domain-free다 — WorldState/DialogueTool 등 특정 게임 시스템을 직접 참조하지 않는다
(정적 가드 테스트로 보존). 특정 시스템 저장은 `SaveSection`을 상속한 adapter가 담당한다.

## 폴더 구조

```text
addons/world_core/save_game/       # 순수 SaveGame core (domain-free)
  save_section.gd                 # class_name SaveSection (Node base contract)
  save_game_manager.gd           # class_name SaveGameManager (등록/envelope/slot/backup)
  save_flow.gd                    # class_name SaveFlow (의도 중심 facade + provider, domain-free)
  tests/                          # 헤드리스 테스트 (core, static guard, slot, backup, save flow)

addons/world_core/save_game_world_state/  # SaveGame ↔ WorldState 통합 adapter (선택)
  world_state_save_section.gd     # class_name WorldStateSaveSection extends SaveSection
  tests/

## 설치 (호스트)

1. **폴더 복사**: `addons/world_core/save_game/`(core)와, WorldState를 저장한다면 `addons/world_core/save_game_world_state/`도 복사.
2. **`--import`**: 최초 1회 `godot --headless --path <project> --import`로 uid/클래스 캐시 생성.
3. **autoload 수동 등록**: `SaveGameManager`를 autoload로 등록한다. 권장 이름은 `SaveGame`이며, autoload 이름은
   `class_name SaveGameManager`와 **달라야** 한다(같으면 "hides an autoload singleton" 파싱 오류).

   | 이름 | 경로 |
   | --- | --- |
   | `SaveGame` | `res://addons/world_core/save_game/save_game_manager.gd` |
   | `SaveFlowFacade`(선택) | `res://addons/world_core/save_game/save_flow.gd` |

   플러그인이 autoload를 자동 등록하지 않는다(호스트가 runtime singleton 순서와 section 배치를 명시하도록).
   autoload 이름은 `class_name`과 **달라야** 한다("hides an autoload singleton" 파싱 오류 방지). `SaveGameManager`는
   `SaveGame`으로, `SaveFlow`는 위 표처럼 `SaveFlowFacade`처럼 다른 이름으로 등록하거나, 코드에서 `SaveFlow.new()`로
   직접 생성/주입한다.
4. **section 배치**: game-specific `SaveSection`을 호스트가 생성/등록한다. WorldState를 저장한다면
   `WorldStateSaveSection`을 `SaveGame`에 등록하고 `WorldStateRuntime` autoload가 먼저 ready인지 확인한다.

## 사용 요약 (core)

```gdscript
SaveGame.register_section(my_section)
var sr := SaveGame.save_slot(&"slot_1", { "display_name": "Chapter 2" })
var lr := SaveGame.load_slot(&"slot_1")
var slots := SaveGame.list_slots()
SaveGame.delete_slot(&"slot_1")
```

- slot 파일: `user://saves/<slot>.json`(+ `.tmp` 임시, `.bak` 한 세대 백업).
- payload는 JSON 호환 Dictionary여야 한다. Godot JSON은 number를 float로 읽으므로, int 의미가 필요하면
  `restore_save`에서 직접 정규화한다.

## SaveFlow facade (선택)

UI/메뉴/이벤트 레이어가 manager의 envelope/backup 내부를 몰라도 호출할 수 있는 thin facade다. UI는 제공하지
않으며 raw report만 반환한다(게임이 자기 slot menu를 만들어 소비).

```gdscript
var flow := SaveFlow.new()
flow.set_manager(SaveGame)                       # 또는 manager_path 기본 ^"/root/SaveGame"
flow.set_metadata_provider(my_meta_provider)     # 선택: make_save_metadata(slot_id) -> Dictionary
flow.set_save_gate_provider(my_gate_provider)    # 선택: query_save_gate(slot_id) -> { ok, reason }

if flow.can_save(&"slot_1").ok:                  # UI 버튼 상태와 실제 저장이 같은 정책 공유
    var r := flow.save_manual(&"slot_1", { "display_name": "Before Boss" })
    # r = { ok, slot_id, error, metadata, manager_report, gate } (성공/실패 동일 shape)
var lr := flow.load_manual(&"slot_1")            # manager.load_slot report 그대로(backup recovery 정보 포함)
```

- **metadata**: provider base + caller override(shallow merge, caller 우선). provider 없으면 caller만.
- **save gate**: 없으면 항상 allow. provider freed/메서드 없음/반환 shape 위반은 fail-closed(저장 차단).
- **manager 미해석**(미설치/freed/wrong type): `{ ok:false, error:&"manager_unavailable" }`,
  `list_slots()`만 단일 실패 entry `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`.
- **manager report 비은닉**: `error`에 manager의 원본 reason을 노출하고 `manager_report`/`gate`에 원본 보존.

자세한 사용법·권장 metadata key·raw report 소비 가이드는 위키 `SaveGame-User-Guide` / `SaveGame-System`을 참고한다.

## Host save slot UI 통합 (SG-003)

SaveGame은 재사용 가능한 save/load **UI scene/theme/layout을 제공하지 않는다**. save slot UI/UX는 게임마다 다르므로,
각 host가 자기 menu를 만들고 `SaveFlow`의 raw report를 직접 소비한다. core는 raw report를 새 enum으로 숨기지 않는다.

host UI가 소비할 핵심 규칙:

- **Slot list 분류** — `flow.list_slots()`는 두 종류의 `ok:false`를 낸다.
  - 단일 `{ ok:false, slot_id:&"", error:&"manager_unavailable" }` = **whole-list failure**(설치/초기화 오류,
    slot count=0).
  - non-empty `slot_id`를 가진 `{ ok:false, error }` = **per-slot failure card**. `error`는 `parse_error`/`corrupt`
    raw reason을 보존하고, 해당 slot만 격리해 다른 정상 slot 표시를 막지 않는다.
- **Save** — 버튼 상태는 `flow.can_save(slot_id)`로 계산한다. `can_save().ok==true`여도 `save_manual()`은
  manager/capture/metadata 문제로 실패할 수 있다. provider/gate 오류는 fail-closed(저장 미호출). overwrite
  confirmation은 host 책임. report는 항상 6키(`ok/slot_id/error/metadata/manager_report/gate`).
- **Load** — `load_manual()`은 save gate를 보지 않는다. `recovered_from_backup`/`source`/`restore`를 보존/표시하고,
  `slot_not_found`/`parse_error`/`corrupt`/`validation_failed`는 raw reason으로 유지한다. load confirmation·화면
  전환은 host 책임.
- **Delete** — confirmation은 host 책임. `delete_slot()`은 primary와 `.bak`을 모두 제거한다. 성공/실패 후 list
  refresh 권장.
- **Metadata fallback** — core는 metadata key를 해석하지 않는다. 권장 key: `display_name`/`play_time_seconds`/
  `chapter`/`location`/`mode`. `{}`/unknown key/wrong 타입에서도 host normalization은 crash 없이 fallback
  표시값을 만들고 raw metadata를 보존한다. empty slot 칸 수/이름은 host UI 정책이다.

> 일부 report 키(`slot_id`, `recovered_from_backup`, `source`, `restore`)는 실패 종류에 따라 빠진다. host는 항상
> `report.get(key, default)`로 접근한다. 전체 report 소비 matrix(list/save/load/delete)는 위키
> `SaveGame-User-Guide` 12절을 참고한다.

## 테스트 (헤드리스)

```text
godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step1_core_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step1_static_guard_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step2_slot_store_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg001_step4_backup_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg002_step1_save_flow_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg002_step1_static_guard_test.tscn
godot --headless --path <project> res://addons/world_core/save_game/tests/sg003_step2_host_flow_test.tscn
godot --headless --path <project> res://addons/world_core/save_game_world_state/tests/sg001_step3_world_state_section_test.tscn
godot --headless --path <project> res://addons/world_core/save_game_world_state/tests/sg002_step2_save_flow_world_state_test.tscn
```
