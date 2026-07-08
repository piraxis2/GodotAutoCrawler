---
id: ADR-019
type: decision
status: accepted
date: 2026-07-08
updated: 2026-07-08
system: Combat
---

# Natural Regen Stats

## Context

SK-001 introduced `Mana : StatusElement`, `SkillDefinition.ManaCost`, and `ManaDrainBlock`. 마나 지불 게이트는 동작하지만 production character scene에는 아직 `Mana`가 배선되지 않았다.

전투 지속 효과는 CB-001 이후 유닛 턴 시작 시 1회만 적용되도록 정리됐다. 이 경로는 프레임 수, 애니메이션 길이, `TurnHelper.Speed`와 독립적이다.

이제 체력과 마나의 자연 회복을 전투 스탯으로 표현해야 한다.

## Decision

### 1. 자연 회복량은 별도 StatusElement로 둔다

`Health`와 `Mana`는 현재/최대량의 저장소 역할을 유지한다.

자연 회복량은 별도 스탯으로 표현한다.

- `HealthRegen : StatusElement`
- `ManaRegen : StatusElement`

각 클래스는 정수 `Value`를 가진다. 기본값은 `0`이다.

이 선택은 최대량(`MaxHealth`/`MaxMana`)과 턴당 회복량을 분리해 장비, 버프, 디버프, 직업 성장에서 회복량만 조정하기 쉽게 한다.

### 2. 자연 회복은 유닛 턴 시작 시 1회 적용한다

자연 회복은 `CharacterArticle.ApplyTurnStartEffects()` 경로에서 적용한다.

적용 순서는 다음과 같이 고정한다.

1. `StatusController.OnTurnStart()`
2. 자연 회복
3. 살아 있으면 기존 지속 효과(`ArticleStatus.ApplyAffectingStatuses()`)

적용은 turn-start 계약을 구현한 각 `StatusElement`가 수행한다.

```csharp
public interface ITurnStartStatusElement
{
    int TurnStartOrder { get; }
    void ApplyTurnStart(ArticleStatus status);
}
```

`ArticleStatus`는 `ITurnStartStatusElement`를 `TurnStartOrder` 기준으로 dispatch하고, `HealthRegen`/`ManaRegen` 같은 구체 스탯 의미를 직접 분기하지 않는다.

`HealthRegen`/`ManaRegen`은 기존 setter를 통해 대상 스탯을 변경한다.

```csharp
health.CurrentHealth += Value;
mana.CurrentMana += Value;
```

따라서 clamp, signal, HealthBar 갱신, 사망 처리는 기존 `Health`/`Mana` 정책을 재사용한다.
지속 피해로 사망 처리된 뒤 같은 턴 시작 hook에서 양수 `HealthRegen`이 HP를 다시 올리는 흐름은 허용하지 않는다.

### 3. 음수 회복량을 허용한다

`HealthRegen.Value`와 `ManaRegen.Value`는 음수일 수 있다.

이로써 자연 회복, 독성 감소, 마나 누수 등을 같은 스탯 축으로 표현할 수 있다.

음수 `HealthRegen`이 체력을 0 이하로 만들면 기존 `Health.CurrentHealth` 정책에 따라 사망할 수 있다. 이 동작은 별도 테스트로 고정한다.
사망한 유닛은 같은 hook 안에서 이후 자연 회복으로 되살리지 않는다.

### 4. 누락된 대상 스탯은 no-op 처리한다

- `HealthRegen`만 있고 `Health`가 없으면 no-op.
- `ManaRegen`만 있고 `Mana`가 없으면 no-op.
- `Value == 0`이면 no-op.

이 정책은 테스트 fixture나 임시 캐릭터가 일부 스탯만 가진 상황에서 fail-closed로 동작하게 한다.

### 5. production 캐릭터에는 Mana와 ManaRegen을 기본 배선한다

`ManaCost` 스킬을 실제 전투 캐릭터가 사용할 수 있으려면 `Mana`가 필요하다.

초기 production 배선은 모든 전투 유닛에 `Mana`, `ManaRegen`, `HealthRegen`을 추가하는 방향으로 한다. 이유는 `ManaDrainBlock` 대상이 되는 유닛도 `Mana`를 가져야 상호작용이 일관적이기 때문이다.

초기 수치는 [[ST-001-Natural-Regen-Stats]] Step 2에서 fixture와 회귀 영향 확인 후 확정한다.

## Alternatives Considered

### A. Health/Mana 안에 Regen 필드를 넣기

거절.

현재/최대량과 회복량의 책임이 섞인다. 장비/버프/디버프가 회복량만 조정하는 경우 `Health`/`Mana` 저장소를 직접 건드리게 된다.

### B. ArticleStatus가 각 regen 타입을 직접 처리하기

거절.

`ArticleStatus`가 `HealthRegen`/`ManaRegen` 구체 타입을 알고 직접 적용하면, 새 turn-start 스탯이 늘어날 때마다 상태 컨테이너가 개별 스탯 규칙을 떠안는다. `ArticleStatus`는 dispatch와 조회만 담당하고, 효과 의미는 각 `StatusElement`가 가진다.

### C. StatusController가 자연 회복까지 소유하기

거절.

`StatusController`는 Stun/Bind/SelfBuff처럼 일시적 제어/버프 카운터를 소유한다. 자연 회복은 캐릭터의 기본 능력치이므로 `StatusElement`가 더 적합하다.

### D. 기존 StatusAffect로 자연 회복 표현

거절.

기본 능력치를 매번 지속 효과로 부여해야 하며, 캐릭터 데이터에서 "턴당 회복량"을 직접 읽기 어렵다.

## Consequences

### Positive

- 회복량이 캐릭터 데이터에 명시된다.
- 턴 시작 1회 적용이 보장돼 결정론 회귀와 맞는다.
- `ManaCost` 스킬, `ManaDrainBlock`, 추후 마나 UI가 같은 `Mana` 스탯을 공유한다.
- 장비/버프/디버프가 회복량을 조정하기 쉽다.

### Negative

- production character scene의 `ArticleStatus.StatusElements` 배열을 수정해야 한다.
- 음수 `HealthRegen`은 사망 경로를 만들 수 있어 QueueFree/턴 리스트 회귀 테스트가 필요하다.
- 회복 적용 순서는 "자연 회복 후 지속 효과"로 확정됐다. 독/출혈 같은 지속 피해가 최종 생존을 결정한다.

## Verification

- 새 StatusElement가 Godot에 GlobalClass로 등록되고 `.tscn`/`.tres`에 저장된다.
- HealthRegen/ManaRegen 값이 턴 시작 시 정확히 1회 적용된다.
- ArticleStatus는 ITurnStartStatusElement dispatch만 수행하고 HealthRegen/ManaRegen 구체 타입을 직접 분기하지 않는다.
- `TurnHelper.Speed`나 Running frame 반복이 적용 횟수를 늘리지 않는다.
- max clamp, 음수 감소, 누락 스탯 no-op, 음수 Health 사망 경로를 검증한다.
- 지속 피해 사망 뒤 같은 hook에서 회복으로 HP가 양수가 되지 않는지 검증한다.
- `ManaCost` 지불 후 다음 턴 `ManaRegen` 회복을 검증한다.
- CB-001 결정론 회귀와 SK-001 마나 게이트 회귀를 유지한다.

## Related

- [[ST-001-Natural-Regen-Stats]]
- [[ST-001-Natural-Regen-Stats-Review]]
- [[ADR-018-Data-Driven-Skill-System]]
- [[ADR-017-Deterministic-Combat-Resolution]]
- [[Article-Status-System]]
- [[Turn-System]]
- [[Skill-System]]


