---
type: review
task: BS-003-Battle-Completion-Lobby-Return
step: 3
status: complete
reviewed: 2026-07-13
tags: [review, combat, battle-session, workspace, result, reentry, completion]
---

# Completion Review: BS-003 Battle Completion and Lobby Return

BS-003 Step 0~3 완료 리뷰다. Step 3(문서)는 제품 코드를 추가하지 않고 시스템 문서 갱신 + 완료 대조만 수행한다.
Step 0 설계 리뷰는 [[BS-003-Battle-Completion-Lobby-Return-Review]], Step 1~2 코드 리뷰는 각 단계에서 완료됐다.
사실은 [[Workspace-Window-System]] "Lobby Battle Entry" / [[Battle-Session-System]] "Consumers"가 보존한다.

## Step별 완료 대조

| Step | 완료 조건 | 검증 | 판정 |
| --- | --- | --- | --- |
| 0 Design Review | OD1~6 확정, cleanup/mount/재진입 경계 확정 | [[BS-003-Battle-Completion-Lobby-Return-Review]] | Approved after design fixes |
| 1 Completion + Return Seam | 세 결과 cleanup-first + mount 숨김 + outgame preset + 결과 라벨 + LastResult, 즉시 완료, preset 실패 격리 | `bs003_step1_completion_return_test`(30) | 수정 후 완료 |
| 2 Workspace Result + Re-entry | 버튼 왕복 → hub 복귀 + 결과 라벨 + 2회 연속 재전투 격리 | `bs003_step2_workspace_roundtrip_test`(23) | 수정 후 완료 |
| 3 Docs + Completion Review | 시스템 문서·인덱스·Task 갱신, 완료 대조 | 이 문서 + Workspace/Battle-Session 시스템 갱신 + 오너 GUI smoke | 완료 |

## Verification Matrix 대조 (Task)

| 영역 | 상태 |
| --- | --- |
| Victory → 승리 표시 + hub 복귀 + cleanup | ✅ `bs003_step1` [A], `bs003_step2` A1 |
| Defeat → 패배 표시 + hub 복귀 + cleanup | ✅ `bs003_step1` [B] |
| Aborted → 시작 실패 표시 + hub 복귀 + cleanup | ✅ `bs003_step1` [C] (+ `GD.PushError` 진단) |
| 첫 턴 lethal 즉시 완료도 동일 전환 | ✅ `bs003_step1` [D] |
| outgame preset/presentation 실패에도 active/session 무누수 | ✅ `bs003_step1` [E] (enter 후 unknown preset 주입 → cleanup-first 격리) |
| 버튼 기반 2회 연속 전투: session 하나씩, 정적 context/결과 격리, 각 완료 1회 | ✅ `bs003_step2` A1/A2 |
| 새 전투 진입 시 mount 재표시 + battle preset 재적용 | ✅ `bs003_step2` A2(재진입 → World battle + mount 표시) |
| World client 실제 결과 문구/재전투 화면 전환 | ✅ 오너 GUI 수동 smoke 사인오프 — 결과 문구·hub 복귀·재전투 표시/진행 확인 |

## 검증 재확인 (2026-07-13)

- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: exit 0(workspace.tscn parse/script 에러 0).
- `bs003_step1_completion_return_test`(30) / `bs003_step2_workspace_roundtrip_test`(23) ALL PASS.
- 회귀: `bs002_step1`(29)/`bs002_step2`(14), `ws001_step1`(66)/`ws001_step2`(62), `ws002_step2`(41)/
  `ws002_step4`(40), `bs001_step1`(27)/`bs001_step2`(79)/`bs001_step3`(45) ALL PASS. `HubPanels/ResultLabel` 추가가
  3-window registry/hub 패널·BattleSession에 무영향.
- 선행 실패 `sk001`/`cb001_step4`(로스터)는 clean-baseline 동등성으로 BS-003과 구분([[Open-Tasks]]).

## 설계 리뷰 반영 요약(회귀 방지)

- Finding 1: 완료 시 `BattleMount` 숨김(빈 SubViewportContainer가 hub 입력/렌더 가림 방지) — `bs003_step1/2`가 완료
  후 `mount hidden` 단언.
- Finding 2/OD3: cleanup을 preset/presentation보다 먼저·독립 — `bs003_step1` [E]가 preset 실패 시에도 cleanup 성립.
- Finding 3: `_resultLabel`/`_windowManager`/`_battleMountVisibility` null-safe — `bs003_step1`이 workspace 없이 완결.
- Finding 4: 완료 session의 mount 무잔존은 다음 process frame 이후 단언(동기 재진입 공존은 관찰 규칙으로 처리).
- Finding 6/OD2: `Completed`가 이미 teardown 후 deferred라 handler 동기 수행(추가 deferral 없음).

## GUI 사인오프 / 후속

- **완료(오너 확인)**: main scene `workspace.tscn`에서 전투 종료 후 화면에 결과 문구/hub가 보이고 같은 `전투 시작`
  버튼으로 재전투가 표시·진행됨을 수동 확인했다. headless의 완료→cleanup→outgame preset→결과 라벨 계약과 실제 픽셀
  전환이 일치한다.
- **후속(범위 밖)**: 보상·XP·주차·위험도·WorldState 정산, HP/마나/ammo 이월, DS-001 DemoState, DL-001 U3
  DungeonRun(PartySnapshot/Encounter/층 진행), 특별 동료 이벤트/다이얼로그, GameLog live 전투 이벤트, 상세 결과/
  리포트/replay, 후퇴/round limit.

## Verdict

**완료.**

P0/P1 없음. Step 0~2의 리뷰 지적은 수정·재검증 완료. headless로 확인 가능한 모든 Done 조건(Victory/Defeat/Aborted
각각 cleanup-first + mount 숨김 + outgame preset + 결과 라벨 + `LastResult`, 첫 턴 lethal 즉시 완료, preset 실패
격리, 버튼 기반 2회 연속 전투의 session/정적 context/Node 격리)이 통과하고 코드와 문서가 일치한다. Task Done
condition의 "결과 문구 표시/재전투 화면 전환"도 오너 GUI 수동 smoke로 사인오프되어 BS-003의 로비↔전투 v0 왕복이
최종 완료됐다.

## Related

- [[BS-003-Battle-Completion-Lobby-Return]]
- [[BS-003-Battle-Completion-Lobby-Return-Review]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[Battle-Session-System]]
- [[Workspace-Window-System]]
- [[BS-001-Battle-Session]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[STEP_REVIEW_WORKFLOW]]
