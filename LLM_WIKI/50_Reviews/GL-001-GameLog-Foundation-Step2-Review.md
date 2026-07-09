---
type: review
task: GL-001-GameLog-Foundation
step: 2
status: complete
reviewed: 2026-07-09
tags: [review, gamelog, code-review, ui]
---

# Code Review: GL-001 GameLog Foundation (Step 2)

## Scope

리뷰 대상은 GL-001 Step 2(LogWindow Read-only UI) 구현이다. SaveGame, SkillSystem adapter, 로그 항목 클릭으로 실제 창 열기, 최종 스킨은 범위 밖이다.

검토 파일:

- `Assets/Script/UI/Window/LogWindowController.cs`
- `Assets/Script/UI/Window/LogChannelTab.cs`
- `Assets/Scenes/workspace.tscn`
- `Assets/Script/Tests/Gl001Step2LogWindowTest.cs`
- `Assets/Script/Tests/gl001_step2_log_window_test.tscn`
- [[GL-001-GameLog-Foundation]] Step 2 완료 조건

## Findings

1. **[P3] `ToggleExpand` 주석/계약과 구현이 어긋난다**
   - 위치: `Assets/Script/UI/Window/LogWindowController.cs:115-120`
   - 문제: 주석은 "접힘 가능한 entry가 아니면 아무 일도 없다"고 하지만, 현재 구현은 전달된 id가 현재 표시 중인 collapsible entry인지 확인하지 않고 `_expanded`에 추가/제거한다.
   - 영향: 실제 UI 버튼은 `AddRow`에서 collapsible row에만 생성되므로 일반 사용 경로에서는 영향이 없다. 다만 public API를 테스트/후속 코드가 임의 id로 호출하면, 아직 존재하지 않는 id가 `_expanded`에 남았다가 나중에 같은 id의 collapsed entry가 append될 때 처음부터 펼쳐진 상태로 보일 수 있다.
   - 권장: `ToggleExpand`에서 `CurrentEntries()` 중 `entry.Id == id && entry.IsCollapsed && !string.IsNullOrEmpty(entry.Body)`인 경우에만 토글하거나, 주석을 "UI는 collapsible row에서만 호출한다"로 낮춘다. Step 2 완료를 막지는 않는다.

## Compliance Notes

- `workspace.tscn` 변경은 `Windows/LogWindow`의 placeholder Label을 `LogView`(`LogWindowController`)로 교체하는 데 한정됐다. managed window id/registry/preset 배선은 유지된다.
- `LogWindowController`는 Step 1 `GameLogModel` API를 그대로 사용하며 severity 필터를 재구현하지 않는다.
- 전체 탭은 모든 채널의 `Event`만 보여주고, 채널 탭은 해당 채널의 `Event + Detail`을 보여준다. `Trace`는 v0 LogWindow에 노출하지 않는다.
- `Reward`와 `System`은 UI 탭에서만 `RewardSystem`으로 묶고, 저장 채널 enum은 확장하지 않았다.
- 대화 아카이브는 `IsCollapsed` + `Body`가 있는 entry에서 제목 1줄 접힘으로 시작하고 토글 시 본문을 표시한다.
- `Importance`는 색 강조에만 쓰이고 노출 판단에는 쓰이지 않는다.
- 링크 클릭, SaveGame, Skill adapter, 집계 정책은 구현하지 않아 Out of Scope를 지켰다.

## Verification

재실행 결과:

- `dotnet build AutoCrawler.sln -c Debug`: PASS, 경고 0 / 오류 0.
- Godot `--headless --path . --import`: PASS, exit 0. 종료 시 ObjectDB/resource shutdown noise는 기존 clean import에서도 관찰되는 benign noise.
- `gl001_step2_log_window_test.tscn`: PASS, 51/51 assertions, exit 0.
- `ws001_step1_registry_test.tscn`: PASS, expected diagnostic ERROR/WARNING 포함.
- `ws001_step3_persistence_test.tscn`: PASS, expected diagnostic ERROR/WARNING 포함.

테스트 커버:

- append 후 UI row 갱신.
- 전체/전투/이야기/획득·시스템 탭 필터.
- Detail 감광 및 Importance 색 구분.
- 대화 아카이브 접힘 시작/펼침/재접힘.
- 200개 trim의 UI row 반영.
- append/clear 자동 refresh.
- 샘플 seed.
- `EntriesForTab` static 필터 함수.
- `workspace.tscn`의 `Windows/LogWindow/LogView` controller 배선.

## Residual Risk

- 실제 GUI screenshot은 수행하지 않았다. 색 값, `Visible`, row count, 씬 배선은 headless로 검증했지만 미세 레이아웃/텍스트 overflow는 수동 또는 screenshot 검증이 더 정확하다.
- `_expanded` set은 trim된 id를 즉시 비우지 않는다. 현재 렌더는 현재 entry만 보므로 무해하고 `Clear`에서 정리된다.
- Step 3 adapter는 기존 선행 조건 3건을 닫기 전 착수 금지: `cast_cancel` 진영 판정, 전역 수명 ADR, `stun_miss`/`selfbuff` 어휘 처리.

## Verdict

완료.

P0/P1/P2 없음. P3 1건은 실제 UI 경로를 막지 않는 계약 정리 사항이다. Step 2 완료 조건은 충족했고, Step 3은 선행 조건 3건을 닫은 뒤 착수한다.

## Related

- [[GL-001-GameLog-Foundation]]
- [[GL-001-GameLog-Foundation-Review]]
- [[GL-001-GameLog-Foundation-Step1-Review]]
- [[STEP_REVIEW_WORKFLOW]]
