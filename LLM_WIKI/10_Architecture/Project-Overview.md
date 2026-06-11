---
type: architecture
system: project
updated: 2026-06-11
---

# Project Overview

## Agent Brief

- 책임: 자동 턴제 전투와 노드 기반 대화 제작 도구를 제공한다.
- 엔진: Godot 4.6.3 Mono
- 언어: C# 중심, 에디터 플러그인과 UI는 GDScript 혼용
- 주요 씬: `main.tscn`, `Assets/Scenes/Map/battle_field.tscn`
- 변경 시 주의: `.tscn`, `.tres`, `.uid` 참조와 사용자 작업 트리를 보존한다.
- 검증: Godot headless editor load, 관련 씬/리소스 왕복 저장, 런타임 흐름 실행

## Main Domains

### Battle

`ArticleBase`가 게임 객체의 기반이며 `CharacterArticle`은 BehaviorTree를 통해 턴 행동을 결정한다. `TurnHelper`가 턴을 순환하고 `BattleFieldTileMapLayer`가 타일 점유와 AStar 이동 정보를 관리한다.

### Status and Actions

`ArticleStatus`는 고정 능력치와 지속 효과를 관리한다. `TurnActionBase`와 그 구현들이 공격, 마법, 연출 단계를 수행한다.

### Editor Addons

- `addons/behaviortree`: C# BehaviorTree 및 에디터
- `addons/dialogtool`: 대화 그래프 에디터, 런타임, UI, 디버거
- `addons/devconsole`: 런타임 개발자 콘솔

## Runtime Flow

```text
ArticlesContainer collects units
  -> TurnHelper selects current unit
  -> CharacterArticle runs BehaviorTree
  -> Move or TurnAction executes
  -> Status and battlefield state change
  -> TurnHelper advances turn
```

## Related

- [[Turn-System]]
- [[BehaviorTree-System]]
- [[Article-Status-System]]
- [[DialogueTool-Architecture]]

