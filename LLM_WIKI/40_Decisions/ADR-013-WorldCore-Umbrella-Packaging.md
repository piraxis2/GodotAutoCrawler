---
id: ADR-013
type: decision
status: accepted
date: 2026-06-17
system: SaveGame, DialogueTool, WorldState
---

# WorldCore Umbrella Packaging

## Context

DT-011은 DialogueTool과 WorldState를 `addons/dialogtool/` 안에 함께 묶어 재사용 가능한 addon 경계를 만들었다.
이 결정은 `state_condition`/`state_set`/`state_add`가 WorldState condition/mutation 모듈을 소비하는 현재
구조에서는 합리적이었다.

이제 SaveGame file/slot system을 설계하면서 더 큰 패키지 경계 문제가 생겼다.

- SaveGame core는 DialogueTool 하위 기능이 아니라 여러 gameplay system이 공유할 수 있는 저장 framework다.
- WorldState는 SaveGame을 몰라야 하고, SaveGame도 WorldState를 몰라야 한다.
- WorldState 저장은 `WorldStateSaveSection` 같은 adapter가 양쪽을 아는 integration layer로 해결해야 한다.
- DialogueTool도 SaveGame에 직접 의존하지 않는 것이 좋다. 저장 트리거는 후속 event/game-layer 방식이 우선이다.

사용자와 논의한 선호 구조는 `dialogtool`보다 더 넓은 umbrella root다.

```text
addons/world_core/
  save_game/
  dialogtool/
  world_state/
  save_game_world_state/
```

## Decision

`addons/world_core/`를 장기 목표 패키징 root로 채택한다.

```text
addons/world_core/save_game/              # 순수 SaveGame core
addons/world_core/world_state/            # StateSchema/WorldStateStore/Runtime/condition
addons/world_core/dialogtool/             # Dialogue editor/runtime
addons/world_core/save_game_world_state/  # SaveGame + WorldState adapter
```

의존 방향:

```text
save_game
  -> domain-specific system을 참조하지 않음

world_state
  -> save_game을 참조하지 않음

dialogtool
  -> world_state condition/mutation contract 소비 가능
  -> save_game을 참조하지 않음

save_game_world_state
  -> save_game + world_state를 참조하는 integration adapter
```

중요: SG-001 Step 1에서 바로 대규모 경로 이동을 수행하지 않는다. 패키징 이동은 별도 Task 또는 SG-001의
명시된 packaging Step에서 수행한다. SaveGame core API/section 계약과 path migration을 같은 Step에 섞지 않는다.

### Relation to ADR-011

이 결정은 [[ADR-011-DialogueWorldState-Addon-Packaging]] D1을 장기 패키징 목표 관점에서 amend한다.
ADR-011의 `addons/dialogtool/` 단일 addon 루트는 DT-011 완료 시점의 current packaging으로 유지한다.
ADR-013은 SaveGame처럼 DialogueTool 밖 core 소비자가 추가될 때의 다음 target packaging을 정의한다.

### Migration Trigger

`world_core` migration은 두 번째 core 소비자가 실제로 등장할 때 수행한다. SG-001 Step 1의 in-memory
SaveGame core 구현은 그 자체로 path migration trigger가 아니다. 아래 중 하나가 충족되면 별도 migration
Task를 연다.

- SaveGame core를 WorldState 외의 두 번째 gameplay system이 소비해야 한다.
- SaveGame core와 WorldState adapter를 addon 외부 프로젝트에 독립 배포/복사해야 한다.
- 기존 `addons/dialogtool/` root가 새 core module 배치 때문에 사용자 설치 문서에서 오해를 만들기 시작한다.

그 전까지는 현재 경로에서 domain-free core 경계를 테스트로 보존한다.

## Consequences

### Positive

- `save_game`이 DialogueTool 하위 기능처럼 보이지 않는다.
- `world_state`도 DialogueTool 부속품이 아니라 sibling core module로 보인다.
- integration adapter(`save_game_world_state`) 위치가 자연스럽다.
- future systems(`inventory`, `party`, `quest`, `map_state`)를 같은 umbrella 아래 추가하기 쉽다.
- SaveGame core의 재사용성과 domain-free 경계를 이름과 폴더 구조가 함께 설명한다.

### Negative

- DT-011에서 정리한 `addons/dialogtool/` 경로를 다시 크게 이동해야 한다.
- `project.godot` autoload, plugin path, `.tres/.tscn ext_resource`, README, tests path rewrite가 필요하다.
- Godot addon 간 의존을 강제할 방법은 약하므로 umbrella 내부 ordering/installation은 문서와 tests로 보완해야 한다.
- 단기적으로는 path churn이 SaveGame 자체 구현보다 더 큰 작업이 될 수 있다.

## Alternatives

### A. 현행 유지 + `addons/save_game`

```text
addons/dialogtool/
  world_state/
addons/save_game/
```

장점:
- path churn 최소.
- SaveGame core를 dialogtool 밖으로 빼는 최소 조치.

단점:
- WorldState가 여전히 DialogueTool 하위로 보인다.
- `WorldStateSaveSection` 위치가 애매하다.
- package naming이 장기 구조를 덜 잘 설명한다.

### B. 완전 독립 addon들

```text
addons/save_game/
addons/world_state/
addons/dialogtool/
addons/save_game_world_state/
```

장점:
- 각 모듈 재사용성이 최대.
- 의존 방향이 이름상 가장 직접적이다.

단점:
- 설치/복사 단위가 많아진다.
- Godot addon dependency 관리가 약해 사용자가 일부만 복사해 깨뜨리기 쉽다.
- DT-011의 "한 폴더 복사" 경험과 멀어진다.

### C. `addons/world_core/` umbrella root

장점:
- 한 폴더 복사 경험과 모듈별 sibling 구조를 동시에 제공한다.
- SaveGame/WorldState/DialogueTool의 위상이 더 정확하다.

단점:
- umbrella 이름과 책임을 장기적으로 유지해야 한다.
- path migration 비용이 크다.

권장: **C**, 단 migration은 SaveGame core 구현 Step과 분리.

## Review Gate

SG-001 Step 0 설계 리뷰에서 아래를 확인했다.

- `world_core` umbrella root가 DT-011의 기존 목표를 더 잘 확장하는가?
- SaveGame core가 WorldState/DialogTool을 직접 참조하지 않는 경계를 유지할 수 있는가?
- migration Step과 SaveGame implementation Step이 충분히 분리돼 있는가?
- fresh-project 설치/수용 테스트 전략이 있는가?

판정: 2026-06-17 **Approved after design fixes**. 위 relation/migration trigger를 반영해 accepted로 확정했다.

## Related

- [[SG-001-SaveGame-Core-Section-System]]
- [[ADR-011-DialogueWorldState-Addon-Packaging]]
- [[DT-011-DialogueWorldState-Addon-Packaging]]
- [[World-State-System]]
- [[DialogueTool]]
