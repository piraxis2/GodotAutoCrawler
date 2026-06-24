---
type: review
system: DialogueTool
status: complete
reviewed: 2026-06-11
---

# DT-004 Nonblocking Effect Flow 리뷰

## Scope

Portrait 같은 비대기 Effect를 주 Flow와 같은 실행 지점에 연결하는
[[DT-004-Nonblocking-Effect-Flow]] Step 1~4의 최종 리뷰. 연결 계약, 런타임 실행,
에디터 포트/저장 왕복, validation, DialogueUI/DialogueManager 통합 수명주기.
설계 근거는 [[ADR-005-Nonblocking-Effect-Connections]].

## Design Summary

- **연결 계약**: connection 딕셔너리의 선택적 `kind: "effect"`로 Effect 연결을 식별한다.
  없거나 다른 값이면 기존 Flow/Data 규칙. 기존 필드·포트 index는 불변(ADR-005 호환).
- **런타임**: `get_runtime_next_node_id`는 Effect를 건너뛰어 주 Flow만 반환하고,
  `get_runtime_effect_node_ids`(port-agnostic, kind 기준)가 Effect 대상을 저장 순서로 반환한다.
  DialoguePlayer가 노드를 떠나는 시점(`_go_to_next_node`/`select_choice`)에 Effect를 비대기로 발행하며,
  visited 셋으로 순환을 막고 Portrait 외 대상·누락은 경고 후 건너뛴다. Effect는 `waiting_for`를 만들지 않는다.
- **에디터**: `port_type`에 `effect`(주황) 추가. Start/Say에 Effect 출력(port 1), Portrait에 Effect 입력(port 1).
  기존 Flow/Data port index 보존. capture가 출력 포트 타입에서 `kind`를 파생한다.
- **validation**: 포트 카테고리(flow/value/effect) 불일치, 주 Flow 다중 대상, Effect 화이트리스트 위반,
  Effect 순환을 저장 전에 차단. 오류 메시지는 공유 헬퍼 `_format_port_edge`로 node id·type·port를 포함한다.

## Important Findings and Fixes

- **[P1] Step 1 형태(kind=effect, port 0→0) 리소스의 로드 시 의미 손실** (Step 2 리뷰): `load_resource`가
  `kind`를 무시하고 저장 포트로 재연결해 Effect가 직렬 Flow로 둔갑했다. → 로드 시 `kind=="effect"`면
  노드의 Effect 포트(`_find_effect_port`)로 정규화, 매핑 불가 시 조용히 Flow로 바꾸지 않고 오류 후 건너뜀.
- **[P2] 저장·재로드 후 Effect 실행 순서 미검증** (Step 2 리뷰): 재캡처 후 종류·개수만 확인했다.
  → 재캡처 Effect 대상 순서 비교 + 재캡처 리소스 재실행으로 `left → right → Say` 순서 증명.
- **[P2] validation 오류 메시지에 port 누락** (Step 3 리뷰 1·2차): 순환은 node id 배열만, Flow 다중·화이트리스트는
  일부 type/port 누락. → 공유 `_format_port_edge`로 통일, 순환은 `_find_effect_cycle`이 간선 배열을 반환해
  각 간선의 out-port/in-port를 표시.
- **[P2] 화이트리스트·순환의 validation 전체 경로 미테스트** (Step 3 리뷰): 단위 헬퍼만 검사했다.
  → 테스트 전용 Effect 포트로 `_validate_runtime_snapshot`이 실제 false를 반환함을 통합 경로(I3·I4)에서 증명.
- **[P2] Effect 콜백 중 교체 stale guard 미회귀** (Step 4 리뷰): 통합 테스트가 콜백 밖에서만 교체했다.
  → C4 추가: `portrait_state` 콜백 안에서 즉시 `play()` 교체 시 OLD 후속 요청 차단·NEW 전달을 검증(source guard 회귀).
- **[P2] 실제 기존 .tres 호환 미검증** (Step 4 리뷰): synthetic snapshot만 실행했다.
  → D3 추가: 실제 `pride_and_prejudice.tres`를 로드·실행해 legacy Say 필드 보존과 무손실 종료를 확인.
- **[P3] Task/ADR 상태 불일치**: DT-004 `proposed → done`, ADR-005 `proposed → accepted`.
- **[P3] Dialogue_UI.tscn 변경 해명** (Step 4 리뷰): 해당 `unique_id`/`anchors_preset` churn은 세션 시작 시점의
  기존 작업본으로 DT-004 변경이 아니며, 비복원 원칙에 따라 손대지 않았다.

## Remaining Limitations

- **[P3]** `_build_portrait_request`의 빈 texture 경고가 Effect 실행 시 source 노드 id를 참조(실행 동작 무관, 메시지 한정).
- Effect 출력 포트는 Start/Say에만 존재한다(Choice/Branch/End 제외). Portrait는 Effect 입력만 갖는 leaf다.
- Say 줄 누적(타이핑/페이지) 클릭 경로는 player.advance 직접 호출로 우회 검증(클릭 paging은 DT-003 범위).
- headless라 실제 화면 픽셀 렌더링이 아닌 논리 상태(`_portrait_state`/visible)로 검증한다.
- `clear_graph`의 deferred queue_free로 재로드 시 노드 이름이 일시적으로 바뀔 수 있음(기존 동작, id 조회로 무해).

## Verification

헤드리스 5개 테스트 파일 전부 PASS:

| 테스트 | 범위 | 결과 |
| --- | --- | --- |
| `dt004_step1_headless_test` | 런타임 Effect 실행/방어 | 6/6 |
| `dt004_step2_editor_test` | 에디터 포트·capture kind·저장/재로드·레거시(0→0) | 38/38 |
| `dt004_step3_validation_test` | validation 행렬·메시지 포맷·전체 경로 | 24/24 |
| `dt004_step4_pipeline_test` | 두 Effect 지점 저장/재로드+런타임 | 13/13 |
| `dt004_step4_integration_test` | DialogueUI/Manager 통합·수명주기·회귀(실제 .tres·콜백 교체 포함) | 42/42 |

Godot 4.6.3(mono) headless editor load 성공.

## 판정

**완료.** 완료 조건 충족: Portrait Effect 여러 개와 주 Flow 하나를 같은 실행 지점에 결정적 순서로 연결,
Effect 전체 처리 후 주 Flow 1회, 일반 Flow 다중 대기 노드 병렬 실행 불허, 기존 직렬/리소스 호환,
저장/재로드 후 의미·포트 순서 보존, 잘못된 그래프 저장 전 차단. P0/P1 없음, P3 1건 문서화.

## Related

- [[DT-004-Nonblocking-Effect-Flow]]
- [[ADR-005-Nonblocking-Effect-Connections]]
- [[DT-002-Portrait-Review]]
