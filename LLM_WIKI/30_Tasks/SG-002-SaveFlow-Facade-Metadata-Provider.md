---
id: SG-002
type: task
status: complete
system: SaveGame
created: 2026-06-17
updated: 2026-06-18
tags: [task, save-game, save-flow, metadata, facade]
---

# SaveFlow Facade and Metadata Provider

## Goal

SG-001의 `SaveGameManager`/`SaveSection`/slot store 위에, 게임별 UI가 가져다 쓰기 쉬운 얇은 호출 계층을
추가한다.

핵심 방향:

- Save slot UI/UX는 게임마다 다르므로 이번 Task에서 만들지 않는다.
- UI, 메뉴, 디버그 도구, game event layer가 공통으로 호출할 수 있는 `SaveFlow` facade를 제공한다.
- metadata는 호출자가 직접 넘길 수도 있고, 없으면 host가 주입한 provider가 만든다.
- provider metadata와 caller metadata가 모두 있으면 caller metadata가 같은 key를 override한다.
- 저장 가능 여부는 UI와 실제 save 호출이 같은 정책을 쓰도록 optional save gate provider로 분리한다.

## User Outcome

- 게임 UI는 `SaveGameManager`의 내부 envelope/backup 정책을 직접 알 필요 없이 `SaveFlow.save_manual()` 같은
  의도 중심 API를 호출할 수 있다.
- 게임별 slot metadata(`display_name`, `play_time_seconds`, `chapter`, `location` 등)는 provider 또는 caller
  metadata로 확장할 수 있다.
- 컷신/전투/로딩 중 저장 금지 같은 정책은 UI와 save 호출 양쪽에서 같은 `can_save()` 결과를 공유할 수 있다.

## Context

- [[SG-001-SaveGame-Core-Section-System]] 완료: `SaveGameManager.save_slot/load_slot/list_slots/delete_slot/has_slot`,
  JSON 호환 metadata 검증, 한 세대 `.bak` 복구, `WorldStateSaveSection` 통합이 동작한다.
- [[SaveGame-User-Guide]]는 SG-001 API를 직접 사용하는 방법을 설명한다.
- SG-002는 새로운 save UI를 만들지 않는다. UI가 사용할 수 있는 stable facade contract를 만드는 작업이다.
- `world_core` path migration은 [[ADR-013-WorldCore-Umbrella-Packaging]] trigger 충족 전까지 제외한다.

## Scope

- 신규 `SaveFlow` Node API 설계.
- manager 해석 정책(`NodePath` 기본 `/root/SaveGame` + explicit injection).
- metadata provider contract.
- caller metadata override merge 정책.
- save gate provider contract.
- report/error 정책.
- UI/game-specific metadata key 표준화는 최소 권장 key만 문서화.

## Out of Scope

- 실제 save/load menu UI.
- quicksave/autosave 구현.
- slot thumbnail/capture image.
- 다세대 backup history.
- schema/section migration registry.
- Dialogue SaveEffect 노드.
- `addons/world_core/` migration.
- `SaveGameManager`의 파일 저장/복구 정책 변경.

## Design Direction

### 위치

```text
addons/save_game/save_flow.gd
addons/save_game/tests/sg002_step1_save_flow_test.gd
```

`SaveFlow`는 SaveGame core helper다. 특정 gameplay system, WorldState, DialogueTool, UI scene을 참조하지 않는다.

### Autoload

호스트가 원하면 `SaveFlow`를 별도 autoload로 등록한다. 권장 이름은 `SaveFlow`.

```text
SaveGame -> res://addons/save_game/save_game_manager.gd
SaveFlow -> res://addons/save_game/save_flow.gd
```

`SaveFlow`는 기본적으로 `/root/SaveGame`을 manager로 찾는다. 테스트/수동 구성에서는 `set_manager(manager)`로
주입한다. manager는 호출마다 lazy resolve하고, 매 호출 `is_instance_valid`와 `is SaveGameManager`를 다시
확인한다. freed manager나 autoload 재생성에 안전해야 하며, stale cached reference를 신뢰하지 않는다.

### Public API Draft

```gdscript
class_name SaveFlow
extends Node

@export var manager_path: NodePath = ^"/root/SaveGame"

func set_manager(manager: SaveGameManager) -> void
# provider는 Object duck-type을 기대하지만 인자는 Variant로 수용한다(Step 1 구현 확정). non-Object 입력도
# 타입 오류 없이 unavailable로 fail-closed되도록 setter/저장 변수/검사 helper를 Variant 경계로 둔다.
func set_metadata_provider(provider) -> void
func set_save_gate_provider(provider) -> void

func can_save(slot_id = &"") -> Dictionary
func save_manual(slot_id, metadata: Dictionary = {}) -> Dictionary
func load_manual(slot_id) -> Dictionary
func delete_slot(slot_id) -> Dictionary
func list_slots() -> Array[Dictionary]
func has_slot(slot_id) -> bool
```

### Manager Resolution

`SaveFlow`는 `SaveGameManager`를 직접 소유하지 않는다.

1. `set_manager(manager)`로 명시 주입된 manager가 있으면 우선한다.
2. 없으면 `manager_path`로 `/root/SaveGame`을 찾는다.
3. 없거나 freed되었거나 `SaveGameManager`가 아니면 report 실패:

```gdscript
{ "ok": false, "error": &"manager_unavailable" }
```

`list_slots()`는 manager를 찾지 못하면 빈 배열 대신 손상/설치 문제를 UI가 알 수 있게 단일 실패 entry를
반환한다. 별도 `list_slots_report()` API는 만들지 않는다. 실패 entry는 manager의 per-slot corrupt entry와
shape를 맞추기 위해 `slot_id`를 포함한다.

```gdscript
[{ "ok": false, "slot_id": &"", "error": &"manager_unavailable" }]
```

의미 차이:

- `error=&"manager_unavailable"` 단일 entry: 리스트 전체가 유효하지 않다. UI는 SaveGame 설치/초기화 문제로
  표시하고 slot 개수로 세지 않는 것이 좋다.
- `error=&"corrupt"` per-slot entry: 해당 slot만 손상됐고 다른 slot entry는 계속 유효하다.

### Metadata Provider

provider는 선택이다. 없으면 `{}`를 base metadata로 사용한다.

```gdscript
func make_save_metadata(slot_id) -> Dictionary
```

정책:

- provider가 null이면 통과.
- provider가 freed/non-Object이거나 메서드가 없으면 `metadata_provider_unavailable`.
- 반환이 Dictionary가 아니면 `metadata_provider_contract_invalid`.
- 반환 Dictionary와 caller metadata는 shallow merge한다.
- merge 순서: provider base -> caller override.
- 최종 metadata JSON 호환 검증은 `SaveGameManager.save_slot()`이 담당한다.

예:

```gdscript
provider: { "chapter": "Forest", "play_time_seconds": 120 }
caller:   { "display_name": "Before Boss", "chapter": "Boss Gate" }
final:    { "chapter": "Boss Gate", "play_time_seconds": 120, "display_name": "Before Boss" }
```

### Recommended Metadata Keys

core는 metadata key를 해석하지 않는다. User Guide에는 아래 key를 권장만 한다.

```text
display_name: String
play_time_seconds: int/float
chapter: String
location: String
mode: String                 # manual / quick / auto 등은 후속에서 사용 가능
```

thumbnail, localized display text, UI sort label은 이번 범위 밖이다.

### Save Gate Provider

save 가능 여부도 UI와 실제 save 호출이 공유해야 하므로 optional provider로 둔다.

```gdscript
func query_save_gate(slot_id) -> Dictionary
# { ok: bool, reason: StringName/String }
```

정책:

- provider가 없으면 `{ "ok": true, "reason": &"" }`.
- provider가 freed/non-Object이거나 `query_save_gate` 메서드가 없으면 **fail-closed**:
  `{ "ok": false, "reason": &"save_gate_unavailable" }`.
- 반환이 Dictionary가 아니거나 `ok`가 bool이 아니면 **fail-closed**:
  `{ "ok": false, "reason": &"save_gate_contract_invalid" }`.
- `SaveFlow.can_save(slot_id)`는 provider report를 정규화해 반환한다.
- `can_save()`는 save gate만 질의한다. manager 가용성은 확인하지 않는다. 따라서 UI에서 `can_save().ok == true`여도
  실제 `save_manual()`은 manager 해석 실패로 `manager_unavailable`이 될 수 있다.
- `save_manual()`은 저장 전에 `can_save(slot_id)`를 호출한다.
- gate가 `ok=false`면 manager를 호출하지 않고 실패한다. 정책상 저장 금지는 `save_not_allowed`, gate 설치/계약
  오류는 각각 `save_gate_unavailable`/`save_gate_contract_invalid`를 `error`에 노출해 UI가 구분할 수 있게 한다.

```gdscript
{
  "ok": false,
  "slot_id": slot_id,
  "error": &"save_not_allowed", # or save_gate_unavailable/save_gate_contract_invalid
  "metadata": {},
  "manager_report": {},
  "gate": gate_report
}
```

### Save Manual Flow

```text
resolve manager
validate save gate
build metadata(provider base + caller override)
manager.save_slot(slot_id, metadata)
wrap report without hiding manager report
```

성공 report:

```gdscript
{
  "ok": true,
  "slot_id": &"slot_1",
  "error": &"",
  "metadata": final_metadata,
  "manager_report": manager_report,
  "gate": gate_report
}
```

실패 report:

```gdscript
{
  "ok": false,
  "slot_id": &"slot_1",
  "error": reason,
  "metadata": final_metadata_or_empty,
  "manager_report": manager_report_if_called_or_empty,
  "gate": gate_report_if_checked_or_empty
}
```

`manager.save_slot()`이 반환한 `capture_failed`, `metadata_not_json_compatible`, `invalid_slot_id`, `backup_failed`,
`rename_failed` 등은 `error`에 그대로 노출하고 `manager_report`에도 원본을 보존한다.

`save_manual()` report는 성공/실패 경로 모두에서 `metadata`, `manager_report`, `gate` 키를 유지한다. 호출되지 않은
단계는 `{}`를 넣어 UI가 shape별 분기를 덜 하게 한다.

### Load/Delete/List/Has

- `load_manual(slot_id)`는 gate를 확인하지 않는다. load 가능 여부는 별도 정책이 생기기 전까지 manager report를
  그대로 감싼다.
- `delete_slot(slot_id)`는 manager delete를 감싼다.
- `list_slots()`는 manager list를 그대로 반환하고 display formatting을 하지 않는다.
- `has_slot(slot_id)`는 manager `has_slot` 위임. manager unavailable이면 false.

### UI Contract

이번 Task는 UI를 제공하지 않는다. 예상 UI는 아래 raw report를 사용해 각 게임 스타일로 표시한다.

- `list_slots()`의 slot entry.
- `metadata` raw Dictionary.
- `ok:false` corrupt entry.
- `load_manual()`의 `recovered_from_backup` / `source`.
- `can_save()`의 `{ ok, reason }`.

## Step Plan

### Step 0: Design

목표:
- `SaveFlow` facade, metadata provider, save gate provider 계약을 확정한다.

작업 범위:
- SG-002 Task 작성.
- 필요 시 ADR 작성.
- 제품 코드 변경 없음.

완료 조건:
- 구현자가 Step 1을 시작할 수 있을 정도로 public API, provider contract, error policy, 테스트 계획이 명확하다.
- UI/UX 제외 범위가 명확하다.

### Step 1: SaveFlow Core

목표:
- UI 없는 `SaveFlow` Node를 구현한다.

작업 범위:
- `addons/save_game/save_flow.gd`.
- `addons/save_game/tests/sg002_step1_save_flow_test.gd/.tscn`.
- manager resolution, metadata provider, save gate provider, manual save/load/delete/list/has.

제외 범위:
- WorldState 통합 e2e.
- 실제 UI.
- quicksave/autosave.

완료 조건:
- provider metadata + caller override merge.
- metadata provider missing/bad return fail-closed.
- save gate allow/deny/unavailable/contract invalid. gate unavailable/contract invalid는 fail-closed이며
  `save_slot`을 호출하지 않는다.
- manager unavailable report.
- manager report passthrough.
- no product references to WorldState/DialogTool in `save_flow.gd`.
- `save_manual()` 성공/실패 report shape 균일화(`metadata`/`manager_report`/`gate` 키 보존).
- `list_slots()` manager unavailable single failure entry shape:
  `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`.

검증:
- Godot headless import.
- SG-002 Step 1 unit test.
- SG-002 또는 확장된 SG-001 static guard가 `save_flow.gd`까지 domain-free 스캔.
- SG-001 Step 1/2/4 regression + static guard.

### Step 2: WorldState Integration Usage Test

목표:
- `SaveFlow`가 `SaveGameManager + WorldStateSaveSection` 조합에서 실제 slot save/load를 올바르게 위임하는지 검증한다.

작업 범위:
- 통합 테스트만 추가하거나 최소 테스트 helper 추가.
- `SaveFlow.save_manual/load_manual`로 WorldState SAVE snapshot 파일 왕복.
- backup recovery report가 `SaveFlow.load_manual`에서 보존되는지 확인.

제외 범위:
- UI.
- quicksave/autosave.

완료 조건:
- WorldState store/session ready일 때 `save_manual` 성공.
- store/session not-ready capture 실패가 원본 manager report로 전달.
- load backup recovery의 `recovered_from_backup/source`가 손실되지 않음.

검증:
- SG-002 Step 2 integration test.
- SG-001 Step 3/4 regression.
- DT-006 step3/step4 regression.

### Step 3: Documentation and Completion Review

목표:
- SaveGame User Guide와 README에 `SaveFlow` 사용법을 추가하고 SG-002를 완료 판정한다.

작업 범위:
- [[SaveGame-User-Guide]] 갱신.
- `addons/save_game/README.md` 갱신.
- [[SaveGame-System]] 현재 사실 갱신.
- SG-002 review 문서 작성.

제외 범위:
- UI/UX 구현.

완료 조건:
- 게임 UI가 어떤 raw report와 metadata key를 소비해야 하는지 문서화.
- SG-002 Step 1~2 검증 결과 정리.

## Design Decisions

- **D1. UI는 제외한다.** Save slot UX는 게임마다 달라지므로 core addon에는 facade와 raw report만 둔다.
- **D2. metadata 정책은 provider + caller override(C안)로 간다.** provider가 기본값을 만들고, 호출자가 같은 key를
  넘기면 caller가 우선한다.
- **D3. display formatting은 core 책임이 아니다.** localization, thumbnail, slot card layout은 UI/game layer 몫이다.
- **D4. save gate provider는 optional로 둔다.** UI와 실제 save 호출이 같은 저장 가능 정책을 공유할 수 있게 한다.
- **D5. manager report는 숨기지 않는다.** facade는 편의층이지 오류 의미를 재해석하는 계층이 아니다.

## Resolved Design Review Decisions

- `list_slots()` manager unavailable은 단일 실패 entry 방식을 채택한다. Shape는
  `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`로 고정하고, per-slot corrupt와 의미 차이를 문서화한다.
- metadata merge는 shallow merge를 유지한다. nested metadata deep merge는 실제 요구가 생기면 후속 Task로 둔다.
- provider/gate provider는 `Object` duck-type을 유지한다. base class 강제는 host 결합을 늘린다.
- save gate provider 오류는 fail-closed다. gate 설치/계약 오류는 `save_not_allowed`와 구분해
  `save_gate_unavailable`/`save_gate_contract_invalid`로 노출한다.
- gate provider 메서드명은 `query_save_gate(slot_id)`로 확정한다. `SaveFlow.can_save()` public API와 혼동하지 않는다.
- manager는 호출마다 lazy resolve + validity/type check를 수행한다.

## Step 0 Design Review Result

설계 리뷰: [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]]

판정: **Approved after design fixes**. 2026-06-17 design fixes 반영 완료:

- P2: gate provider 오류 fail-closed 및 error 구분 반영.
- P2: `list_slots()` manager unavailable single-entry shape에 `slot_id:&""` 포함, 의미 차이 명시.
- P2: Step 1 산출물에 `save_flow.gd` domain-free 정적 가드 추가.
- P3: `save_manual()` report shape 균일화.
- P3: gate provider 메서드명을 `query_save_gate`로 변경.
- P3: manager lazy resolve/`can_save()` manager 비의존성 명시.

## Step 1 Implementation Result

**Step 1 SaveFlow Core 구현 완료 — 리뷰 대기.**

변경 파일:
- `addons/save_game/save_flow.gd`(신규): `class_name SaveFlow extends Node`. manager를 소유하지 않고
  호출마다 lazy resolve(`_resolve_manager`: 1순위 `set_manager` 주입 manager가 valid일 때, 그다음
  `manager_path` 기본 `/root/SaveGame`, 매번 `is_instance_valid`+`is SaveGameManager` 재확인 → 미해석 시 null).
- `addons/save_game/tests/sg002_step1_save_flow_test.gd`/`.tscn`(신규): A~T 20 시나리오.
- `addons/save_game/tests/sg002_step1_static_guard_test.gd`/`.tscn`(신규): `save_flow.gd` domain-free 정적 가드
  (SG-001 core 가드와 동일 FORBIDDEN 토큰, 주석 제외). SG-001 가드는 미변경(별도 SG-002 전용 가드 추가).

구현 내용:
- **Manager resolution**: 주입 우선, freed/wrong-type/미설치면 일반 report `{ ok:false, error:&"manager_unavailable" }`.
  `list_slots()`만 단일 실패 entry `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`, `has_slot()`은 false.
  freed 주입 manager는 신뢰하지 않고 `manager_path` 폴백(둘 다 실패 시 unavailable).
- **Metadata provider**(`make_save_metadata(slot_id)` duck-type): 없음=base `{}`, freed/non-Object/메서드 없음=
  `metadata_provider_unavailable`, 반환 non-Dictionary=`metadata_provider_contract_invalid`(둘 다 fail-closed,
  `save_slot` 미호출). shallow merge(provider base → caller override). 최종 JSON 호환 검증은 manager.save_slot 위임.
- **Save gate provider**(`query_save_gate(slot_id)` duck-type): 없음=`{ ok:true, reason:&"" }`,
  freed/non-Object/메서드 없음=`save_gate_unavailable`, 반환 non-Dictionary 또는 `ok` 비-bool=
  `save_gate_contract_invalid`(fail-closed). provider deny는 reason 보존해 정규화. `save_manual`은 저장 전
  `can_save(slot_id)` 호출, `ok:false`면 manager.save_slot 미호출. 정책 금지=`save_not_allowed`,
  gate 설치/계약 오류=`save_gate_unavailable`/`save_gate_contract_invalid`로 `error` 구분.
- **save_manual 흐름**: manager resolve → gate → metadata → manager.save_slot. 성공/실패 모두 6키
  (`ok/slot_id/error/metadata/manager_report/gate`) 보존, 미호출 단계는 `{}`. manager report는 숨기지 않고
  `error`에 원본 reason 노출 + `manager_report`에 원본 보존(passthrough).
- **load/delete/list/has**: `load_manual`은 gate 미확인, manager.load_slot report를
  `recovered_from_backup`/`source`/`restore` 손실 없이 그대로 반환. `delete_slot`/`list_slots`/`has_slot`은
  manager 위임(`list_slots`는 display formatting 없음). `can_save()`는 manager 가용성과 무관.
- **경계**: `save_flow.gd`는 WorldState/DialogTool을 직접 참조하지 않음(정적 가드 통과).

검증:
- `godot --headless --path . --import`: 0 parse 에러, `SaveFlow` class 등록.
- `sg002_step1_save_flow_test`(A~T 20 시나리오) ALL PASS, exit 0, 실제 SCRIPT ERROR 0.
- `sg002_step1_static_guard_test` ALL PASS(12 토큰).
- SG-001 회귀: `sg001_step1_core_test`/`sg001_step1_static_guard_test`/`sg001_step2_slot_store_test`/
  `sg001_step4_backup_test` ALL PASS(SCRIPT ERROR 0 — core 테스트의 "SCRIPT ERROR count: 1"은 테스트 print
  텍스트 매칭 오탐).

Design Deviation: 없음. 정적 가드는 SG-001 가드 확장 대신 SG-002 전용 가드를 추가했다(Task가 둘 중 하나를 허용).

코드 리뷰 처리 결과:
- **[P2] provider/gate non-Object 경계 fail-closed — 수정.** `set_metadata_provider`/`set_save_gate_provider`
  인자와 저장 변수(`_metadata_provider`/`_save_gate_provider`)를 `Object`에서 Variant 경계로 열었다
  (Public API Draft은 `provider: Object`였으나 리뷰 권고대로 non-Object 입력이 타입 오류 없이 unavailable로
  정규화되도록 변경 — duck-type 계약은 동일). `_provider_usable()`의 검사 순서를 `null → typeof != TYPE_OBJECT →
  is_instance_valid → has_method`로 재정렬해 `is_instance_valid()`가 non-Object를 받기 전에 걸러지게 했다.
  회귀 방지: `sg002_step1_save_flow_test`에 metadata(D2)·gate(I2) 각각 String/int/Array를 넘기는 케이스 추가
  (unavailable report + `save_slot` 미호출 + 파일 미작성 + 실제 SCRIPT ERROR 0 확인). 재검증 결과 ALL PASS,
  SG-001 회귀(core/static_guard/slot_store/backup) ALL PASS, `--import` 0 에러.

남은 위험/공백:
- WorldState 통합 e2e(`SaveGameManager + WorldStateSaveSection` 조합 실제 slot 왕복, backup recovery report
  보존)는 Step 2 범위라 미검증.
- manager_path 양성 해석은 in-tree manager의 `get_path()`로 검증했다. 실제 `/root/SaveGame` autoload 구성은
  호스트 설치 시점이라 테스트에서 직접 띄우지 않았다(autoload는 Step 3 문서 범위).

## Step 2 Implementation Result

**Step 2 WorldState Integration Usage Test 구현 완료 — 리뷰 대기. 제품 코드 변경 없음(통합 테스트만 추가).**

변경 파일:
- `addons/save_game_world_state/tests/sg002_step2_save_flow_world_state_test.gd`/`.tscn`(신규): A~D 통합
  시나리오. `SaveFlow`가 SaveGame core의 domain-free 경계를 깨지 않도록, 의도적으로 SaveGame↔WorldState
  결합을 넘는 이 테스트는 `addons/save_game/tests/`가 아니라 통합 adapter와 같은
  `addons/save_game_world_state/tests/`에 둔다.

구현 내용(검증):
- **A. 전체 왕복**: `SaveGameManager + WorldStateSaveSection`에 `SaveFlow`를 주입(`set_manager`)하고
  store/session ready(`start_new_game`) 상태에서 `flow.save_manual(sid, caller_metadata)` 성공 →
  `flow.load_manual(sid)`로 SAVE snapshot 파일 왕복. INT/FLOAT/String/StringName 타입이 schema 타입으로
  복원되고(파일 JSON 왕복 후에도) SESSION은 default로 시작. metadata provider base + caller override가
  통합 경로에서도 merge되고, manager report(path/sections.world_state)가 passthrough됨을 함께 확인.
- **B. store not-ready passthrough**: store 미주입 runtime → `save_manual`이 manager의 `capture_failed`를
  `error`로 노출하고 `manager_report.capture.section_reason == &"store_not_ready"`까지 원본 보존, 파일 미작성.
- **C. session not-ready passthrough**: store ready지만 `start_new_game` 미호출 → `capture_failed` +
  `section_reason == &"session_not_ready"` 원본 전달, 파일 미작성.
- **D. backup recovery report 보존**: 2회 저장으로 `.bak` 회전(bak=stage 11, primary=stage 22) 후 primary를
  제거해 복구 경로를 강제 → `flow.load_manual`이 `recovered_from_backup=true`/`source=&"backup"`/`restore`를
  손실 없이 전달하고, bak(v1) 값(stage 11)으로 실제 복원됨을 확인.

검증:
- `sg002_step2_save_flow_world_state_test`(A~D) ALL PASS, exit 0, 실제 SCRIPT ERROR 0.
- 회귀: SG-001 Step 3(`sg001_step3_world_state_section_test`)·Step 4(`sg001_step4_backup_test`),
  DT-006 step3(`dt006_step3_lifecycle_test`)·step4(`dt006_step4_adapter_test`), SG-002 Step 1
  (save_flow + static guard) **모두 ALL PASS**(실제 SCRIPT ERROR 0). `--import` 0 에러.

Design Deviation: 없음. 새 product 결합을 만들지 않고 통합 테스트만 추가했다.

남은 위험/공백:
- 실제 `/root/WorldStateRuntime`/`/root/SaveGame` autoload 부팅 조합이 아니라 주입(`set_manager`/
  `set_runtime`) 구성으로 검증했다(autoload 설치는 호스트 책임, Step 3 문서 범위).
- User Guide / `README.md`의 SaveFlow 사용법·권장 metadata key 문서화는 Step 3 범위.

## Step 3 Implementation Result

**Step 3 Documentation and Completion Review 완료 — SG-002 전체 완료. 제품 코드 변경 없음(문서만).**

변경 파일:
- [[SaveGame-User-Guide]]: §1에 SaveFlow 추가, 신규 §8 "SaveFlow facade (SG-002)"(manager 해석 / metadata
  provider / save gate / `save_manual` 6키 shape / 권장 metadata key / UI가 소비할 raw report + `list_slots()`
  의미 차이), §9 설치에 SaveFlow autoload·주입 주의(이름≠class_name), §10 reason 표에 facade reason 행 추가,
  후행 섹션 renumber.
- `addons/save_game/README.md`: 폴더 구조·설치 표(autoload 이름 `SaveFlowFacade` 안전 예시)·"SaveFlow facade"
  사용 절·테스트 목록에 SG-002 추가.
- [[SaveGame-System]]: SG-002 Step 1~3 완료 사실 + SaveFlow 현재 동작 갱신.

완료 조건 대비:
- 게임 UI가 소비할 raw report(slot entry / metadata / corrupt·manager_unavailable entry /
  `recovered_from_backup`·`source` / `can_save` `{ok,reason}`)와 권장 metadata key 문서화 완료.
- SG-002 Step 1~2 검증 결과를 완료 리뷰에 정리.

완료 판정: [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]] "Step 1~3 완료 리뷰" **판정: 완료**(Step 1~3
완료 조건 충족, Step 1 코드 리뷰 [P2] non-Object fail-closed 수정 확인, Step 3 문서 리뷰 [P2] README autoload
이름·[P3] Public API Draft signature 정리 반영, P0/P1 없음).

검증:
- `--import` 0 parse 에러. 최종 매트릭스 **10/10 GREEN**(실제 SCRIPT ERROR 0): SG-002 step1(save_flow A~T,
  static guard)·step2(integration A~D), SG-001 step1(core, static guard)/step2(slot)/step3(world_state)/
  step4(backup), DT-006 step3(lifecycle)/step4(adapter).

Design Deviation: 없음(문서 정리만). Step 1에서 확정한 provider Variant 경계를 Public API Draft에도 반영했다.

## Verification Matrix

| 영역 | 정상 | 실패/회귀 |
| --- | --- | --- |
| Manager resolution | explicit manager, `/root/SaveGame` lookup | missing/freed/wrong type |
| Metadata | provider only, caller only, provider+caller override | missing method, non-Dictionary, non-JSON final metadata |
| Save gate | no provider allow, provider allow | deny, invalid report, non-bool ok |
| Save flow | manager save success, manager error passthrough | invalid slot, capture failed, backup/rename errors |
| Load/list/delete | manager report preserved | corrupt/recovered report preserved |
| Boundaries | domain-free SaveFlow | WorldState/DialogTool references in core |

## Related

- [[SG-001-SaveGame-Core-Section-System]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
