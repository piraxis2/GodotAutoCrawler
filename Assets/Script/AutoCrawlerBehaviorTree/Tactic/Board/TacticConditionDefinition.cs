using Godot;

namespace AutoCrawler.Assets.Script.AutoCrawlerBehaviorTree.Tactic.Board;

// 조건 definition의 typed base(ADR-024 §2). 각 조건은 안정 TypeId를 가져 compiler registry가 지원 여부를
// 명시적으로 판단하고, validator/진단이 code/field로 행·필드를 가리킨다. TypeId는 C# 타입명이 아니라 안정
// 문자열이라 클래스 rename에도 저장 schema·진단·compiler 매핑이 깨지지 않는다.
//
// v1 어휘(기획 H-4 v0 일부): always / any_enemy / enemy_casting. TypeId는 계산 프로퍼티(비-Export)라
// .tres에 직렬화되지 않는다 — 직렬화되는 것은 concrete subtype(script)와 그 Export 필드다.
//
// 직렬화되는 C# Resource는 파일명=클래스명이어야 재로드된다(Godot C# script 매핑). 그래서 concrete 조건은
// 각각 별도 파일로 둔다(SkillSystem/Blocks 동형).
[GlobalClass, Tool]
public abstract partial class TacticConditionDefinition : Resource
{
    public abstract StringName TypeId { get; }
}
