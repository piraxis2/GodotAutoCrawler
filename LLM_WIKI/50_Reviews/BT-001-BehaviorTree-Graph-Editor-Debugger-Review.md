---
id: BT-001-review
type: review
status: complete
updated: 2026-07-05
system: BehaviorTree
---

# BT-001 BehaviorTree Graph Editor and Debugger Review

## Scope

BT-001 Step 1~5 구현 결과를 검토한다. Step 5 리뷰의 초점은 battle debug integration, target selector, session 재수집, runtime start/stop 호환, 다중 `tree_path` 탭 라우팅, stale lifecycle이다.

## Step 5 Findings

1. **[P2] `elapsed_time` fractional delta truncation — 수정 완료**
   - 원인: `BehaviorTree_Node._elapsedTime`이 `long`이고 `_elapsedTime = (long)(_elapsedTime + delta)`로 누적되어 `0.016` 같은 frame delta가 매번 0으로 잘렸다.
   - 영향: remote debug graph의 elapsed label이 일반 전투 tick에서 non-zero로 증가하지 않아 디버깅 정보가 부정확했다.
   - 수정: `_elapsedTime`과 `Util.BehaviorLog.Time`을 `double`로 변경하고, legacy `BehaviorTreeGraphView` debug tick 파싱을 `AsDouble()`로 맞췄다.
   - 회귀: `BehaviorTreeValidationTest` N 케이스에 실제 `Running` action을 `Behave(0.016)` 3회 실행해 `elapsed_time ~= 0.048`을 단언하는 검사를 추가했다.

## Verification

- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- `bt_validation_test.tscn`: A~T ALL PASS. 신규 `N.Tick_Report_Fractional_Delta_Accumulates` PASS.
- 기존 Step 5 자동 회귀: S(다중 `tree_path` tick 라우팅 격리), T(tab close stop 대상 격리) PASS.
- 수동 smoke 기록: `battle_field.tscn` F5에서 register discovery, remote graph 생성, manual Stop 후 `[STALE]` 확인.

## Remaining Gaps

- 현 `battle_field.tscn` smoke에서는 live BehaviorTree discovery 대상이 1개라 둘째 캐릭터 수동 Start 및 시각 multi-tab 확인은 자동 payload 테스트(S/T)로 보강했다.
- natural death unregister stale은 수동으로 재확인하지 못했고 R/P 테스트의 payload 경로로 보강했다.
- BT test 종료 시 기존 `SalvageChildren` owner warning 및 ObjectDB leak warning이 출력된다. 이번 P2 수정으로 새로 생긴 실패는 아니다.

## Verdict

**완료.** P0/P1 없음. Step 5 P2는 수정 및 재검증 완료. BT-001 Completion Criteria를 만족한다.