---
id: BT-003-completion-review
type: review
status: complete
updated: 2026-07-15
system: BehaviorTree
---

# BT-003 Tactic Board Resource/Validation/Compiler — Completion Evidence

## Status

**완료.** Step 4 구현 증거를 코드 리뷰로 대조했고, P0/P1/P2 발견 사항 없이 BT-003 완료로 판정한다.

설계 계약은 [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]](`accepted`), 설계 리뷰는 [[BT-003-Tactic-Board-Resource-Validation-Compiler-Review]]다.

## Review Result

- 발견 사항: 없음.
- 판정: **완료**. Step 1~4 완료 조건과 Minimum Verification Matrix가 자동 증거로 연결되어 있고, atomic apply/교체/실패 보존/death·tree exit teardown 경계에서 Step 진행을 막는 결함을 찾지 못했다.
- 남은 경계: BattleSession/BT-004 UI가 실제 전투 시작 전에 `SealTacticBoardForBattle()`를 호출하고 stale/revert UX를 노출하는 작업은 후속이다.

## Step별 증거

| Step | 구현/증거 | 검증 | 구현 상태 |
|---|---|---|---|
| 0 | OD1~12 확정, ADR-024 accepted | 정적 코드 대조 | 설계 승인 |
| 1 | versioned draft/typed definitions/action catalog `.tres` 왕복 | `bt003_step1_draft_roundtrip_test` 60 assertions | 완료 |
| 2 | pure validator, stable structured diagnostics, system row/catalog 규칙 | `bt003_step2_validator_test` 61 assertions | 완료 |
| 3 | 정규 BT-002 subtree compiler, fresh per-unit action, signature/snapshot | `bt003_step3_compiler_test` 49 assertions; 실제 전장 parity 17 assertions | 완료 |
| 4 | atomic installer, apply state/gate, replacement, death/tree exit teardown | `bt003_step4_apply_lifecycle_test` 51 assertions | 완료 |

## Step 4 구현 사실

- `BehaviorTree.InstallRoot(BehaviorTree_Node)`는 detached root만 받아 child-0에 설치하고, subtree의 `Tree`를 동기 연결한다. 설치/기존 root 제거 중 `ChildOrderChanged` 갱신을 억제해 구조 update를 한 번만 내보낸다. 성공 뒤에만 이전 택틱 root를 reset/free한다.
- `BehaviorTree.RemoveInstalledRoot(expectedRoot)`는 apply owner가 설치한 동일 identity의 root만 제거한다. 수제 BT를 임의로 지우지 않는다.
- `TacticBoardApplyState`는 유닛별 last-good `CompiledBoardSnapshot`, preparation gate, applied `CompileInputSignature` 비교를 소유한다. null/실패 compile, parented snapshot, 다른 unit snapshot, sealed preparation, 실행 중 action은 fail-closed이며 기존 root/metadata를 보존한다.
- `CharacterArticle`은 `TryApplyCompiledTacticBoard`, `BeginTacticBoardPreparation`, `SealTacticBoardForBattle`, signature query seam을 제공한다. 사망은 deferred teardown으로 root/action binding을 해제하고, tree exit은 Godot 부모 정리에 맡기되 C# metadata/binding을 즉시 잊는다.
- BattleSession이 preparation gate를 실제로 열고 닫는 consumer 배선과 전투 전 UI는 범위 밖이다. 이 Step은 그 소비자가 사용할 runtime seam만 제공한다.

## Minimum Verification Matrix 대응

| Matrix | 자동 증거 |
|---|---|
| 1~7 | Step 1 round-trip과 Step 2 validator: Resource 보존, invalid/future/null/duplicate, system rows, warning과 resolver diagnostics |
| 8~12 | Step 3 compiler: 정규 structure/RowId/policy, 결정론 signature, two-unit action isolation, factory/validation failure cleanup |
| 13 | Step 4 B: apply 직후 `Root`/`Tree`/structure event가 동기 준비되고 deferred frame 없이 실제 공격 |
| 14~16 | Step 4 C/D/F: A→B→A recompile, old root/binding free, failed/foreign/parented/sealed apply가 last-good 보존 |
| 17 | Step 4 F/G: 실제 death deferred teardown 및 tree exit에서 snapshot root/action binding/state 정리 |
| 18 | `bt_validation_test`, BT-002 Step 1~4, SK-004, BS-001 Step 1/3 회귀 통과 |
| 19~23 | Step 3 battle fixture: candidate supply, EnemyCasting+NearestEnemy, invalid context 차단, default/wait 하한 |
| 24~27 | Step 3 compiler + Step 4 E/F: fresh action identity, catalog 유지, resolver failure가 기존 적용본을 훼손하지 않음 |
| 28 | Step 3 signature + Step 4 B/C: catalog revision을 포함한 `CompileInputSignature` current/stale query |

## Verification

- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- Godot `--headless --import`: exit 0. clean import에는 기존 `ObjectDB instances leaked`와 resource 11건 경고가 재현된다.
- `bt003_step4_apply_lifecycle_test`: 51 assertions ALL PASS. 동일 fixture 3회 반복에서 exit 0, `ObjectDB instances leaked`/`resources still in use` 경고 0줄.
- 영향권 회귀 ALL PASS: `bt_validation_test`, BT-003 Step 1~3, BT-002 Step 1~4, SK-004 Step 1, BS-001 Step 1/3.
- fail-closed 테스트가 의도한 `GD.PushError`와 기존 BT editor warning은 종료 실패가 아니며 각 fixture가 ALL PASS로 종료했다.
- 리뷰 재검증(2026-07-15): `dotnet build AutoCrawler.sln -c Debug` 경고 0/오류 0, `bt003_step1_draft_roundtrip_test`, `bt003_step2_validator_test`, `bt003_step3_compiler_test`, `bt003_step3_compile_battle_test`, `bt003_step4_apply_lifecycle_test` 모두 ALL PASS. Step 3 fixture의 ObjectDB 경고는 기존 문서화된 baseline 경고로 재현됐다.

## Remaining Boundaries

- **소비자 배선**: BattleSession/BT-004 UI가 실제 전투 시작 직전에 `SealTacticBoardForBattle()`를 호출하는 경로는 후속이다. 현재는 API와 headless seam만 있다.
- **lifetime 경고 범위**: Step 4 fixture의 snapshot 소유 Node/action은 반복 실행에서 경고 없이 정리됐다. clean import와 이전 Step 3 fixture에서 보이는 baseline ObjectDB/resource 경고를 BT-003 runtime leak 0으로 일반화하지 않는다.

## Related

- [[BT-003-Tactic-Board-Resource-Validation-Compiler]]
- [[BT-003-Tactic-Board-Resource-Validation-Compiler-Review]]
- [[ADR-024-Tactic-Board-Draft-Compile-Snapshot]]
- [[BehaviorTree-System]]
- [[STEP_REVIEW_WORKFLOW]]
