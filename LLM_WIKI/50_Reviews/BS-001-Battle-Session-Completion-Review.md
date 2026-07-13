---
type: review
task: BS-001-Battle-Session
step: 4
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, completion]
---

# Completion Review: BS-001 BattleSession Runtime Boundary

BS-001 Step 0~4 완료 리뷰다. Step 4(문서)는 제품 코드를 추가하지 않고 시스템 문서 신규 + 인덱스 갱신 + 전체 완료
대조만 수행한다. 각 Step의 설계/코드 리뷰는 진행 중 완료됐다(Step 0 설계 리뷰 [[BS-001-Battle-Session-Review]],
Step 1~3은 각 단계 코드 리뷰에서 수정 후 완료). 사실은 [[Battle-Session-System]]이 보존한다.

## Step별 완료 대조

| Step | 완료 조건 | 검증 | 판정 |
| --- | --- | --- | --- |
| 0 Design Review + ADR | request/result·수명 주기·완료 순서·정적 정책·TurnHelper seam 검토, Open Decisions 확정 | [[BS-001-Battle-Session-Review]] | Approved after design fixes |
| 1 TurnHelper Explicit Lifecycle Seam | `AutoStart`+configure/start/stop, 멱등, 현재 유닛 사망 커서 보정 | `bs001_step1`(27) + cb001_step1~3·sk002_step3 회귀 | 수정 후 완료(P2×2, P3×1) |
| 2 BattleSession Start and Ownership | request/result 타입+state machine, 씬 소유, invalid matrix 1회, 세션 경로 ammo/seed | `bs001_step2`(79) | 수정 후 완료(P2×4) |
| 3 Completion, Result Capture, Re-entry | Victory/Defeat/mutual kill, deferred/1회, 결과 보존, 정적 정리, 2회 연속 | `bs001_step3`(45) | 미완료→수정 후 완료(P1×1) |
| 4 Docs + Completion Review | 시스템 문서·인덱스·Task 갱신, 완료 대조 | 이 문서 + `20_Systems/Battle-Session-System.md` | 완료 |

## Verification Matrix 대조 (Task)

| 영역 | 상태 |
| --- | --- |
| `AutoStart` on/off, configure/start/stop, 중복 호출 | ✅ `bs001_step1` — AutoStart 자동 시작·미시작, Configured 미진행, start/stop 멱등, stop 정지 |
| 현재 턴 유닛 제거 시 cursor 보정 | ✅ `bs001_step1` — 후임 선택/wrap/선행 제거, 목록 처음 리셋 제거 |
| request validation failure matrix | ✅ `bs001_step2` — null/wrong-type/instantiate-fail/missing-TurnHelper/missing-Articles/PC miss/PC not Ally/forged-Ally/empty factions 9종, 모두 deferred 1회 `Aborted/InvalidSetup` |
| Victory 결과와 unit snapshot | ✅ `bs001_step3` [A] — SurvivingOpponents=0, opponent Survived=false/FinalHealth=0, PC Survived |
| PC 사망 + 다른 Ally 생존 Defeat | ✅ `bs001_step3` [C] — `Defeat/PlayerDefeated`, SurvivingAllies=1 |
| 사망/진영 공백 same-frame Completed 1회 | ✅ `bs001_step3` [D] mutual kill Defeat 우선 + 전 케이스 `completed_once` |
| scene remove/static cleanup | ✅ `bs001_step3` [A] `static_cleared`, `_ExitTree` `==this` 정리 |
| seed 다른 두 세션 연속 실행과 상태 격리 | ✅ `bs001_step3` [G] — handler 내 재진입, seed=222 격리, 독립 Victory |
| 첫 턴 lethal turn-start 효과 완료(리뷰 P1) | ✅ `bs001_step3` [H] — Running 전환 후 사망도 deferred 완료 1회 |
| Completed subscriber 예외 격리(OD2) | ✅ `bs001_step2` [E] — 첫 handler throw, 두 번째 수신 |
| CB-001/SK-002/ST-001 전투 회귀 보존 | ✅ cb001_step1~3, sk002_step1/3, st001_step1 ALL PASS(모두 `battle_field` instantiate+free, `_ExitTree` 무회귀) |

## 검증 재확인 (2026-07-13)

- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: exit 0.
- `bs001_step1`(27) / `bs001_step2`(79) / `bs001_step3`(45) ALL PASS — 신규 151 assertion.
- 회귀: `cb001_step1`(15)/`cb001_step2`(3)/`cb001_step3`(12), `sk002_step1`(41)/`sk002_step3`(14),
  `st001_step1`(46) ALL PASS.
- 선행 실패 `cb001_step4`/`sk001_step1`/`sk001_step3`은 로스터 개편(`Articles/Opponent/Character`→`Character2`)
  으로 인한 셋업 NPE이며 clean-baseline에서 동일 재현됨 — BS-001과 무관, rebaseline Task 소관([[Open-Tasks]]).

## 리뷰에서 잡아 고친 결함(회귀 방지 요약)

- Step 1 [P2] Configure 재호출 OnDead 누적 → handler 보관 후 해제. [P2] 명시 경로 진영 공백 검사 → `AutoStart`
  경로로 한정. [P3] ADR OD5 동시 사망 결정 상태 stale → [[ADR-022-Battle-Session-Lifecycle]] Status 갱신.
- Step 2 [P2] PC를 부모 이름이 아니라 Ally registry 등록+턴 참가자로 검증(forged-Ally 거부). [P2] invalid matrix
  보강(instantiate-fail/missing nodes). [P2] Completed 예외 격리 검증. [P2 재리뷰] 빈 PackedScene `CanInstantiate()`
  선검사로 엔진 ERROR 제거.
- Step 3 [P1] 첫 턴 lethal 효과 사망이 완료 누락 → `StartBattle` 직전에 Running 전환(lethal HealthRegen fixture로
  검증).

## 남은 후속 (U1 경계 밖)

- `EncounterDefinition`/`EncounterModifier`/`PartySnapshot`으로 request 확장, `PlayerPath`→안정 `unit_id`.
- 명시적 Faction/`BattleRoster`, `CombatContext`/`CombatRng` 이동, `BattleRecorder` 추출.
- U3 DungeonRun의 `BattleResult` 소비(층 순회·HP/마나/ammo 이월·보상/XP/주차 정산), workspace 전장 scene 장착.
- `Retreat`/`RoundLimit`/수동 후퇴 실효화, 전투 이벤트 스트림 GameLog kill/death live 배선.
- 모든 직접 실행 소비자 전환 뒤 `TurnHelper.AutoStart` legacy seam 삭제.

## Verdict

**완료.** P0/P1 없음(Step 1~3의 리뷰 P1/P2/P3는 수정·재검증 완료). **BS-001 신규 테스트(151)와 영향권 회귀
(cb001_step1~3·sk002_step1/3·st001_step1)가 통과**하며, 완료 조건 "전체 회귀 matrix 통과"의 필수 후보 중
`cb001_step4`/`sk001_step1`/`sk001_step3`은 로스터 개편 선행 실패로 **clean-baseline 동등성 확인 후 rebaseline
Task로 분리**했다(BS-001 회귀 아님 — 이 세 건은 "전체 통과"가 아님을 명시). U3가 `BattleRequest`를 만들고
`BattleResult`를 소비할 수 있는 계약이 값 객체 경계로 고정됐고, 문서와 코드가 일치하며 후속은 U1 경계 밖으로
명확히 남았다.

## Related

- [[BS-001-Battle-Session]]
- [[BS-001-Battle-Session-Review]]
- [[Battle-Session-System]]
- [[Turn-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]
