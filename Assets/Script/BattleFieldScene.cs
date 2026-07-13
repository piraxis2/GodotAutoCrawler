using System;
using System.Collections.Generic;
using Godot;

namespace AutoCrawler.Assets.Script;

public partial class BattleFieldScene : Node2D
{
   private static BattleFieldScene _battleFieldScene;
   public static BattleFieldScene BattleField => _battleFieldScene;

   [Export] private BattleFieldTileMapLayer _battleFieldTileMap;
   [Export] private TurnHelper _turnHelper;
   [Export] private ArticlesContainer _articles;
   [Export] private FxPlayer _fxPlayer;
   [Export] private Node _damageFloater;
   

   public BattleFieldTileMapLayer BattleFieldTileMap => _battleFieldTileMap;
   public TurnHelper TurnHelper => _turnHelper;
   public ArticlesContainer Articles => _articles;
   public FxPlayer FxPlayer => _fxPlayer;
   
   public Node DamageFloater => _damageFloater;
   
   public override void _Ready()
   {
      _battleFieldScene = this;
   }

   // 정적 context 안전 정리(BS-001 Step 3, ADR-022 §7). 자신이 현재 인스턴스일 때만 null로 만들어, 다른 세션이
   // 이미 새 인스턴스를 설정한 경우 덮어쓰지 않는다. BattleSession이 RemoveChild로 이 정리를 동기 유도한다.
   public override void _ExitTree()
   {
      if (_battleFieldScene == this)
         _battleFieldScene = null;
   }
}