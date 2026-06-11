---
type: index
project: AutoCrawler
updated: 2026-06-11
---

# AutoCrawler LLM Wiki

AutoCrawler 개발을 위한 Agent 지식 베이스다. 작업 기록은 과정과 검증을, 시스템 문서는 현재 코드의 사실을, ADR은 설계 판단의 이유를 보존한다.

## 시작점

- [[Current-State]]: 현재 구현 상태와 최근 완료 작업
- [[Open-Tasks]]: 다음 작업 후보와 우선순위
- [[Project-Overview]]: 프로젝트 전체 구조
- [[DialogueTool-Architecture]]: DialogueTool의 현재 설계
- [[STEP_REVIEW_WORKFLOW]]: Step -> Review 작업 절차

## 시스템

- [[DialogueTool]]
- [[Turn-System]]
- [[BehaviorTree-System]]
- [[Article-Status-System]]

## 작업과 결정

- [[DT-001-DialogueTool-Foundation]]
- [[DT-002-Portrait-State]]
- [[DT-003-Say-Line-Paging]]
- [[DialogueTool-Step-1-to-8]]
- [[DT-002-Portrait-Review]]
- [[ADR-001-Runtime-Snapshot]]
- [[ADR-002-Editor-Adapter]]
- [[ADR-003-DialogueManager-Autoload]]
- [[ADR-004-Portrait-Texture-Path-MVP]]

## 문서 역할

| 위치 | 역할 |
| --- | --- |
| `00_Index` | 탐색, 현재 상태, 작업 목록 |
| `10_Architecture` | 프로젝트 전체에 영향을 주는 안정된 설계 |
| `20_Systems` | 시스템별 현재 구현 사실 |
| `30_Tasks` | 작업 목표, 변경, 검증, 후속 과제 |
| `40_Decisions` | 중요한 설계 선택과 근거 |
| `50_Reviews` | 리뷰 결과와 완료 판정 |
| `90_Templates` | Agent가 복제해 쓰는 문서 템플릿 |
| `Archive` | 더 이상 현재 사실이 아닌 기록 |
