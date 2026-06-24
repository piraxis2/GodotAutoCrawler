---
type: guide
system: SaveGame
status: current
updated: 2026-06-18
---

# SaveGame User Guide

이 문서는 SG-001로 구현된 재사용 가능한 SaveGame core(`SaveSection`/`SaveGameManager`)와 WorldState
통합 adapter(`WorldStateSaveSection`)를 게임 코드에서 사용하는 방법을 설명한다. 패키징 경계는
[[ADR-013-WorldCore-Umbrella-Packaging]], 현재 구현 사실은 [[SaveGame-System]], WorldState 쪽은
[[World-State-User-Guide]]를 참고한다.

## 1. 현재 사용할 수 있는 범위

- `SaveSection`을 상속해 저장 대상을 추가하고, `SaveGameManager`에 명시적으로 등록한다.
- in-memory `capture_all()` / `validate_envelope()` / `restore_all()`로 envelope를 만들고 검증·복원한다.
- 파일 slot: `save_slot` / `load_slot` / `list_slots` / `delete_slot` / `has_slot`
  (`user://saves/<slot>.json`).
- atomic write(tmp + rename) + 한 세대 백업(`.bak`)과 load 복구.
- `WorldStateSaveSection`으로 실제 WorldState SAVE snapshot을 slot에 저장/복원.
- `SaveFlow` facade(SG-002)로 UI/이벤트 레이어가 metadata provider + caller override, optional save gate,
  manager report passthrough를 쓰는 의도 중심 save/load 호출(8절). UI는 제공하지 않는다.
- host save slot UI가 `SaveFlow` raw report를 안전하게 소비하는 integration contract(report 소비 matrix +
  metadata fallback 정책, SG-003, 12절). production UI scene/theme는 제공하지 않는다.

아직 구현되지 않은(또는 범위 밖) 기능:

- save slot UI, autosave/quicksave 정책
- 다세대 백업 history, compression/encryption, cloud sync
- schema/section version migration registry
- Dialogue SaveEffect 노드(저장 트리거는 game/event layer에서 호출 권장)
- `addons/world_core/` umbrella 패키징 이동(별도 Task, ADR-013 trigger 충족 시)

## 2. 핵심 개념

### SaveSection

저장 가능한 최소 단위다. `Node`를 상속하므로 SceneTree에 배치하거나 코드로 생성해 manager에 등록한다.

```gdscript
@export var section_id: StringName       # 빈 값/중복 금지
@export var section_version: int = 1     # adapter contract version (>= 1)
@export var restore_order: int = 0       # 작은 값 먼저, tie는 section_id lexical
@export var required: bool = true        # save에 없을 때 load 실패 여부

func capture_save() -> Dictionary        # { ok, payload, reason }
func validate_save(payload) -> Dictionary # { ok, reason } (read-only)
func restore_save(payload) -> Dictionary  # { ok, reason }
```

- `capture_save()`는 `ok==true`일 때만 `payload`(JSON 호환 Dictionary)를 반환한다. 준비되지 않았으면
  `{ "ok": false, "reason": ... }`를 반환하라 — manager는 한 section이라도 capture 실패하면 envelope/파일을
  만들지 않는다.
- `validate_save(payload)`는 read-only로 payload가 복원 가능한지 점검한다(restore 전 호출).
- `restore_save(payload)`는 실제 도메인 상태를 복원한다.

### SaveGameManager

section 등록과 envelope orchestration, 파일 slot을 담당한다. `Node`이며 호스트가 autoload(`SaveGame`)로
등록하는 것을 권장한다(아래 9절).

### Envelope

`capture_all()`이 만드는 in-memory 구조(JSON 호환). 파일 저장 시 slot 메타가 더해진다.

```json
{
  "save_version": 1,
  "slot_id": "slot_1",
  "created_at_unix": 0,
  "updated_at_unix": 0,
  "metadata": { "display_name": "", "play_time_seconds": 0 },
  "sections": { "<id>": { "section_version": 1, "payload": {} } }
}
```

version은 3계층으로 분리한다:

- `save_version` — manager(core) envelope version. mismatch면 load 실패.
- `section_version` — `SaveSection` adapter contract version. required mismatch면 load 실패,
  비-required는 skip+report.
- payload 내부 `schema_version`(있다면) — payload/domain이 소유. core는 해석하지 않고 section
  `validate_save()`가 판단한다.

## 3. SaveSection 만들기

```gdscript
class_name InventorySaveSection
extends SaveSection

var _inventory: Node  # 실제 게임 시스템 참조(주입/NodePath 등)

func _init() -> void:
    section_id = &"inventory"
    section_version = 1
    restore_order = 0

func capture_save() -> Dictionary:
    if _inventory == null:
        return { "ok": false, "reason": &"not_ready" }
    return { "ok": true, "payload": { "items": _inventory.serialize_items() } }

func validate_save(payload: Dictionary) -> Dictionary:
    if not payload.has("items"):
        return { "ok": false, "reason": &"missing_items" }
    return { "ok": true }

func restore_save(payload: Dictionary) -> Dictionary:
    _inventory.load_items(payload["items"])
    return { "ok": true }
```

payload는 **JSON 호환**이어야 한다(4절). int 의미가 중요한 값은 `restore_save`에서 직접 정규화한다.

## 4. JSON 호환 규칙 (중요)

core는 capture/validate 시 payload를 재귀 검증한다. 허용: `null`/`bool`/`int`(±(2^53-1))/`float`/`String`,
`Array`(원소 호환), `Dictionary`(**String key** + 값 호환). 거부: `StringName`/`Vector*`/`Object`/`Resource`/
`Node`, non-String Dictionary key, 범위 밖 int → `payload_not_json_compatible`.

**number는 float로 돌아온다.** Godot `JSON.parse_string`은 모든 number를 float로 읽으므로(`7`→`7.0`),
파일에서 load한 payload의 정수 값은 `float`다. JSON은 int/float를 구분하지 않으므로 core는 추측 정규화를
하지 않는다. int 의미가 필요하면 `restore_save`에서 `int(payload["x"])`처럼 직접 변환하라. (WorldState는
schema를 알기 때문에 Store가 자동 정규화한다 — 7절.)

## 5. 등록과 in-memory API

```gdscript
var manager := SaveGameManager.new()
manager.register_section(inventory_section)   # 1순위: 명시 등록
manager.register_section(party_section)
# 보조: 서브트리에서 SaveSection을 찾아 등록(전체 SceneTree 자동 검색은 기본 아님)
manager.discover_sections(some_root)

var cap := manager.capture_all()              # { ok, envelope } 또는 { ok=false, reason }
if cap["ok"]:
    var report := manager.restore_all(cap["envelope"])
```

- 등록 검증: 빈 id/중복 id/`section_version<1` 거부.
- 순서: `restore_order` 오름차순, tie는 `section_id` lexical(deterministic).
- `capture_all`: 한 section이라도 실패하면 envelope를 만들지 않는다.
- `validate_envelope`: restore 없이 비파괴 검증(`{ ok, errors, ignored_sections, missing_required,
  skipped_sections, plan }`).
- `restore_all`: validate 통과해야 restore 시작(실패 시 restore 0회). 중간 실패 시 즉시 중단하고
  `partial_restore` report(이미 복원된 id + 실패 id/reason). 이미 복원된 section은 되돌리지 않는다.
- unknown saved section은 `ignored_sections`로 report하고 실패하지 않는다.
- `capture_all`/`restore_all` 재진입은 `busy`로 거부한다.

## 6. 파일 slot

```gdscript
var sr := manager.save_slot(&"slot_1", { "display_name": "Chapter 2", "play_time_seconds": 3600 })
# { ok, slot_id, error, path, sections }

var lr := manager.load_slot(&"slot_1")
# { ok, slot_id, error, recovered_from_backup, source, restore }

var slots := manager.list_slots()   # [{ ok, slot_id, save_version, created_at_unix, updated_at_unix, metadata }, ...]
manager.delete_slot(&"slot_1")
manager.has_slot(&"slot_1")
```

- `slot_id`는 `^[a-zA-Z0-9_-]{1,64}$`만 허용(`invalid_slot_id`).
- 저장은 `<slot>.json.tmp`에 쓴 뒤 rename으로 교체한다(partial write가 실제 slot을 덮지 않음).
- `save_slot`은 기존 slot의 `created_at_unix`를 보존하고 `updated_at_unix`만 갱신한다.
- `list_slots`는 `.json`만 나열하고, 손상 slot은 `ok:false`로 격리하되 다른 slot 나열을 막지 않는다. raw
  `error`는 JSON 파싱 실패면 `parse_error`, 구조 손상이면 `corrupt`다.

### 백업과 복구

- overwrite 시 기존 primary가 **유효할 때만** `<slot>.json.bak`으로 회전한다(한 세대). 손상된 primary는
  회전하지 않아 마지막 good 백업을 덮지 않는다.
- `load_slot`은 primary가 없거나 손상이면 `.bak`을 시도한다. 복구되면 `recovered_from_backup=true`,
  `source=&"backup"`로 보고한다. primary+bak 둘 다 없으면 `slot_not_found`, 둘 다 손상이면 실제 원인
  (`parse_error`/`corrupt`)을 보고하고 실패한다(게임 상태 불변).
- `delete_slot`은 primary와 `.bak`을 모두 제거한다.

## 7. WorldState 저장 (WorldStateSaveSection)

WorldState SAVE snapshot을 slot에 저장하려면 `WorldStateSaveSection`(`addons/world_core/save_game_world_state/`)을
등록한다. 이 adapter만 SaveGame과 WorldState 양쪽을 알고, core와 WorldStateRuntime은 서로를 모른다.

```gdscript
var ws := WorldStateSaveSection.new()
# 기본 NodePath는 /root/WorldStateRuntime. 다르면 world_state_runtime_path를 지정하거나
# 테스트/통합에서는 ws.set_runtime(runtime_node)로 주입한다.
manager.register_section(ws)
```

- `capture_save`는 store ready + session ready를 먼저 확인한다(준비 안 됐으면 빈 payload 없이 실패 →
  save 전체 미작성). `start_new_game()` 또는 `restore_world_state()`로 session-ready가 된 뒤 저장하라.
- `validate_save`는 `WorldStateRuntime.peek_world_state_compatibility()`(비파괴), `restore_save`는
  `restore_world_state()`(transactional: SAVE import + SESSION default, 실패 시 기존 상태 보존)를 쓴다.
- **int/float 정규화는 자동**이다. WorldStateStore의 `import_snapshot`이 JSON round-trip의 정수형 float→int,
  int→float(FLOAT key), String→StringName를 schema 타입으로 복원하므로, 파일 저장 왕복이 무손실이다.

## 8. SaveFlow facade (SG-002)

`SaveFlow`(`addons/world_core/save_game/save_flow.gd`, `class_name SaveFlow extends Node`)는 `SaveGameManager` 위의 얇은
호출 계층이다. 게임 UI/메뉴/디버그 도구/이벤트 레이어가 envelope/backup 내부 정책을 몰라도 의도 중심 API로
저장/로드를 호출할 수 있게 한다. **UI는 제공하지 않는다** — raw report만 반환하므로 각 게임이 자기 slot menu를
만들어 소비한다. core와 동일하게 domain-free다(WorldState/DialogTool 직접 참조 없음).

### manager 해석

`SaveFlow`는 manager를 소유하지 않고 **호출마다 lazy resolve**한다.

```gdscript
var flow := SaveFlow.new()
flow.set_manager(save_game_manager)   # 1순위: 명시 주입
# 또는 주입하지 않으면 manager_path(기본 ^"/root/SaveGame") 로 해석
```

- 우선순위: `set_manager()` 주입 manager(valid일 때) → `manager_path`.
- 매 호출 `is_instance_valid` + `is SaveGameManager`를 재확인하므로 freed manager/autoload 재생성에 안전하다.
- 미해석(미설치/freed/wrong type)이면 일반 report `{ ok:false, error:&"manager_unavailable" }`,
  `list_slots()`만 빈 배열 대신 단일 실패 entry, `has_slot()`은 false.

### metadata provider (선택)

```gdscript
flow.set_metadata_provider(provider)  # provider.make_save_metadata(slot_id) -> Dictionary
```

- provider 없으면 base는 `{}`. caller가 `save_manual(slot_id, metadata)`로 넘긴 metadata만 쓰인다.
- provider base와 caller metadata는 **shallow merge**(provider base 먼저 → caller가 같은 key override).
- provider가 freed/non-Object/메서드 없으면 `metadata_provider_unavailable`, 반환이 Dictionary가 아니면
  `metadata_provider_contract_invalid`로 **fail-closed**(이 경우 `save_slot`을 호출하지 않는다).
- 최종 metadata의 JSON 호환 검증은 `SaveGameManager.save_slot()`이 한다(caller가 non-JSON을 넣으면 manager의
  `metadata_not_json_compatible`가 그대로 전달된다).

```gdscript
provider: { "chapter": "Forest", "play_time_seconds": 120 }
caller:   { "display_name": "Before Boss", "chapter": "Boss Gate" }
final:    { "chapter": "Boss Gate", "play_time_seconds": 120, "display_name": "Before Boss" }
```

### save gate provider (선택)

저장 가능 여부를 UI 버튼 상태와 실제 save 호출이 **같은 정책**으로 공유하게 한다.

```gdscript
flow.set_save_gate_provider(provider)  # provider.query_save_gate(slot_id) -> { ok: bool, reason }
var gate := flow.can_save(&"slot_1")   # { ok, reason }
```

- provider 없으면 `{ ok:true, reason:&"" }`(allow).
- provider freed/non-Object/메서드 없으면 `save_gate_unavailable`, 반환이 Dictionary가 아니거나 `ok`가 bool이
  아니면 `save_gate_contract_invalid`로 **fail-closed**(`ok:false`).
- provider가 `{ ok:false, reason }`을 반환하면 reason을 보존해 deny로 정규화한다.
- `can_save()`는 gate만 본다 — **manager 가용성은 보지 않는다**. 따라서 `can_save().ok == true`여도
  `save_manual()`이 manager 미해석으로 `manager_unavailable`이 될 수 있다.

### save_manual / load_manual / delete_slot / list_slots / has_slot

```gdscript
if flow.can_save(&"slot_1").ok:
    var r := flow.save_manual(&"slot_1", { "display_name": "Before Boss" })
var lr := flow.load_manual(&"slot_1")
var slots := flow.list_slots()
flow.delete_slot(&"slot_1")
flow.has_slot(&"slot_1")
```

- `save_manual()`은 저장 전 `can_save(slot_id)`를 호출한다. gate가 `ok:false`면 `save_slot`을 호출하지 않고
  실패한다. 정책상 금지는 `error:&"save_not_allowed"`, gate 설치/계약 오류는 `save_gate_unavailable`/
  `save_gate_contract_invalid`로 `error`에 구분 노출한다.
- `save_manual()` report는 성공/실패 모두 **6키를 유지**한다(호출되지 않은 단계는 `{}`):

```gdscript
{ "ok", "slot_id", "error", "metadata", "manager_report", "gate" }
```

- **manager report는 숨기지 않는다.** `manager.save_slot()`의 `capture_failed`/`metadata_not_json_compatible`/
  `invalid_slot_id`/`backup_failed`/`rename_failed` 등은 `error`에 그대로 노출하고 `manager_report`에 원본을 보존한다.
- `load_manual()`은 gate를 확인하지 않고 `manager.load_slot()` report를 그대로 감싼다 —
  `recovered_from_backup`/`source`/`restore`가 손실되지 않는다.
- `list_slots()`는 manager list를 display formatting 없이 그대로 반환한다.

### 권장 metadata key

core는 metadata key를 해석하지 않는다. 게임 UI 간 일관성을 위해 아래 key를 권장만 한다.

```text
display_name: String
play_time_seconds: int/float
chapter: String
location: String
mode: String                 # manual / quick / auto 등(후속 사용)
```

thumbnail, localized display text, UI sort label은 SG-002 범위 밖이다.

### UI가 소비할 raw report

이번 범위는 UI를 만들지 않는다. 예상 UI는 아래 raw report를 각 게임 스타일로 표시한다.

- `list_slots()`의 slot entry(정상 entry + non-empty `slot_id`를 가진 per-slot failure entry(`parse_error`/
  `corrupt` 등 raw error) + manager_unavailable 단일 entry).
- `save_manual()`/`load_manual()`의 `metadata` raw Dictionary.
- `load_manual()`의 `recovered_from_backup`/`source`.
- `can_save()`의 `{ ok, reason }`(버튼 enable/disable + 사유 표시).

> `list_slots()` 의미 차이: **단일 `manager_unavailable` entry(`slot_id:&""`) = 리스트 전체 무효**(SaveGame
> 설치/초기화 문제로 표시, slot 개수로 세지 말 것), **non-empty `slot_id`를 가진 per-slot failure entry
> (`parse_error`/`corrupt` 등) = 해당 slot만 무효**(raw error 보존, 다른 slot entry는 유효).
>
> host UI 통합의 전체 report 소비 규칙·matrix·metadata fallback 정책은 12절을 참고한다(SG-003).

## 9. 호스트 설치

SaveGame은 플러그인이 autoload를 자동 등록하지 않는다(WorldState와 동일 정책). 호스트가 직접 등록한다.

1. `addons/world_core/save_game/`(core)와, WorldState를 저장한다면 `addons/world_core/save_game_world_state/`를 프로젝트에 둔다.
2. 최초 실행 시 `godot --headless --path <project> --import`로 uid/클래스 캐시를 생성한다.
3. `SaveGameManager`를 autoload로 등록한다(권장 이름 `SaveGame`). 이름은 `class_name SaveGameManager`와
   달라야 한다(autoload 이름 = class_name이면 "hides an autoload singleton" 파싱 오류).

   | 이름 | 경로 |
   | --- | --- |
   | `SaveGame` | `res://addons/world_core/save_game/save_game_manager.gd` |

4. game-specific section은 host가 배치/등록한다. WorldState를 저장한다면 `WorldStateSaveSection`을
   `SaveGame`에 등록하고, `WorldStateRuntime` autoload가 먼저 ready인지 확인한다([[World-State-User-Guide]] 8절).
5. (선택) `SaveFlow`를 쓰려면 코드에서 `SaveFlow.new()`로 만들어 `set_manager(SaveGame)`로 주입하거나, autoload로
   등록한다. autoload 이름은 `class_name SaveFlow`와 **달라야** 한다(예: `SaveFlowFacade`). autoload면
   `manager_path` 기본값 `^"/root/SaveGame"`으로 manager를 자동 해석한다.

## 10. 보고형 reason 요약

| 단계 | reason | 의미 |
| --- | --- | --- |
| register | `section_id_empty` / `section_id_duplicate` / `section_version_invalid` | 등록 거부 |
| capture | `capture_failed` / `payload_not_json_compatible` / `sections_invalid` / `busy` | envelope 미생성 |
| validate | `malformed_envelope` / `save_version_mismatch` / `required_missing` / `section_version_mismatch` / `malformed_section` | 검증 실패 |
| restore | `validation_failed` / `partial_restore` / `busy` | 복원 실패/중단 |
| slot | `invalid_slot_id` / `slot_not_found` / `parse_error` / `corrupt` / `backup_failed` / `rename_failed` | 파일 IO |
| WorldState adapter | `store_not_ready` / `session_not_ready` / `runtime_unavailable` / `runtime_contract_invalid` | adapter 경계 |
| SaveFlow facade | `manager_unavailable` / `save_not_allowed` / `save_gate_unavailable` / `save_gate_contract_invalid` / `metadata_provider_unavailable` / `metadata_provider_contract_invalid` | facade/provider 경계 |

## 11. 한계와 후속

- 다중 section 전역 rollback은 없다(MVP는 validate-first로 위험을 줄임). WorldState 자체는 transactional이다.
- 백업은 한 세대만 유지한다.
- `world_core` umbrella 패키징 이동은 별도 Task다(ADR-013 migration trigger).

## 12. Host Save Slot UI Integration (SG-003)

SaveGame core는 재사용 가능한 save/load **UI scene/theme/layout을 제공하지 않는다**([[ADR-014-SaveFlow-Facade-And-Metadata-Provider]], [[SG-003-SaveSlot-UI-Host-Integration]]). save slot UI/UX는 게임마다 다르므로, 각 host(게임)가 자기 menu를 만들고 `SaveFlow`의 **raw report를 직접 소비**한다. 이 절은 host UI 구현자가 따라야 할 report 소비 규칙·상태 전이·failure handling·metadata 표시 계약을 정의한다.

핵심 원칙:

- **raw report를 숨기지 않는다.** `SaveFlow`는 manager report를 새 enum/추상화로 덮어쓰지 않는다. host UI는 `error`/`reason`/`recovered_from_backup`/`source`/`restore`/`manager_report`/`gate` 원본을 그대로 소비한다.
- **fail-closed를 신뢰한다.** provider/gate 오류와 manager 미해석은 모두 `ok:false`로 정규화되고, save 경로는 실제 `save_slot`을 호출하지 않는다. host는 `ok` 플래그를 1차 분기로 쓴다.
- **report shape는 실패 종류에 따라 키가 다르다.** 일부 키(`slot_id`, `recovered_from_backup`, `source`, `restore`)는 특정 실패 report에서 **빠진다**. host는 항상 `report.get(key, default)`로 접근해 missing key crash를 피한다(아래 각 절 참고).

### 12.1 Slot list 분류

`var entries := flow.list_slots()` 결과는 두 종류의 `ok:false`를 포함할 수 있다. host는 이를 **같은 "빈 슬롯"으로 취급하지 않는다**.

- **whole-list failure (전체 무효)**
  - 조건: `entries.size() == 1 && entries[0].ok == false && String(entries[0].slot_id) == "" && entries[0].error == &"manager_unavailable"`.
  - 의미: SaveGame 설치/초기화 문제(manager 미해석). slot 목록 자체가 무효다.
  - host 동작: "저장 시스템을 사용할 수 없음" 같은 설치/초기화 오류로 표시하고, **slot count에 포함하지 않는다**(0개로 본다). 개별 slot card를 그리지 않는다.
- **per-slot failure (해당 slot만 무효)**
  - 조건: `entry.ok == false && String(entry.slot_id) != ""`.
  - 의미: 해당 slot 파일만 손상/읽기 실패다. `error`는 manager가 반환한 **raw reason**(`parse_error` = JSON 파싱 실패, `corrupt` = 구조 손상)을 그대로 보존한다.
  - host 동작: 해당 slot만 **failure card**로 격리해 표시한다(예: "손상된 슬롯 — parse_error"). load/overwrite/delete 허용 여부는 host 정책이다. **다른 정상 slot 표시를 막지 않는다.**
- **normal slot (정상 card)**
  - 조건: `entry.ok == true`.
  - 키: `slot_id`(StringName), `save_version`(int), `created_at_unix`(int), `updated_at_unix`(int), `metadata`(Dictionary).
  - host 동작: slot card로 표시. metadata 표시는 12.5 fallback 정책을 따른다.

> per-slot raw error(`parse_error`/`corrupt`)를 알 수 없는 전역 실패로 처리하지 말 것. 한 slot만 손상돼도 전체 목록 UX가 무너진다.

### 12.2 Manual save flow

1. host가 선택 slot에 대해 `flow.can_save(slot_id)`로 **버튼 enable/disable**을 계산한다(`{ ok, reason }`). 실제 `save_manual()`도 내부에서 같은 gate를 다시 호출하므로 버튼을 누르는 순간 상태가 바뀌어도 fail-closed된다.
2. 기존 slot에 덮어쓰기면 **host가 overwrite confirmation**을 띄운다(core는 confirmation을 강제하지 않는다).
3. 확정하면 `flow.save_manual(slot_id, caller_metadata)`를 호출한다.
4. 실패 시 `report.error`를 표시하되, 상세/로그 view는 `report.manager_report`와 `report.gate` 원본을 본다.
5. 성공 시 slot list refresh를 호출한다.

주의:

- `can_save().ok == true`여도 `save_manual()`은 **실패할 수 있다.** `can_save()`는 gate provider만 보고 manager 가용성·capture·metadata는 보지 않는다. 따라서 결과가 `manager_unavailable`/`capture_failed`/`metadata_not_json_compatible` 등이 될 수 있다.
- **provider/gate 오류는 fail-closed**다. `save_gate_unavailable`/`save_gate_contract_invalid`/`metadata_provider_unavailable`/`metadata_provider_contract_invalid`인 경우 `save_manual()`은 `manager.save_slot()`을 **호출하지 않는다**(파일 미작성).

`save_manual()` report는 성공/실패 모두 **6키 shape**를 유지한다(호출되지 않은 단계는 `{}`):

```gdscript
{ "ok": bool, "slot_id", "error": StringName, "metadata": Dictionary,
  "manager_report": Dictionary, "gate": Dictionary }
```

### 12.3 Manual load flow

1. host가 slot을 선택한다.
2. **host가 load confirmation**을 띄운다("현재 진행상태를 잃을 수 있음"). core는 confirmation을 강제하지 않는다.
3. 확정하면 `flow.load_manual(slot_id)`를 호출한다.
4. 성공 시 **메뉴 닫기·게임 화면 전환은 host 책임**이다.
5. report의 `recovered_from_backup`/`source`/`restore`를 host state에 보존해 표시/진단에 쓴다.

주의:

- `load_manual()`은 **save gate를 보지 않는다.** load는 정책 게이트 대상이 아니다.
- `recovered_from_backup == true`(그리고 `source == &"backup"`)이면 "백업에서 복구됨" 안내를 표시할 수 있다(숨기지 말 것).
- 실패 report(`slot_not_found`/`parse_error`/`corrupt`/`validation_failed`)는 **raw reason으로 유지**한다. 실패 시 WorldState adapter transactional 보존 또는 manager validate-first 정책으로 가능한 범위에서 기존 게임 상태가 보존되지만, UI는 실패 report를 숨기지 않는다.
- **load report는 실패 종류에 따라 키가 다르다**(아래 12.6 load matrix). `recovered_from_backup`/`source`/`restore`는 `manager_unavailable`/`invalid_slot_id`/`slot_not_found`/read-fail report에서 빠질 수 있으므로 `report.get(...)`로 접근한다.

### 12.4 Delete flow

1. **삭제 confirmation은 host UI가 소유한다.**
2. 확정하면 `flow.delete_slot(slot_id)`.
3. 성공/실패와 관계없이 list refresh를 권장한다(특히 `slot_not_found`는 이미 사라진 상태이므로 목록을 새로 고친다).

주의:

- `delete_slot()`은 primary(`<slot>.json`)와 백업(`<slot>.json.bak`)을 **모두 제거**한다(백업이 남아 slot이 되살아나지 않게 — 6절/SG-001 Step 4).
- delete undo/trash는 범위 밖이다.

### 12.5 Metadata display fallback

- **core는 metadata key를 해석하지 않는다.** 표시는 전적으로 host 정책이다.
- 게임 UI 간 일관성을 위한 **권장 key**(강제 아님):

  ```text
  display_name: String          # slot 표시 이름
  play_time_seconds: int/float  # 누적 플레이 시간
  chapter: String               # 챕터/진행 단계
  location: String              # 현재 위치
  mode: String                  # manual / quick / auto 등
  ```

- host normalization은 **crash 없이 fallback 표시값**을 만들어야 한다:
  - metadata가 `{}`이거나 권장 key가 없을 때 → fallback(예: `display_name` 없으면 `slot_id`를 이름으로, `play_time_seconds` 없으면 `—`/`0`).
  - 권장 key가 있으나 **타입이 예상과 다를 때**(예: `display_name`이 String이 아님, `play_time_seconds`가 number가 아님) → 타입 검사 후 fallback으로 대체(잘못된 캐스팅으로 crash 금지).
  - unknown key는 무시하거나 detail view에만 표시한다.
- **raw metadata는 보존**한다. inspect/log/detail view에서 원본 Dictionary를 그대로 볼 수 있게 둔다.
- **empty slot grid의 칸 수/이름은 core가 아니라 host UI 정책**이다. `flow.list_slots()`는 실제로 저장된 slot만 나열한다. "빈 슬롯" 개념(고정 칸 수, "Empty" 라벨 등)은 host가 만든다.

### 12.6 Report consumption matrix

각 row의 마지막 칸은 **host UI가 해야 할 일**이다. `error`/`reason`은 StringName raw 값이다.

**List** (`flow.list_slots() -> Array[Dictionary]`)

| 케이스 | 식별 | host UI 동작 |
| --- | --- | --- |
| normal | `entry.ok == true` | slot card 표시(`slot_id`/timestamps/`metadata`), metadata는 12.5 fallback |
| whole-list `manager_unavailable` | `size==1 && slot_id==&"" && error==&"manager_unavailable"` | 설치/초기화 오류 표시, slot count=0, slot card 미생성 |
| per-slot `parse_error` | `entry.ok==false && slot_id!="" && error==&"parse_error"` | 해당 slot만 failure card(JSON 파싱 실패)로 격리, 다른 slot 정상 표시 |
| per-slot `corrupt` | `entry.ok==false && slot_id!="" && error==&"corrupt"` | 해당 slot만 failure card(구조 손상)로 격리, 다른 slot 정상 표시 |

**Save** (`flow.save_manual(slot_id, metadata) -> Dictionary`, 항상 6키)

| 케이스 | `error` | host UI 동작 |
| --- | --- | --- |
| success | `&""` (`ok:true`) | 성공 표시 + slot list refresh |
| `save_not_allowed` | `&"save_not_allowed"` | 정책상 저장 금지. `gate.reason`을 사유로 표시, 버튼 disabled 유지 |
| `save_gate_unavailable` | `&"save_gate_unavailable"` | gate provider 설치 오류(fail-closed, 미저장). 저장 불가 처리 + 진단 로그 |
| `save_gate_contract_invalid` | `&"save_gate_contract_invalid"` | gate 반환 계약 위반(fail-closed, 미저장). 저장 불가 처리 + 진단 로그 |
| `metadata_provider_unavailable` | `&"metadata_provider_unavailable"` | metadata provider 설치 오류(fail-closed, 미저장). 오류 표시 + 진단 로그 |
| `metadata_provider_contract_invalid` | `&"metadata_provider_contract_invalid"` | provider 반환 계약 위반(fail-closed, 미저장). 오류 표시 + 진단 로그 |
| `capture_failed` | `&"capture_failed"` | 게임 상태 capture 실패(미저장). `manager_report.capture`로 원인 표시, 재시도 안내 |
| `metadata_not_json_compatible` | `&"metadata_not_json_compatible"` | caller/provider metadata가 JSON 비호환. metadata 수정 필요 |
| `invalid_slot_id` | `&"invalid_slot_id"` | slot_id 패턴 위반. host가 입력 slot_id 검증 |
| `manager_unavailable` | `&"manager_unavailable"` | 설치/초기화 오류 표시 |
| 파일 IO | `&"backup_failed"`/`&"rename_failed"`/`&"tmp_open_failed"`/`&"saves_dir_unavailable"` | 저장 실패로 표시(저장됐다고 가정 금지), `manager_report` 보존, refresh |

> save 실패/성공 모두 `manager_report`(원본 manager report)와 `gate`(`can_save` 결과)를 보존한다. 상세/로그 view에서 활용한다.

**Load** (`flow.load_manual(slot_id) -> Dictionary`, 키는 실패 종류별로 다름)

| 케이스 | `error` | 존재 키 | host UI 동작 |
| --- | --- | --- | --- |
| success (primary) | `&""` (`ok:true`) | `slot_id`,`recovered_from_backup:false`,`source:&"primary"`,`restore` | 메뉴 닫기·게임 화면 전환(host) |
| recovered from backup | `&""` (`ok:true`) | `slot_id`,`recovered_from_backup:true`,`source:&"backup"`,`restore` | success 동작 + "백업에서 복구됨" 안내 |
| `slot_not_found` | `&"slot_not_found"` | `slot_id` | "저장 데이터 없음" 표시(`recovered_from_backup`/`source`/`restore` 없음 → `.get`) |
| `parse_error` | `&"parse_error"` | `slot_id`,`recovered_from_backup:false` | 손상 안내, 기존 게임 상태 불변. `source`/`restore` 없음 → `.get` |
| `corrupt` | `&"corrupt"` | `slot_id`,`recovered_from_backup:false` | 손상 안내, 기존 게임 상태 불변. `source`/`restore` 없음 → `.get` |
| `validation_failed` | `&"validation_failed"` | `slot_id`,`recovered_from_backup`,`source`,`restore` | 복원 실패 표시, `restore`(validation/partial 상세) 보존 |
| `invalid_slot_id` | `&"invalid_slot_id"` | `slot_id` | slot_id 검증 오류 |
| `manager_unavailable` | `&"manager_unavailable"` | (`slot_id` 없음) | 설치/초기화 오류 표시 |

**Delete** (`flow.delete_slot(slot_id) -> Dictionary`)

| 케이스 | `error` | host UI 동작 |
| --- | --- | --- |
| success | `&""` (`ok:true`, `slot_id`) | 성공 표시 + list refresh |
| `slot_not_found` | `&"slot_not_found"` (`slot_id`) | 이미 없음 안내 + list refresh |
| `invalid_slot_id` | `&"invalid_slot_id"` (`slot_id`) | slot_id 검증 오류 |
| 파일 IO | `&"delete_failed"`/`&"saves_dir_unavailable"` (`slot_id`) | 삭제 실패 표시 + list refresh |
| `manager_unavailable` | `&"manager_unavailable"` (`slot_id` 없음) | 설치/초기화 오류 표시 |

> load/delete의 `manager_unavailable` report에는 `slot_id`가 **없다**(SaveFlow가 manager 미해석 단계에서 조기 반환). 모든 optional 키는 `report.get(key, default)`로 접근해 missing key crash를 피한다.

### 12.7 검증 경계

SG-003은 production `Control` scene을 만들지 않으므로 pixel/layout 검증은 하지 않는다. 대신 Step 2에서 test 파일 내부 fake host controller/test double(`FakeSaveSlotHostController`)가 실제 `SaveFlow + SaveGameManager` 위에서 list 분류, save gate fail-closed, save/load/delete report passthrough, backup recovery 데이터 보존, per-slot failure 격리, metadata fallback을 검증한다. 이 test double은 public API가 아니다.

## Related

- [[SaveGame-System]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[World-State-User-Guide]]
- [[SG-001-SaveGame-Core-Section-System]]
