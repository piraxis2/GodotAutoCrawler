---
id: SG-001-Review
type: review
task: SG-001
status: completed
date: 2026-06-17
system: SaveGame, WorldState
---

# SG-001 SaveGame Core and Section System Review

SG-001 Step 0(설계)~Step 5(문서/완료)의 단계별 리뷰 결과와 완료 판정을 통합 기록한다. 단계별 구현/검증
증거는 [[SG-001-SaveGame-Core-Section-System]], 현재 사실은 [[SaveGame-System]], 사용법은
[[SaveGame-User-Guide]]를 참고한다.

## 단계별 판정

| Step | 내용 | 판정 |
| --- | --- | --- |
| Step 0 | 설계 리뷰 + ADR-013 | Approved after design fixes |
| Step 1 | in-memory core (SaveSection / SaveGameManager) | 수정 후 완료 (리뷰 P1 JSON 호환, P2 등록 후 식별자, 2차 P1 고유 rename 모두 수정) |
| Step 2 | 파일 slot store | 수정 후 완료 (P1 section_version 정수형, P2 corrupt 로그/ list 구조 손상 격리 수정) |
| Step 3 | WorldStateSaveSection 통합 | 수정 후 완료 (P2 duck-type 반환 shape 방어 수정) |
| Step 4 | backup/recovery | 수정 후 완료 (P1 손상 primary가 good bak 미덮음, P2 손상 bak 실제 원인 보고 수정) |
| Step 5 | 패키징/User Guide 문서 + 완료 | 완료 |

## 완료 조건 대조

- 재사용 가능한 domain-free core: `addons/save_game/`의 `SaveSection`/`SaveGameManager`가 WorldState/
  DialogueTool을 직접 참조하지 않음(정적 가드 테스트 `sg001_step1_static_guard_test` 통과).
- section 등록/검증: 명시 `register_section` 1순위 + 보조 `discover_sections`, 빈/중복 id·invalid version 거부,
  등록 후 식별자 변경 재검증(`sections_invalid`).
- envelope/version: `save_version`/`section_version`/payload `schema_version` 3계층 분리, 정수형 number 강제,
  JSON 호환 payload 재귀 검증.
- load transaction: validate-all 통과해야 restore 시작(validate 실패 시 restore 0회), 중간 실패 시
  `partial_restore` report.
- 파일 slot: `user://saves/<slot>.json` save/load/list/delete, slot_id 패턴, atomic write(tmp+rename),
  missing/corrupt 보고, per-slot corrupt isolation.
- 백업: 한 세대 `.bak` 회전(손상 primary는 good bak 미덮음), load 복구(`recovered_from_backup`), delete가
  primary+bak 제거.
- WorldState 통합: `WorldStateSaveSection`(별도 `addons/save_game_world_state/`)이 capture(ready 선확인)/
  validate(`peek_world_state_compatibility`)/restore(`restore_world_state`)를 duck-type으로 연결,
  반환 shape 위반은 `runtime_contract_invalid` fail-closed. WorldStateRuntime은 SaveGame 역의존 0.
- 파일 round-trip 타입 보존: WorldStateStore `import_snapshot`이 JSON wire(정수형 float/String↔StringName)를
  schema 타입으로 복원.

## 검증

헤드리스(Godot 4.6.3), `--import` exit 0 / parse error 0:

- `sg001_step1_core_test` (A~Z, 26) ALL PASS
- `sg001_step1_static_guard_test` ALL PASS (core domain 참조 0)
- `sg001_step2_slot_store_test` (A~L, 12) ALL PASS
- `sg001_step3_world_state_section_test` (A~F, 6) ALL PASS
- `sg001_step4_backup_test` (A~H, 8) ALL PASS
- 회귀: `dt006_step3_lifecycle_test` / `dt006_step4_adapter_test` ALL PASS (WorldStateRuntime 변경은
  `peek_world_state_compatibility` 추가뿐, 무회귀)

## 남은 위험 / 후속

- 다중 section 전역 rollback 없음(validate-first로 위험 완화, WorldState 자체는 transactional).
- 백업 한 세대만 유지.
- save slot UI, autosave, compression/encryption, schema migration registry는 범위 밖.
- `addons/world_core/` umbrella 패키징 이동은 별도 Task(ADR-013 migration trigger 충족 시).

## 판정

**SG-001 완료.** Step 1~4 수정 후 완료, Step 5(문서) 완료. 전체 SaveGame core/section/slot/backup/WorldState
adapter 경계가 완료 조건을 만족하며, 2026-06-17 completion review에서 단계별 회귀(`sg001_step1~4` +
`sg001_step3_world_state` + DT-006 step3/step4 ALL PASS, `--import` 0 parse error)를 재확인했다.
지적된 문서 상태 불일치(P2)는 본 리뷰 반영 시 해소했다. 후속 항목은 모두 SG-001 범위 밖으로 분리된다.

## Related

- [[SG-001-SaveGame-Core-Section-System]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
