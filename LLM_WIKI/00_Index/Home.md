---
type: index
project: AutoCrawler
updated: 2026-07-14
---

# AutoCrawler LLM Wiki

AutoCrawler 개발을 위한 Agent 지식 베이스다. 작업 기록은 과정과 검증을, 시스템 문서는 현재 코드의 사실을, ADR은 설계 판단의 이유를 보존한다.

## 시작점

- [[Current-State]]: 현재 구현 상태와 최근 완료 작업
- [[Open-Tasks]]: 다음 작업 후보와 우선순위
- [[Project-Overview]]: 프로젝트 전체 구조
- [[DialogueTool-Architecture]]: DialogueTool의 현재 설계
- [[STEP_REVIEW_WORKFLOW]]: Step -> Review 작업 절차
- [[Design-Review-Prompt]]: 구현 전 설계 검토용 Agent 프롬프트
- [[Implementation-Prompt]]: 승인된 Step 구현용 Agent 프롬프트

## 시스템

- [[DialogueTool]]
- [[DialogueTool-User-Guide]]
- [[World-State-System]]
- [[World-State-User-Guide]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
- [[Turn-System]]
- [[Battle-Session-System]]
- [[BehaviorTree-System]]
- [[Article-Status-System]]
- [[Skill-System]]
- [[Workspace-Window-System]]
- [[GameLog-System]]

## 작업과 결정

- [[DT-001-DialogueTool-Foundation]]
- [[DT-002-Portrait-State]]
- [[DT-003-Say-Line-Paging]]
- [[DT-004-Nonblocking-Effect-Flow]]
- [[DT-005-StateSchema-WorldStateStore]]
- [[DT-006-WorldState-Runtime-Integration]]
- [[DT-007-ConditionSet-ConditionEvaluator]]
- [[DT-008-State-Condition-Dialogue-Integration]]
- [[DT-009-State-Mutation-Dialogue-Effects]]
- [[DT-010-Dialogue-Debug-WorldState-Preview]]
- [[DT-011-DialogueWorldState-Addon-Packaging]]
- [[DT-012-Condition-Authoring-UX]]
- [[DT-013-State-Read-Data-Node]]
- [[DT-014-Say-Line-Paging-UI-Regression]]
- [[DT-015-Dialogue-Integrated-Regression-Graph]]
- [[DT-016-DialogueManager-Lifecycle-Regression]]
- [[WC-001-WorldCore-Umbrella-Migration]]
- [[SG-001-SaveGame-Core-Section-System]]
- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[SG-003-SaveSlot-UI-Host-Integration]]
- [[BT-001-BehaviorTree-Graph-Editor-Debugger]]
- [[BT-002-Tactic-Board-Runtime-Extension]]
- [[CB-001-Deterministic-Combat-Resolution]]
- [[SK-001-Data-Driven-Skill-System]]
- [[SK-002-Skill-Ammo-System]]
- [[SK-003-Skill-Based-Character-Scene]]
- [[SK-001-Data-Driven-Skill-System-Review]]
- [[ST-001-Natural-Regen-Stats-Review]]
- [[ST-001-Natural-Regen-Stats]]
- [[WS-001-Native-Window-Workspace-Shell]]
- [[GL-001-GameLog-Foundation]]
- [[BS-001-Battle-Session]]
- [[BS-002-Lobby-to-Battle-Entry-Integration]]
- [[BS-003-Battle-Completion-Lobby-Return]]
- [[BS-002-Lobby-to-Battle-Entry-Integration-Review]]
- [[BS-002-Lobby-to-Battle-Entry-Integration-Completion-Review]]
- [[BS-003-Battle-Completion-Lobby-Return-Review]]
- [[BS-003-Battle-Completion-Lobby-Return-Completion-Review]]
- [[BS-001-Battle-Session-Review]]
- [[BS-001-Battle-Session-Completion-Review]]
- [[GL-001-GameLog-Foundation-Review]]
- [[GL-001-GameLog-Foundation-Step1-Review]]
- [[GL-001-GameLog-Foundation-Step2-Review]]
- [[GL-001-GameLog-Foundation-Step3-Review]]
- [[GL-001-GameLog-Foundation-Completion-Review]]
- [[WS-001-Native-Window-Workspace-Shell-Review]]
- [[DialogueTool-Step-1-to-8]]
- [[DT-002-Portrait-Review]]
- [[DT-004-Effect-Flow-Review]]
- [[DT-005-WorldState-Review]]
- [[DT-006-WorldState-Runtime-Review]]
- [[DT-007-Condition-Review]]
- [[DT-008-Choice-Integration-Review]]
- [[DT-009-State-Mutation-Review]]
- [[DT-010-Dialogue-Debug-WorldState-Preview-Review]]
- [[DT-011-DialogueWorldState-Addon-Packaging-Review]]
- [[DT-012-Condition-Authoring-UX-Review]]
- [[SG-001-SaveGame-Core-Section-System-Review]]
- [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]]
- [[SG-003-SaveSlot-UI-Host-Integration-Review]]
- [[CB-001-Deterministic-Combat-Resolution-Review]]
- [[ADR-001-Runtime-Snapshot]]
- [[ADR-002-Editor-Adapter]]
- [[ADR-003-DialogueManager-Autoload]]
- [[ADR-004-Portrait-Texture-Path-MVP]]
- [[ADR-005-Nonblocking-Effect-Connections]]
- [[ADR-006-Typed-World-State]]
- [[ADR-007-WorldState-Runtime-Lifecycle]]
- [[ADR-008-Structured-Condition-Evaluation]]
- [[ADR-009-State-Condition-Dialogue-Consumption]]
- [[ADR-010-State-Mutation-Dialogue-Effects]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[ADR-012-Dialogue-Debug-Preview-Provider]]
- [[ADR-013-WorldCore-Umbrella-Packaging]]
- [[ADR-014-SaveFlow-Facade-And-Metadata-Provider]]
- [[ADR-015-State-Read-Data-Node]]
- [[ADR-016-BehaviorTree-Remote-Debug-Channel]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[ADR-019-Natural-Regen-Stats]]
- [[ADR-020-GameLog-Service-Lifetime]]
- [[ADR-021-Skill-Ammo-System]]
- [[ADR-022-Battle-Session-Lifecycle]]
- [[ADR-023-Tactic-Board-Runtime-Execution-Contract]]

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
