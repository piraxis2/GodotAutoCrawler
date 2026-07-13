using System;
using System.Collections.Generic;
using System.Linq;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Article.Status.Element;
using Godot;

namespace AutoCrawler.Assets.Script.Battle;

// 전투 한 번의 수명 주기 소유자(ADR-022). BattleRequest로 전투 씬을 생성·소유하고 명시적 Start로 시작하며,
// 승패를 판정해 값 결과(BattleResult)를 정확히 한 번 돌려준 뒤 씬·정적 context를 정리한다. 같은 프로세스에서
// 다음 전투를 독립적으로 다시 실행할 수 있다.
public partial class BattleSession : Node
{
    public enum SessionState { Created, Configured, Running, Completed }

    private SessionState _state = SessionState.Created;
    private BattleRequest _request;
    private BattleFieldScene _battleScene;
    private BattleResult _pendingResult;

    // 참가자 추적(초기 identity 기록 + 사망 event-identity 캡처). 사망 유닛이 queue_free된 뒤에도 결과를 보존한다.
    private readonly List<TrackedUnit> _units = new();
    private TrackedUnit _playerUnit;
    private int _aliveOpponents;
    private bool _completionPending;

    // 결과를 정확히 한 번 전달하는 C# event(ADR-022 §2). Godot Variant signal에 plain record를 넣지 않는다.
    public event Action<BattleResult> Completed;

    public SessionState State => _state;

    // SceneTree 진입 전 호출한다. request를 보관하고 Created -> Configured로 넘어간다. 중복 호출은 fail-closed.
    public void Configure(BattleRequest request)
    {
        if (_state != SessionState.Created)
        {
            GD.PushError($"BattleSession.Configure: Created 상태에서만 호출할 수 있습니다(현재 {_state}).");
            return;
        }

        _request = request;
        _state = SessionState.Configured;
    }

    // 명시적 시작. 유효하면 전투 씬을 생성·소유하고 TurnHelper를 시작한다. 실패하면 Aborted/InvalidSetup을
    // deferred로 정확히 한 번 완료한다(OD4: Start 콜스택 안에서 동기 완료하지 않는다).
    public void Start()
    {
        if (_state != SessionState.Configured)
        {
            GD.PushError($"BattleSession.Start: Configure 이후에만 호출할 수 있습니다(현재 {_state}).");
            return;
        }

        // 성공 시 TryBeginBattle이 StartBattle 전에 _state를 Running으로 전환한다(첫 턴 lethal 효과 감지, 리뷰 P1).
        if (!TryBeginBattle(out BattleEndReason failReason))
            CompleteAbortDeferred(BuildAbortResult(failReason));
    }

    // 성공 시 true. 실패 시 false + InvalidSetup 이유. 부분 생성된 씬은 정리하고 crash/SCRIPT ERROR 없이 닫는다.
    private bool TryBeginBattle(out BattleEndReason failReason)
    {
        failReason = BattleEndReason.InvalidSetup;

        // 단일 활성 세션 가드(ADR-022 §7): 이미 유효한 전투 컨텍스트가 있으면 덮어쓰지 않고 거부한다.
        if (GodotObject.IsInstanceValid(BattleFieldScene.BattleField))
            return false;

        // CanInstantiate 선검사: 빈/손상 PackedScene에 Instantiate를 호출하면 Godot이 엔진 ERROR를 남겨
        // 로그 감시/CI에서 오탐이 되므로, 구성 오류를 ERROR 없이 InvalidSetup으로 닫는다(Step 2 리뷰 P2).
        if (_request?.BattleScene == null || !_request.BattleScene.CanInstantiate())
            return false;

        var instance = _request.BattleScene.Instantiate();
        if (instance is not BattleFieldScene battleScene)
        {
            instance?.QueueFree();
            return false;
        }

        // 필수 노드 검증은 트리 진입 전에 가능한 만큼 수행한다. AutoStart를 끈 뒤 AddChild해야 TurnHelper가
        // 자동 시작하지 않는다(트리 진입 전 설정 = ADR-022 Start 순서).
        var turnHelper = battleScene.TurnHelper;
        var articles = battleScene.Articles;
        if (turnHelper == null || articles == null)
        {
            battleScene.QueueFree();
            return false;
        }

        turnHelper.AutoStart = false;
        _battleScene = battleScene;
        AddChild(battleScene); // _Ready: static context 설정 + ArticlesContainer dict 채움

        // 초기 진영: Ally/Opponent 모두 비어 있지 않아야 한다.
        if (articles.Articles["Ally"].Count == 0 || articles.Articles["Opponent"].Count == 0)
        {
            TeardownScene();
            return false;
        }

        // PC 해석: 전투 씬 루트 기준 경로. 없거나, Ally registry에 등록되지 않았거나, 턴 참가자가 아니면
        // fail-closed(제품 구성 오류를 Defeat로 위장하지 않음). 부모 이름이 아니라 ArticlesContainer 등록으로
        // 검증해야 컨테이너 밖 위조 "Ally" 노드를 막고 사망 추적/결과 캡처와 정합한다(Step 2 리뷰 P2).
        var player = battleScene.GetNodeOrNull<ArticleBase>(_request.PlayerPath);
        if (player == null
            || !articles.Articles["Ally"].Contains(player)
            || player is not ITurnAffectedArticle<ArticleBase>)
        {
            TeardownScene();
            return false;
        }

        // 참가자 준비 + 전투 시작 ammo 충전을 TurnHelper 밖(세션)에서 수행한다(ADR-022 §3 / SK-002 §4).
        var participants = articles.GetTurnParticipants().ToList();
        foreach (var participant in participants)
            if (participant is CharacterArticle character) character.ChargeBattleAmmo();

        // 초기 참가자 기록 + 사망 구독(StartBattle 전에 배선해 첫 턴 사망도 놓치지 않는다).
        TrackParticipants(participants, player);
        if (_playerUnit == null)
        {
            UnsubscribeParticipants();
            TeardownScene();
            return false;
        }

        // reflection 없이 seed 주입.
        turnHelper.Configure(participants, _request.Seed);

        // StartBattle은 첫 행동자의 ApplyTurnStartEffects(외부 코드)를 동기 실행하고, 여기서 lethal turn-start
        // 효과(예: 음수 HealthRegen)로 PC나 마지막 상대가 죽을 수 있다. 그 사망이 완료로 이어지도록 StartBattle
        // 전에 Running으로 전환한다(리뷰 P1: 그러지 않으면 OnParticipantDead가 _state != Running으로 조기 반환해
        // 완료가 영구 누락된다).
        _state = SessionState.Running;
        turnHelper.StartBattle();
        return true;
    }

    private void TrackParticipants(IEnumerable<ITurnAffectedArticle<ArticleBase>> participants, ArticleBase player)
    {
        _units.Clear();
        _playerUnit = null;
        _aliveOpponents = 0;
        _completionPending = false;

        ulong playerId = player.GetInstanceId();
        foreach (var participant in participants)
        {
            if (participant is not ArticleBase article) continue;

            string faction = article.GetParent()?.Name ?? string.Empty;
            var unit = new TrackedUnit
            {
                Article = article,
                SessionUnitId = $"{faction}:{article.SpawnIndex}",
                Faction = faction,
                IsPlayer = article.GetInstanceId() == playerId,
                IsOpponent = faction == "Opponent",
            };

            if (unit.IsOpponent) _aliveOpponents++;
            if (unit.IsPlayer) _playerUnit = unit;

            ArticleBase.OnDeadEventHandler handler = _ => OnParticipantDead(unit);
            article.OnDead += handler;
            unit.OnDeadHandler = handler;

            _units.Add(unit);
        }
    }

    // 사망 콜스택(물리/signal) 안에서 동기적으로는 tracking 갱신 + 완료 latch만 처리한다(Finding 4). 실제 판정·결과
    // 캡처·teardown·event는 deferred(Finding 1)라, 같은 프레임의 모든 사망이 기록된 뒤 outcome을 정한다.
    private void OnParticipantDead(TrackedUnit unit)
    {
        if (!unit.Dead)
        {
            unit.Dead = true;
            // Finding 2: Health setter가 clamp(0) 전에 Dead()를 emit → 라이브 HP/IsAlive는 아직 사망 전 값이다.
            // event identity를 진실로 취급: 사망 유닛은 HP 0, 위치는 아직 valid한 지금 값으로 기록한다.
            unit.FinalHealth = 0;
            unit.FinalTile = GodotObject.IsInstanceValid(unit.Article) ? unit.Article.TilePosition : unit.FinalTile;
            if (unit.IsOpponent) _aliveOpponents--;
        }

        if (_completionPending || _state != SessionState.Running) return;

        // 자체 bookkeeping으로 판정 조건을 본다(Finding 3): ArticlesContainer dict 제거 순서에 의존하지 않는다.
        if (_playerUnit.Dead || _aliveOpponents <= 0)
        {
            _completionPending = true;
            Callable.From(CompleteBattleDeferred).CallDeferred();
        }
    }

    private void CompleteBattleDeferred()
    {
        if (_state == SessionState.Completed) return;

        // OD5: mutual kill(같은 프레임 PC 사망 + 상대 전멸) 시 PC 사망을 먼저 평가한다(Defeat 우선). deferred라
        // 같은 프레임의 모든 사망이 기록된 뒤 판정하므로 detection 순서와 무관하게 결정적이다.
        BattleOutcome outcome;
        BattleEndReason reason;
        if (_playerUnit.Dead)
        {
            outcome = BattleOutcome.Defeat;
            reason = BattleEndReason.PlayerDefeated;
        }
        else
        {
            outcome = BattleOutcome.Victory;
            reason = BattleEndReason.OpponentsEliminated;
        }

        var result = BuildResult(outcome, reason); // 생존자 라이브 상태를 teardown 전에 읽는다

        _state = SessionState.Completed;
        _battleScene?.TurnHelper?.StopBattle();
        UnsubscribeParticipants();
        TeardownScene(); // RemoveChild → BattleFieldScene._ExitTree가 정적 context를 동기 null

        InvokeCompleted(result);
    }

    private BattleResult BuildResult(BattleOutcome outcome, BattleEndReason reason)
    {
        var units = new List<BattleUnitResult>(_units.Count);
        int survivingAllies = 0;
        int survivingOpponents = 0;

        foreach (var u in _units)
        {
            bool survived = !u.Dead;
            int? finalHealth;
            Vector2I finalTile;
            if (u.Dead)
            {
                finalHealth = u.FinalHealth; // event-identity(0)
                finalTile = u.FinalTile;
            }
            else
            {
                finalHealth = ReadHealth(u.Article);
                finalTile = GodotObject.IsInstanceValid(u.Article) ? u.Article.TilePosition : u.FinalTile;
            }

            if (survived && u.Faction == "Ally") survivingAllies++;
            if (survived && u.Faction == "Opponent") survivingOpponents++;

            units.Add(new BattleUnitResult
            {
                SessionUnitId = u.SessionUnitId,
                Faction = u.Faction,
                Survived = survived,
                FinalHealth = finalHealth,
                FinalTile = finalTile,
            });
        }

        return new BattleResult
        {
            Outcome = outcome,
            EndReason = reason,
            Seed = _request.Seed,
            PlayerAlive = _playerUnit is { Dead: false },
            SurvivingAllies = survivingAllies,
            SurvivingOpponents = survivingOpponents,
            Units = units,
        };
    }

    private static int? ReadHealth(ArticleBase article)
    {
        if (!GodotObject.IsInstanceValid(article)) return null;
        return article.ArticleStatus.StatusElementsDictionary.GetValueOrDefault(typeof(Health)) is Health health
            ? health.CurrentHealth
            : null;
    }

    private void UnsubscribeParticipants()
    {
        foreach (var u in _units)
            if (u.OnDeadHandler != null && GodotObject.IsInstanceValid(u.Article))
                u.Article.OnDead -= u.OnDeadHandler;
    }

    // 씬을 트리에서 떼고 free한다. RemoveChild가 BattleFieldScene._ExitTree를 동기 호출해 정적 context를 null로
    // 만들어, 완료 handler가 곧바로 다음 세션을 시작해도 stale singleton과 충돌하지 않는다.
    private void TeardownScene()
    {
        if (!GodotObject.IsInstanceValid(_battleScene))
        {
            _battleScene = null;
            return;
        }

        RemoveChild(_battleScene);
        _battleScene.QueueFree();
        _battleScene = null;
    }

    private BattleResult BuildAbortResult(BattleEndReason reason) => new()
    {
        Outcome = BattleOutcome.Aborted,
        EndReason = reason,
        Seed = _request?.Seed ?? 0,
        PlayerAlive = false,
        SurvivingAllies = 0,
        SurvivingOpponents = 0,
        Units = Array.Empty<BattleUnitResult>(),
    };

    // invalid setup 완료를 idle frame으로 미룬다(OD4). Start() 콜스택 안에서 동기 완료하지 않는다. 완료 latch
    // (_state == Completed)는 동기적으로 설정해 중복 완료를 no-op으로 만든다.
    private void CompleteAbortDeferred(BattleResult result)
    {
        if (_state == SessionState.Completed) return;

        _state = SessionState.Completed;
        _pendingResult = result;
        Callable.From(FireAbort).CallDeferred();
    }

    private void FireAbort()
    {
        var result = _pendingResult;
        _pendingResult = null;
        InvokeCompleted(result);
    }

    private void InvokeCompleted(BattleResult result)
    {
        var handlers = Completed;
        if (handlers == null) return;

        // subscriber별 예외 격리(OD2): 한 handler가 던져도 GD.PushError 후 나머지를 계속 호출한다.
        foreach (var handler in handlers.GetInvocationList().Cast<Action<BattleResult>>())
        {
            try { handler(result); }
            catch (Exception ex) { GD.PushError($"BattleSession.Completed handler threw: {ex}"); }
        }
    }

    private sealed class TrackedUnit
    {
        public ArticleBase Article;
        public string SessionUnitId;
        public string Faction;
        public bool IsPlayer;
        public bool IsOpponent;
        public bool Dead;
        public int? FinalHealth;
        public Vector2I FinalTile;
        public ArticleBase.OnDeadEventHandler OnDeadHandler;
    }
}
