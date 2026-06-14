# Agent Instructions

이 저장소에서 작업하는 에이전트는 아래 절차를 따른다.

## Start Here

작업 전 다음 문서를 순서대로 읽는다.

1. `LLM_WIKI/00_Index/Home.md`
2. `LLM_WIKI/00_Index/Current-State.md`
3. `LLM_WIKI/00_Index/Open-Tasks.md`
4. 요청과 관련된 `20_Systems`, `30_Tasks`, `40_Decisions` 문서
5. `LLM_WIKI/STEP_REVIEW_WORKFLOW.md`

Wiki는 탐색과 인수인계를 위한 지도다. 문서와 코드가 다르면 실제 코드와 실행 결과를 최종 사실로 보고, 차이를 사용자에게 알린다.

## Before Editing

- `git status`와 관련 파일을 확인한다.
- 기존 사용자 변경을 되돌리거나 덮어쓰지 않는다.
- 요청의 범위, 제외 범위, 완료 조건을 확인한다.
- 규모가 있는 작업은 `LLM_WIKI/30_Tasks`에 Task 문서를 생성하거나 기존 Task를 갱신한다.
- 중요한 설계 선택이 필요하면 `LLM_WIKI/40_Decisions`에 ADR을 작성한다.

## Development Workflow

- 구현 전 설계가 필요한 작업은 `Design Review`와 `Implementation`을 별도 세션 또는 별도 요청으로 진행한다.
- 설계 리뷰 중에는 제품 코드를 수정하지 않는다.
- 설계 리뷰가 `Approved` 또는 설계 수정 후 승인되기 전에는 구현을 시작하지 않는다.
- 설계 리뷰 프롬프트는 `LLM_WIKI/90_Templates/Design-Review-Prompt.md`를 사용한다.
- 구현 프롬프트는 `LLM_WIKI/90_Templates/Implementation-Prompt.md`를 사용한다.
- 큰 작업은 독립적으로 검증 가능한 Step으로 나눈다.
- 한 번에 하나의 Step만 구현한다.
- 기능 추가와 관련 없는 구조 변경을 같은 Step에 섞지 않는다.
- 기존 코드 패턴과 시스템 경계를 우선한다.
- Step 완료 후 `LLM_WIKI/STEP_REVIEW_WORKFLOW.md`에 따라 리뷰와 재검증을 수행한다.
- P0/P1 문제가 남아 있으면 다음 Step으로 넘어가지 않는다.

## Verification

변경 위험에 맞는 검증을 수행한다.

- Godot/GDScript 변경: 가능하면 Godot headless editor load
- 에디터 도구 변경: 생성 -> 편집 -> 저장 -> 재로드 왕복 확인
- 런타임 변경: 정상 종료, 반복 실행, 재진입, 교체 흐름 확인
- 리소스 변경: `.tscn`, `.tres`, `.uid` 참조와 데이터 보존 확인
- 실행하지 못한 검증은 완료 보고에 명시한다.

## Wiki Maintenance

작업 완료 후 필요한 문서를 갱신한다.

- `30_Tasks`: 구현 내용, 검증 결과, 후속 작업
- `00_Index/Current-State.md`: 현재 구현 사실
- `00_Index/Open-Tasks.md`: 완료 작업 제거 및 새 작업 추가
- `20_Systems`: 시스템의 현재 동작과 주요 진입점
- `40_Decisions`: 장기적으로 유지할 설계 판단
- `50_Reviews`: 중요한 리뷰 결과와 완료 판정

Task 문서는 작업 과정과 증거를 기록한다. System 문서는 현재 사실만 유지한다. 같은 설명을 여러 문서에 복제하지 않고 Obsidian 링크로 연결한다.

## Completion Report

최종 보고에는 다음을 포함한다.

- 변경한 내용과 주요 파일
- 수행한 검증과 결과
- 남은 위험 또는 테스트 공백
- 갱신한 Wiki 문서
