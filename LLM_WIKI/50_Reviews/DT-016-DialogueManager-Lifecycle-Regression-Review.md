---
type: review
task: DT-016
status: completed
updated: 2026-06-19
---

# DT-016 DialogueManager Lifecycle Regression Review

`DialogueManager.play(...)` 진입점 기준의 반복 실행/교체/same-frame latest-wins/callback 재진입/
stale signal 차단/provider tuple isolation 계약을 전용 headless matrix로 고정한 작업의 완료 리뷰다.

## 범위와 산출물

- 신규 테스트: `addons/world_core/dialogtool/RunTime/tests/dt016_step1_manager_lifecycle_test.gd` +
  `.tscn`.
- **제품 코드 변경 없음.** 기존 `dialogue_manager.gd`/`dialogue_ui.gd`/`dialogue_player.gd`의 source
  guard·latest-wins·reentry 보장이 required tests를 그대로 통과해 Design Deviation이 없었다.
- 모든 graph는 runtime-only `DialogueGraphResource`를 코드에서 생성한다(영구 `.tres` 추가 없음).

## 발견 사항

P0/P1/P2/P3 결함 없음. 구현 보고 검토 시 다음을 추적해 회귀가 아님을 확인했다.

- **stale old-player valid window seam이 공허하지 않음:** 시나리오 [2]/[3]/[8]은 `play(NEW)`/`_dismiss()`
  직후 `await` 없이 `is_instance_valid(old_player)`가 `true`임을 단언하고 같은 프레임에 stale
  `advance()`/`select_choice(0)`를 실제로 호출한다. `is_instance_valid`로 호출을 스킵해 단언을
  무력화하지 않았다. queue_free는 프레임 종료에 실제 해제되므로 호출은 안전하고 source guard를 의미
  있게 검증한다.
- **source guard 차단 경로 확인:** stale old player 호출이 Manager `ui_request`/`dialogue_end` log를
  늘리지 않음을 호출 직전/직후 size·count 비교로 단언했다(`_on_ui_request`/`_on_end`의 `source_ui != _ui`
  guard). OLD의 지연 종료가 NEW 실행으로 섞이지 않는다.
- **same-frame latest-wins:** 시나리오 [4]는 같은 프레임 연속 `play()` 후 request log가 `["say:NEW"]`,
  `dialogue_started` count 1, OLD Say 0회임을 단언해 `_dismiss()`의 `cancel_pending_start()`와
  `DialogueUI._pending_start` latest-wins를 직접 검증한다.
- **end callback reentry 순서 보장:** 시나리오 [6]은 `dialogue_end` listener에서 새 대화를 시작해도
  첫 end 후 `is_playing()==true`이고 NEXT가 대기 상태임을 단언해 `_on_end()`가 `_dismiss()` 후
  `dialogue_end.emit()`하는 순서를 검증한다. listener는 one-shot으로 두 번째 end 무한 재진입을 막았다.
- **provider tuple isolation:** 시나리오 [7]은 test-only untyped spy mutation provider로 same-frame
  교체 후 OLD provider 호출 0회 / NEW provider 1회를 구분 단언한다. `WorldStateStore`로 대체하지 않았고
  spy signature(`apply_state_batch(changes)`, `add_state(key, delta)` 모두 untyped)는 `_method_accepts`
  계약을 통과한다.

### 테스트 seam 관찰(결함 아님)

- provider isolation은 **same-frame latest-wins**(폐기 player가 deferred start 자체를 안 함)로 검증한다.
  "waiting replace 후 stale player를 effect까지 강제 구동"하는 변형은, source guard가 signal만 거르고
  mutation provider 호출은 막지 않으므로(각 player가 자기 provider tuple을 보유) OLD count를 0으로
  단언할 수 없다. dt009_step4의 latest-wins isolation과 같은 안전한 경계로 의도적으로 제외했다.

## 검증 결과

### Completion 회귀 matrix(2026-06-19 재실행)

- `dt016_step1_manager_lifecycle_test.tscn` — **ALL PASS**, `SCRIPT ERROR:` 0
- `dt015_step1_integrated_graph_test.tscn` — **ALL PASS**, `SCRIPT ERROR:` 0
- `dt004_step4_integration_test.tscn` — **ALL PASS**, `SCRIPT ERROR:` 0
- `dt009_step4_e2e_completion_test.tscn` — **ALL PASS**, `SCRIPT ERROR:` 0
- `godot --headless --path . --import` — exit 0, 0 parse error(ObjectDB/resource 누수 경고는 clean
  import에도 나오는 benign shutdown noise).

### Step 1 완료 조건 대조

| 완료 조건 | 결과 |
| --- | --- |
| 모든 required tests headless PASS | 8 시나리오 ALL PASS |
| `SCRIPT ERROR:` 0 | 0건 확인 |
| 예상 `push_warning` 명시 | 시나리오 [5] `portrait_show` 빈 texture_path 경고 1회(테스트 주석/문서 명시) |
| 불필요한 Godot `ERROR` 로그 무생성 | 새 `ERROR` 없음 |
| 임시 resource 파일 무생성 | 영구/임시 `.tres` 미생성 |
| 제품 코드 변경 없음 | 변경 없음(Design Deviation 없음) |

### 예상 경고(SCRIPT ERROR 아님)

- 시나리오 [5]: `effect_then_say` fixture의 빈 `texture_path` `portrait_show` Effect 발행으로
  `DialoguePlayer: portrait_show node 0 has empty texture_path` `push_warning` 1회. portrait 렌더는
  DT-016 Non-Goal이라 의도된 경고이며 테스트 파일 상단 주석에 명시했다.

## 판정

**완료** — Step 1/Step 2 완료 조건 충족, P0/P1/P2 없음. `DialogueManager.play` 반복/교체/연속/
same-frame/reentry/provider isolation lifecycle이 전용 headless matrix로 고정되었고, stale UI/Player의
지연 `ui_request`/`dialogue_end`가 active Manager signal로 섞이지 않음을 단언한다.

## Related

- [[DT-016-DialogueManager-Lifecycle-Regression]]
- [[DialogueTool]]
- [[DT-004-Effect-Flow-Review]]
- [[DT-009-State-Mutation-Review]]
- [[DT-015-Dialogue-Integrated-Regression-Graph-Review]]
- [[STEP_REVIEW_WORKFLOW]]
