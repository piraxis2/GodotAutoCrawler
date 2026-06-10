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


}