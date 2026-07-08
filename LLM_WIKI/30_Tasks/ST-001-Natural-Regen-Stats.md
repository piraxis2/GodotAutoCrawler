---
id: ST-001
type: task
status: complete
system: ArticleStatus
created: 2026-07-08
updated: 2026-07-08
tags: [task, combat, status, regeneration, mana, health]
---

# ST-001 Natural Regen Stats

## Goal

체력과 마나의 자연 회복을 전투 유닛의 명시적 스탯으로 표현한다.

목표 상태:

- 캐릭터가 `HealthRegen`/`ManaRegen` 값을 가진다.
- 자연 회복은 유닛 턴 시작 시 정확히 1회 적용된다.
- 회복은 프레임 수, 애니메이션 길이, `TurnHelper.Speed`에 영향을 받지 않는다.
- `ManaCost`가 있는 데이터 기반 스킬과 함께 production 캐릭터에 `Mana`/`ManaRegen`을 배선할 수 있다.

## Current Code Facts

- `Health : StatusElement`는 `MaxHealth`/`CurrentHealth`를 가지고, `CurrentHealth` setter에서 clamp, 사망 처리, signal, HealthBar 갱신을 수행한다.
- `Mana : StatusElement`는 `MaxMana`/`CurrentMana`, `CanAfford`, `TrySpend`를 가지고, `CurrentMana` setter에서 `0..MaxMana` clamp와 `OnManaChanged` signal을 수행한다.
- `ArticleStatus`는 export된 `StatusElements` 배열을 초기화 시 `StatusElementsDictionary`에 타입별로 넣고 `StatusElement.Init(owner)`를 호출한다.
- `TurnHelper.AdvanceToNextTurn()`은 현재 유닛의 `ITurnAffectedArticle.ApplyTurnStartEffects()`를 턴 시작마다 정확히 1회 호출한다.
- `CharacterArticle.ApplyTurnStartEffects()`는 현재 `StatusController.OnTurnStart()` 후 `ArticleStatus.ApplyAffectingStatuses()`를 호출한다.
- SK-001은 `SkillDefinition.ManaCost` 지불 게이트와 `ManaDrainBlock`을 구현했지만, production 캐릭터 씬의 `Mana` 배선은 후속으로 남아 있다.

## Scope

- `HealthRegen : StatusElement`, `ManaRegen : StatusElement`를 추가한다.
- 자연 회복 적용 로직을 턴 시작 경로에 연결한다.
- 회복량은 정수 `Value`로 시작한다.
- 음수 `Value`를 허용해 독/마나 누수 같은 감소 효과도 같은 축으로 표현할 수 있게 한다.
- 회복은 기존 `Health.CurrentHealth`/`Mana.CurrentMana` setter를 통해 적용해 clamp, signal, UI 갱신을 재사용한다.
- production 캐릭터 씬에 최소 기본 `Mana`/`ManaRegen`/`HealthRegen` 배선을 추가한다.

## Out of Scope

- 전투 UI에 마나 바/회복량 표시 추가.
- 아웃게임 성장, 장비, 직업, 스킬 구매와 회복 스탯 연결.
- 회복량 비율 공식, 레벨 스케일링, AP/행동력 회복 모델.
- `ManaCost` 무대상 환불 정책 변경.
- `DamageBlock.ApplyDamage`의 legacy UI 결합 제거.

## Proposed Architecture

### Data Model

- `HealthRegen : StatusElement`
  - `[Export] public int Value { get; set; } = 0`
  - 턴 시작 시 자신이 `Health`를 찾아 `Health.CurrentHealth += Value`를 적용한다.
- `ManaRegen : StatusElement`
  - `[Export] public int Value { get; set; } = 0`
  - 턴 시작 시 자신이 `Mana`를 찾아 `Mana.CurrentMana += Value`를 적용한다.

`Health`와 `Mana`는 현재/최대량의 저장소로 유지한다. 자연 회복량은 별도 스탯으로 분리해 장비/버프/디버프가 `Value`를 조정할 수 있게 한다.

### Turn-Start StatusElement Contract

`ArticleStatus`가 `HealthRegen`/`ManaRegen` 같은 개별 스탯 의미를 직접 알지 않도록, 턴 시작에 반응하는 스탯은 공통 계약을 구현한다.

```csharp
public interface ITurnStartStatusElement
{
    int TurnStartOrder { get; }
    void ApplyTurnStart(ArticleStatus status);
}
```

책임 경계:

- `CharacterArticle`: 턴 시작 순서만 오케스트레이션한다.
- `ArticleStatus`: `StatusElement` 보관, 타입 조회, 생존 guard, `ITurnStartStatusElement` dispatch만 담당한다.
- `HealthRegen`/`ManaRegen`: 어떤 대상 스탯을 어떻게 바꿀지 직접 담당한다.

`ArticleStatus`는 `ApplyTurnStartStatusElements()` 같은 dispatch API에서 `StatusElementsDictionary.Values` 중 `ITurnStartStatusElement`를 `TurnStartOrder` 기준으로 실행한다. 실행 전후로 `HasLivingHealth()`를 확인해 사망한 유닛의 뒤이은 turn-start 스탯 처리를 중단한다.

초기 order 제안:

| StatusElement | TurnStartOrder |
| --- | ---: |
| `HealthRegen` | 100 |
| `ManaRegen` | 110 |

이 순서로 음수 `HealthRegen`이 사망을 만들면 `ManaRegen`은 같은 hook에서 적용되지 않는다.

### Application Timing

자연 회복은 `CharacterArticle.ApplyTurnStartEffects()`에서 적용한다.

확정 순서:

1. `StatusController.OnTurnStart()`
2. natural regen 적용
3. 살아 있으면 `ArticleStatus.ApplyAffectingStatuses()`

자연 회복은 지속 피해보다 먼저 적용한다. `Health.CurrentHealth`는 0 이하 입력 시 즉시 `Owner.Dead()`를 호출하므로,
지속 피해로 사망 처리된 뒤 같은 hook에서 양수 `HealthRegen`이 HP를 다시 올리는 흐름을 만들지 않는다.
natural regen 적용 뒤 사망한 경우에도 지속 효과 반복 적용은 건너뛴다.

### Failure Policy

- `HealthRegen`이 있어도 `Health`가 없으면 아무 것도 하지 않는다.
- `ManaRegen`이 있어도 `Mana`가 없으면 아무 것도 하지 않는다.
- `Value == 0`은 no-op이다.
- 음수 `HealthRegen`으로 `CurrentHealth <= 0`이 되면 기존 `Health.CurrentHealth` 정책에 따라 사망할 수 있다.
- 사망한 유닛에는 이후 자연 회복을 적용하지 않는다. 같은 턴 시작 hook 안에서 회복으로 사망을 되돌리지 않는다.
- 회복 적용은 RNG를 소비하지 않는다.

### Production Defaults

초기 제안:

| Unit | Mana Max | ManaRegen | HealthRegen |
| --- | ---: | ---: | ---: |
| PrincessKnight | 60 | 8 | 0 |
| Puppet | 30 | 3 | 0 |
| TempArticle2 | 40 | 5 | 0 |
| TempArticle3 | 60 | 8 | 0 |

실제 수치는 Step 2에서 production scene 배선 시 테스트/밸런스 영향과 함께 확정한다.

## Steps

### Step 0 - Design Review

Scope:

- 이 Task와 [[ADR-019-Natural-Regen-Stats]]를 실제 코드와 대조해 리뷰한다.
- 제품 코드와 리소스는 수정하지 않는다.

Done condition:

- 설계 리뷰 판정 `Approved` 또는 `Approved after design fixes`.
- 적용 순서, 사망/부활 금지 가드, 음수 회복, production 기본값 초안, 누락 StatusElement 정책이 구현 전에 확정돼 있다.

### Step 1 - Regen StatusElements and Turn Start Application

Scope:

- `HealthRegen`/`ManaRegen` StatusElement를 추가한다.
- `ITurnStartStatusElement` 계약을 추가하고, `HealthRegen`/`ManaRegen`이 직접 구현한다.
- `ArticleStatus`에 `StatusElement` 타입 조회 API와 turn-start dispatch API를 추가한다.
- `ArticleStatus`는 `HealthRegen`/`ManaRegen` 구체 타입을 직접 분기하지 않는다.
- `CharacterArticle.ApplyTurnStartEffects()`의 턴 시작 경로에 dispatch를 연결한다.
- production scene은 아직 수정하지 않고 테스트 fixture에서만 배선한다.

Done condition:

- 턴 시작 1회당 Health/Mana가 각각 `Value`만큼 회복된다.
- Max clamp가 보존된다.
- 음수 `Value`가 감소로 적용된다.
- `Health`/`Mana`/regen 스탯 누락 시 SCRIPT ERROR 없이 no-op이다.
- 자연 회복이나 지속 효과로 사망한 유닛은 같은 hook 안에서 HP가 다시 양수가 되지 않는다.
- Running frame 반복과 `TurnHelper.Speed` 변화가 회복 횟수를 늘리지 않는다.
- `ArticleStatus`의 자연 회복 경로에 `HealthRegen`/`ManaRegen` 구체 타입 지식이 없다.
- 새 turn-start 스탯을 추가할 때 `ArticleStatus` 수정 없이 `ITurnStartStatusElement` 구현만으로 확장 가능하다.
- 기존 CB-001 턴 시작 효과 회귀가 유지된다.

Out of Scope:

- production 캐릭터 씬 배선.
- UI 표시.

### Step 2 - Production Character Mana/Regen Wiring

Scope:

- `PrincessKnight`, `Puppet`, `TempArticle2`, `TempArticle3` 등 현재 전투 캐릭터 씬의 `ArticleStatus.StatusElements`에 `Mana`, `ManaRegen`, `HealthRegen`을 추가한다.
- 필요하면 캐릭터별 `MaxMana`/`ManaRegen` 기본값을 조정한다.
- 데이터 기반 `ManaCost` 스킬을 production에 연결하기 전, 기존 legacy TurnAction 회귀를 깨지 않는지 확인한다.

Done condition:

- 씬 저장/재로드 후 StatusElements 배열에 새 스탯이 보존된다.
- 전투 시작 시 각 캐릭터의 `Mana.CurrentMana == MaxMana`가 보장된다.
- 턴 시작 시 마나가 1회 회복된다.
- `ManaCost`가 있는 `TurnAction_Skill`을 fixture에서 실행했을 때 지불 후 다음 턴 회복이 적용된다.
- CB-001 결정론 회귀가 유지되거나, production scene 수치 변경에 따른 기대 로그 갱신 필요성이 명시된다.

Out of Scope:

- legacy TurnAction을 데이터 스킬로 교체.

### Step 3 - Documentation and Completion Review

Scope:

- [[Article-Status-System]], [[Turn-System]], [[Skill-System]], [[Current-State]], [[Open-Tasks]]를 현재 사실로 갱신한다.
- 완료 리뷰를 작성한다.

Done condition:

- 회복 스탯 모델, 적용 타이밍, production 배선 상태, 검증 결과가 문서화된다.
- P0/P1 리뷰 문제가 없다.

## Verification Plan

- C# headless test:
  - pure `ArticleStatus`/helper natural regen application.
  - `CharacterArticle.ApplyTurnStartEffects()` integration.
  - `TurnHelper` running-frame/speed 불변 회귀.
  - 음수 `HealthRegen` 사망 가능 경로와 지속 피해 사망 뒤 회복 부활 방지.
  - `ManaCost` 지불 후 다음 턴 `ManaRegen` 회복.
- Godot headless editor load/import:
  - 새 `[GlobalClass]` StatusElement 등록.
  - 캐릭터 씬 parse/import.
- Existing regression:
  - CB-001 Step 1~4.
  - SK-001 Step 3/6 중 마나 게이트와 데이터 팩 관련 테스트.

## Risks

- `ArticleStatus.StatusElementsDictionary.Add`는 중복 타입이 있으면 예외가 난다. production scene에 새 스탯을 수동 추가할 때 중복을 피해야 한다.
- `Health.CurrentHealth`는 `value <= 0`에서 `Owner.Dead()`를 호출한다. 음수 `HealthRegen`은 사망/QueueFree 순서 회귀를 만들 수 있으므로 별도 fixture가 필요하다.
- `battle_field.tscn`은 각 캐릭터 인스턴스의 `ArticleStatus`를 로컬 sub-resource로 override한다. 캐릭터 씬에 스탯을 추가해도 전투 경로에는 반영되지 않으므로, 스탯 추가 시 override 배열 5개(Ally 1 + Opponent 4)를 함께 고쳐야 한다. 이 override 구조 자체가 스탯 추가를 깨지기 쉽게 만든다(후속 정리 후보).
- 자연 회복 적용 순서가 독/출혈 같은 지속 피해와 게임 디자인 의미를 바꿀 수 있다.
- production 캐릭터 씬 수정은 `.tscn` 재직렬화 범위가 커질 수 있다. 필요하면 수동 patch나 Godot 저장 경로 중 재직렬화가 작은 쪽을 선택한다.
- `ArticleStatus.ApplyAffectingStatuses()`는 `AffectingStatusesList`를 `foreach`로 순회하는데, 만료(`Cost == 0`)된 `StatusAffect`가 `OnAffectedEnd` → `RemoveAffectStatus()`로 같은 리스트를 수정한다. 현재 `IAffectedOnlyMyTurn`/`IAffectedUntilTheEnd` production 구현이 없어 드러나지 않았지만, 첫 구현이 들어오는 즉시 `InvalidOperationException`이 난다. ST-001 범위 밖이며 별도 Task 후보다.

## Step 1 Implementation Result

상태: **superseded by [[#Step 1 Rework Result]]**. 아래는 rework 이전(구체 타입 분기) 구현 기록이다. 구조 검토에서 `ArticleStatus`가 `HealthRegen`/`ManaRegen` 구체 타입 처리를 직접 소유하는 문제가 확인돼 `ITurnStartStatusElement` dispatch로 재작업했다.

변경 파일:

- `Assets/Script/Article/Status/Element/HealthRegen.cs`: `[GlobalClass, Tool]` 정수 `Value` 스탯 추가(기본 0).
- `Assets/Script/Article/Status/Element/ManaRegen.cs`: 동일 형태의 마나 회복 스탯 추가.
- `Assets/Script/Article/Status/ArticleStatus.cs`: `ApplyNaturalRegen()`, `HasLivingHealth()`, private `TryGetStatus<T>()` 추가. **Rework 필요:** `ApplyNaturalRegen()`이 `HealthRegen`/`ManaRegen` 구체 타입을 직접 알고 있어 `ITurnStartStatusElement` dispatch로 바꾼다.
- `Assets/Script/Article/CharacterArticle.cs`: `ApplyTurnStartEffects()`를 `OnTurnStart()` → `ApplyNaturalRegen()` → 살아 있으면 `ApplyAffectingStatuses()` 순서로 변경.
- `Assets/Script/Tests/St001Step1NaturalRegenTest.cs`, `st001_step1_natural_regen_test.tscn`: Step 1 headless 검증.

구현 내용:

- 회복은 `Health.CurrentHealth` / `Mana.CurrentMana` setter를 통해 적용하므로 clamp, signal, HealthBar 갱신, 사망 처리를 기존 정책 그대로 재사용한다.
- `ApplyNaturalRegen()`은 진입 시와 체력 회복 직후에 `HasLivingHealth()`를 확인한다. 음수 `HealthRegen`으로 사망하면 같은 hook 안에서 마나 회복을 적용하지 않는다.
- `CharacterArticle.ApplyTurnStartEffects()`는 자연 회복으로 사망한 유닛에 지속 효과를 적용하지 않는다. ADR-019의 "사망을 같은 hook 안에서 되돌리지 않는다"를 순서 + 가드 두 겹으로 만족한다.
- `HasLivingHealth()`는 `Health`가 없으면 true를 돌려준다. `ArticleBase.IsAlive`와 달리 dictionary indexer 예외 없이 동작하므로 스탯 일부만 가진 fixture/임시 캐릭터에서 fail-closed 대신 no-op으로 닫힌다.
- 누락 스탯(`Health` 없이 `HealthRegen`, `Mana` 없이 `ManaRegen`)과 `Value == 0`은 모두 no-op이다. RNG를 소비하지 않는다.
- production character scene은 수정하지 않았다. 테스트는 `StatusElementsDictionary`에 직접 스탯을 주입하는 기존 SK 테스트 패턴을 따른다(설계 리뷰 [P3]의 export 배열 왕복 검증은 Step 2 범위).

검증 결과:

- `dotnet build` (Debug, `TOOLS` 정의) — 경고 0, 오류 0. 새 테스트가 출력 어셈블리에 포함되는 것까지 확인했다.
- **미실행**: `st001_step1_natural_regen_test.tscn`, `godot --headless --path . --import`, CB-001 Step 1~4 및 SK-001 회귀.
  이 개발 머신에 Godot 4.6.3 Mono 실행 파일이 설치돼 있지 않아 headless 실행 경로를 하나도 돌리지 못했다.
  Godot 바이너리가 있는 환경에서 아래를 실행해야 Step 1 완료 판정이 가능하다.

```powershell
godot --headless --path . --import
godot --headless --path . res://Assets/Script/Tests/st001_step1_natural_regen_test.tscn
godot --headless --path . res://Assets/Script/Tests/cb001_step1_turn_effect_test.tscn
godot --headless --path . res://Assets/Script/Tests/sk001_step3_mana_hit_test.tscn
```

테스트가 덮는 완료 조건:

- `[A0]` 턴 시작 1회당 Health/Mana가 각각 `Value`만큼 정확히 회복된다.
- `[A]` Max clamp 보존, 반복 호출 시에도 clamp 유지.
- `[B]` `Health`/`Mana`/regen 스탯 누락과 `Value == 0`에서 no-op.
- `[B2]` 음수 `ManaRegen` 감소와 0 clamp.
- `[C]` 음수 `HealthRegen` 사망, `OnDead` 1회, 사망 후 마나 회복 스킵.
- `[D]` `CharacterArticle` 턴 시작 순서(regen → affect)와 지속 피해 사망/자연 사망 이후 지속 효과 스킵.
- `[E]` `TurnHelper` running frame 반복과 `Speed=1/4`에서 회복 횟수가 1회로 고정된다.

남은 위험:

- Godot headless 실행을 하지 못해 새 `[GlobalClass]` 등록, `.tscn` 파싱, 기존 회귀 통과 여부가 정적 빌드 수준에서만 확인됐다.
- `ArticleBase.IsAlive`는 `StatusElementsDictionary[typeof(Health)]` indexer를 쓰므로 `Health`가 없는 Article에서 `KeyNotFoundException`을 던진다. 기존 결함이며 `ArticleStatus.HasLivingHealth()`와 개념이 중복된다. Step 1 범위 밖이라 손대지 않았고, 후속 cleanup 후보로 남긴다(P3).
- production 배선(Step 2)과 문서/완료 리뷰(Step 3)는 미착수다.
- `ManaCost` 지불 후 다음 턴 `ManaRegen` 회복 검증은 Task Verification Plan에 있으나, production/skill fixture가 필요해 Step 2에서 다룬다.

## Step 1 Design Rework Requirement

사용자/구조 검토 결과, Step 1의 현재 구현은 동작은 맞지만 책임 경계가 충분히 좋지 않다.

문제:

- `ArticleStatus.ApplyNaturalRegen()`이 `HealthRegen`/`ManaRegen`을 직접 알고 처리한다.
- 이 방식은 새 turn-start 스탯이 늘어날 때마다 `ArticleStatus`에 개별 스탯 로직이 쌓인다.
- 이는 `StatusElement`를 분리해 각 스탯이 자기 의미를 갖게 하려는 설계 의도와 어긋난다.

수정 방향:

- Step 1 구현은 리뷰 전에 `ITurnStartStatusElement` 기반으로 재작업한다.
- `HealthRegen`/`ManaRegen`이 자기 회복 적용 로직을 직접 가진다.
- `ArticleStatus`는 status 조회와 turn-start dispatch만 담당한다.
- 기존 Step 1 테스트는 유지하되, `ArticleStatus`에 `HealthRegen`/`ManaRegen` 구체 타입 분기가 없는지 구조 검증을 추가한다.

## Step 1 Rework Result

상태: **reworked, review needed**. 기능 동작은 유지하고 책임 경계만 재정렬했다. 자체 완료 판정은 하지 않는다.

변경 파일:

- `Assets/Script/Article/Status/Element/ITurnStartStatusElement.cs` (신규): `int TurnStartOrder`, `void ApplyTurnStart(ArticleStatus status)` 계약.
- `Assets/Script/Article/Status/Element/HealthRegen.cs`: `ITurnStartStatusElement` 구현. `TurnStartOrder = 100`, `Health`를 스스로 조회해 `CurrentHealth += Value`.
- `Assets/Script/Article/Status/Element/ManaRegen.cs`: `ITurnStartStatusElement` 구현. `TurnStartOrder = 110`, `Mana`를 스스로 조회해 `CurrentMana += Value`.
- `Assets/Script/Article/Status/ArticleStatus.cs`: `ApplyNaturalRegen()` 제거 → `ApplyTurnStartStatusElements()` dispatch. `TryGetStatus<T>()`를 public `TryGetStatusElement<T>()`로 승격(스탯이 대상 스탯을 조회하는 창구).
- `Assets/Script/Article/CharacterArticle.cs`: `ApplyTurnStartEffects()`는 순서만 유지. `OnTurnStart()` → `ApplyTurnStartStatusElements()` → 살아 있으면 `ApplyAffectingStatuses()`.
- `Assets/Script/Tests/St001Step1NaturalRegenTest.cs`: 기존 A0/A/B/B2/C/D/E 케이스를 새 API로 갱신하고 확장성/순서 케이스 F, G 추가.

책임 경계:

- `CharacterArticle`: 턴 시작 순서만 오케스트레이션.
- `ArticleStatus`: 보관, 타입 조회, 생존 guard, `ITurnStartStatusElement` dispatch.
- `HealthRegen`/`ManaRegen`: 대상 스탯과 변경 방식 소유.

dispatch 규칙:

- `StatusElementsDictionary.Values` 중 `ITurnStartStatusElement`를 `TurnStartOrder` 오름차순으로 실행한다. Dictionary 열거 순서에 의존하지 않도록 동점자는 타입 `FullName` ordinal로 고정한다(결정론).
- 각 스탯 실행 직전에 `HasLivingHealth()`를 확인한다. 앞선 스탯이 사망을 만들면 뒤 스탯은 같은 hook에서 실행되지 않는다. `HealthRegen(100) < ManaRegen(110)`이므로 기존 "음수 HealthRegen 사망 → ManaRegen 스킵" 동작이 그대로 유지된다.
- `Value == 0`과 대상 스탯 누락 no-op 판단은 각 regen 스탯 내부로 이동했다. `ArticleStatus`는 값 의미를 모른다.

검증 결과:

- `dotnet build` (Debug, `TOOLS`) — 경고 0, 오류 0.
- `ApplyNaturalRegen`/구 `TryGetStatus` 잔여 호출자 없음(`grep`), `ArticleStatus.cs`/`CharacterArticle.cs`에 `HealthRegen`/`ManaRegen` 식별자 없음(`grep`) → 완료 조건 "ArticleStatus에 구체 타입 직접 참조 없음" 충족.
- 새 테스트 케이스:
  - `[F]` 테스트 전용 `TurnStartProbe`(order 90/200)를 `ArticleStatus` 수정 없이 추가해 `early → HealthRegen → ManaRegen → late` 순서와 회복 결과를 검증. order 90 probe는 회복 전 HP, order 200 probe는 회복 후 HP를 관측한다.
  - `[G]` 음수 `HealthRegen` 사망 시 order 200 커스텀 스탯이 같은 hook에서 실행되지 않음.
- **미실행**: `st001_step1_natural_regen_test.tscn`, `godot --headless --path . --import`, CB-001 Step 1~4, SK-001 Step 3/6 회귀. 이 머신에 Godot 4.6.3 Mono 실행 파일이 없다. CB-001/SK-001은 정적으로 변경 API의 외부 호출자가 없음(`ApplyNaturalRegen` 미참조, `CharacterArticle.ApplyTurnStartEffects()` 시그니처/순서 불변)만 확인했다.

남은 위험:

- 위 headless 실행 미검증. `[GlobalClass]` 재등록과 `.tscn` 파싱, CB-001/SK-001 회귀는 Godot 환경에서 확인해야 한다.
- `ITurnStartStatusElement`는 `[GlobalClass]` Resource가 아니라 순수 C# interface다. `.tres` 직렬화 영향은 없지만, GDScript 쪽에서 같은 계약을 구현할 수는 없다(현재 StatusElement는 모두 C#).
- `TurnStartOrder`가 각 클래스 하드코딩 상수다. 스탯이 늘어나면 order 표를 한곳에서 볼 수 없다. 필요해지면 상수 클래스로 모은다(P3).
- `ArticleBase.IsAlive`의 indexer 예외 결함과 `HasLivingHealth()` 개념 중복은 여전히 남아 있다(Step 1 범위 밖, P3).

## Step 1 Rework Review Fixes

리뷰 판정 **미완료 / Rework required**에 대한 처리 결과다. 제품 코드는 바꾸지 않았다. 두 지적 모두 테스트 fixture 결함이다.

### [P1] 전용 headless 테스트 3개 assertion 실패 - 수정

- 원인: `TestHealthDeltaAffect`가 `IAffectedImmediately`였다. `ArticleStatus.ApplyAffectStatus()`는 즉시 효과를 그 자리에서 적용하고 `AffectingStatusesList`에 넣지도 않는다. 그래서 `[D]`의 피해가 턴 시작 hook 이전(fixture 준비 시점)에 이미 적용됐고, `D.DeadOnce`/`D.NaturalDeath`/`D.AffectSkippedAfterNaturalDeath`가 깨졌다.
- 수정: fixture를 `IAffectedOnlyMyTurn` 기반으로 교체했다. 이제 `ApplyAffectStatus()`는 리스트 등록만 하고, 실제 적용은 `ApplyAffectingStatuses()`에서 일어난다. 이것이 "턴 시작 지속 효과"의 실제 경로다.
- 부수 조치: `MasterCost => 99`. `MasterCost = 1`이면 첫 `Apply()`에서 `Cost == 0` → `AffectedEnd()` → `OnAffectedEnd` → `RemoveAffectStatus()`가 `ApplyAffectingStatuses()`의 `foreach` 도중 `AffectingStatusesList`를 수정해 `InvalidOperationException`이 난다. 테스트는 1턴만 관찰하므로 만료되지 않게 뒀다. **이 열거 중 수정 문제는 제품 코드의 잠재 결함**이며 별도 Task 후보로 남긴다(아래 Risks 참고).
- 회귀 방지: `[D]`는 이제 regen(+5) → 지속 피해(-12) → 사망 순서를 실제 `ApplyAffectingStatuses()` 경로로 검증한다. 자연 사망(`[D]` 두 번째 fixture)에서는 지속 효과가 0회 적용된다.

### [P2] HealthBar method-not-found 에러 반복 출력 - 수정

- 원인: `MakeArticle()`이 plain `ProgressBar`를 `HealthBar`로 넣는데, `Health`는 `init_health`/`_set_health`를 `Call`한다.
- 수정: `Assets/Script/Tests/test_health_bar.gd` test double을 추가하고 `GDScript.New()`로 인스턴스화해 붙였다. production `healthbar.gd`는 `$DamageBar` child와 `get_tree()` 타이머(`await`)에 의존해 SceneTree 밖 fixture에서 쓸 수 없으므로, 계약(`init_health`/`_set_health`)만 만족하는 double을 택했다.
- 회귀 방지: `Health` 회복/피해 경로가 매번 두 메서드를 호출하므로, 계약이 깨지면 double이 함께 깨진다.

검증 결과:

- `dotnet build` — 경고 0, 오류 0.
- **미실행**: 이 머신에는 여전히 Godot 4.6.3 Mono 실행 파일이 없어 `st001_step1_natural_regen_test.tscn` 재실행으로 3개 assertion 복구를 확인하지 못했다. 리뷰어 환경에서 재검증이 필요하다.
  같은 이유로 `GDScript.New()` 기반 HealthBar double의 실제 로드/호출도 정적으로만 확인됐다.

## Step 1 Rework Review Fix Verification

[[ST-001-Natural-Regen-Stats-Review]] Step 1 Rework Fix Verification 판정: **수정 후 완료**.

처리 결과:

- [P1] `TestHealthDeltaAffect`를 `IAffectedOnlyMyTurn` fixture로 바꿔 turn-start 지속 효과 경로를 실제로 검증하게 했다.
- [P2] `test_health_bar.gd` test double로 `Health`가 호출하는 `init_health`/`_set_health` 계약을 만족시켜 headless ERROR 로그를 제거했다.

검증:

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0(기존 종료 로그 남음).
- `st001_step1_natural_regen_test.tscn` — ALL PASS.
- CB-001 Step 1~4 — ALL PASS.
- SK-001 Step 3/6 — ALL PASS.

남은 후속:

- Step 2 production 캐릭터 `Mana`/`ManaRegen`/`HealthRegen` 배선.
- `ArticleStatus.ApplyAffectingStatuses()`의 foreach 중 affect 만료 제거 문제는 별도 Task 후보.
## Step 2 Implementation Result

상태: **implemented, review needed**. 자체 완료 판정은 하지 않는다.

변경 파일:

- `Assets/Scenes/Character/PrincessKnight.tscn`, `Puppet.tscn`, `Temp/TempArticle2.tscn`, `Temp/TempArticle3.tscn`: `ArticleStatus.StatusElements` 배열에 `Mana`/`ManaRegen`/`HealthRegen` sub-resource 추가.
- `Assets/Script/Tests/St001Step2ProductionWiringTest.cs`, `st001_step2_production_wiring_test.tscn`: Step 2 headless 검증.

배선한 수치(Task Production Defaults 표와 일치):

| Unit | MaxMana | ManaRegen | HealthRegen |
| --- | ---: | ---: | ---: |
| PrincessKnight | 60 | 8 | 0 |
| Puppet | 30 | 3 | 0 |
| TempArticle2 | 40 | 5 | 0 |
| TempArticle3 | 60 | 8 | 0 |

구현 판단:

- `.tscn`은 Godot 저장이 아니라 **수동 patch**로 편집했다. Risk에 적힌 대로 에디터 저장은 씬 전체 재직렬화(포맷 드리프트, BT-001 Step 3에서 실제로 겪은 문제)를 만들 수 있다. 추가한 줄은 파일당 ext_resource 3줄 + sub_resource 3블록 + `StatusElements` 배열 항목 3개뿐이다.
- 기존 스탯과 동일하게 `resource_local_to_scene = true`를 붙였다. 그렇지 않으면 같은 씬을 여러 번 인스턴스화하는 `Puppet`(battle_field에 4개)이 `Mana`를 공유한다.
- `HealthRegen = 0`으로 배선했다. 회복량이 0이면 no-op이므로 CB-001 결정론 회귀의 HP/RNG 흐름이 바뀌지 않는다. `Mana`/`ManaRegen`은 legacy TurnAction이 마나를 보지 않으므로 전투 결과에 영향이 없다. 따라서 **CB-001 기대 로그 갱신은 불필요할 것으로 예상**하지만, 실제 실행으로 확인해야 한다.
- `load_steps`는 파일당 +6으로 올렸다(`TempArticle3.tscn`은 원래 `load_steps`가 없어 그대로 뒀다).

검증 결과:

- `dotnet build` (Debug, `TOOLS`) — 경고 0, 오류 0.
- 정적 확인: 4개 씬 모두 새 `ext_resource` id 3개 중복 없음, `Resource_st001_*` sub-resource 3개, `StatusElements` 배열에 각 1회만 등장.
- 새 테스트 `st001_step2_production_wiring_test.tscn`가 덮는 완료 조건:
  - `[A]` export 배열에 타입 중복 없음(`StatusElementsDictionary.Add` 예외 방지), `InitStatus()` 통과, 4개 스탯 존재와 수치 일치.
  - `[B]` `InitStatus()` 직후 `Mana.CurrentMana == MaxMana`. 턴 시작 hook 1회당 `ManaRegen`만큼 1회 회복, Max clamp 보존, `HealthRegen = 0`이라 HP 불변.
  - `[C]` `ResourceSaver.Save` → `CacheMode.Ignore` 재로드 왕복 후에도 새 스탯/수치 보존, 중복 없음(설계 리뷰 [P3] 요구).
  - `[D]` `battle_field.tscn`의 Ally(TempArticle3)가 fixture `AddMana` 없이 production `Mana`로 `ManaCost=5` `TurnAction_Skill`을 지불하고, 다음 턴 시작에 `ManaRegen`만큼 회복.
- **미실행**: 이 머신에 Godot 4.6.3 Mono 실행 파일이 없어 아래를 하나도 돌리지 못했다.

```powershell
godot --headless --path . --import
godot --headless --path . res://Assets/Script/Tests/st001_step2_production_wiring_test.tscn
godot --headless --path . res://Assets/Script/Tests/st001_step1_natural_regen_test.tscn
godot --headless --path . res://Assets/Script/Tests/cb001_step4_determinism_test.tscn
godot --headless --path . res://Assets/Script/Tests/sk001_step3_mana_hit_test.tscn
godot --headless --path . res://Assets/Script/Tests/sk001_step6_data_pack_test.tscn
```

남은 위험:

- 수동 patch한 `.tscn` 4개의 실제 파싱/import를 확인하지 못했다. `load_steps` 값과 sub-resource 선언 순서가 틀리면 로드가 깨진다(sub-resource는 참조하는 `ArticleStatus` 블록보다 앞에 두었다).
- `Sk001Step3ManaHitTest`/`Sk001Step6DataPackTest`는 `StatusElementsDictionary[typeof(Mana)] = ...` 인덱서로 fixture Mana를 주입한다. production `Mana`가 생겨도 덮어쓰기라 예외는 없지만, 실행으로 확인해야 한다.
- CB-001 Step 1~4 결정론 회귀 미실행. HP/RNG 불변이라는 근거는 정적 추론뿐이다.
- 전투 UI 마나 바는 여전히 없다(Out of Scope). `Mana`는 데이터에만 존재한다.

## Step 2 Code Review

[[ST-001-Natural-Regen-Stats-Review]] Step 2 Code Review 판정: **미완료 / Rework required**.

지적 사항:

- [P1] `battle_field.tscn`의 캐릭터 인스턴스들이 로컬 `ArticleStatus` sub-resource를 override하고 있어, 원본 `TempArticle3.tscn`/`Puppet.tscn`에 추가한 `Mana`/`ManaRegen`/`HealthRegen`이 실제 전투 씬에 전파되지 않는다. `st001_step2_production_wiring_test.tscn`의 `D.CasterHasProductionMana`가 실패했다.
- [P1] Step 2 테스트 [B]의 2회 회복 기대값이 `Mana.CurrentMana` clamp 정책과 충돌한다. `PrincessKnight`/`TempArticle3`는 두 번째 회복에서 MaxMana 60으로 clamp되어야 하는데 테스트가 66을 기대한다.

검증:

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0.
- `st001_step2_production_wiring_test.tscn` — FAIL, 3 assertions failed.

수정 후 다시 Step 2 리뷰를 요청한다.
## Step 2 Rework Result

리뷰 판정 **미완료 / Rework required**(P1 2건)에 대한 처리 결과다. 자체 완료 판정은 하지 않는다.

### [P1] battle_field.tscn 인스턴스에 Mana/Regen 없음 - 수정

- 원인: `battle_field.tscn`이 각 캐릭터 인스턴스의 `ArticleStatus`를 **로컬 sub-resource로 override**한다(`ArticleStatus = SubResource("Resource_f5ues")` 등 5개). 원본 캐릭터 씬의 `StatusElements`를 고쳐도 override 배열이 이기므로 실제 전투 경로에는 `Mana`가 없었다. Step 2 구현은 원본 4개 씬만 보고 override 존재를 놓쳤다.
- 수정: `battle_field.tscn`의 override `ArticleStatus` 5개(Ally 1 + Opponent 4) 모두에 `Mana`/`ManaRegen`/`HealthRegen` sub-resource를 추가했다. Ally는 TempArticle3 기준 60/8/0, Opponent 4명은 Puppet 기준 30/3/0. 원본 씬과 동일하게 수동 patch + `resource_local_to_scene = true`.
- 회귀 방지: 테스트 `[D]`가 원본 씬이 아니라 **`battle_field.tscn`에서 인스턴스화된 실제 전투 Article 5개 전부**를 순회하며 세 스탯의 존재와 수치, `CurrentMana == MaxMana`를 확인한다. 앞으로 override를 빠뜨리면 `[D]`가 먼저 깨진다.

### [P1] 2회 회복 기대값이 max clamp와 충돌 - 수정

- 원인: `[B]`가 `MaxMana - 10`에서 시작해 2턴 회복을 기대했다. PrincessKnight/TempArticle3(regen 8)는 `50 + 16 = 66`이 되어 `Mana.CurrentMana`의 `0..MaxMana` clamp에 걸린다. clamp가 옳고 기대값이 틀렸다.
- 수정: 시작값을 `MaxMana - (ManaRegen * 2 + 1)`로 잡아 2턴을 회복해도 Max에 닿지 않게 했다. 모든 캐릭터 수치에서 성립한다(PK 43→51→59, Puppet 23→26→29, TempArticle2 29→34→39).
- clamp 자체는 같은 `[B]` 안의 `ManaClampedAtMax`가 계속 검증한다(`MaxMana - 1`에서 1턴 회복 → `MaxMana`).

검증 결과:

- `dotnet build` — 경고 0, 오류 0.
- 정적: `battle_field.tscn`에 `Resource_st001_*` sub-resource 15개(5 인스턴스 × 3 스탯), `MaxMana` 60/30/30/30/30, ext id 중복 없음.
- **미실행**: 이 머신에 Godot 실행 파일이 없어 `st001_step2_production_wiring_test.tscn` 재실행과 `--import`, CB-001/SK-001 회귀를 확인하지 못했다. 리뷰어 환경에서 재검증이 필요하다.
- CB-001 결정론 회귀 예상: 추가 스탯은 `HealthRegen = 0`(no-op)과 legacy TurnAction이 읽지 않는 `Mana`뿐이라 HP/RNG 흐름은 바뀌지 않는다. 다만 이번에는 `battle_field.tscn` 자체를 수정했으므로 `cb001_step4_determinism_test` 실행이 필수다.

## Step 2 Rework Fix Verification

[[ST-001-Natural-Regen-Stats-Review]] Step 2 Rework Fix Verification 판정: **수정 후 완료**.

처리 확인:

- [P1] `battle_field.tscn`의 로컬 `ArticleStatus` override 5개에 `Mana`/`ManaRegen`/`HealthRegen` 배선이 추가됐고, 전용 테스트 `[D]`가 실제 전투 인스턴스 5개 전부를 검증한다.
- [P1] Step 2 테스트 `[B]`의 2회 회복 시작값이 max clamp에 걸리지 않도록 수정됐고, max clamp는 별도 assertion으로 유지된다.

검증:

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0.
- `st001_step2_production_wiring_test.tscn` — ALL PASS.
- `st001_step1_natural_regen_test.tscn` — ALL PASS.
- CB-001 Step 1~4 — ALL PASS.
- SK-001 Step 3/6 — ALL PASS.

남은 후속:

- Step 3 문서/완료 리뷰.
- `battle_field.tscn`의 캐릭터별 `ArticleStatus` override 구조 정리는 별도 후속 후보.
## Step 3 Implementation Result

상태: **complete**. 판정은 [[ST-001-Natural-Regen-Stats-Review]] "ST-001 Completion Review (Step 3)"에 있다.

제품 코드와 리소스는 수정하지 않았다. 문서만 현재 사실로 갱신했다.

갱신한 문서:

- [[Article-Status-System]]: `ITurnStartStatusElement` 계약과 dispatch 규칙, `HealthRegen`/`ManaRegen` order 표, 실패 정책, production 배선 표, `battle_field.tscn` override 주의, 검증 자산 2종. Known Risks에 `IsAlive` indexer 예외, `resource_local_to_scene` 필요성, `ApplyAffectingStatuses()` 열거-중-수정 결함 추가.
- [[Turn-System]]: 턴 시작 hook을 3단계 순서(`OnTurnStart` → turn-start 스탯 → 지속 효과)로 명시. 자연 회복이 RNG를 소비하지 않아 결정론 시퀀스를 바꾸지 않음. `st001_step1` 검증 자산 추가.
- [[Skill-System]]: `Mana` production 배선 "후속" → "ST-001 Step 2에서 완료"로 사실 갱신.
- [[Current-State]], [[Open-Tasks]]: ST-001 완료 반영.
- [[ST-001-Natural-Regen-Stats-Review]]: 완료 리뷰 추가, frontmatter `status: complete`.

### 문서 리뷰 지적 처리

**[P2] `Current-State`에 오래된 후속 문구 잔존 - 수정.**

- SK-001 Step 3 항목이 "Mana의 production scene 배선과 무대상 fail-closed는 후속"이라고 적고 있어, 같은 문서의 ST-001 완료 항목 및 [[Skill-System]]과 충돌했다.
- 당시 사실과 현재 사실을 구분해 다시 썼다: "당시 `Mana`의 production scene 배선은 후속이었고, 현재는 ST-001 Step 2에서 완료됐다. 무대상 fail-closed는 여전히 후속."
- 회귀 방지: `production scene 배선`/`production character scene`/`미배선` 문구를 `00_Index`와 `20_Systems` 전체에서 재검색해 다른 잔존 문구가 없음을 확인했다.

Step 3 완료 조건:

- 회복 스탯 모델, 적용 타이밍, production 배선 상태, 검증 결과가 모두 문서화됐다.
- P0/P1 리뷰 문제 없음. 문서 리뷰 P2 1건은 수정 완료. 남은 항목은 P3 3건과 별도 Task 1건이다.

## Follow-ups

- ~~**[별도 Task]** `ArticleStatus.ApplyAffectingStatuses()` 열거 중 리스트 수정~~ → **수정 완료**(2026-07-08, 별도 Task 없이 처리). `AffectingStatusesList.ToArray()` snapshot 순회. 회귀 테스트 `st001_step1_natural_regen_test` `[H]`: `MasterCost = 1` 효과가 적용 중 만료돼도 예외 없이 같은 턴 1회 적용 후 목록에서 빠지고, 남은 효과는 다음 턴에 계속 적용된다.
- ~~**[P3]** `ArticleBase.IsAlive` indexer 예외와 `ArticleStatus.HasLivingHealth()` 개념 중복 정리.~~ → **수정 완료**(2026-07-08, 별도 Task 없이 처리). `IsAlive => ArticleStatus.HasLivingHealth()` 위임. 호출부 17곳 무변경, `Health`를 가진 Article에서 동작 동일. 회귀 테스트 `st001_step1_natural_regen_test` `[I]`.
- **[P3]** `battle_field.tscn` per-instance `ArticleStatus` override 구조 정리(같은 수치 5중 복제).
- **[P3]** `TurnStartOrder` 상수 집중화.
- 전투 UI 마나 바/회복량 표시(Out of Scope).

## Step 3 Documentation Review

[[ST-001-Natural-Regen-Stats-Review]] Step 3 Documentation Review 판정: **수정 필요**.

지적 사항:

- [P2] `LLM_WIKI/00_Index/Current-State.md`의 SK-001 Step 3 기록에 "Mana의 production scene 배선과 무대상 fail-closed는 후속"이라는 오래된 문구가 남아 있다. ST-001 Step 2에서 `Mana` production 배선은 완료됐으므로 현재 사실과 충돌한다.

수정 후 다시 Step 3 확인을 요청한다.
## Step 3 Documentation Review Fix Verification

[[ST-001-Natural-Regen-Stats-Review]] Step 3 Documentation Review Fix Verification 판정: **수정 후 완료**.

처리 확인:

- [P2] `Current-State.md`의 SK-001 Step 3 문구가 당시 사실과 현재 사실을 구분하도록 수정됐다. `Mana` production scene 배선은 ST-001 Step 2에서 완료됐고, 무대상 fail-closed만 후속으로 남는다.

ST-001 상태는 **complete**로 유지한다.
## Related

- [[ADR-019-Natural-Regen-Stats]]
- [[ST-001-Natural-Regen-Stats-Review]]
- [[Article-Status-System]]
- [[Turn-System]]
- [[Skill-System]]
- [[SK-001-Data-Driven-Skill-System]]
- [[CB-001-Deterministic-Combat-Resolution]]








## Post-Completion Follow-up Verification (2026-07-08)

ST-001 완료 리뷰 직후 처리한 두 후속 수정의 Godot headless 검증을 추가로 수행했다.

검증 대상:

- `ArticleStatus.ApplyAffectingStatuses()` snapshot 순회(`AffectingStatusesList.ToArray()`): 회귀 `[H]`.
- `ArticleBase.IsAlive => ArticleStatus.HasLivingHealth()` 위임: 회귀 `[I]`.

검증 결과:

- `dotnet build AutoCrawler.sln -c Debug` — PASS, warnings 0, errors 0.
- `godot --headless --path . --import` — exit 0. 기존 `graph_offset deprecated`, ObjectDB/resource leak 종료 로그만 출력.
- `st001_step1_natural_regen_test.tscn` — ALL PASS. `[H]`/`[I]` 포함.
- CB-001 Step 1~4 — ALL PASS. Step 4의 기존 `Walk`/`Attack` animation missing 로그는 재현되나 판정 PASS.
- SK-001 Step 3/6 — ALL PASS. 기존 ObjectDB 종료 경고만 출력.

판정: 남아 있던 headless 검증 공백은 닫혔다. ST-001 상태는 **complete**로 유지한다.
