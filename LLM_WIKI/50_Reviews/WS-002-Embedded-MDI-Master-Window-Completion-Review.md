---
type: review
task: WS-002-Embedded-MDI-Master-Window
step: 5
status: complete
reviewed: 2026-07-10
tags: [review, workspace, window, mdi, completion]
---

# Completion Review: WS-002 Embedded MDI Master Window

WS-002 Step 1~5 완료 리뷰다. Step 5는 제품 코드를 추가하지 않고, 완료 조건 대조와 문서 갱신만 수행했다. Step 0 설계 리뷰는 [[WS-002-Embedded-MDI-Master-Window-Review]]가 보존한다.

## Step별 완료 대조

| Step | 완료 조건 | 검증 | 판정 |
| --- | --- | --- | --- |
| 0 Design Review | OD 1~6 소유 배정, Step 완료 조건 테스트 가능화 | [[WS-002-Embedded-MDI-Master-Window-Review]] | Approved after design fixes |
| 1 Embedded Spike | 전역 `gui_embed_subwindows` on, 모든 managed 창 embedded, World 크기 tween 구조 probe, battle_field 부팅 회귀 | `ws002_embedded_tween_probe`, WS-001 회귀, battle_field 부팅 | 구조 PASS, GUI 트윈 사인오프 대기 |
| 2 Master + Log Dock | 마스터 content rect, 하단 로그 도크, LogWindowController 도크 이식, content gather/resize hook | `ws002_step2_master_dock_test` 41/41, GL-001 회귀 | 완료 |
| 3 Layout Slot Snap | visible slot resolve, 거리/tie-break/disable, preview/commit, 하이라이트 배선 | `ws002_step3_slot_snap_test` 31/31 | 완료, GUI drag 사인오프 대기 |
| 4 Window Diet + Preset v1 | 3 floating window, World hub 패널 이관/gating, E-7 비율, v2 persistence fallback | `ws002_step4_window_diet_test` 40/40 | 완료(P2 수정 반영) |
| 5 Review/Docs | system/current/open/task/review 갱신, 후속 분리 | 이 문서 + Wiki 갱신 | 완료 |

## Verification Matrix 대조

| 영역 | 상태 |
| --- | --- |
| Embedded spike | `project.godot` embed on, probe가 registry 전체 embedded와 World 크기 tween 구조를 단언. 시각적 부드러움은 GUI 사인오프 항목. |
| Master bounds | `ws002_step2`가 content rect, tiny master clamp, resize hook, content gather, visible 3창 비겹침을 단언. |
| Log dock | floating Log 제거, `CanvasLayer/LogDock/LogView`에 `LogWindowController` 이식. GL-001 step1/2/3 회귀 PASS. |
| Slot snap | `ws002_step3`가 거리 안/밖, inclusive boundary, nearest, Y→X→id tie-break, disable, applied rect clamp, preset별 slot 수, manager preview/commit을 단언. |
| Window diet | `ws002_step4`가 registry 3종, `log/situation/weekly_action/calendar` 미등록, World hub panel 이관 및 mode gating을 단언. |
| Preset v1 | E-7 비율 원 계약으로 수정: battle World 64% + TacticBoard 33%, analysis Report 34% + TacticBoard 36% + World 26%. |
| Persistence | `WorkspaceLayoutStore.CurrentVersion = 2`; old v1 config는 `UnsupportedVersion` fallback, 파일 보존. |
| Regression | `dotnet build`, `--import`, WS-001 3종, WS-002 4종, GL-001 3종, cb001 step1, battle_field 부팅 확인. |
| Scope boundary | 실제 World/BattleSession/TacticBoard/Report/DemoState 콘텐츠는 구현하지 않고 후속으로 분리. |

## 검증 재확인 (2026-07-10)

- `dotnet build AutoCrawler.sln -c Debug`: 경고 0 / 오류 0.
- `godot --headless --path . --import`: exit 0.
- WS-001 회귀: `ws001_step1_registry_test`, `ws001_step2_geometry_test`, `ws001_step3_persistence_test` ALL PASS.
- WS-002: `ws002_embedded_tween_probe`, `ws002_step2_master_dock_test`, `ws002_step3_slot_snap_test`, `ws002_step4_window_diet_test` ALL PASS.
- GL-001 회귀: `gl001_step1_game_log_model_test`, `gl001_step2_log_window_test`, `gl001_step3_adapter_test` ALL PASS.
- 전투 부팅/기초 회귀: `cb001_step1_turn_effect_test` ALL PASS, `battle_field.tscn` headless 부팅 exit 0.

검증 출력의 unknown preset, unsupported version, corrupt config warning은 expected diagnostic이다.

## 남은 GUI 사인오프

- World hub 98x79 ↔ battle 64x76 크기 tween의 실제 시각적 부드러움.
- 하단 로그 도크 전폭 상주와 리사이즈 시 화면 체감.
- 슬롯 드래그 중 highlight 표시, drop 정렬, Alt disable 조작.
- 메뉴에 제거된 `Situation`/`WeeklyAction`/`Calendar`/floating `Log` 토글이 노출되지 않는지.

구조와 로직은 headless로 단언됐고, 위 항목은 화면 품질 관찰이다.

## 후속

- World hub 실물화: 거점 씬 PC 1인, 48주 캘린더, 위험/가호/D-day, 행동 카드 6계열.
- World battle/replay, BattleSession/전장 scene, 출정/귀환 연출 실물화.
- TacticBoard 실제 콘텐츠(read/edit), Report 분석 콘텐츠, DemoState/자동 국면 전환.
- GameLog live 이벤트 발행과 도크 통합 후속(GL-001 후속과 연결).
- custom preset override 저장, slot snap UX 고도화.
- 레거시 `WindowManager.cs`/`GameWindow.cs`/`field_window.tscn` cleanup.
- `battle_field.tscn` stretch 정책, `run/main_scene` 전환 결정.

기존 Known Regressions(`cb001_step4 D.deaths_recorded`, `sk001_step6 D.ChainHitsThree`, SK-001 다중 상대 테스트)은 WS-002 범위 밖이다. 원인은 `battle_field.tscn` 상대 로스터 변경이며, 별도 rebaseline Task가 필요하다.

## Verdict

**완료.** Step 1~5 완료 조건 충족, P0/P1 없음. Step 4 P2 2건(E-7 비율 drift, World hub panel gating)은 수정·재검증 완료했다. WS-002는 embedded MDI 마스터 윈도우 W0를 닫고, 실제 콘텐츠 장착은 후속으로 넘긴다.

## Related

- [[WS-002-Embedded-MDI-Master-Window]]
- [[WS-002-Embedded-MDI-Master-Window-Review]]
- [[Workspace-Window-System]]
- [[Current-State]]
- [[Open-Tasks]]

