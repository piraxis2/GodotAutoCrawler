---
id: SG-003-Review
type: review
task: SG-003
status: complete
date: 2026-06-18
---

# SG-003 Save Slot UI Host Integration Review

이 문서는 SG-003의 Step 0 설계 리뷰와 이후 completion review를 함께 보존한다.

## Step 0 설계 리뷰

검토 범위: [[SG-003-SaveSlot-UI-Host-Integration]], [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]],
[[SaveGame-System]], [[SaveGame-User-Guide]], `SaveFlow`, `SaveGameManager`.

## Findings

### [P2] per-slot failure contract가 `corrupt`로만 좁게 표현돼 있다

- 조건: Task가 `list_slots()`의 per-slot 실패를 주로 `corrupt` entry로 설명한다.
- 실제 코드: `SaveGameManager.list_slots()`는 `_read_envelope()` 실패를 그대로 entry로 반환할 수 있고,
  이때 `error`는 `parse_error`일 수 있다. `_extract_slot_meta()`의 구조 손상은 `corrupt`다.
- 영향: host UI가 `parse_error`를 알 수 없는 전역 실패로 처리하면, 한 slot만 손상된 상황에서 전체 목록 UX가
  무너질 수 있다.
- 권장: list 분류를 다음처럼 확정한다.
  - 단일 `ok:false, slot_id:&"", error:&"manager_unavailable"` = whole-list failure.
  - non-empty `slot_id`를 가진 모든 `ok:false` entry = per-slot failure card. Raw `error`
    (`parse_error`/`corrupt` 등)를 보존한다.

### [P3] metadata fallback이 계획에는 있지만 테스트 shape가 부족하다

- 조건: D5와 Step 1은 metadata fallback을 말하지만, Step 2 완료 조건에는 missing/unknown/wrong-type metadata
  host normalization 검증이 없다.
- 영향: 문서상으로는 core가 metadata를 해석하지 않는다고 되어 있으나, 실제 host flow 예시가 display key 타입
  오류에서 깨지지 않는지 보장하지 못한다.
- 권장: Step 2에 `{}`/unknown keys/wrong display-key types에서 crash 없이 fallback 표시값을 만들고 raw metadata를
  보존하는 assertion을 추가한다.

P0/P1 없음.

## Open Decisions

Blocking decision 없음. 기존 Open Questions는 아래 권장안으로 확정해도 된다.

- fake host controller는 테스트 내부에만 둔다. 문서용 sample script는 public API 오해 위험이 있다.
- per-slot failure overwrite 정책은 host 정책으로 남긴다.
- empty slot 개념은 core에 추가하지 않는다.

## Step Assessment

- Step 1 Host Integration Guide: 독립 문서 Step으로 적절하다. `parse_error`를 per-slot list/load failure로 포함하고
  metadata fallback을 명시하면 충분하다.
- Step 2 Reference Host Flow Test: production UI 없이 검증 가능하다. 실제 `SaveFlow + SaveGameManager`와
  test-only fake host controller로 list/save/load/delete flow를 검증하면 된다.
- Step 3 Completion Review: 문서/테스트/status closeout으로 적절하다. Step 2와 합치지 않는 것이 현재 workflow와 맞다.

## Verification Assessment

필요한 검증:

- User Guide/README/System/Open-Tasks 링크 정적 검토.
- Godot headless `sg003_step2_host_flow_test`.
- 회귀: SG-002 Step 1, SG-001 Step 2/Step 4, `--import`.

추가해야 할 실패 커버리지:

- `list_slots()` per-slot `parse_error`와 structural `corrupt`.
- missing/unknown/wrong-type display metadata fallback.
- 선택 사항: `validation_failed` load failure에서 host가 report/restore 정보를 보존하는지 확인.

## Verdict

**Approved after design fixes**.

설계는 ADR-014와 정합적이다. reusable production UI를 만들지 않고, raw report를 숨기지 않으며,
`SaveFlow` 책임도 drift하지 않는다. 구현 전 필요한 설계 수정은 per-slot failure 용어 확장과 metadata fallback
테스트 조건 추가다.

## Design Fixes Applied

2026-06-18 Task에 반영 완료:

- per-slot `corrupt` 표현을 non-empty `slot_id`를 가진 per-slot failure(`parse_error`/`corrupt` 등 raw error
  보존)로 확장.
- Step 1 matrix와 Step 2 완료 조건에 `parse_error` per-slot failure 추가.
- metadata fallback 테스트 조건 추가.
- Open Questions를 Resolved Recommendations로 전환.

## Completion Review (Step 1~3)

2026-06-18 SG-003 Step 1~3 구현·검증 완료. **판정: 완료**.

### Step 1 (Host Integration Guide) 완료 조건 대조

- UI가 어떤 raw report/key를 소비해야 하는지 문서만 보고 구현 가능 — [[SaveGame-User-Guide]] §12
  (12.1 slot list 분류 ~ 12.6 report consumption matrix)로 충족. README에 요약 + §12 cross-reference.
- whole-list `manager_unavailable`(`slot_id:&""`) vs per-slot failure(non-empty `slot_id`, raw
  `parse_error`/`corrupt`) 의미 차이 명확 — §12.1 + list matrix.
- backup recovery 안내·load failure 표시 정책 문서화 — §12.3 + load matrix(키가 실패 종류별로 다르고
  `report.get(key, default)` 소비를 명시).
- Step 0 리뷰 P2(per-slot failure 용어 확장)·P3(metadata fallback) 모두 반영. 후속 코드 리뷰 P3(기존 §6/§8/Task
  상단 `corrupt` 전용 표현)도 per-slot failure(`parse_error`/`corrupt`)로 정정.

### Step 2 (Reference Host Flow Test) 완료 조건 대조

`addons/save_game/tests/sg003_step2_host_flow_test`(테스트 내부 `FakeSaveSlotHostController`, 제품 helper 0)로
실제 `SaveFlow + SaveGameManager` 위에서 검증:

- whole-list `manager_unavailable`를 `list_state` + slot count 0으로 분류(A).
- `parse_error`와 `corrupt` slot을 per-slot failure로 격리하고 정상 slot 표시 비차단(B).
- `{}`/unknown/wrong-type metadata를 crash 없이 fallback 처리 + raw metadata 보존(C).
- save gate deny/unavailable/contract invalid가 fail-closed하고 `SpyManager.save_calls==0`(save_slot 미호출)(D).
- `save_manual` 성공/실패 report 6키 shape를 host state가 보존(E).
- `load_manual`의 `recovered_from_backup`/`source`/`restore` + raw 실패 reason 보존(F, G).
- delete 후 list refresh(H).

### 검증 결과(재실행, Godot 4.6.3 headless)

- `sg003_step2_host_flow_test` **ALL PASS**(A~H).
- 회귀 **ALL PASS**: `sg002_step1_save_flow_test`, `sg002_step1_static_guard_test`,
  `sg001_step2_slot_store_test`, `sg001_step4_backup_test`(추가로 `sg001_step1_core_test`/static guard도 PASS).
- `--import` 0 parse error. leaked/in-use 경고는 clean import에도 나타나는 benign shutdown noise.

### Verdict

**완료**. P0/P1/P2 없음. SaveGame core는 production UI를 제공하지 않고, host UI는 `SaveFlow` raw report를 직접
소비하는 contract(문서 + test-only fake host flow)로 완결됐다. `SaveFlow`/manager 책임 drift 없음, 제품 코드
변경은 테스트 파일뿐. SG-003 Task status를 complete로 닫는다.

### 후속(범위 밖, Open-Tasks 유지)

- 실제 production save menu UI scene/theme/localization/input focus.
- quicksave/autosave 정책.
- thumbnail/capture image.
- Dialogue SaveEffect 노드(저장 트리거는 game/event layer 우선).
- schema/section version migration registry.
- `addons/world_core/` umbrella 패키징 이동(ADR-013 trigger).

## Related

- [[SG-003-SaveSlot-UI-Host-Integration]]
- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[SG-001-SaveGame-Core-Section-System]]
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]
- [[SaveGame-User-Guide]]
- [[SaveGame-System]]
