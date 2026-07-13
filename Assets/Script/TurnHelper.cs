using System.Collections.Generic;
using System.Linq;
using AutoCrawler.addons.behaviortree;
using AutoCrawler.Assets.Script.Article;
using AutoCrawler.Assets.Script.Article.Interface;
using AutoCrawler.Assets.Script.Util;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class TurnHelper : Node
{
    // 턴 실행 수명 주기 상태(BS-001 Step 1). 신규 Session 경로는 명시적 Configure -> StartBattle로 진행하고,
    // 직접 씬 실행(battle_field.tscn)은 AutoStart=true로 _Ready에서 자동 부팅한다.
    private enum RunState { Idle, Configured, Running, Stopped }
    private RunState _runState = RunState.Idle;

    private readonly List<ITurnAffectedArticle<ArticleBase>> _turnAffectedArticleList = new();
    private ITurnAffectedArticle<ArticleBase> _currentTurnArticle;

    // OnDead 구독을 보관해 재구성/정지 시 해제한다(BS-001 Step 1 리뷰 P2). 보관하지 않으면 같은 참가자 객체로
    // 재-Configure할 때 handler가 누적된다.
    private readonly List<(ArticleBase Article, ArticleBase.OnDeadEventHandler Handler)> _deathSubscriptions = new();

    // 턴 순서 링의 정수 커서(BS-001 Step 1). 객체 IndexOf 대신 커서를 진실로 삼아, 현재 턴 유닛이 사망으로
    // 리스트에서 제거될 때 순번이 목록 처음으로 부당하게 리셋되던 결함을 없앤다.
    private int _turnCursor = -1;
    private bool _currentUnitRemoved;

    private FxPlayer _fxPlayer;
    private FxPlayer FxPlayer => _fxPlayer ??= BattleFieldScene.BattleField.FxPlayer;
    private readonly RandomNumberGenerator _combatRng = new();

    [Export] private ArticlesContainer _articlesContainer;
    [Export] private long _combatSeed = 1;

    // 직접 씬 실행 호환 seam(ADR-022 §2). true면 _Ready에서 참가자 수집·ammo 충전·시작을 자동 수행한다.
    // 신규 Session 경로는 false로 두고 Configure/StartBattle을 명시 호출한다. 모든 직접 실행 소비자 전환 뒤
    // 삭제 후보다(BS-001 Follow-ups).
    [Export] public bool AutoStart { get; set; } = true;

    // "더 진행할 행동자가 없음"의 턴 스케줄러 종료 신호다. 진영 공백 판정은 legacy 직접 실행(AutoStart) 경로에서만
    // 유지하고, 신규 명시 경로는 참가자 링 진행 가능 여부만 본다 — 승패/게임오버 의미는 BattleSession이 소유한다
    // (ADR-022 §3, BS-001 Step 1 리뷰 P2).
    private bool IsGameOver =>
        _turnAffectedArticleList.Count <= 1
        || _currentTurnArticle == null
        || (AutoStart && _articlesContainer != null
            && (_articlesContainer.Articles["Opponent"].Count == 0 || _articlesContainer.Articles["Ally"].Count == 0));
    private bool IsPaused => Speed == 0;

    [Export] public float Speed { get; private set; } = 1.0f;
    public long CombatSeed => _combatSeed;

    public override void _Ready()
    {
        if (!AutoStart) return;

        // 직접 씬 실행 호환 경로: 컨테이너에서 참가자를 수집하고 전투 시작 ammo를 충전한 뒤 명시 seam으로 시작한다.
        var participants = CollectContainerParticipantsAndChargeAmmo();
        Configure(participants, _combatSeed);
        StartBattle();
    }

    // 신규 Session 경로의 명시적 참가자 입력(BS-001 Step 1). seed를 적용하고 턴 순서를 초기화·구독한다.
    // ammo 충전과 참가자 발견은 U1에서 이 메서드가 소유하지 않는다(직접 실행 경로는 _Ready가 미리 수행).
    public void Configure(IEnumerable<ITurnAffectedArticle<ArticleBase>> participants, long seed)
    {
        if (_runState == RunState.Running)
        {
            GD.PushError("TurnHelper.Configure: Running 중에는 재구성할 수 없습니다.");
            return;
        }

        ClearDeathSubscriptions();
        ResetCombatRng(seed);
        _turnAffectedArticleList.Clear();
        _turnCursor = -1;
        _currentUnitRemoved = false;
        _currentTurnArticle = null;

        foreach (var participant in participants)
        {
            _turnAffectedArticleList.Add(participant);

            // 사망 시 턴 순서에서 제거(턴 스케줄러 책임). 승패 판정이 아니라 링 유지가 목적이다.
            // handler를 보관해 재구성/정지 시 해제한다(누적 방지).
            if (participant is ArticleBase articleBase)
            {
                ArticleBase.OnDeadEventHandler handler = _ => RemoveFromTurnOrder(participant);
                articleBase.OnDead += handler;
                _deathSubscriptions.Add((articleBase, handler));
            }
        }

        SortTurnAffectedArticles();
        _runState = RunState.Configured;
    }

    // 턴 진행을 시작한다. Configure 이후에만 유효하고 중복 호출은 무시한다(멱등).
    public void StartBattle()
    {
        if (_runState == RunState.Running) return;
        if (_runState != RunState.Configured)
        {
            GD.PushError("TurnHelper.StartBattle: Configure 이전에는 시작할 수 없습니다.");
            return;
        }

        AdvanceToNextTurn();
        _runState = RunState.Running;
    }

    // 외부(BattleSession) 요청으로 턴 진행을 멈춘다. 중복 호출은 안전하다(멱등). 정지 시 OnDead 구독을 해제해
    // 정지 이후 stale 콜백과 teardown 재진입을 막는다(BS-001 Step 1 리뷰 P2).
    public void StopBattle()
    {
        if (_runState is RunState.Running or RunState.Configured)
        {
            _runState = RunState.Stopped;
            ClearDeathSubscriptions();
        }
    }

    // 보관한 OnDead 구독을 해제한다. 이미 free된 유닛은 IsInstanceValid로 걸러 안전하게 건너뛴다.
    private void ClearDeathSubscriptions()
    {
        foreach (var (article, handler) in _deathSubscriptions)
        {
            if (GodotObject.IsInstanceValid(article))
                article.OnDead -= handler;
        }
        _deathSubscriptions.Clear();
    }

    private List<ITurnAffectedArticle<ArticleBase>> CollectContainerParticipantsAndChargeAmmo()
    {
        var participants = new List<ITurnAffectedArticle<ArticleBase>>();
        var articles = _articlesContainer?.Articles;
        if (articles == null) return participants;

        foreach (var (_, value) in articles)
        {
            foreach (var articleBase in value)
            {
                if (articleBase is not ITurnAffectedArticle<ArticleBase> turnAffectedArticle) continue;
                participants.Add(turnAffectedArticle);

                // 전투 시작 reset(SK-002 Step 3, ADR-021 §4): 제한 스킬 ammo를 전투마다 full charge.
                if (articleBase is CharacterArticle character) character.ChargeBattleAmmo();
            }
        }

        return participants;
    }

    // 턴 순서 정규 키: Priority 오름차순 -> 스폰 순번 오름차순 (ADR-017)
    private void SortTurnAffectedArticles()
    {
        var orderedArticles = _turnAffectedArticleList
            .OrderBy(article => article.Priority)
            .ThenBy(article => article.SpawnIndex)
            .ToList();
        _turnAffectedArticleList.Clear();
        _turnAffectedArticleList.AddRange(orderedArticles);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_runState != RunState.Running) return;
        if (IsPaused) return;

        FxPlayer?.Tick(delta);

        if (IsGameOver)
        {
            // 턴 스케줄러 관점의 종료. 승패 판정/정리는 BattleSession이 소유한다(ADR-022 §3, U1 이후).
            return;
        }

        if (_currentTurnArticle is ArticleBase { IsAlive: false })
        {
            AdvanceToNextTurn();
            return;
        }

        BtStatus status = _currentTurnArticle.TurnPlay(delta * Speed);
        if (status is BtStatus.Success or BtStatus.Failure)
        {
            AdvanceToNextTurn();
        }
    }

    public void ResetCombatRng(long seed)
    {
        _combatSeed = seed;
        _combatRng.Seed = unchecked((ulong)seed);
    }

    public double CombatRandRange(double min, double max)
    {
        return _combatRng.RandfRange((float)min, (float)max);
    }

    public int CombatRandRange(int min, int max)
    {
        return _combatRng.RandiRange(min, max);
    }

    private void AdvanceToNextTurn()
    {
        _currentTurnArticle = SelectNextTurnArticle();
        if (IsGameOver) return;
        _currentTurnArticle?.ApplyTurnStartEffects();
    }

    // 정수 커서 기반 다음 행동자 선택. 현재 유닛이 제거된 경우 후임이 이미 커서 슬롯으로 당겨졌으므로 커서를
    // +1 하지 않고 리스트 길이로 wrap만 한다(현재 유닛 사망 시 목록 처음 리셋 결함 수정, BS-001 Step 1).
    private ITurnAffectedArticle<ArticleBase> SelectNextTurnArticle()
    {
        int count = _turnAffectedArticleList.Count;
        if (count == 0)
        {
            _turnCursor = -1;
            return null;
        }

        if (_turnCursor < 0)
        {
            _turnCursor = 0;
        }
        else if (_currentUnitRemoved)
        {
            _currentUnitRemoved = false;
            _turnCursor %= count; // 제거로 당겨진 후임 슬롯. 마지막 슬롯이 비면 처음으로 wrap.
        }
        else
        {
            _turnCursor = (_turnCursor + 1) % count;
        }

        return _turnAffectedArticleList[_turnCursor];
    }

    // 사망 등으로 턴 순서에서 유닛을 제거하고 커서를 보정한다. 제거 위치가 커서보다 앞이면 커서를 당기고,
    // 커서와 같으면(현재 행동자) 다음 선택이 후임을 고르도록 플래그를 세운다. 뒤면 커서는 불변이다.
    private void RemoveFromTurnOrder(ITurnAffectedArticle<ArticleBase> article)
    {
        int removedIndex = _turnAffectedArticleList.IndexOf(article);
        if (removedIndex < 0) return;

        _turnAffectedArticleList.RemoveAt(removedIndex);

        if (removedIndex < _turnCursor)
            _turnCursor--;
        else if (removedIndex == _turnCursor)
            _currentUnitRemoved = true;
    }
}
