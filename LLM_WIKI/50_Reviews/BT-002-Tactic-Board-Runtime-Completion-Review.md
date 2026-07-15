---
id: BT-002-completion-review
type: review
status: complete
updated: 2026-07-14
system: BehaviorTree
---

# BT-002 Tactic Board Runtime Extension — Completion Review

## Verdict

**완료.** Step 0~4 전부 완료. P0/P1 잔여 없음. 최소 실행 계약 R1~R9와 Minimum Verification Matrix 14항목이 코드와 테스트로 고정됐다.

설계 계약: [[ADR-023-Tactic-Board-Runtime-Execution-Contract]](accepted). 설계 리뷰: [[BT-002-Tactic-Board-Runtime-Extension-Review]].

## Step별 증거

| Step | 내용 | 테스트 | 판정 |
|---|---|---|---|
| 0 | 설계 리뷰, OD1~8 확정, ADR-023 accepted | 정적 대조 | Approved after design fixes |
| 1 | 행 우선순위, `Running` 행 latch, commit 후 턴 종료, turn reset | `bt002_step1_priority_lifecycle_test` 54 | 완료 |
| 2 | 조건 후보 교집합, 확정 대상 공유, ADR-017 tie-break, freed 후보 제외 | `bt002_step2_condition_target_test` 33 | 완료 |
| 3 | 접근 정책(Immediate/ApproachAllowed), commit 경계, 최종 대기 | `bt002_step3_approach_action_test` 43 | 완료 |
| 3.5 | 실제 전투 어댑터, `Mobility+1` feasibility, 제품 어휘, 멀티턴 재개 | `bt002_step35_battle_fixture_test` 40 | 완료 |
| 4 | RowId debug payload, 실제 사망/teardown reset, 두 유닛·두 전투 격리, 구조 validation | `bt002_step4_lifecycle_test` 61 | 완료 |

## Minimum Verification Matrix 대응

1. 첫 행 성립 → 첫 행 실행 — Step 1 A
2. 첫 행 불성립 → 두 번째 행 — Step 1 B
3. `Running` 중 조건 변화 → 첫 행 유지 — Step 1 C, Step 3 V(접근 Running)
4. `Immediate` 대상이 멂 → 접근 없이 다음 행 — Step 3 S, Step 3.5 E(실제 row, 1칸 부족)
5. `Immediate` 이동 후 사거리 내 → 같은 턴 행동 — Step 3 T, Step 3.5 C/E
6. `ApproachAllowed` 미도달 → 접근으로 턴 종료, 다음 행 미실행 — Step 3 U
7. 두 대상 조건 → 같은 유닛이 모두 만족할 때만 — Step 2 L/M
8. 여러 후보 → 거리→Y→X — Step 2 O1~O3
9. commit 후 대상 무효 → 다음 행 미실행 — Step 3 Z4
10. 모든 행동 불가 → wait — Step 3 S, Step 3.5 E
11. 다음 턴 → latch/대상 제거 후 첫 행부터 — Step 1 E/H, Step 3.5 D/H
12. 동일 구조 두 유닛/두 전투 → context 격리 — 두 유닛: Step 1 F, Step 4 C. **두 전투: Step 4 F** — 전투 1에서 latch/문맥/바인딩을 만들고 유닛을 사망시킨 뒤 teardown하고, 전투 2를 새로 생성해 초기 상태(빈 latch/문맥)와 독립 실행을 확인한다. 두 보드가 **같은 `TurnAction` 리소스를 공유**하게 해 리소스에 남은 바인딩이 새지 않는지도 본다.
13. 성립 검사 → mutation 없음 — Step 3 S, Step 3.5 B(위치·타일 점유·마나·탄약 불변)
14. teardown → stale Node/context 없음 — **Step 4 E**: 실제 접근 중(latch/문맥/이동 tween 보유) 상태에서 HP 0 → `ArticleBase.Dead()` → `OnDead` → `ResetTacticRuntime()`으로 latch·문맥 대상·행동 바인딩·`CurrentTurnAction`/State·이동 tween이 모두 정리됨을 확인한다.

## 핵심 설계 결정(구현 확정)

- **전역 `BtStatus` 미확장**: 택틱 내부 결과를 selector 경계에서 변환. 기존 composite/editor/debugger 무회귀.
- **latch 소유**: selector가 `Running` 행을 latch하며 **action과 approach 두 단계 모두**를 덮는다. approach는 `CurrentTurnAction`을 거치지 않으므로 이 latch가 유일한 preempt 방어다.
- **turn reset**: `OnInit`이 아니라 실제 턴 경계(`ApplyTurnStartEffects`)에서 `ResetForNewTurn()`을 명시 호출한다. 멀티턴 action이 `CurrentTurnAction`으로 BT tick을 우회해 트리가 Running으로 남는 경로를 닫는다.
- **per-action feasibility 바인딩(§10)**: 행의 행동이 사거리·`CanStart`를 컨텍스트에 공급하고 어댑터가 전장을 담당한다. 근접/스킬마다 올바른 사거리가 적용된다.
- **fail-closed**: commit 규칙을 우회할 수 있는 구조(비택틱 selector/시퀀스 자식, 비택틱 행동, 셀렉터 위치 오류, Running 조건)는 실행하지 않는다. 같은 규칙을 `BehaviorTreeValidation`이 편집 시점에도 표면화한다.
- **확정 대상 공유**: `TurnActionBase`의 명시적 바인딩을 근접 `GetTarget`과 스킬 `SelectTarget`이 모두 소비한다. 무효 대상은 **fallback 금지**.

## 검증

- `dotnet build -c Debug`: 경고 0 / 오류 0. Godot `--import` 성공.
- BT-002 전용 테스트 5종 전부 PASS(총 231 단언).
- 회귀 17종 ALL PASS: BT-001, CB-001 Step1~3, SK-002 Step1/3, SK-004 Step1, ST-001 Step1, BS-001 Step1/3, BS-002 Step1, BS-003 Step1.

## 알려진 한계 / 후속

- **선행 실패(BT-002와 무관)**: `sk001_step1/2/3/5/6`, `cb001_step4`, `st001_step2`는 전장 로스터 개편·씬 데이터 drift로 이미 깨져 있다([[battlefield-skill-test-gotchas]]). rebaseline은 별도 소관이다.
- **종료 시 ObjectDB leak(실측)**: 택틱 어댑터는 이동 tween을 **하나만** 소유하고 Kill+Dispose로 즉시 반납한다. **Tween/SceneTreeTimer 누수는 해소**됐다 — `bt002_step4_lifecycle_test`는 반복 실행에서 **leak 0**이다. `bt002_step35_battle_fixture_test`는 실행에 따라 **`AudioStreamMP3`/`AudioStreamPlaybackMP3`/`AudioStreamPlaybackPolyphonic` 4건**이 남을 수 있다(공격 SFX가 재생 중인 상태로 종료되는 경우). 이는 기존 전투 오디오의 수명 주기 문제이며 택틱 런타임 밖이다. 전투 VFX(`damage_floater.gd`의 SceneTree tween)는 fixture가 FxPlayer를 flush하고 실제 시간을 준 뒤 teardown하도록 해 닫았다.
- **씬 BT 배선 회귀 가드 부재(후속)**: 완료 리뷰 중 `SkillCaster.tscn`의 `StaffMagicBolt`가 루트 Selector 밖으로 reparent된 상태가 발견됐다(그래프 에디터 드래그 저장으로 추정, `git checkout`으로 되돌림). 이 단절은 **어떤 테스트도 잡지 못했다** — `sk004_step1_usable_attack_test`는 `HasUsableAttack`/사용 가능 행동 **조회**만 보는데, 그 조회는 `ITurnActionProvider` raw 노드 워크라 부모와 무관하게 노드를 찾는다. 반면 실제 **실행**은 `BehaviorTree.Root` 하위만 탄다. 이 비대칭(조회는 raw walk, 실행은 Root subtree)을 잡는 구조/행동 회귀 가드가 필요하다.
- **후속 Task**: 택틱보드 Resource/validator/compiler와 compiled snapshot, 편집·검증·적용 UI, H-4 전체 어휘와 행별 발동 통계, DungeonRun 층 준비 연결.

## Related

- [[BT-002-Tactic-Board-Runtime-Extension]]
- [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]
- [[BehaviorTree-System]]
- [[STEP_REVIEW_WORKFLOW]]
