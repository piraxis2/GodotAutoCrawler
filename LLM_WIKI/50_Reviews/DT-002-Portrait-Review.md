---
type: review
system: DialogueTool
status: complete
reviewed: 2026-06-11
---

# DT-002 Portrait State 리뷰

## Scope

Portrait를 Say와 분리된 비대기 UI 상태 명령으로 도입한 [[DT-002-Portrait-State]] Step 1~4의
최종 리뷰. 런타임 명령 계약, 에디터 노드/저장 왕복, DialogueUI 슬롯 렌더링, 통합 수명주기.

## Important Findings and Fixes

- **[P1] 대화 교체 후 이전 Player의 stale 요청 발행** (Step 1): `portrait_*`는 비대기로 요청 후
  루프를 계속 진행한다. 요청 콜백에서 `play()`로 교체 시 이전 player가 stale `display_text`를
  발행했다. → `DialogueManager.ui_request` 중계에 `dialogue_end`와 동일한 source guard 적용.
- **[P2] 잘못된 slot 값이 편집/재저장에서 손실** (Step 2): 저장된 slot이 유효 집합 밖이면
  Adapter가 `center`로 보정해 원본을 덮어썼다. → `portrait_editor_adapter`가 알 수 없는 값을
  임시 OptionButton 항목으로 보존하고, 사용자가 명시적으로 바꿀 때만 교체.
- **[스펙 위반→수정] expression의 빈 texture_path가 기존 Texture를 제거** (Step 3): expression을
  show와 동일 처리했다. → expression을 분리해 "제공된(비어있지 않은) 값만 갱신, 빈 texture_path는
  기존 Texture 유지"로 교정.

## Final Verification

- Godot 4.6.3 headless editor load 성공.
- Step 1: 런타임 계약 20 + reentrancy 3 = 15→재구성 후 통과.
- Step 2: 에디터 노드/저장 왕복 44 + slot 보존 5 통과.
- Step 3: DialogueUI 슬롯/상태/렌더링 22 통과.
- Step 4: 통합/수명주기(저장·재로드, 전체 실행, 반복, 교체, 종료 재시작, callback 교체 guard,
  기존 리소스) 29 통과.
- 검증은 임시 헤드리스 테스트로 수행 후 제거.

## Judgment

- **완료**. DT-002 완료 조건 4항목 충족, P0/P1 없음.
- Say–Portrait 독립, DialoguePlayer 무상태, DialogueUI 상태 소유 경계 유지.

## Accepted Debt / Deferred

- transition 애니메이션, Portrait Focus/dim, actor database/actor·expression resolver.
- speaker 기반 자동 Portrait 선택, `SayDef.portrait` 마이그레이션 도구.
- 고정된 DialogueTool 자동 통합 회귀 테스트 자산(현재는 임시 스크립트로만 검증).
- 실제 화면 픽셀 렌더링의 시각 캡처(헤드리스 상태 검증으로 대체).

## Related

- [[DT-002-Portrait-State]]
- [[ADR-004-Portrait-Texture-Path-MVP]]
- [[DialogueTool]]
