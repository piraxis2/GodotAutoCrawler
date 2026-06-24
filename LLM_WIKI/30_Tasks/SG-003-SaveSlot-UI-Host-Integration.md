---
id: SG-003
type: task
status: complete
system: SaveGame
created: 2026-06-18
updated: 2026-06-18
tags: [task, save-game, save-flow, ui-integration, host-owned-ui]
---

# Save Slot UI Host Integration

## Goal

SG-002의 `SaveFlow`를 실제 게임 save/load UI가 소비하는 방식을 설계한다.

핵심 방향:

- Save slot UI/UX는 게임마다 다르므로 SaveGame core는 reusable UI scene/theme/layout을 제공하지 않는다.
- 대신 host가 자기 UI를 만들 때 따라야 할 raw report 소비 규칙, 상태 전이, failure handling, metadata 표시 계약을 문서화한다.
- 구현 검증은 실제 UI 위젯이 아니라 reference host controller/test double로 수행한다.
- `SaveFlow`의 public API와 manager report passthrough를 그대로 사용하고, envelope/file/backup 정책을 새 계층에서 재해석하지 않는다.

## User Outcome

- 게임별 Save/Load menu는 `SaveFlow`만 의존해 slot list, save 가능 여부, 저장, 로드, 삭제, 복구 안내를 구현할 수 있다.
- UI는 whole-list `manager_unavailable`과 per-slot failure(`parse_error`/`corrupt` 등 raw error)를 구분하고,
  backup recovery 같은 중요한 report를 숨기지 않는다.
- 각 게임은 visual layout, localization, controller/keyboard focus, confirmation dialog, thumbnail, animation을 자유롭게 소유한다.
- SaveGame addon은 UI를 강제하지 않으면서도 가져다 쓰기 쉬운 integration recipe와 검증된 reference flow를 제공한다.

## Context

- [[SG-001-SaveGame-Core-Section-System]] 완료: slot save/load/list/delete/backup/WorldState adapter가 동작한다.
- [[SG-002-SaveFlow-Facade-Metadata-Provider]] 완료: `SaveFlow`가 metadata provider, caller override, save gate,
  manager report passthrough를 제공한다.
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]는 core가 UI를 제공하지 않고 facade만 제공하기로 결정했다.
- [[SaveGame-User-Guide]] §8은 `SaveFlow` API를 설명하지만, 실제 host UI의 화면 상태 전이와 report 소비 matrix는 아직 얇다.

## Scope

- Host-owned save/load UI가 따라야 할 integration contract 설계.
- Slot list entry normalization 규칙(정상 slot, per-slot failure(`parse_error`/`corrupt` 등), `manager_unavailable` 전체 실패).
- Manual save/load/delete flow의 confirmation, gate, report handling 정책.
- Metadata key 표시 권장과 missing/unknown key fallback 정책.
- Backup recovery report 노출 정책.
- Reference host controller/test double 기반 검증 계획.
- 문서 산출물: SaveGame User Guide/README/System 문서 갱신, SG-003 Review.

## Out of Scope

- 재사용 가능한 production save menu scene.
- 게임 테마, localization string, icon, animation, controller focus 구현.
- thumbnail/capture image.
- quicksave/autosave.
- 다세대 backup history.
- cloud save, compression/encryption.
- Dialogue SaveEffect.
- `addons/world_core/` migration.

## Design Principles

### D1. UI는 host가 소유한다

SaveGame addon은 `Control` 기반 production menu를 제공하지 않는다. Core가 scene hierarchy, theme token,
translation key, input/focus 정책을 소유하면 게임별 UX와 충돌한다.

SG-003의 산출물은 **flow contract + reference test harness + 문서**다.

### D2. raw report를 숨기지 않는다

Host UI는 `SaveFlow` report를 직접 소비한다. SG-003 helper나 reference flow가 생기더라도 manager report를
새 error enum으로 덮어쓰지 않는다.

특히 아래 정보는 UI에서 접근 가능해야 한다:

- `save_manual().manager_report`
- `load_manual().recovered_from_backup`
- `load_manual().source`
- `load_manual().restore`
- `list_slots()`의 per-slot failure entry(`parse_error`/`corrupt` 등 raw error)
- `can_save().reason`

### D3. Slot entry shape를 먼저 분기한다

`SaveFlow.list_slots()`는 두 종류의 `ok:false`를 반환할 수 있다.

1. 단일 `manager_unavailable` entry: slot list 전체가 무효다. 설치/초기화 문제로 표시하고 slot count에 넣지 않는다.
2. non-empty `slot_id`를 가진 per-slot failure entry: 해당 slot만 무효다. `error`는 `parse_error`,
   `corrupt` 등 manager가 반환한 raw reason을 보존한다. 다른 정상 slot은 계속 표시한다.

Host UI는 이 두 경우를 같은 "빈 슬롯"처럼 취급하지 않는다.

### D4. Save 버튼 상태와 실제 save 호출은 같은 gate를 쓴다

UI button enable/disabled는 `SaveFlow.can_save(slot_id)`를 소비한다. 실제 `save_manual()`도 내부에서 같은 gate를
다시 호출하므로, 버튼을 누르는 순간 상태가 바뀌어도 fail-closed된다.

`can_save().ok == true`는 manager 가용성을 보장하지 않는다. 저장 클릭 결과는 여전히
`manager_unavailable`일 수 있다.

### D5. Metadata는 표시 권장만 둔다

Core는 metadata key를 해석하지 않는다. Host UI는 아래 권장 key를 우선 표시하되 missing/unknown key에 안전해야 한다.

- `display_name`
- `play_time_seconds`
- `chapter`
- `location`
- `mode`

Unknown metadata는 보존/inspect 가능하지만 기본 card layout이 반드시 모두 표시할 필요는 없다. 권장 표시 key가
없거나 타입이 예상과 달라도 host normalization은 crash 없이 fallback label/value를 만들고 raw metadata를 보존한다.

### D6. Reference 검증은 UI 위젯이 아니라 host flow로 한다

SG-003은 visual UI를 만들지 않으므로 Godot `Control` scene pixel/layout 검증은 하지 않는다.
대신 fake host controller/test double이 아래를 검증한다:

- list state 분류
- save gate 기반 save action 차단
- save/load/delete report passthrough
- backup recovery 안내 데이터 보존
- per-slot failure(`parse_error`/`corrupt` 등) 격리
- missing/unknown/wrong-type metadata fallback

## Host Flow Contract

### Slot List 화면

입력:

```gdscript
var entries := flow.list_slots()
```

분기:

- `entries.size() == 1 && entries[0].ok == false && entries[0].error == &"manager_unavailable"`
  - 전체 실패 상태.
  - SaveGame 설치/초기화 문제로 표시.
  - slot count는 0으로 본다.
- `entry.ok == true`
  - 정상 slot card.
  - `slot_id`, `created_at_unix`, `updated_at_unix`, `metadata`를 표시한다.
- `entry.ok == false`
  - per-slot failure card.
  - `slot_id`와 `error`를 표시하고 load/save overwrite/delete 가능 여부는 host 정책으로 결정한다.

### Manual Save

권장 흐름:

1. UI가 선택 slot에 대해 `flow.can_save(slot_id)`로 버튼 상태를 계산한다.
2. 기존 slot에 덮어쓰기면 host가 confirmation을 띄운다.
3. 사용자가 확정하면 `flow.save_manual(slot_id, caller_metadata)`를 호출한다.
4. 실패 시 `report.error`를 표시하되, 상세/로그 UI는 `report.manager_report`와 `report.gate`를 볼 수 있다.
5. 성공 시 list refresh를 호출한다.

주의:

- `can_save()` 성공 후에도 `save_manual()`은 실패할 수 있다(manager unavailable, capture_failed, metadata invalid 등).
- metadata provider 오류와 gate 오류는 저장을 호출하지 않는 fail-closed다.

### Manual Load

권장 흐름:

1. 사용자가 slot을 선택한다.
2. host가 "현재 진행상태를 잃을 수 있음" confirmation을 띄운다.
3. 확정하면 `flow.load_manual(slot_id)`를 호출한다.
4. 성공 시 게임 화면 전환/메뉴 닫기는 host가 결정한다.
5. `recovered_from_backup == true`면 "백업에서 복구됨" 안내를 표시할 수 있다.

주의:

- `load_manual()`은 save gate를 보지 않는다.
- 실패 시 WorldState adapter의 transactional 보존 또는 manager validate-first 정책에 따라 가능한 범위에서 기존 상태가 보존된다. UI는 실패 report를 숨기지 않는다.

### Delete

권장 흐름:

1. 삭제 confirmation은 host UI가 소유한다.
2. 확정하면 `flow.delete_slot(slot_id)`.
3. 성공/실패와 관계없이 list refresh를 고려한다.

주의:

- `delete_slot()`은 primary와 `.bak`을 모두 제거한다(SG-001 Step 4).
- Delete undo/trash는 범위 밖이다.

## Proposed Artifacts

### Documents

- `SaveGame-User-Guide`: "Host Save Slot UI Integration" 절 추가.
- `addons/save_game/README.md`: SaveFlow 다음에 host UI integration summary 추가.
- `SaveGame-System`: SG-003 완료 후 현재 사실만 요약.
- `Open-Tasks`: 실제 production UI, thumbnail, quick/autosave는 Later 유지.
- `50_Reviews/SG-003-SaveSlot-UI-Host-Integration-Review.md`: Step 0 설계 리뷰 + completion review.

### Optional Test-Only Helper

제품 코드에는 UI helper를 추가하지 않는다. 필요한 경우 테스트 파일 안에만 `FakeSaveSlotHostController`를 둔다.

이 test double은 UI 위젯을 흉내 내지 않고, host가 유지할 수 있는 상태 모델만 만든다:

```gdscript
{
  "list_state": &"ready" | &"manager_unavailable",
  "slot_cards": Array[Dictionary],
  "selected_slot_id": StringName,
  "can_save": Dictionary,
  "last_action": Dictionary,
}
```

이 구조는 테스트 설명용이며 public API가 아니다.

## Step Plan

### Step 0: Design Review

목표:
- SG-003가 UI를 core에 넣지 않으면서 host integration을 충분히 구체화했는지 검토한다.

작업 범위:
- 이 Task 문서와 관련 SaveGame 문서/ADR 검토.
- 필요하면 설계 문서만 수정.

제외 범위:
- 제품 코드, `.tscn`, `.tres` 수정.
- 구현 착수.

완료 조건:
- UI 제외 범위와 host-owned 책임이 명확하다.
- reference 검증 방식이 implementation 가능한 수준으로 정의되어 있다.
- Step 1~3의 완료 조건이 관찰 가능하다.

검증 방법:
- 설계 리뷰 문서 작성.
- 판정: Approved / Approved after design fixes / Rework required.

## Step 0 Design Review Result

설계 리뷰: [[SG-003-SaveSlot-UI-Host-Integration-Review]]

판정: **Approved after design fixes**. 2026-06-18 design fixes 반영 완료:

- P2: `list_slots()`의 per-slot failure contract를 `corrupt` 전용에서 non-empty `slot_id`를 가진
  per-slot failure entry로 확장했다. Host는 raw `error`(`parse_error`/`corrupt` 등)를 보존하고 해당 slot만
  failure card로 격리한다. 단일 `slot_id:&"" + manager_unavailable`만 whole-list failure다.
- P3: metadata fallback을 Step 2 완료 조건에 추가했다. Host normalization은 `{}`/unknown keys/wrong display-key
  types에서 crash 없이 fallback 표시값을 만들고 raw metadata를 보존해야 한다.
- Open Questions 3건은 blocking decision이 아니므로 Resolved Recommendations로 확정했다.

### Step 1: Host Integration Guide

목표:
- 게임 UI 구현자가 `SaveFlow` raw report를 안전하게 소비할 수 있는 문서를 만든다.

작업 범위:
- `SaveGame-User-Guide`에 host save slot UI integration 절 추가.
- `addons/save_game/README.md`에 요약 추가.
- report 소비 matrix 작성:
  - list: normal/per-slot failure(`parse_error`/`corrupt` 등 raw error)/manager_unavailable
  - save: success/save_not_allowed/provider error/manager error/capture_failed/metadata invalid
  - load: success/recovered_from_backup/not_found/parse_error/corrupt/validation_failed
  - delete: success/not_found/invalid id
- metadata display fallback 정책 작성.

제외 범위:
- 새 runtime/product code.
- actual UI scene.
- thumbnail/quicksave/autosave.

완료 조건:
- UI가 어떤 raw report와 key를 소비해야 하는지 문서만 보고 구현할 수 있다.
- `manager_unavailable` 전체 실패와 per-slot failure(`parse_error`/`corrupt` 등)의 의미 차이가 명확하다.
- backup recovery 안내와 load failure 표시 정책이 문서화되어 있다.

검증 방법:
- 문서 정적 검토.
- `rg`로 SG-003/User Guide/README 링크 확인.

### Step 1 Implementation Result

2026-06-18 완료(문서 전용 Step, 제품 코드 변경 없음).

구현:

- [[SaveGame-User-Guide]] §12 "Host Save Slot UI Integration (SG-003)" 신규 절 추가.
  - 12.1 Slot list 분류(whole-list `manager_unavailable` vs non-empty `slot_id` per-slot `parse_error`/`corrupt`),
    12.2 Manual save(can_save 버튼 게이트 + 6키 shape + provider/gate fail-closed 미저장), 12.3 Manual load(gate
    미확인 + `recovered_from_backup`/`source`/`restore` 보존 + raw 실패 reason), 12.4 Delete(host confirmation +
    primary/`.bak` 동시 제거 + refresh), 12.5 Metadata fallback(권장 key + `{}`/unknown/wrong-type fallback +
    raw 보존 + empty slot=host 정책), 12.6 Report consumption matrix(list/save/load/delete 각 row에 host UI
    동작), 12.7 검증 경계.
  - §1 scope에 SG-003 host UI integration 한 줄 추가, frontmatter `updated: 2026-06-18`.
- `addons/save_game/README.md`에 "Host save slot UI 통합 (SG-003)" 요약 절 추가(list 분류/save/load/delete/
  metadata fallback + missing key `.get` 주의 + User Guide §12 cross-reference).
- [[00_Index/Current-State]], [[Open-Tasks]]에 Step 1 완료 반영.

실제 코드 대조(문서가 코드와 일치함을 확인):

- `SaveFlow.list_slots()`(save_flow.gd:135): manager 미해석 시 단일 `{ ok:false, slot_id:&"", error:&"manager_unavailable" }`,
  아니면 `SaveGameManager.list_slots()` passthrough. manager `_read_envelope`(save_game_manager.gd:613) JSON 파싱
  실패=`parse_error`, `json.data` 비-Dictionary 또는 `_extract_slot_meta` 구조 손상=`corrupt`. per-slot entry는
  non-empty `slot_id` 보존.
- `SaveFlow.save_manual()`(save_flow.gd:81): 6키 shape, gate deny=`save_not_allowed`/`save_gate_unavailable`/
  `save_gate_contract_invalid`, metadata provider 오류=`metadata_provider_unavailable`/
  `metadata_provider_contract_invalid`(둘 다 `save_slot` 미호출). manager 오류(`capture_failed`/
  `metadata_not_json_compatible`/`invalid_slot_id`/`backup_failed`/`rename_failed` 등) passthrough.
- `SaveFlow.load_manual()`(save_flow.gd:117): `manager.load_slot` passthrough. **report 키가 실패 종류별로 다름**
  (save_game_manager.gd:415) — `manager_unavailable`는 `slot_id` 없음, `slot_not_found`/`invalid_slot_id`는
  `recovered_from_backup`/`source`/`restore` 없음, read-fail(`parse_error`/`corrupt`)은 `recovered_from_backup:false`만,
  success/`validation_failed`는 전체 키. 문서에 `report.get(key, default)` 소비를 명시.
- `SaveFlow.delete_slot()`(save_flow.gd:125): `manager.delete_slot`(save_game_manager.gd:511) passthrough —
  success/`slot_not_found`/`invalid_slot_id`/`delete_failed`/`saves_dir_unavailable`, `manager_unavailable`는
  `slot_id` 없음. primary+`.bak` 동시 제거.

검증:

- 문서 정적 검토 + `rg`로 핵심 용어 반영 확인(아래 Verification 절).
- 제품 코드(`.gd`/`.tscn`/`.tres`) 변경 없음(`git diff` 확인).
- Godot headless는 문서 전용 Step이라 실행하지 않음.

완료 조건 대조:

- UI가 소비할 raw report/key를 문서만 보고 구현 가능: §12.1~12.6 matrix로 충족.
- `manager_unavailable` 전체 실패 vs per-slot failure(`parse_error`/`corrupt`) 의미 차이 명확: 12.1 + list matrix.
- backup recovery 안내·load failure 표시 정책 문서화: 12.3 + load matrix.

### Step 2: Reference Host Flow Test

목표:
- Step 1 문서의 host flow가 실제 `SaveFlow` reports 위에서 동작 가능한지 제품 UI 없이 검증한다.

작업 범위:
- `addons/save_game/tests/sg003_step2_host_flow_test.gd`/`.tscn` 추가.
- 테스트 파일 내부 fake host controller/test double 추가.
- 실제 `SaveFlow + SaveGameManager`를 사용해 list/save/load/delete 흐름을 검증.

제외 범위:
- product helper 추가.
- Control scene, theme, localization, input focus.
- WorldState adapter 통합(필요 시 기존 SG-002 Step 2 회귀로 충분).

완료 조건:
- manager unavailable list 전체 실패를 slot count 0으로 분류.
- `parse_error`와 `corrupt` slot은 모두 per-slot failure로 격리하고 정상 slot 표시를 막지 않는다.
- `{}`/unknown keys/wrong display-key types metadata를 host normalization이 crash 없이 fallback 처리하고 raw
  metadata를 보존한다.
- save gate deny/unavailable/contract invalid가 save action을 fail-closed하고 manager save를 호출하지 않는다.
- `save_manual` 실패/성공 report의 6키 shape를 host state가 보존한다.
- `load_manual`의 `recovered_from_backup/source/restore`가 host state에 보존된다.
- delete 후 list refresh 흐름이 동작한다.

검증 방법:
- Godot headless `sg003_step2_host_flow_test`.
- 회귀: SG-002 step1 save_flow test, SG-001 step2/step4 slot/backup test.
- `--import` 0 parse error.

### Step 2 Implementation Result

2026-06-18 완료(docs-only 제약 해제 후 사용자 승인하에 진행).

구현:

- `addons/save_game/tests/sg003_step2_host_flow_test.gd`/`.tscn` 신규(`.gd.uid`는 `--import`가 생성).
- 테스트 파일 내부 test-only `FakeSaveSlotHostController`(`extends RefCounted`) 추가 — UI 위젯이 아니라
  Task의 상태 모델(`list_state`/`slot_cards`/`selected_slot_id`/`can_save_state`/`last_action`)만 흉내 낸다.
  제품 코드/helper 추가 없음(Resolved Recommendation 1 준수). `refresh_list`/`_classify_entry`/
  `_normalize_metadata`/`select`/`do_save`/`do_load`/`do_delete`/`find_card`로 §12 contract를 소비한다.
- 실제 `SaveFlow + SaveGameManager`(+ `SpyManager`로 save_slot 호출 횟수 추적)와 `GateProvider`/
  `NoMethodGate` duck-type provider로 list/save/load/delete + gate + metadata fallback을 검증.

검증(Godot 4.6.3 headless):

- `sg003_step2_host_flow_test` **ALL PASS**(A~H):
  - A: whole-list `manager_unavailable`(단일 `slot_id:&""`) → `list_state=manager_unavailable`, slot count 0.
  - B: 정상 slot + `parse_error`(깨진 JSON) + `corrupt`(non-Dict JSON `[1,2,3]`)를 같은 디렉터리에 두고
    refresh → 정상은 `normal` card, 두 손상은 raw error 보존 `failure` card로 격리(정상 비차단).
  - C: `{}`/unknown key/wrong-type(display_name=int, play_time=String, chapter=int) metadata에서 crash 없이
    display_name=slot_id·play_time="—"·chapter="" fallback + raw metadata 보존(JSON round-trip float 포함).
  - D: gate deny(`save_not_allowed`)/unavailable(`save_gate_unavailable`)/contract invalid
    (`save_gate_contract_invalid`)가 모두 `SpyManager.save_calls==0`(save_slot 미호출)·파일 미작성으로
    fail-closed, last_action 6키 보존.
  - E: save 성공(`error:&""`, manager_report.path 보존)/실패(`manager_unavailable`, metadata/manager_report
    `{}`) 모두 last_action 6키 보존.
  - F: primary 손상 후 load가 `recovered_from_backup=true`/`source=&"backup"`/non-empty `restore`를 host
    state에 보존, bak 값(v1) 복원.
  - G: `slot_not_found`(누락 키는 `.get` 기본값 안전)·`parse_error`(`recovered_from_backup=false`) raw reason 보존.
  - H: delete 후 refresh로 card 제거 + 파일 제거, 재삭제 시 `slot_not_found` + refresh 동작.
- 회귀 **ALL PASS**: `sg002_step1_save_flow_test`, `sg002_step1_static_guard_test`,
  `sg001_step2_slot_store_test`, `sg001_step4_backup_test`, `sg001_step1_core_test`,
  `sg001_step1_static_guard_test`.
- `--import` 0 parse error(헤드리스 editor load 성공, 신규 `.gd.uid` 생성). leaked/in-use 경고는 clean
  import에도 나타나는 benign shutdown noise.

완료 조건 대조: A~H가 Step 2 완료 조건 7개(manager unavailable count 0 / parse_error+corrupt 격리 /
metadata fallback+raw 보존 / gate fail-closed save_slot 미호출 / save 6키 / load recovered/source/restore /
delete 후 refresh)를 각각 충족.

### Step 3: Completion Review and Follow-up Sorting

목표:
- SG-003 문서/테스트를 완료 판정하고 다음 SaveGame 후속 우선순위를 정리한다.

작업 범위:
- `SG-003 Review` 작성.
- `SaveGame-System`, `Current-State`, `Open-Tasks`, `Home` 갱신.
- Later 항목을 정리:
  - actual production save menu UI
  - quicksave/autosave policy
  - thumbnail/capture image
  - Dialogue SaveEffect
  - schema migration registry

제외 범위:
- 새 기능 구현.

완료 조건:
- SG-003 Step 1~2 완료 조건이 리뷰에서 대조되어 있다.
- 후속 작업이 Next/Later에 명확히 남아 있다.
- SG-003 Task status가 complete로 닫힌다.

검증 방법:
- 문서 링크/상태 정적 검토.
- Step 2 테스트 및 관련 회귀 재실행 결과 기록.

### Step 3 Implementation Result

2026-06-18 완료. **SG-003 전체 완료(Step 1~3)**.

구현:

- 완료 리뷰: [[SG-003-SaveSlot-UI-Host-Integration-Review]] "Completion Review (Step 1~3)" 추가, review
  status `design-approved` → `complete`, 판정 **완료**(Step 1~2 완료 조건 대조, 후속 정리).
- Index/System 갱신: [[00_Index/Current-State]] SG-003 Step 1·2 완료 반영, [[Open-Tasks]] SG-003을 Step 3
  대기에서 완료로 정리, [[SaveGame-System]] SG-003 절 + 검증 목록 + 미구현 후속 갱신. [[00_Index/Home]]은
  SG-003 Task/Review 링크가 이미 존재해 변경 없음.
- Task status `approved` → `complete`.

검증(재실행, Godot 4.6.3 headless):

- `sg003_step2_host_flow_test` ALL PASS.
- 회귀 ALL PASS: `sg002_step1_save_flow_test`, `sg002_step1_static_guard_test`,
  `sg001_step2_slot_store_test`, `sg001_step4_backup_test`.
- `--import` 0 parse error.

완료 조건 대조: Step 1~2 완료 조건이 리뷰 Completion Review에 대조됨, 후속(production UI/quicksave/autosave/
thumbnail/Dialogue SaveEffect/migration registry/world_core 패키징)은 [[Open-Tasks]] Later 유지, Task status
complete 마감.

## Resolved Recommendations

1. Step 2 fake host controller는 테스트 파일 내부에만 둔다. `addons/save_game/examples/` sample script는 public
   API처럼 오해될 수 있으므로 만들지 않는다.
2. Per-slot failure overwrite 허용/금지 문구는 host 정책으로 남긴다. Core는 같은 slot id로 새 파일을 쓸 수 있지만,
   confirmation copy와 위험 표시 수준은 게임마다 다르다.
3. Empty slot 개념은 core에 추가하지 않는다. Empty grid size/name은 UI 정책이며 `SaveFlow.list_slots()`는 실제
   저장된 slot만 나열한다.

## Verification Matrix

| 영역 | 정상 | 실패/경계 |
| --- | --- | --- |
| Slot list | normal slot cards | manager_unavailable 전체 실패, per-slot `parse_error`/`corrupt` |
| Save | gate allow + metadata + success refresh | gate deny/unavailable/contract invalid, capture_failed, invalid metadata |
| Load | success | recovered_from_backup, slot_not_found, parse_error, corrupt, validation_failed |
| Delete | delete + refresh | invalid/missing slot report passthrough |
| Metadata | recommended keys displayed | missing/unknown/wrong-type display keys fallback + raw metadata preserved |
| UI boundary | host-owned state model | no production UI scene/theme/localization |
| Regression | SG-002 SaveFlow, SG-001 slot/backup | parse/import errors |

## Related

- [[SG-001-SaveGame-Core-Section-System]]
- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]
