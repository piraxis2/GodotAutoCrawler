---
id: ADR-014
type: decision
status: accepted
date: 2026-06-17
system: SaveGame
---

# SaveFlow Facade and Metadata Provider

## Context

SG-001로 `SaveGameManager`와 `SaveSection` 기반 save framework가 완성됐다. 하지만 실제 게임 UI가
`SaveGameManager.save_slot()`을 직접 호출하면 다음 문제가 생긴다.

- slot UI/UX는 게임마다 다르므로 core가 특정 menu scene을 제공하면 재사용성이 떨어진다.
- metadata 생성(`chapter`, `location`, `play_time_seconds`)은 게임별 정보에 의존한다.
- 저장 가능 여부(컷신/전투/로딩 중 저장 금지)는 UI 버튼 상태와 실제 save 호출 양쪽에서 같은 정책을 써야 한다.
- `SaveGameManager`는 envelope/file/backup을 담당하는 lower-level core이므로, game/event layer가 쓰기 좋은
  의도 중심 facade가 있으면 더 안전하다.

## Decision

`addons/save_game/save_flow.gd`에 `SaveFlow` facade를 둔다.

`SaveFlow`는 UI를 제공하지 않고, `SaveGameManager`를 호출하기 좋은 thin layer만 제공한다.

```gdscript
class_name SaveFlow
extends Node

func save_manual(slot_id, metadata := {}) -> Dictionary
func load_manual(slot_id) -> Dictionary
func delete_slot(slot_id) -> Dictionary
func list_slots() -> Array[Dictionary]
func has_slot(slot_id) -> bool
func can_save(slot_id = &"") -> Dictionary
```

Metadata는 **provider base + caller override** 정책을 사용한다.

```gdscript
func make_save_metadata(slot_id) -> Dictionary
```

Save gate도 optional provider로 둔다.

```gdscript
func query_save_gate(slot_id) -> Dictionary # { ok, reason }
```

`SaveFlow.can_save(slot_id)`는 이 provider를 정규화해 반환한다. provider가 없으면 allow다. provider가 없지 않은데
unavailable/contract-invalid이면 fail-closed(`ok:false`)이며, `save_manual()`은 manager를 호출하지 않는다.
정책상 저장 금지는 `save_not_allowed`, gate 설치/계약 오류는 `save_gate_unavailable`/
`save_gate_contract_invalid`로 구분한다.

`list_slots()`에서 manager를 찾지 못하면 빈 배열 대신 단일 실패 entry를 반환한다.

```gdscript
[{ "ok": false, "slot_id": &"", "error": &"manager_unavailable" }]
```

metadata merge는 shallow merge다. provider base를 먼저 만들고 caller metadata가 같은 key를 override한다.

## Consequences

### Positive

- SaveGame core가 UI/UX를 소유하지 않는다.
- 각 게임은 자기 slot menu를 만들되 같은 `SaveFlow` 호출 계약을 재사용할 수 있다.
- metadata 생성을 game layer로 분리하면서도 호출자가 일회성 override를 줄 수 있다.
- UI 표시와 실제 저장 호출이 같은 save gate report를 공유할 수 있다.
- `SaveGameManager` report를 숨기지 않아 backup recovery/corrupt/capture failure 정보를 UI가 그대로 사용할 수 있다.
- `list_slots()`의 manager-unavailable도 corrupt slot entry와 비슷한 shape로 처리할 수 있다.

### Negative

- `SaveGameManager`와 `SaveFlow` 두 계층이 생겨 API surface가 늘어난다.
- provider duck-type contract가 잘못 구현되면 runtime 오류 위험이 있으므로 shape 검증과 테스트가 필요하다.
- `list_slots()` manager-unavailable single entry는 실제 slot이 아니므로 UI가 slot count에 포함하지 않아야 한다.

## Alternatives

### A. UI가 `SaveGameManager`를 직접 호출

장점:
- 새 계층이 없다.
- 구현이 가장 작다.

단점:
- 각 UI가 metadata/gate/error wrapping을 반복한다.
- 게임별 UI가 lower-level slot/backup report를 직접 알아야 한다.

### B. Core가 slot UI까지 제공

장점:
- 바로 보이는 기능이 생긴다.

단점:
- 게임별 UX 차이가 커서 재사용성이 낮다.
- core가 UI theme/localization/thumbnail/controller 정책을 떠안는다.

### C. `SaveFlow` facade + provider

장점:
- UI 자유도와 framework 재사용성을 동시에 얻는다.
- metadata와 save gate를 host가 확장할 수 있다.

단점:
- public API와 provider contract를 꼼꼼히 검증해야 한다.

결정: **C**.

## Review Gate

SG-002 Step 0 설계 리뷰에서 아래를 확인한다.

- `SaveFlow`가 `SaveGameManager` 책임을 중복하거나 숨기지 않는가?
- provider/gate contract가 Godot duck-type 경계에서 fail-closed 가능한가?
- metadata merge 정책이 단순하면서 충분한가?
- UI를 제외한 범위가 명확한가?
- Step 1/2 테스트로 provider, gate, WorldState integration, backup report passthrough를 검증할 수 있는가?

판정: 2026-06-17 [[SG-002-SaveFlow-Facade-Metadata-Provider-Review]]에서 **Approved after design fixes**.
Design fixes 반영 후 accepted:

- gate provider 오류 fail-closed 및 error 구분.
- `list_slots()` manager-unavailable single-entry shape 확정(`slot_id:&""` 포함).
- metadata shallow merge 유지.
- provider/gate duck-type 유지.
- save gate provider 메서드명 `query_save_gate` 확정.
- manager는 호출마다 lazy resolve + validity/type check.

## Related

- [[SG-002-SaveFlow-Facade-Metadata-Provider]]
- [[SG-001-SaveGame-Core-Section-System]]
- [[SaveGame-System]]
- [[SaveGame-User-Guide]]
