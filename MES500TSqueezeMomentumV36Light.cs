#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class MES500TSqueezeMomentumV36Light : Indicator
	{
		private class PostedSignal
		{
			public int Bar;
			public int TradeId;
			public bool IsLong;
			public bool IsApproach;
			public ApproachKind ApproachKind;
			public EntryTrigger Trigger;
			public SignalGrade Grade;
			public bool WasReEntry;
			public bool QualifiedReEntry;
			public string Tag;
			public bool Voided;
			public double EntryPrice;
		}
		private enum CloseHoldVerdict
		{
			Hold,
			HoldMaybe,
			Exit
		}

		private enum ApproachKind
		{
			Buy,
			Sell,
			ReBuy,
			ReSell,
			Close
		}

		private enum SignalGrade
		{
			A,
			B,
			C
		}

		private enum TradeCharacter
		{
			Runner,
			Chop,
			Normal
		}

		private enum TradeState
		{
			Flat,
			Long,
			Short
		}

		private enum EntryTrigger
		{
			None,
			Breakout,
			Midline,
			Ignition,
			MacdCross,
			Continuation
		}

		private enum CloseReason
		{
			None,
			OppositeSignal,
			NoTradeZone,
			Exhaustion,
			KcBreak,
			MomentumFade,
			AtrTrail,
			ProfitGiveback
		}

		private enum SqueezeState
		{
			Off,
			Partial,
			Full
		}

		private enum SqueezeReleaseDirection
		{
			None,
			Bullish,
			Bearish,
			Both
		}

		private EMA bbEmaInd;
		private StdDev bbStdDevInd;
		private EMA kcEmaInd;
		private ATR kcAtrInd;
		private MACD macdInd;
		private SimpleFont signalFont;
		private SimpleFont closeFont;
		private SimpleFont statsFont;
		private SimpleFont warningFont;
		private TradeState tradeState;
		private SqueezeState priorSqueezeState;
		private int nextTradeId;
		private int openTradeId;
		private int openEntryBar;
		private double openEntryPrice;
		private int barsInTrade;
		private int longKcBreakBars;
		private int shortKcBreakBars;
		private double trailStopPrice;
		private double maxFavorableTicks;
		private CloseReason lastExitWarningReason;
		private EntryTrigger openEntryTrigger;
		private int lastCloseBar;
		private bool hasClosedTrade;
		private bool lastClosedWasLong;
		private bool pendingLongEntry;
		private bool pendingShortEntry;
		private EntryTrigger pendingLongTrigger;
		private EntryTrigger pendingShortTrigger;
		private int pendingEntryBar;
		private double pendingEntryPrice;
		private int statTotalTrades;
		private int statWins;
		private int statLosses;
		private double statTotalTicks;
		private double statPeakNetTicks;
		private double statMaxDrawdownTicks;
		private double statGrossProfitTicks;
		private double statGrossLossTicks;
		private double statWinTicksTotal;
		private double statLossTicksTotal;
		private int[] triggerTrades;
		private int[] triggerWins;
		private double[] triggerNetTicks;
		private SignalGrade openSignalGrade;
		private TradeCharacter openTradeCharacter;
		private TradeCharacter liveTradeCharacter;
		private string liveCharacterTag;
		private DateTime openEntryTime;
		private double postPeakMaxTicks;
		private bool needsChartRefresh;
		private bool recoveryWatchActive;
		private bool recoveryWatchIsLong;
		private double recoveryWatchEntryPrice;
		private DateTime recoveryWatchEndTime;
		private bool recoveryMissShown;
		private CloseReason lastCloseReason;
		private double lastClosePnlTicks;
		private TradeCharacter lastCloseTradeCharacter;
		private bool openIsReEntry;
		private List<PostedSignal> postedSignals;
		private List<TrackedHistoricalMarker> trackedHistoricalMarkers;

		private struct TrackedHistoricalMarker
		{
			public int Bar;
			public string Tag;
			public bool IsLongEntry;
			public bool IsShortEntry;
			public bool IsCloseMarker;
		}
		private SimpleFont approachFont;
		private SimpleFont hintFont;
		private Window screenPopupWindow;
		private DispatcherTimer screenPopupTimer;
		private string lastScreenPopupKey = string.Empty;

		[NinjaScriptProperty]
		[Display(Name = "Manual Trade Mode", Description = "V36: Pro ruční obchod — CLOSE až po X svíčkách, více času využít signál.", Order = 1, GroupName = "15. V36 Manual Trading")]
		public bool ManualTradeMode { get; set; }

		[NinjaScriptProperty]
		[Range(3, 40)]
		[Display(Name = "Min Bars Before Soft Close", Description = "GB/KC/MACD/ATR/EXH/SQZ CLOSE až po tolika svíčkách od vstupu.", Order = 2, GroupName = "15. V36 Manual Trading")]
		public int MinBarsBeforeSoftClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Early Flip Close", Description = "Opačný LONG/SHORT může zavřít i před minimem (bezpečnost).", Order = 3, GroupName = "15. V36 Manual Trading")]
		public bool AllowEarlyFlipClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Hold Window Tag", Description = "Během ochranného okna zobrazit DRŽ · X/Y svíček.", Order = 4, GroupName = "15. V36 Manual Trading")]
		public bool ShowHoldWindowTag { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Suppress Exit Warning In Hold", Description = "EXIT? neukazovat dokud nevyprší hold okno.", Order = 5, GroupName = "15. V36 Manual Trading")]
		public bool SuppressExitWarningInHold { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Approach Ring", Description = "Jemné kolečko nad/pod svíčkou — sílí s blížícím se NÁKUP/PRODEJ signálem.", Order = 1, GroupName = "16. Approach Ring")]
		public bool ShowApproachRing { get; set; }

		[NinjaScriptProperty]
		[Range(1, 6)]
		[Display(Name = "Ring Min Score", Description = "Od jakého skóre se kolečko vůbec objeví.", Order = 2, GroupName = "16. Approach Ring")]
		public int ApproachRingMinScore { get; set; }

		[NinjaScriptProperty]
		[Range(2, 12)]
		[Display(Name = "Ring Size Ticks", Description = "Velikost kolečka (poloměr v tickách).", Order = 3, GroupName = "16. Approach Ring")]
		public int ApproachRingSizeTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0.02, 0.25)]
		[Display(Name = "Ring Min Opacity", Description = "Minimální viditelnost kolečka (slabé podmínky).", Order = 4, GroupName = "16. Approach Ring")]
		public double ApproachRingMinOpacity { get; set; }

		[NinjaScriptProperty]
		[Range(0.15, 0.70)]
		[Display(Name = "Ring Max Opacity", Description = "Maximální viditelnost (silné podmínky blízko signálu).", Order = 5, GroupName = "16. Approach Ring")]
		public double ApproachRingMaxOpacity { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Screen Popup", Description = "Velké okno uprostřed obrazovky (Topmost) — viditelné i přes jiná okna.", Order = 1, GroupName = "17. Windows Popup")]
		public bool EnableScreenPopup { get; set; }

		[NinjaScriptProperty]
		[Range(1, 15)]
		[Display(Name = "Popup Duration Sec", Description = "Kolik sekund popup zůstane na obrazovce.", Order = 2, GroupName = "17. Windows Popup")]
		public int PopupDurationSec { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Popup On Approach", Description = "Popup u Nákup? / Prodej? / Zavřít? hintů.", Order = 3, GroupName = "17. Windows Popup")]
		public bool PopupOnApproach { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Popup On Entry", Description = "Popup u NÁKUP / PRODEJ signálů.", Order = 4, GroupName = "17. Windows Popup")]
		public bool PopupOnEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Popup On Close", Description = "Popup u ZAVŘÍT a varování BRZY ZAVŘÍT?.", Order = 5, GroupName = "17. Windows Popup")]
		public bool PopupOnClose { get; set; }

		[NinjaScriptProperty]
		[Range(20, 200)]
		[Display(Name = "Max Marker History Bars", Description = "Light: starší značky na grafu se mažou (výchozí 60 svíček).", Order = 1, GroupName = "18. V36 Light")]
		public int MaxMarkerHistoryBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Close Hold Advice", Description = "U CLOSE zobrazit DRŽ dál / NEDRŽ — držet pozici i po signálu ZAVŘÍT?", Order = 1, GroupName = "14. V35 Close Advice")]
		public bool ShowCloseHoldAdvice { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Signal Auto-Correct", Description = "V34: Kontrola značek ~5 svíček zpět — chybné se smažou nebo opraví.", Order = 1, GroupName = "13. V34 Auto-Correct")]
		public bool EnableSignalAutoCorrect { get; set; }

		[NinjaScriptProperty]
		[Range(2, 12)]
		[Display(Name = "Signal Review Bars", Description = "Kolik svíček zpět kontrolovat a opravovat značky.", Order = 2, GroupName = "13. V34 Auto-Correct")]
		public int SignalReviewBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "Min Bars Before Review", Description = "Po kolika svíčkách od značky začít kontrolu.", Order = 3, GroupName = "13. V34 Auto-Correct")]
		public int SignalMinBarsBeforeReview { get; set; }

		[NinjaScriptProperty]
		[Range(4, 40)]
		[Display(Name = "False Signal Adverse Ticks", Description = "Pokud cena jde proti o X ticků bez potvrzení = chybná značka.", Order = 4, GroupName = "13. V34 Auto-Correct")]
		public int FalseSignalAdverseTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Signal Confirm Ticks", Description = "Min. pohyb ve směru signálu, jinak hrozí smazání.", Order = 5, GroupName = "13. V34 Auto-Correct")]
		public int SignalConfirmTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Block Weak Re-Entry Signals", Description = "V33: Po CLOSE stejným směrem se značka ukáže jen když projde přísným RE filtrem. Žádný RE = žádný obchod.", Order = 1, GroupName = "12. V33 Smart Signals")]
		public bool BlockWeakReEntrySignals { get; set; }

		[NinjaScriptProperty]
		[Range(1, 40)]
		[Display(Name = "Re-Entry Min Bars Since Close", Description = "Min. pauza po CLOSE než povolit další signál stejným směrem.", Order = 2, GroupName = "12. V33 Smart Signals")]
		public int ReEntryMinBarsSinceClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Re-Entry Require Grade B+", Description = "RE-BUY/RE-SELL jen pro grade A nebo B, ne C.", Order = 3, GroupName = "12. V33 Smart Signals")]
		public bool ReEntryRequireGradeBOrBetter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Block Re-Entry After Chop Close", Description = "Po CLOSE s CHOP? / giveback neukazovat RE signál.", Order = 4, GroupName = "12. V33 Smart Signals")]
		public bool BlockReEntryAfterChopClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Block Re-Entry After Loss", Description = "Po ztrátovém CLOSE neukazovat RE signál stejným směrem.", Order = 5, GroupName = "12. V33 Smart Signals")]
		public bool BlockReEntryAfterLossClose { get; set; }

		[NinjaScriptProperty]
		[Range(3, 10)]
		[Display(Name = "Re-Entry Approach Min Score", Description = "Hint BUY?/SELL? po CLOSE — vyšší práh než běžný (RE hinty vypnuty).", Order = 6, GroupName = "12. V33 Smart Signals")]
		public int ReEntryApproachMinScore { get; set; }

		[NinjaScriptProperty]
		[Range(2, 10)]
		[Display(Name = "CNT Min KC Mid Slope Bars", Description = "CNT/RE-entry jen když KC mid jde stejným směrem X svíček.", Order = 1, GroupName = "10. V32 Quality")]
		public int CntMinKcMidSlopeBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Trail Line", Description = "Čárkovaná ATR trail linka během obchodu.", Order = 2, GroupName = "10. V32 Quality")]
		public bool ShowTrailLine { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Signal Grade", Description = "Ke signálu přidat grade A/B/C.", Order = 3, GroupName = "10. V32 Quality")]
		public bool ShowSignalGrade { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Trade Character", Description = "RUNNER / CHOP? / HOLD? během obchodu.", Order = 4, GroupName = "10. V32 Quality")]
		public bool ShowTradeCharacter { get; set; }

		[NinjaScriptProperty]
		[Range(8, 80)]
		[Display(Name = "Chop Quick Peak Ticks", Description = "Rychlý peak (~20 USD MES) pro detekci whipsaw.", Order = 5, GroupName = "10. V32 Quality")]
		public int ChopQuickPeakTicks { get; set; }

		[NinjaScriptProperty]
		[Range(8, 120)]
		[Display(Name = "Chop Giveback Ticks", Description = "Kolik ticků z peaku = whipsaw varování.", Order = 6, GroupName = "10. V32 Quality")]
		public int ChopGivebackTicks { get; set; }

		[NinjaScriptProperty]
		[Range(20, 200)]
		[Display(Name = "Runner Target Ticks", Description = "Cíl typického runneru (~50 USD MES).", Order = 7, GroupName = "10. V32 Quality")]
		public int RunnerTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(15, 300)]
		[Display(Name = "Recovery Window Sec", Description = "Po pullbacku sledovat jestli by došlo k runner cíli.", Order = 8, GroupName = "10. V32 Quality")]
		public int RecoveryWindowSec { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Taskbar Flash", Description = "Win11: blikání ikony NinjaTrader v liště při alertu.", Order = 1, GroupName = "11. V32 Alerts")]
		public bool EnableTaskbarFlash { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Flash On Entry", Order = 2, GroupName = "11. V32 Alerts")]
		public bool FlashOnEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Flash On Close", Order = 3, GroupName = "11. V32 Alerts")]
		public bool FlashOnClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Flash On Approach", Order = 4, GroupName = "11. V32 Alerts")]
		public bool FlashOnApproach { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Approach Hints", Description = "Čtverec u svíčky = blíží se BUY / SELL / CLOSE (typicky svíčka před signálem).", Order = 14, GroupName = "6. Display")]
		public bool ShowApproachHints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Approach Labels", Description = "BUY?/SELL? (modře/růžově) + CLOSE?. V33: RE hinty vypnuty — jen kvalifikované BUY?/SELL?.", Order = 15, GroupName = "6. Display")]
		public bool ShowApproachLabels { get; set; }

		[NinjaScriptProperty]
		[Range(1, 7)]
		[Display(Name = "Approach Min Score", Description = "Kolik z 7 podmínek musí být splněno pro hint (5 = dřív, 7 = přísněji).", Order = 1, GroupName = "9. Approach")]
		public int ApproachMinScore { get; set; }

		[NinjaScriptProperty]
		[Range(1, 30)]
		[Display(Name = "Approach Near Ticks", Description = "Jak blízko KC musí být cena pro hint.", Order = 2, GroupName = "9. Approach")]
		public int ApproachNearTicks { get; set; }

		[NinjaScriptProperty]
		[Range(2, 30)]
		[Display(Name = "Approach Offset Ticks", Description = "Vzdálenost čtverce od High/Low svíčky.", Order = 3, GroupName = "9. Approach")]
		public int ApproachOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(5, 500)]
		[Display(Name = "Re-Entry Lookback Bars", Description = "Okno po CLOSE, kdy může přijít RE-BUY / RE-SELL (jen po splnění V33 filtru).", Order = 4, GroupName = "9. Approach")]
		public int ReEntryLookbackBars { get; set; }

		[XmlIgnore]
		[Display(Name = "Approach Buy Color", Order = 15, GroupName = "7. Visual")]
		public Brush ApproachBuyBrush { get; set; }

		[Browsable(false)]
		public string ApproachBuyBrushSerializable
		{
			get { return Serialize.BrushToString(ApproachBuyBrush); }
			set { ApproachBuyBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Approach Sell Color", Order = 16, GroupName = "7. Visual")]
		public Brush ApproachSellBrush { get; set; }

		[Browsable(false)]
		public string ApproachSellBrushSerializable
		{
			get { return Serialize.BrushToString(ApproachSellBrush); }
			set { ApproachSellBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Approach Close Color", Order = 17, GroupName = "7. Visual")]
		public Brush ApproachCloseBrush { get; set; }

		[Browsable(false)]
		public string ApproachCloseBrushSerializable
		{
			get { return Serialize.BrushToString(ApproachCloseBrush); }
			set { ApproachCloseBrush = Serialize.StringToBrush(value); }
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500T V3.6 Light — pro slabší PC. Stejná logika signálů jako V36, méně kreslení, značky max 60 svíček zpět.";
				Name = "MES500TSqueezeMomentumV36Light";
				Calculate = Calculate.OnBarClose;
				IsOverlay = true;
				DisplayInDataBox = true;
				DrawOnPricePanel = true;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot = 25;

				BbPeriod = 20;
				BbStdDev = 2.0;
				KcPeriod = 20;
				KcMultiplier = 1.5;
				MacdFast = 6;
				MacdSlow = 13;
				MacdSignal = 9;

				TangleSeparationTicks = 4;
				TangleSlopeTicks = 1;
				RequireThreeBarMomentum = false;
				RequireDirectionalRelease = true;
				EntryBufferTicks = 1;
				AllowKcMidEntry = true;
				AllowMomentumIgnition = true;
				AllowMacdCrossEntry = false;
				AllowBarBreakEntry = false;
				AllowTrendContinuationEntry = true;
				AllowPartialSqueezeEntry = true;
				BlockFullSqueezeOnly = true;
				UseIntrabarEntries = false;
				RequireBounceForContinuation = true;
				RequireKcMidTrend = true;
				MinHistStepTicks = 1;
				EntryCooldownBars = 4;
				KcBreakConfirmBars = 2;
				MinBarsInTrade = 8;
				UseAtrTrail = true;
				AtrTrailMultiplier = 2.0;

				UseIntrabarExits = false;
				ShowExitWarning = true;
				ShowApproachHints = true;
				ShowApproachLabels = true;
				ApproachMinScore = 5;
				ApproachNearTicks = 10;
				ApproachOffsetTicks = 6;
				ReEntryLookbackBars = 25;
				ReEntryMinBarsSinceClose = 8;
				BlockWeakReEntrySignals = true;
				ReEntryRequireGradeBOrBetter = true;
				BlockReEntryAfterChopClose = true;
				BlockReEntryAfterLossClose = true;
				ReEntryApproachMinScore = 7;
				EnableSignalAutoCorrect = false;
				SignalReviewBars = 5;
				SignalMinBarsBeforeReview = 1;
				FalseSignalAdverseTicks = 12;
				SignalConfirmTicks = 4;
				ShowCloseHoldAdvice = false;
				ManualTradeMode = true;
				MinBarsBeforeSoftClose = 10;
				AllowEarlyFlipClose = true;
				ShowHoldWindowTag = true;
				SuppressExitWarningInHold = true;
				ShowApproachRing = false;
				ApproachRingMinScore = 3;
				ApproachRingSizeTicks = 4;
				ApproachRingMinOpacity = 0.06;
				ApproachRingMaxOpacity = 0.42;
				EnableScreenPopup = false;
				PopupDurationSec = 3;
				PopupOnApproach = false;
				PopupOnEntry = false;
				PopupOnClose = false;
				MaxMarkerHistoryBars = 60;
				CntMinKcMidSlopeBars = 3;
				ShowTrailLine = false;
				ShowSignalGrade = true;
				ShowTradeCharacter = false;
				ChopQuickPeakTicks = 16;
				ChopGivebackTicks = 24;
				RunnerTargetTicks = 40;
				RecoveryWindowSec = 60;
				EnableTaskbarFlash = true;
				FlashOnEntry = true;
				FlashOnClose = true;
				FlashOnApproach = false;
				EnableExitWarningAlerts = true;
				UseProfitGivebackExit = true;
				MinProfitForGiveback = 24;
				ProfitGivebackTicks = 40;

				EnableAlerts = true;
				ShowSignals = true;
				ShowSignalLabels = true;
				ShowCloseSignals = true;
				ShowCloseLabels = true;
				ShowCloseReason = true;
				ShowTradeLines = false;
				ShowReleaseMarker = false;
				ShowStatsPanel = false;
				ShowKcMid = true;
				ShowEntryTrigger = false;
				EnableCloseAlerts = true;
				RequireMomentumFadeForClose = true;
				UseExhaustionForClose = true;
				ShowSqueezeBackground = false;

				ArrowOffsetTicks = 8;
				LabelOffsetTicks = 12;
				CloseOffsetTicks = 6;

				BbBrush = Brushes.DodgerBlue;
				KcBrush = Brushes.Gray;
				KcMidBrush = Brushes.DimGray;
				SqueezeFullBrush = Brushes.Goldenrod;
				SqueezePartialBrush = Brushes.Khaki;
				LongSignalBrush = Brushes.LimeGreen;
				ShortSignalBrush = Brushes.Red;
				CloseSignalBrush = Brushes.Orange;
				ReleaseBullBrush = Brushes.LimeGreen;
				ReleaseBearBrush = Brushes.Red;
				ExitWarningBrush = Brushes.Yellow;
				ApproachBuyBrush = Brushes.DeepSkyBlue;
				ApproachSellBrush = Brushes.DeepPink;
				ApproachCloseBrush = Brushes.Gold;

				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "BB Upper");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "BB Lower");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 2), PlotStyle.Line, "KC Upper");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 2), PlotStyle.Line, "KC Lower");
				AddPlot(new Stroke(Brushes.DimGray, DashStyleHelper.Dot, 1), PlotStyle.Line, "KC Mid");
				AddPlot(new Stroke(Brushes.LimeGreen, 3), PlotStyle.TriangleUp, "Long Marker");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.TriangleDown, "Short Marker");
				AddPlot(new Stroke(Brushes.Orange, 3), PlotStyle.Dot, "Close Marker");
			}
			else if (State == State.Configure)
			{
				Calculate = UseIntrabarEntries || UseIntrabarExits ? Calculate.OnPriceChange : Calculate.OnBarClose;
				Plots[5].AutoWidth = true;
				Plots[6].AutoWidth = true;
				Plots[7].AutoWidth = true;
			}
			else if (State == State.DataLoaded)
			{
				ResetTradeTracking();
				nextTradeId = 1;
				priorSqueezeState = SqueezeState.Off;
				statTotalTrades = 0;
				statWins = 0;
				statLosses = 0;
				statTotalTicks = 0;
				statPeakNetTicks = 0;
				statMaxDrawdownTicks = 0;
				statGrossProfitTicks = 0;
				statGrossLossTicks = 0;
				statWinTicksTotal = 0;
				statLossTicksTotal = 0;
				triggerTrades = new int[6];
				triggerWins = new int[6];
				triggerNetTicks = new double[6];
				liveCharacterTag = string.Empty;
				recoveryWatchActive = false;
				recoveryMissShown = false;
				lastCloseReason = CloseReason.None;
				lastClosePnlTicks = 0;
				lastCloseTradeCharacter = TradeCharacter.Normal;
				openIsReEntry = false;
				postedSignals = new List<PostedSignal>();
				trackedHistoricalMarkers = new List<TrackedHistoricalMarker>();
				lastCloseBar = -1000;
				hasClosedTrade = false;
				lastClosedWasLong = false;
				pendingLongEntry = false;
				pendingShortEntry = false;
				pendingEntryBar = -1;
				pendingEntryPrice = 0;
				signalFont = new SimpleFont("Arial", 14) { Bold = true };
				closeFont = new SimpleFont("Arial", 11) { Bold = true };
				statsFont = new SimpleFont("Consolas", 11);
				warningFont = new SimpleFont("Arial", 12) { Bold = true };
				approachFont = new SimpleFont("Arial", 12) { Bold = true };
				hintFont = new SimpleFont("Arial", 10);
				bbEmaInd = EMA(BbPeriod);
				bbStdDevInd = StdDev(BbPeriod);
				kcEmaInd = EMA(KcPeriod);
				kcAtrInd = ATR(KcPeriod);
				macdInd = MACD(MacdFast, MacdSlow, MacdSignal);
			}
			else if (State == State.Terminated)
			{
				CloseScreenPopup();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToPlot)
				return;

			if (IsFirstTickOfBar && CurrentBar > 0)
				TryExecutePendingEntry();

			double bbMid = bbEmaInd[0];
			double stdDev = bbStdDevInd[0];
			double bbUpper = bbMid + (BbStdDev * stdDev);
			double bbLower = bbMid - (BbStdDev * stdDev);

			double kcMid = kcEmaInd[0];
			double atr = kcAtrInd[0];
			double kcUpper = kcMid + (KcMultiplier * atr);
			double kcLower = kcMid - (KcMultiplier * atr);
			double entryBuffer = EntryBufferTicks * TickSize;

			Values[0][0] = bbUpper;
			Values[1][0] = bbLower;
			Values[2][0] = kcUpper;
			Values[3][0] = kcLower;
			Values[4][0] = ShowKcMid ? kcMid : double.NaN;
			if (IsFirstTickOfBar)
			{
				Values[5][0] = double.NaN;
				Values[6][0] = double.NaN;
				Values[7][0] = double.NaN;
			}

			PlotBrushes[0][0] = BbBrush;
			PlotBrushes[1][0] = BbBrush;
			PlotBrushes[2][0] = KcBrush;
			PlotBrushes[3][0] = KcBrush;
			PlotBrushes[4][0] = KcMidBrush;

			SqueezeState squeezeState = GetSqueezeState(bbUpper, bbLower, kcUpper, kcLower);
			SqueezeReleaseDirection releaseDirection = GetSqueezeReleaseDirection(bbUpper, bbLower, kcUpper, kcLower);
			bool inNoTradeZone = squeezeState != SqueezeState.Off;
			bool longEntryBlocked = IsEntryBlocked(squeezeState, releaseDirection, true);
			bool shortEntryBlocked = IsEntryBlocked(squeezeState, releaseDirection, false);

			if (ShowSqueezeBackground)
			{
				if (squeezeState == SqueezeState.Full)
					BackBrush = SqueezeFullBrush;
				else if (squeezeState == SqueezeState.Partial)
					BackBrush = SqueezePartialBrush;
				else
					BackBrush = null;
			}
			else
			{
				BackBrush = null;
			}

			if (ShowReleaseMarker && priorSqueezeState != SqueezeState.Off && squeezeState == SqueezeState.Off)
				DrawReleaseMarker(releaseDirection);

			priorSqueezeState = squeezeState;

			double hist0 = macdInd.Diff[0];
			double hist1 = macdInd.Diff[1];
			double hist2 = macdInd.Diff[2];
			double hist3 = CurrentBar >= 3 ? macdInd.Diff[3] : hist2;

			bool macdTangle = IsMacdTangle();

			EntryTrigger longTrigger;
			EntryTrigger shortTrigger;
			bool longSignal = EvaluateLongEntry(
				squeezeState, releaseDirection, longEntryBlocked, kcMid, kcUpper, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle, out longTrigger);
			bool shortSignal = EvaluateShortEntry(
				squeezeState, releaseDirection, shortEntryBlocked, kcMid, kcLower, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle, out shortTrigger);

			if (tradeState == TradeState.Long)
			{
				if (IsFirstTickOfBar)
					barsInTrade++;

				UpdateLongTrail(atr);
				UpdateMaxFavorable(true);
				UpdateLiveTradeCharacter(true, kcMid, hist0, hist1, hist2);
				DrawActiveTrailLine(true);

				if (IsFirstTickOfBar)
				{
					if (Close[0] < kcUpper)
						longKcBreakBars++;
					else
						longKcBreakBars = 0;
				}

				CloseReason closeReason;
				if (TryEvaluateLongClose(inNoTradeZone, kcUpper, hist0, hist1, hist2, hist3, shortSignal, out closeReason))
				{
					CloseTrade(openTradeId, true, closeReason);
				}
				else if (ShouldShowExitWarning() && TryGetLongExitWarning(inNoTradeZone, kcUpper, hist0, hist1, hist2, hist3, out closeReason))
				{
					DrawExitWarning(openTradeId, true, closeReason);
				}
			}
			else if (tradeState == TradeState.Short)
			{
				if (IsFirstTickOfBar)
					barsInTrade++;

				UpdateShortTrail(atr);
				UpdateMaxFavorable(false);
				UpdateLiveTradeCharacter(false, kcMid, hist0, hist1, hist2);
				DrawActiveTrailLine(false);

				if (IsFirstTickOfBar)
				{
					if (Close[0] > kcLower)
						shortKcBreakBars++;
					else
						shortKcBreakBars = 0;
				}

				CloseReason closeReason;
				if (TryEvaluateShortClose(inNoTradeZone, kcLower, hist0, hist1, hist2, hist3, longSignal, out closeReason))
				{
					CloseTrade(openTradeId, false, closeReason);
				}
				else if (ShouldShowExitWarning() && TryGetShortExitWarning(inNoTradeZone, kcLower, hist0, hist1, hist2, hist3, out closeReason))
				{
					DrawExitWarning(openTradeId, false, closeReason);
				}
			}

			if (tradeState == TradeState.Flat && IsEntryCooldownElapsed())
			{
				if (UsesDeferredBarCloseEntry())
					UpdatePendingEntry(longSignal, shortSignal, longTrigger, shortTrigger, hist0, hist1, hist2);
				else if (CanEvaluateEntry())
				{
					if (longSignal && shortSignal)
					{
						// chop – neobchodovat oběma směry najednou
					}
					else if (longSignal && ShouldAllowEntry(true, longTrigger, hist0, hist1, hist2))
						OpenTrade(true, longTrigger);
					else if (shortSignal && ShouldAllowEntry(false, shortTrigger, hist0, hist1, hist2))
						OpenTrade(false, shortTrigger);
				}
			}
			else if (tradeState == TradeState.Flat)
			{
				UpdateRecoveryWatch();
			}

			if (ShowStatsPanel)
				DrawStatsPanel();

			DrawManualHoldWindow();

			if (ShowApproachHints && ShouldDrawApproachHint())
				DrawApproachHints(
					inNoTradeZone, longEntryBlocked, shortEntryBlocked, releaseDirection, squeezeState,
					kcMid, kcUpper, kcLower, entryBuffer,
					hist0, hist1, hist2, hist3, macdTangle, longSignal, shortSignal);

			if (ShowApproachRing && tradeState == TradeState.Flat && !longSignal && !shortSignal && IsEntryCooldownElapsed())
				DrawApproachStrengthRing(
					longEntryBlocked, shortEntryBlocked, releaseDirection,
					kcMid, kcUpper, kcLower, entryBuffer,
					hist0, hist1, hist2, hist3, macdTangle);

			if (needsChartRefresh)
			{
				needsChartRefresh = false;
				PerformChartRefresh();
			}

			if (EnableSignalAutoCorrect)
				ValidateAndCorrectRecentSignals();

			if (IsFirstTickOfBar)
				PruneHistoricalMarkers();
		}

		private bool UsesDeferredBarCloseEntry()
		{
			return !UseIntrabarEntries && Calculate == Calculate.OnPriceChange;
		}

		private void UpdatePendingEntry(bool longSignal, bool shortSignal, EntryTrigger longTrigger, EntryTrigger shortTrigger, double hist0, double hist1, double hist2)
		{
			if (longSignal && !shortSignal && ShouldAllowEntry(true, longTrigger, hist0, hist1, hist2))
			{
				pendingLongEntry = true;
				pendingShortEntry = false;
				pendingLongTrigger = longTrigger;
				pendingEntryBar = CurrentBar;
				pendingEntryPrice = Close[0];
			}
			else if (shortSignal && !longSignal && ShouldAllowEntry(false, shortTrigger, hist0, hist1, hist2))
			{
				pendingShortEntry = true;
				pendingLongEntry = false;
				pendingShortTrigger = shortTrigger;
				pendingEntryBar = CurrentBar;
				pendingEntryPrice = Close[0];
			}
			else
			{
				pendingLongEntry = false;
				pendingShortEntry = false;
				pendingEntryBar = -1;
			}
		}

		private void TryExecutePendingEntry()
		{
			if (!UsesDeferredBarCloseEntry() || pendingEntryBar != CurrentBar - 1)
				return;

			if (tradeState != TradeState.Flat || !IsEntryCooldownElapsed())
			{
				pendingLongEntry = false;
				pendingShortEntry = false;
				pendingEntryBar = -1;
				return;
			}

			int barsAgo = 1;
			double hist0 = macdInd.Diff[barsAgo];
			double hist1 = CurrentBar >= barsAgo + 1 ? macdInd.Diff[barsAgo + 1] : hist0;
			double hist2 = CurrentBar >= barsAgo + 2 ? macdInd.Diff[barsAgo + 2] : hist1;

			if (pendingLongEntry && ShouldAllowEntry(true, pendingLongTrigger, hist0, hist1, hist2))
				OpenTrade(true, pendingLongTrigger, barsAgo, pendingEntryPrice);
			else if (pendingShortEntry && ShouldAllowEntry(false, pendingShortTrigger, hist0, hist1, hist2))
				OpenTrade(false, pendingShortTrigger, barsAgo, pendingEntryPrice);

			pendingLongEntry = false;
			pendingShortEntry = false;
			pendingEntryBar = -1;
		}

		private bool ShouldDrawApproachHint()
		{
			if (Calculate == Calculate.OnBarClose)
				return true;

			// Tick/intrabar graf: kreslit každý tick (stejný tag = 1 hint na svíčku)
			return true;
		}

		private void DrawApproachHints(
			bool inNoTradeZone,
			bool longEntryBlocked,
			bool shortEntryBlocked,
			SqueezeReleaseDirection releaseDirection,
			SqueezeState squeezeState,
			double kcMid,
			double kcUpper,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle,
			bool longSignal,
			bool shortSignal)
		{
			if (tradeState == TradeState.Flat)
			{
				if (longSignal || shortSignal || !IsEntryCooldownElapsed())
					return;

				int longMinScore = IsLongReEntryContext() ? ReEntryApproachMinScore : ApproachMinScore;
				int shortMinScore = IsShortReEntryContext() ? ReEntryApproachMinScore : ApproachMinScore;

				int longScore = CountLongApproachScore(
					longEntryBlocked, releaseDirection, kcMid, kcUpper, entryBuffer,
					hist0, hist1, hist2, hist3, macdTangle);
				int shortScore = CountShortApproachScore(
					shortEntryBlocked, releaseDirection, kcMid, kcLower, entryBuffer,
					hist0, hist1, hist2, hist3, macdTangle);

				if (longScore >= longMinScore && longScore > shortScore
					&& PassesReEntryApproachFilter(true)
					&& IsLongEntryImminent(
						squeezeState, releaseDirection, longEntryBlocked, kcMid, kcUpper, entryBuffer,
						hist0, hist1, hist2, hist3, macdTangle))
					DrawApproachHint(ApproachKind.Buy);
				else if (shortScore >= shortMinScore && shortScore > longScore
					&& PassesReEntryApproachFilter(false)
					&& IsShortEntryImminent(
						squeezeState, releaseDirection, shortEntryBlocked, kcMid, kcLower, entryBuffer,
						hist0, hist1, hist2, hist3, macdTangle))
					DrawApproachHint(ApproachKind.Sell);
			}
			else if (tradeState == TradeState.Long)
			{
				CloseReason closeReason;
				if (TryEvaluateLongClose(inNoTradeZone, kcUpper, hist0, hist1, hist2, hist3, shortSignal, out closeReason))
					return;

				if (!IsInManualHoldWindow() && TryGetLongExitWarning(inNoTradeZone, kcUpper, hist0, hist1, hist2, hist3, out closeReason))
					DrawApproachHint(ApproachKind.Close);
			}
			else if (tradeState == TradeState.Short)
			{
				CloseReason closeReason;
				if (TryEvaluateShortClose(inNoTradeZone, kcLower, hist0, hist1, hist2, hist3, longSignal, out closeReason))
					return;

				if (!IsInManualHoldWindow() && TryGetShortExitWarning(inNoTradeZone, kcLower, hist0, hist1, hist2, hist3, out closeReason))
					DrawApproachHint(ApproachKind.Close);
			}
		}

		private int CountLongApproachScore(
			bool entryBlocked,
			SqueezeReleaseDirection releaseDirection,
			double kcMid,
			double kcUpper,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			int score = 0;
			double nearBand = ApproachNearTicks * TickSize;
			double upperTrigger = kcUpper + entryBuffer;
			double midTrigger = kcMid + entryBuffer;

			if (!entryBlocked)
				score++;

			if (IsBullReleaseOk(releaseDirection))
				score++;

			if (!macdTangle)
				score++;

			if (Close[0] > upperTrigger - nearBand || Close[0] > midTrigger - nearBand)
				score++;

			if (hist0 > 0)
				score++;

			if (RequireThreeBarMomentum ? hist0 > hist1 && hist1 > hist2 : hist0 > hist1)
				score++;

			if (!IsLongExhaustion(hist0, hist1, hist2, hist3))
				score++;

			return score;
		}

		private int CountShortApproachScore(
			bool entryBlocked,
			SqueezeReleaseDirection releaseDirection,
			double kcMid,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			int score = 0;
			double nearBand = ApproachNearTicks * TickSize;
			double lowerTrigger = kcLower - entryBuffer;
			double midTrigger = kcMid - entryBuffer;

			if (!entryBlocked)
				score++;

			if (IsBearReleaseOk(releaseDirection))
				score++;

			if (!macdTangle)
				score++;

			if (Close[0] < lowerTrigger + nearBand || Close[0] < midTrigger + nearBand)
				score++;

			if (hist0 < 0)
				score++;

			if (RequireThreeBarMomentum ? hist0 < hist1 && hist1 < hist2 : hist0 < hist1)
				score++;

			if (!IsShortExhaustion(hist0, hist1, hist2, hist3))
				score++;

			return score;
		}

		private const int ApproachScoreMax = 7;

		private double GetApproachStrength(int score)
		{
			if (score < ApproachRingMinScore)
				return 0;

			double range = ApproachScoreMax - ApproachRingMinScore;
			if (range <= 0)
				return ApproachRingMaxOpacity;

			return Math.Min(1.0, (score - ApproachRingMinScore) / range);
		}

		private Brush CreateRingBrush(byte red, byte green, byte blue, double strength)
		{
			double opacity = ApproachRingMinOpacity + strength * (ApproachRingMaxOpacity - ApproachRingMinOpacity);
			byte alpha = (byte)Math.Max(0, Math.Min(255, opacity * 255));
			Brush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
			if (brush.CanFreeze)
				brush.Freeze();
			return brush;
		}

		private void DrawApproachStrengthRing(
			bool longEntryBlocked,
			bool shortEntryBlocked,
			SqueezeReleaseDirection releaseDirection,
			double kcMid,
			double kcUpper,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			int longScore = CountLongApproachScore(
				longEntryBlocked, releaseDirection, kcMid, kcUpper, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle);
			int shortScore = CountShortApproachScore(
				shortEntryBlocked, releaseDirection, kcMid, kcLower, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle);

			double longStrength = GetApproachStrength(longScore);
			double shortStrength = GetApproachStrength(shortScore);
			if (longStrength <= 0 && shortStrength <= 0)
				return;

			string tag = "MES500TV36L_Ring_" + CurrentBar;
			double radius = Math.Max(TickSize, ApproachRingSizeTicks * TickSize);

			if (longStrength >= shortStrength && longStrength > 0)
			{
				double centerY = High[0] + (ApproachOffsetTicks * 0.35 * TickSize);
				Brush ringBrush = CreateRingBrush(46, 204, 64, longStrength);
				Draw.Ellipse(this, tag, 0, centerY + radius, 0, centerY - radius, ringBrush);
			}
			else if (shortStrength > 0)
			{
				double centerY = Low[0] - (ApproachOffsetTicks * 0.35 * TickSize);
				Brush ringBrush = CreateRingBrush(220, 60, 70, shortStrength);
				Draw.Ellipse(this, tag, 0, centerY + radius, 0, centerY - radius, ringBrush);
			}

			RegisterHistoricalMarker(CurrentBar, tag);
		}

		private bool IsLongReEntryContext()
		{
			return IsInReEntryWindow(true);
		}

		private bool IsShortReEntryContext()
		{
			return IsInReEntryWindow(false);
		}

		private bool IsInReEntryWindow(bool isLong)
		{
			if (!hasClosedTrade || CurrentBar - lastCloseBar > ReEntryLookbackBars)
				return false;

			return isLong ? lastClosedWasLong : !lastClosedWasLong;
		}

		private bool WasLastCloseBadForReEntry()
		{
			if (BlockReEntryAfterChopClose)
			{
				if (lastCloseTradeCharacter == TradeCharacter.Chop)
					return true;
				if (lastCloseReason == CloseReason.ProfitGiveback)
					return true;
			}

			if (BlockReEntryAfterLossClose && lastClosePnlTicks < 0)
				return true;

			return false;
		}

		private bool PassesReEntryApproachFilter(bool isLong)
		{
			if (!IsInReEntryWindow(isLong))
				return true;

			if (CurrentBar - lastCloseBar < ReEntryMinBarsSinceClose)
				return false;

			return !WasLastCloseBadForReEntry();
		}

		private bool PassesReEntryFilter(bool isLong, EntryTrigger trigger, SignalGrade grade)
		{
			if (!IsInReEntryWindow(isLong))
				return true;

			if (!BlockWeakReEntrySignals)
				return true;

			if (CurrentBar - lastCloseBar < ReEntryMinBarsSinceClose)
				return false;

			if (WasLastCloseBadForReEntry())
				return false;

			if (ReEntryRequireGradeBOrBetter && grade == SignalGrade.C)
				return false;

			if (trigger == EntryTrigger.Continuation)
			{
				if (CntMinKcMidSlopeBars > 0 && !HasKcMidSlope(isLong, CntMinKcMidSlopeBars))
					return false;

				if (grade != SignalGrade.A && grade != SignalGrade.B)
					return false;
			}

			return true;
		}

		private bool ShouldAllowEntry(bool isLong, EntryTrigger trigger, double hist0, double hist1, double hist2)
		{
			SignalGrade grade = GetSignalGrade(isLong, trigger, hist0, hist1, hist2);
			return PassesReEntryFilter(isLong, trigger, grade);
		}

		private bool IsLongReEntryEntry()
		{
			return openIsReEntry;
		}

		private bool IsShortReEntryEntry()
		{
			return openIsReEntry;
		}

		private bool IsLongEntryImminent(
			SqueezeState squeezeState,
			SqueezeReleaseDirection releaseDirection,
			bool entryBlocked,
			double kcMid,
			double kcUpper,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			EntryTrigger trigger;
			if (EvaluateLongEntry(
				squeezeState, releaseDirection, entryBlocked, kcMid, kcUpper, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle, out trigger))
				return false;

			double nearBand = ApproachNearTicks * TickSize;
			double upperTrigger = kcUpper + entryBuffer;
			double midTrigger = kcMid + entryBuffer;

			if (entryBlocked || macdTangle || IsLongExhaustion(hist0, hist1, hist2, hist3))
				return false;

			if (!IsBullOk(squeezeState, releaseDirection))
				return false;

			bool nearEntryLevel = Close[0] > upperTrigger - nearBand || Close[0] > midTrigger - nearBand;
			if (!nearEntryLevel)
				return false;

			return HasEarlyMomentum(true, hist0, hist1, hist2) || IsLongMomentumReady(hist0, hist1, hist2);
		}

		private bool IsShortEntryImminent(
			SqueezeState squeezeState,
			SqueezeReleaseDirection releaseDirection,
			bool entryBlocked,
			double kcMid,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			EntryTrigger trigger;
			if (EvaluateShortEntry(
				squeezeState, releaseDirection, entryBlocked, kcMid, kcLower, entryBuffer,
				hist0, hist1, hist2, hist3, macdTangle, out trigger))
				return false;

			double nearBand = ApproachNearTicks * TickSize;
			double lowerTrigger = kcLower - entryBuffer;
			double midTrigger = kcMid - entryBuffer;

			if (entryBlocked || macdTangle || IsShortExhaustion(hist0, hist1, hist2, hist3))
				return false;

			if (!IsBearOk(squeezeState, releaseDirection))
				return false;

			bool nearEntryLevel = Close[0] < lowerTrigger + nearBand || Close[0] < midTrigger + nearBand;
			if (!nearEntryLevel)
				return false;

			return HasEarlyMomentum(false, hist0, hist1, hist2) || IsShortMomentumReady(hist0, hist1, hist2);
		}

		private int CountLongReEntryApproachScore(
			bool entryBlocked,
			SqueezeReleaseDirection releaseDirection,
			double kcMid,
			double kcUpper,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			int score = 0;
			double nearBand = ApproachNearTicks * TickSize;
			double upperTrigger = kcUpper + entryBuffer;
			double midTrigger = kcMid + entryBuffer;

			if (!entryBlocked)
				score++;

			if (IsBullReleaseOk(releaseDirection))
				score++;

			if (!macdTangle)
				score++;

			if (Close[0] > kcMid && Close[0] > Open[0])
				score++;

			if (!RequireKcMidTrend || kcMid > kcEmaInd[1])
				score++;

			if (!RequireBounceForContinuation || Close[1] <= Open[1] || Close[1] <= Close[2])
				score++;

			if (Close[0] > upperTrigger - nearBand || Close[0] > midTrigger - nearBand)
				score++;

			if (HasEarlyMomentum(true, hist0, hist1, hist2) && !IsLongExhaustion(hist0, hist1, hist2, hist3))
				score++;

			return score;
		}

		private int CountShortReEntryApproachScore(
			bool entryBlocked,
			SqueezeReleaseDirection releaseDirection,
			double kcMid,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle)
		{
			int score = 0;
			double nearBand = ApproachNearTicks * TickSize;
			double lowerTrigger = kcLower - entryBuffer;
			double midTrigger = kcMid - entryBuffer;

			if (!entryBlocked)
				score++;

			if (IsBearReleaseOk(releaseDirection))
				score++;

			if (!macdTangle)
				score++;

			if (Close[0] < kcMid && Close[0] < Open[0])
				score++;

			if (!RequireKcMidTrend || kcMid < kcEmaInd[1])
				score++;

			if (!RequireBounceForContinuation || Close[1] >= Open[1] || Close[1] >= Close[2])
				score++;

			if (Close[0] < lowerTrigger + nearBand || Close[0] < midTrigger + nearBand)
				score++;

			if (HasEarlyMomentum(false, hist0, hist1, hist2) && !IsShortExhaustion(hist0, hist1, hist2, hist3))
				score++;

			return score;
		}

		private void DrawApproachHint(ApproachKind kind)
		{
			string tag = "MES500TV36L_Hint_" + CurrentBar;
			double offset = ApproachOffsetTicks * TickSize;
			double markerY;
			double labelY;
			Brush brush;
			string label = ShowApproachLabels ? GetApproachMainLabel(kind) : string.Empty;
			string hint = GetApproachHint(kind);

			switch (kind)
			{
				case ApproachKind.Buy:
					brush = ApproachBuyBrush;
					markerY = Low[0] - offset;
					labelY = markerY - (LabelOffsetTicks * TickSize);
					break;
				case ApproachKind.ReBuy:
					brush = LongSignalBrush;
					markerY = Low[0] - offset;
					labelY = markerY - (LabelOffsetTicks * TickSize);
					break;
				case ApproachKind.Sell:
					brush = ApproachSellBrush;
					markerY = High[0] + offset;
					labelY = markerY + (LabelOffsetTicks * TickSize);
					break;
				case ApproachKind.ReSell:
					brush = ShortSignalBrush;
					markerY = High[0] + offset;
					labelY = markerY + (LabelOffsetTicks * TickSize);
					break;
				default:
					brush = ApproachCloseBrush;
					if (tradeState == TradeState.Long)
					{
						markerY = High[0] + offset;
						labelY = markerY + (LabelOffsetTicks * TickSize);
					}
					else
					{
						markerY = Low[0] - offset;
						labelY = markerY - (LabelOffsetTicks * TickSize);
					}
					break;
			}

			Draw.Square(this, tag + "_Sq", false, 0, markerY, brush);

			if (ShowApproachLabels && label.Length > 0)
			{
				Draw.Text(this, tag + "_Txt", false, label, 0, labelY, 0, brush,
					approachFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				bool hintAbove = kind == ApproachKind.Sell || kind == ApproachKind.ReSell
					|| (kind == ApproachKind.Close && tradeState == TradeState.Long);
				DrawSubHint(tag + "_Hint", 0, labelY, hintAbove, hint, brush);
			}

			RequestChartRefresh();
			if (FlashOnApproach && EnableTaskbarFlash && IsFirstTickOfBar)
				FlashTaskbar();

			if (PopupOnApproach)
				TryShowApproachPopup(kind, hint);

			RegisterHistoricalMarker(CurrentBar, tag);

			if (EnableSignalAutoCorrect)
				RegisterPostedApproach(CurrentBar, kind, tag);
		}

		private void ResetTradeTracking()
		{
			tradeState = TradeState.Flat;
			openTradeId = 0;
			openEntryBar = 0;
			openEntryPrice = 0;
			barsInTrade = 0;
			longKcBreakBars = 0;
			shortKcBreakBars = 0;
			trailStopPrice = 0;
			maxFavorableTicks = 0;
			lastExitWarningReason = CloseReason.None;
			openEntryTrigger = EntryTrigger.None;
			openSignalGrade = SignalGrade.C;
			openTradeCharacter = TradeCharacter.Normal;
			liveTradeCharacter = TradeCharacter.Normal;
			liveCharacterTag = string.Empty;
			postPeakMaxTicks = 0;
			openIsReEntry = false;
			RemoveDrawObject("MES500TV36L_Trail");
			RemoveDrawObject("MES500TV36L_Char");
			RemoveDrawObject("MES500TV36L_Char_Sub");
			RemoveDrawObject("MES500TV36L_Char_Hint");
		}

		private bool IsEntryCooldownElapsed()
		{
			return CurrentBar - lastCloseBar >= EntryCooldownBars;
		}

		private bool HasMinHistStep(double hist0, double hist1)
		{
			return Math.Abs(hist0 - hist1) >= MinHistStepTicks * TickSize;
		}

		private bool IsLongContinuationSetup(double kcMid, double hist0, double hist1, double hist2)
		{
			if (Close[0] <= kcMid || Close[0] <= Open[0])
				return false;

			if (RequireKcMidTrend && kcMid <= kcEmaInd[1])
				return false;

			if (RequireBounceForContinuation && !(Close[1] <= Open[1] || Close[1] <= Close[2]))
				return false;

			if (CntMinKcMidSlopeBars > 0 && !HasKcMidSlope(true, CntMinKcMidSlopeBars))
				return false;

			return IsLongIgnition(hist0, hist1, hist2) && HasMinHistStep(hist0, hist1);
		}

		private bool IsShortContinuationSetup(double kcMid, double hist0, double hist1, double hist2)
		{
			if (Close[0] >= kcMid || Close[0] >= Open[0])
				return false;

			if (RequireKcMidTrend && kcMid >= kcEmaInd[1])
				return false;

			if (RequireBounceForContinuation && !(Close[1] >= Open[1] || Close[1] >= Close[2]))
				return false;

			if (CntMinKcMidSlopeBars > 0 && !HasKcMidSlope(false, CntMinKcMidSlopeBars))
				return false;

			return IsShortIgnition(hist0, hist1, hist2) && HasMinHistStep(hist0, hist1);
		}

		private bool HasEarlyMomentum(bool isLong, double hist0, double hist1, double hist2)
		{
			if (isLong)
				return IsLongIgnition(hist0, hist1, hist2) && HasMinHistStep(hist0, hist1);

			return IsShortIgnition(hist0, hist1, hist2) && HasMinHistStep(hist0, hist1);
		}

		private bool IsEntryBlocked(SqueezeState squeezeState, SqueezeReleaseDirection releaseDirection, bool isLong)
		{
			if (BlockFullSqueezeOnly)
				return squeezeState == SqueezeState.Full;

			if (squeezeState == SqueezeState.Off)
				return false;

			if (AllowPartialSqueezeEntry && squeezeState == SqueezeState.Partial)
			{
				if (isLong)
					return releaseDirection != SqueezeReleaseDirection.Bullish && releaseDirection != SqueezeReleaseDirection.Both;
				return releaseDirection != SqueezeReleaseDirection.Bearish && releaseDirection != SqueezeReleaseDirection.Both;
			}

			return true;
		}

		private bool IsLongMomentumReady(double hist0, double hist1, double hist2)
		{
			if (RequireThreeBarMomentum)
				return hist0 > hist1 && hist1 > hist2 && hist0 > 0;

			return hist0 > hist1 && hist0 > 0;
		}

		private bool IsShortMomentumReady(double hist0, double hist1, double hist2)
		{
			if (RequireThreeBarMomentum)
				return hist0 < hist1 && hist1 < hist2 && hist0 < 0;

			return hist0 < hist1 && hist0 < 0;
		}

		private bool IsLongIgnition(double hist0, double hist1, double hist2)
		{
			return (hist0 > 0 && hist1 <= 0) || (hist0 > hist1 && hist0 > 0 && hist1 <= hist2);
		}

		private bool IsShortIgnition(double hist0, double hist1, double hist2)
		{
			return (hist0 < 0 && hist1 >= 0) || (hist0 < hist1 && hist0 < 0 && hist1 >= hist2);
		}

		private bool IsLongMacdCross()
		{
			return macdInd[0] > macdInd.Avg[0] && macdInd[1] <= macdInd.Avg[1];
		}

		private bool IsShortMacdCross()
		{
			return macdInd[0] < macdInd.Avg[0] && macdInd[1] >= macdInd.Avg[1];
		}

		// Mimo squeeze (squeezeState == Off) se release direction nepočítá — trh už je v trendu.
		// Stačí že KC mid stoupá (long) nebo klesá (short).
		private bool IsBullOk(SqueezeState squeezeState, SqueezeReleaseDirection releaseDirection)
		{
			if (!RequireDirectionalRelease)
				return true;
			if (squeezeState == SqueezeState.Off)
				return kcEmaInd[0] >= kcEmaInd[1]; // KC mid rising or flat = bull trend
			return releaseDirection == SqueezeReleaseDirection.Bullish
				|| releaseDirection == SqueezeReleaseDirection.Both;
		}

		private bool IsBearOk(SqueezeState squeezeState, SqueezeReleaseDirection releaseDirection)
		{
			if (!RequireDirectionalRelease)
				return true;
			if (squeezeState == SqueezeState.Off)
				return kcEmaInd[0] <= kcEmaInd[1]; // KC mid falling or flat = bear trend
			return releaseDirection == SqueezeReleaseDirection.Bearish
				|| releaseDirection == SqueezeReleaseDirection.Both;
		}

		private bool IsBullReleaseOk(SqueezeReleaseDirection releaseDirection)
		{
			return !RequireDirectionalRelease
				|| releaseDirection == SqueezeReleaseDirection.Bullish
				|| releaseDirection == SqueezeReleaseDirection.Both;
		}

		private bool IsBearReleaseOk(SqueezeReleaseDirection releaseDirection)
		{
			return !RequireDirectionalRelease
				|| releaseDirection == SqueezeReleaseDirection.Bearish
				|| releaseDirection == SqueezeReleaseDirection.Both;
		}

		private bool EvaluateLongEntry(
			SqueezeState squeezeState,
			SqueezeReleaseDirection releaseDirection,
			bool entryBlocked,
			double kcMid,
			double kcUpper,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle,
			out EntryTrigger trigger)
		{
			trigger = EntryTrigger.None;

			if (entryBlocked || macdTangle || IsLongExhaustion(hist0, hist1, hist2, hist3))
				return false;

			double upperTrigger = kcUpper + entryBuffer;
			double midTrigger = kcMid + entryBuffer;
			bool momentumReady = IsLongMomentumReady(hist0, hist1, hist2);
			bool macdCross = AllowMacdCrossEntry && IsLongMacdCross() && hist0 > hist1;
			// Mimo squeeze stačí KC mid směr, ve squeeze/release požadujeme bullish release
			bool bullOk = IsBullOk(squeezeState, releaseDirection);

			if (AllowTrendContinuationEntry && bullOk && IsLongContinuationSetup(kcMid, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Continuation;
				return true;
			}

			if (AllowPartialSqueezeEntry
				&& squeezeState == SqueezeState.Partial
				&& bullOk
				&& Close[0] > midTrigger
				&& HasEarlyMomentum(true, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Ignition;
				return true;
			}

			if (AllowKcMidEntry && bullOk && Close[0] > midTrigger && HasEarlyMomentum(true, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Ignition;
				return true;
			}

			if (AllowMacdCrossEntry && macdCross && Close[0] > midTrigger && hist0 > 0 && HasMinHistStep(hist0, hist1))
			{
				trigger = EntryTrigger.MacdCross;
				return true;
			}

			if (AllowBarBreakEntry && bullOk && High[0] > upperTrigger && HasEarlyMomentum(true, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Breakout;
				return true;
			}

			if (bullOk && Close[0] > upperTrigger && (HasEarlyMomentum(true, hist0, hist1, hist2) || momentumReady))
			{
				trigger = EntryTrigger.Breakout;
				return true;
			}

			return false;
		}

		private bool EvaluateShortEntry(
			SqueezeState squeezeState,
			SqueezeReleaseDirection releaseDirection,
			bool entryBlocked,
			double kcMid,
			double kcLower,
			double entryBuffer,
			double hist0,
			double hist1,
			double hist2,
			double hist3,
			bool macdTangle,
			out EntryTrigger trigger)
		{
			trigger = EntryTrigger.None;

			if (entryBlocked || macdTangle || IsShortExhaustion(hist0, hist1, hist2, hist3))
				return false;

			double lowerTrigger = kcLower - entryBuffer;
			double midTrigger = kcMid - entryBuffer;
			bool momentumReady = IsShortMomentumReady(hist0, hist1, hist2);
			bool macdCross = AllowMacdCrossEntry && IsShortMacdCross() && hist0 < hist1;
			// Mimo squeeze stačí KC mid směr, ve squeeze/release požadujeme bearish release
			bool bearOk = IsBearOk(squeezeState, releaseDirection);

			if (AllowTrendContinuationEntry && bearOk && IsShortContinuationSetup(kcMid, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Continuation;
				return true;
			}

			if (AllowPartialSqueezeEntry
				&& squeezeState == SqueezeState.Partial
				&& bearOk
				&& Close[0] < midTrigger
				&& HasEarlyMomentum(false, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Ignition;
				return true;
			}

			if (AllowKcMidEntry && bearOk && Close[0] < midTrigger && HasEarlyMomentum(false, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Ignition;
				return true;
			}

			if (AllowMacdCrossEntry && macdCross && Close[0] < midTrigger && hist0 < 0 && HasMinHistStep(hist0, hist1))
			{
				trigger = EntryTrigger.MacdCross;
				return true;
			}

			if (AllowBarBreakEntry && bearOk && Low[0] < lowerTrigger && HasEarlyMomentum(false, hist0, hist1, hist2))
			{
				trigger = EntryTrigger.Breakout;
				return true;
			}

			if (bearOk && Close[0] < lowerTrigger && (HasEarlyMomentum(false, hist0, hist1, hist2) || momentumReady))
			{
				trigger = EntryTrigger.Breakout;
				return true;
			}

			return false;
		}

		private void DrawSubHint(string tag, int barsAgo, double anchorY, bool above, string hint, Brush brush)
		{
			if (string.IsNullOrEmpty(hint))
				return;

			double hintY = above
				? anchorY - (LabelOffsetTicks * 0.8 * TickSize)
				: anchorY + (LabelOffsetTicks * 0.8 * TickSize);

			Draw.Text(this, tag, false, hint, barsAgo, hintY, 0, brush,
				hintFont, TextAlignment.Center, Brushes.Black, Brushes.DimGray, 90);
		}

		private string GetTriggerStatName(int index)
		{
			switch (index)
			{
				case 0: return "Průraz hranice";
				case 1: return "Průraz středu pásma";
				case 2: return "Silný start pohybu";
				case 3: return "Potvrzení směru";
				case 4: return "Návrat do trendu";
				default: return "Signál";
			}
		}

		private string GetEntryTriggerText(EntryTrigger trigger)
		{
			switch (trigger)
			{
				case EntryTrigger.Breakout: return "Průraz hranice";
				case EntryTrigger.Midline: return "Průraz středu pásma";
				case EntryTrigger.Ignition: return "Silný start pohybu";
				case EntryTrigger.MacdCross: return "Potvrzení směru";
				case EntryTrigger.Continuation: return "Návrat do trendu";
				default: return string.Empty;
			}
		}

		private string GetEntryTriggerHint(EntryTrigger trigger, bool isLong)
		{
			switch (trigger)
			{
				case EntryTrigger.Breakout:
					return isLong
						? "Cena prorazila horní hranici — otevři NÁKUP"
						: "Cena prorazila dolní hranici — otevři PRODEJ";
				case EntryTrigger.Midline:
					return isLong
						? "Cena nad středem pásma a roste — otevři NÁKUP"
						: "Cena pod středem pásma a klesá — otevři PRODEJ";
				case EntryTrigger.Ignition:
					return isLong
						? "Pohyb nahoru se zrychluje — brzký NÁKUP"
						: "Pohyb dolů se zrychluje — brzký PRODEJ";
				case EntryTrigger.MacdCross:
					return isLong
						? "Momentum se otáčí nahoru — otevři NÁKUP"
						: "Momentum se otáčí dolů — otevři PRODEJ";
				case EntryTrigger.Continuation:
					return isLong
						? "Po korekci trend zase roste — otevři NÁKUP"
						: "Po korekci trend zase klesá — otevři PRODEJ";
				default: return string.Empty;
			}
		}

		private string GetSignalGradeHint(SignalGrade grade)
		{
			switch (grade)
			{
				case SignalGrade.A: return "Silný signál, vysoká šance pokračování";
				case SignalGrade.B: return "Dobrý signál, obchodovatelný";
				default: return "Slabší signál — buď opatrný";
			}
		}

		private string BuildEntryMainLabel(bool isLong, bool isReEntry, int tradeId)
		{
			if (isLong)
				return isReEntry ? "ZPĚTNÝ NÁKUP #" + tradeId : "NÁKUP #" + tradeId;
			return isReEntry ? "ZPĚTNÝ PRODEJ #" + tradeId : "PRODEJ #" + tradeId;
		}

		private string BuildEntrySubLabel(EntryTrigger trigger, SignalGrade grade, bool chopRisk)
		{
			string text = string.Empty;
			if (ShowEntryTrigger)
				text = GetEntryTriggerText(trigger);
			if (ShowSignalGrade)
				text += (text.Length > 0 ? " · " : string.Empty) + GetSignalGradeText(grade);
			if (chopRisk)
				text += (text.Length > 0 ? " · " : string.Empty) + "Riziko falešného signálu";
			return text;
		}

		private string BuildEntryHint(bool isLong, EntryTrigger trigger, SignalGrade grade)
		{
			string triggerHint = GetEntryTriggerHint(trigger, isLong);
			if (triggerHint.Length > 0)
				return triggerHint;
			return (isLong ? "Otevři NÁKUP" : "Otevři PRODEJ") + ". " + GetSignalGradeHint(grade);
		}

		private string GetApproachMainLabel(ApproachKind kind)
		{
			switch (kind)
			{
				case ApproachKind.Buy: return "Nákup?";
				case ApproachKind.ReBuy: return "Zpětný nákup?";
				case ApproachKind.Sell: return "Prodej?";
				case ApproachKind.ReSell: return "Zpětný prodej?";
				default: return "Zavřít?";
			}
		}

		private string GetApproachHint(ApproachKind kind)
		{
			switch (kind)
			{
				case ApproachKind.Buy: return "Připrav se — brzy může přijít NÁKUP";
				case ApproachKind.ReBuy: return "Brzy může přijít další NÁKUP";
				case ApproachKind.Sell: return "Připrav se — brzy může přijít PRODEJ";
				case ApproachKind.ReSell: return "Brzy může přijít další PRODEJ";
				default: return "Pozor — brzy může přijít ZAVŘÍT";
			}
		}

		private string GetTradeCharacterMainLabel(string tag)
		{
			switch (tag)
			{
				case "RUNNER": return "Silný trend";
				case "CHOP?": return "Nejistý trh?";
				case "HOLD?": return "Drž pozici?";
				default: return "Začátek";
			}
		}

		private string GetTradeCharacterHint(string tag)
		{
			switch (tag)
			{
				case "RUNNER": return "Trend jede silně — nech obchod běžet";
				case "CHOP?": return "Rychlý zisk a pád — zvaž zavření";
				case "HOLD?": return "Jen korekce — trend může pokračovat";
				default: return "Počkej na potvrzení směru";
			}
		}

		private bool CanEvaluateEntry()
		{
			if (UseIntrabarEntries)
				return true;

			if (UsesDeferredBarCloseEntry())
				return false;

			return Calculate == Calculate.OnBarClose || IsFirstTickOfBar;
		}

		private double GetCurrentPnlTicks(bool wasLong)
		{
			return wasLong
				? (Close[0] - openEntryPrice) / TickSize
				: (openEntryPrice - Close[0]) / TickSize;
		}

		private void UpdateMaxFavorable(bool wasLong)
		{
			maxFavorableTicks = Math.Max(maxFavorableTicks, GetCurrentPnlTicks(wasLong));
		}

		private bool HasKcMidSlope(bool isLong, int bars)
		{
			if (bars <= 1 || CurrentBar < bars)
				return false;

			for (int i = 0; i < bars - 1; i++)
			{
				if (isLong && kcEmaInd[i] <= kcEmaInd[i + 1])
					return false;
				if (!isLong && kcEmaInd[i] >= kcEmaInd[i + 1])
					return false;
			}

			return true;
		}

		private int GetTriggerIndex(EntryTrigger trigger)
		{
			switch (trigger)
			{
				case EntryTrigger.Breakout: return 0;
				case EntryTrigger.Midline: return 1;
				case EntryTrigger.Ignition: return 2;
				case EntryTrigger.MacdCross: return 3;
				case EntryTrigger.Continuation: return 4;
				default: return -1;
			}
		}

		private SignalGrade GetSignalGrade(bool isLong, EntryTrigger trigger, double hist0, double hist1, double hist2)
		{
			if (IsMacdTangle())
				return SignalGrade.C;

			switch (trigger)
			{
				case EntryTrigger.Breakout:
					if (isLong ? IsLongMomentumReady(hist0, hist1, hist2) : IsShortMomentumReady(hist0, hist1, hist2))
						return SignalGrade.A;
					return HasMinHistStep(hist0, hist1) ? SignalGrade.B : SignalGrade.C;

				case EntryTrigger.Continuation:
					if (HasKcMidSlope(isLong, CntMinKcMidSlopeBars) && HasMinHistStep(hist0, hist1))
						return SignalGrade.B;
					return SignalGrade.C;

				case EntryTrigger.Ignition:
				case EntryTrigger.MacdCross:
					return HasMinHistStep(hist0, hist1) ? SignalGrade.B : SignalGrade.C;

				case EntryTrigger.Midline:
					return SignalGrade.C;

				default:
					return SignalGrade.C;
			}
		}

		private string GetSignalGradeText(SignalGrade grade)
		{
			switch (grade)
			{
				case SignalGrade.A: return "Silný (A)";
				case SignalGrade.B: return "Dobrý (B)";
				default: return "Slabší (C)";
			}
		}

		private string GetEntryAlertSound(SignalGrade grade, EntryTrigger trigger)
		{
			if (grade == SignalGrade.A)
				return "Alert1.wav";
			if (grade == SignalGrade.B)
				return trigger == EntryTrigger.Continuation ? "Alert3.wav" : "Alert2.wav";
			return "Alert4.wav";
		}

		private void UpdateLiveTradeCharacter(bool isLong, double kcMid, double hist0, double hist1, double hist2)
		{
			if (!ShowTradeCharacter)
				return;

			double pnl = GetCurrentPnlTicks(isLong);
			postPeakMaxTicks = maxFavorableTicks;
			string tag;

			if (maxFavorableTicks >= RunnerTargetTicks)
			{
				liveTradeCharacter = TradeCharacter.Runner;
				tag = "RUNNER";
			}
			else if (maxFavorableTicks >= ChopQuickPeakTicks
				&& maxFavorableTicks - pnl >= ChopGivebackTicks)
			{
				liveTradeCharacter = TradeCharacter.Chop;
				tag = "CHOP?";
			}
			else
			{
				liveTradeCharacter = TradeCharacter.Normal;
				tag = maxFavorableTicks >= ChopQuickPeakTicks * 0.5 ? "HOLD?" : "EARLY";
			}

			liveCharacterTag = tag;
			string mainLabel = GetTradeCharacterMainLabel(tag);
			string hint = GetTradeCharacterHint(tag);
			double labelY = isLong
				? High[0] + ((LabelOffsetTicks + 4) * TickSize)
				: Low[0] - ((LabelOffsetTicks + 4) * TickSize);
			Brush brush = liveTradeCharacter == TradeCharacter.Chop
				? Brushes.DeepPink
				: liveTradeCharacter == TradeCharacter.Runner
					? Brushes.LimeGreen
					: Brushes.Gold;

			Draw.Text(this, "MES500TV36L_Char", false, mainLabel, 0, labelY, 0, brush,
				warningFont, TextAlignment.Center, Brushes.Black, Brushes.DimGray, 100);
			DrawSubHint("MES500TV36L_Char_Hint", 0, labelY, isLong, hint, brush);
			RequestChartRefresh();
		}

		private void DrawActiveTrailLine(bool isLong)
		{
			if (!ShowTrailLine || !UseAtrTrail || trailStopPrice <= 0)
				return;

			Brush brush = isLong ? Brushes.MediumSeaGreen : Brushes.IndianRed;
			Draw.Line(this, "MES500TV36L_Trail", false, 1, trailStopPrice, 0, trailStopPrice,
				brush, DashStyleHelper.Dash, 2);
		}

		private void StartRecoveryWatch(bool wasLong, double entryPrice)
		{
			if (maxFavorableTicks < ChopQuickPeakTicks)
				return;

			recoveryWatchActive = true;
			recoveryWatchIsLong = wasLong;
			recoveryWatchEntryPrice = entryPrice;
			recoveryWatchEndTime = Time[0].AddSeconds(RecoveryWindowSec);
			recoveryMissShown = false;
		}

		private void UpdateRecoveryWatch()
		{
			if (!recoveryWatchActive)
				return;

			if (Time[0] > recoveryWatchEndTime)
			{
				recoveryWatchActive = false;
				RemoveDrawObject("MES500TV36L_Missed");
				return;
			}

			double favorable = recoveryWatchIsLong
				? (High[0] - recoveryWatchEntryPrice) / TickSize
				: (recoveryWatchEntryPrice - Low[0]) / TickSize;

			if (favorable >= RunnerTargetTicks && !recoveryMissShown)
			{
				recoveryMissShown = true;
				string text = string.Format("Zmeškaný runner +{0} ticků\n(Po whipsaw by cena došla na cíl)", RunnerTargetTicks);
				Draw.TextFixed(this, "MES500TV36L_Missed", text, TextPosition.BottomRight,
					Brushes.OrangeRed, statsFont, Brushes.Black, Brushes.DarkRed, 80);
				if (EnableTaskbarFlash && FlashOnClose)
					FlashTaskbar();
			}
		}

		private void RequestChartRefresh()
		{
			needsChartRefresh = true;
		}

		private void PerformChartRefresh()
		{
			if (State != State.Realtime)
				return;

			ForceRefresh();
			if (ChartControl != null)
			{
				ChartControl.Dispatcher.BeginInvoke(new Action(() =>
				{
					if (ChartControl != null)
						ChartControl.InvalidateVisual();
				}));
			}
		}

		private void RegisterHistoricalMarker(int bar, string tag, bool isLongEntry = false, bool isShortEntry = false, bool isCloseMarker = false)
		{
			if (string.IsNullOrEmpty(tag) || bar < 0 || trackedHistoricalMarkers == null)
				return;

			for (int i = 0; i < trackedHistoricalMarkers.Count; i++)
			{
				if (trackedHistoricalMarkers[i].Tag == tag && trackedHistoricalMarkers[i].Bar == bar)
					return;
			}

			trackedHistoricalMarkers.Add(new TrackedHistoricalMarker
			{
				Bar = bar,
				Tag = tag,
				IsLongEntry = isLongEntry,
				IsShortEntry = isShortEntry,
				IsCloseMarker = isCloseMarker
			});
		}

		private void RemoveHistoricalMarkerDrawObjects(TrackedHistoricalMarker marker)
		{
			string tag = marker.Tag;
			RemoveDrawObject(tag);
			RemoveDrawObject(tag + "_Sq");
			RemoveDrawObject(tag + "_Txt");
			RemoveDrawObject(tag + "_Hint");
			RemoveDrawObject(tag + "_Arrow");
			RemoveDrawObject(tag + "_Text");
			RemoveDrawObject(tag + "_HoldAdvice");
			RemoveDrawObject(tag + "_HoldHint");
			RemoveDrawObject(tag + "_ReasonHint");
			RemoveDrawObject(tag + "_Diamond");
			RemoveDrawObject(tag + "_Line");
			RemoveDrawObject(tag + "_Mark");
			RemoveDrawObject(tag + "_Bull");
			RemoveDrawObject(tag + "_BullTxt");
			RemoveDrawObject(tag + "_BullHint");
			RemoveDrawObject(tag + "_Bear");
			RemoveDrawObject(tag + "_BearTxt");
			RemoveDrawObject(tag + "_BearHint");

			int barsAgo = CurrentBar - marker.Bar;
			if (barsAgo >= 0 && barsAgo <= CurrentBar)
			{
				if (marker.IsLongEntry)
					Values[5][barsAgo] = double.NaN;
				else if (marker.IsShortEntry)
					Values[6][barsAgo] = double.NaN;
				else if (marker.IsCloseMarker)
					Values[7][barsAgo] = double.NaN;
			}
		}

		private void PruneHistoricalMarkers()
		{
			if (MaxMarkerHistoryBars <= 0 || trackedHistoricalMarkers == null)
				return;

			int cutoffBar = CurrentBar - MaxMarkerHistoryBars;
			bool removedAny = false;

			for (int i = trackedHistoricalMarkers.Count - 1; i >= 0; i--)
			{
				if (trackedHistoricalMarkers[i].Bar <= cutoffBar)
				{
					RemoveHistoricalMarkerDrawObjects(trackedHistoricalMarkers[i]);
					trackedHistoricalMarkers.RemoveAt(i);
					removedAny = true;
				}
			}

			for (int bar = 0; bar <= cutoffBar; bar++)
			{
				string hintTag = "MES500TV36L_Hint_" + bar;
				RemoveDrawObject(hintTag + "_Sq");
				RemoveDrawObject(hintTag + "_Txt");
				RemoveDrawObject(hintTag + "_Hint");
				RemoveDrawObject("MES500TV36L_Ring_" + bar);

				string releaseTag = "MES500TV36L_Release_" + bar;
				RemoveDrawObject(releaseTag + "_Bull");
				RemoveDrawObject(releaseTag + "_BullTxt");
				RemoveDrawObject(releaseTag + "_BullHint");
				RemoveDrawObject(releaseTag + "_Bear");
				RemoveDrawObject(releaseTag + "_BearTxt");
				RemoveDrawObject(releaseTag + "_BearHint");
			}

			if (removedAny)
				RequestChartRefresh();
		}

		[DllImport("user32.dll")]
		private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

		[StructLayout(LayoutKind.Sequential)]
		private struct FLASHWINFO
		{
			public uint cbSize;
			public IntPtr hwnd;
			public uint dwFlags;
			public uint uCount;
			public uint dwTimeout;
		}

		private const uint FLASHW_TRAY = 0x00000002;
		private const uint FLASHW_TIMERNOFG = 0x0000000C;

		private void FlashTaskbar()
		{
			if (!EnableTaskbarFlash)
				return;

			try
			{
				IntPtr hwnd = IntPtr.Zero;
				if (ChartControl != null)
				{
					Window window = Window.GetWindow(ChartControl.Parent);
					if (window != null)
						hwnd = new WindowInteropHelper(window).Handle;
				}

				if (hwnd == IntPtr.Zero)
					return;

				FLASHWINFO fwi = new FLASHWINFO
				{
					cbSize = Convert.ToUInt32(Marshal.SizeOf(typeof(FLASHWINFO))),
					hwnd = hwnd,
					dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
					uCount = 3,
					dwTimeout = 0
				};
				FlashWindowEx(ref fwi);
			}
			catch
			{
				// Win32 flash je best-effort; alert zvuk zůstává
			}
		}

		private void TryShowApproachPopup(ApproachKind kind, string hint)
		{
			string title;
			Color accent;
			switch (kind)
			{
				case ApproachKind.Buy:
					title = "NÁKUP SE BLÍŽÍ";
					accent = Colors.LimeGreen;
					break;
				case ApproachKind.ReBuy:
					title = "ZPĚTNÝ NÁKUP SE BLÍŽÍ";
					accent = Colors.LimeGreen;
					break;
				case ApproachKind.Sell:
					title = "PRODEJ SE BLÍŽÍ";
					accent = Colors.OrangeRed;
					break;
				case ApproachKind.ReSell:
					title = "ZPĚTNÝ PRODEJ SE BLÍŽÍ";
					accent = Colors.OrangeRed;
					break;
				default:
					title = "ZAVŘENÍ SE BLÍŽÍ";
					accent = Colors.Gold;
					break;
			}

			TryShowScreenPopup("Approach_" + kind, title, hint, accent);
		}

		private void TryShowScreenPopup(string dedupKey, string title, string message, Color accentColor)
		{
			if (!EnableScreenPopup || ChartControl == null || string.IsNullOrEmpty(title))
				return;

			string key = CurrentBar + "_" + dedupKey;
			if (key == lastScreenPopupKey)
				return;

			lastScreenPopupKey = key;

			ChartControl.Dispatcher.BeginInvoke(new Action(() =>
			{
				try
				{
					CloseScreenPopupInternal();

					var titleBlock = new TextBlock
					{
						Text = title,
						Foreground = new SolidColorBrush(accentColor),
						FontSize = 26,
						FontWeight = FontWeights.Bold,
						TextAlignment = System.Windows.TextAlignment.Center,
						TextWrapping = TextWrapping.Wrap
					};

					var messageBlock = new TextBlock
					{
						Text = message ?? string.Empty,
						Foreground = Brushes.WhiteSmoke,
						FontSize = 15,
						Margin = new Thickness(0, 8, 0, 0),
						TextAlignment = System.Windows.TextAlignment.Center,
						TextWrapping = TextWrapping.Wrap,
						MaxWidth = 460
					};

					var panel = new StackPanel();
					panel.Children.Add(titleBlock);
					if (!string.IsNullOrEmpty(message))
						panel.Children.Add(messageBlock);

					var border = new Border
					{
						Background = new SolidColorBrush(Color.FromArgb(235, 18, 18, 18)),
						BorderBrush = new SolidColorBrush(accentColor),
						BorderThickness = new Thickness(4),
						CornerRadius = new CornerRadius(10),
						Padding = new Thickness(24, 18, 24, 18),
						Child = panel
					};

					screenPopupWindow = new Window
					{
						Content = border,
						WindowStyle = WindowStyle.None,
						AllowsTransparency = true,
						Background = Brushes.Transparent,
						Topmost = true,
						ShowInTaskbar = false,
						ResizeMode = ResizeMode.NoResize,
						SizeToContent = SizeToContent.WidthAndHeight,
						WindowStartupLocation = WindowStartupLocation.Manual,
						IsHitTestVisible = false
					};

					screenPopupWindow.Loaded += (s, e) =>
					{
						screenPopupWindow.UpdateLayout();
						screenPopupWindow.Left = (SystemParameters.PrimaryScreenWidth - screenPopupWindow.ActualWidth) / 2;
						screenPopupWindow.Top = (SystemParameters.PrimaryScreenHeight - screenPopupWindow.ActualHeight) / 2;
					};

					screenPopupWindow.Show();

					screenPopupTimer = new DispatcherTimer
					{
						Interval = TimeSpan.FromSeconds(Math.Max(1, PopupDurationSec))
					};
					screenPopupTimer.Tick += (s, e) =>
					{
						screenPopupTimer.Stop();
						CloseScreenPopupInternal();
					};
					screenPopupTimer.Start();
				}
				catch
				{
					// Popup je best-effort; zvukový alert zůstává
				}
			}));
		}

		private void CloseScreenPopup()
		{
			if (ChartControl != null)
				ChartControl.Dispatcher.BeginInvoke(new Action(CloseScreenPopupInternal));
			else
				CloseScreenPopupInternal();
		}

		private void CloseScreenPopupInternal()
		{
			if (screenPopupTimer != null)
			{
				screenPopupTimer.Stop();
				screenPopupTimer = null;
			}

			if (screenPopupWindow != null)
			{
				try { screenPopupWindow.Close(); }
				catch { }
				screenPopupWindow = null;
			}
		}

		private void RegisterPostedApproach(int bar, ApproachKind kind, string tag)
		{
			postedSignals.RemoveAll(s => s.IsApproach && s.Bar == bar && s.ApproachKind == kind);
			postedSignals.Add(new PostedSignal
			{
				Bar = bar,
				IsApproach = true,
				ApproachKind = kind,
				Tag = tag,
				Voided = false
			});
		}

		private void RegisterPostedEntry(int tradeId, bool isLong, int barsAgo, EntryTrigger trigger, SignalGrade grade, bool wasReEntry, string tag, double entryPrice)
		{
			int bar = CurrentBar - barsAgo;
			postedSignals.RemoveAll(s => !s.IsApproach && s.TradeId == tradeId && s.Bar == bar);

			postedSignals.Add(new PostedSignal
			{
				Bar = bar,
				TradeId = tradeId,
				IsLong = isLong,
				IsApproach = false,
				Trigger = trigger,
				Grade = grade,
				WasReEntry = wasReEntry,
				QualifiedReEntry = wasReEntry,
				Tag = tag,
				Voided = false,
				EntryPrice = entryPrice
			});
		}

		private void PruneOldPostedSignals()
		{
			int keepBars = Math.Max(MaxMarkerHistoryBars, SignalReviewBars + 8);
			for (int i = postedSignals.Count - 1; i >= 0; i--)
			{
				PostedSignal sig = postedSignals[i];
				if (CurrentBar - sig.Bar > keepBars)
				{
					if (!sig.Voided)
						VoidPostedSignal(sig);
					postedSignals.RemoveAt(i);
				}
			}
		}

		private bool IsMacdTangleAt(int barsAgo)
		{
			if (CurrentBar < barsAgo + 1)
				return false;

			double separation = Math.Abs(macdInd[barsAgo] - macdInd.Avg[barsAgo]);
			double macdSlope = macdInd[barsAgo] - macdInd[barsAgo + 1];
			double signalSlope = macdInd.Avg[barsAgo] - macdInd.Avg[barsAgo + 1];
			double sepThreshold = TangleSeparationTicks * TickSize;
			double slopeThreshold = TangleSlopeTicks * TickSize;

			return separation <= sepThreshold
				&& Math.Abs(macdSlope) <= slopeThreshold
				&& Math.Abs(signalSlope) <= slopeThreshold;
		}

		private bool IsEntryLogicStillValid(PostedSignal sig, int barsAgo)
		{
			if (CurrentBar < barsAgo + 3)
				return true;

			double hist0 = macdInd.Diff[barsAgo];
			double hist1 = macdInd.Diff[barsAgo + 1];
			double hist2 = macdInd.Diff[barsAgo + 2];
			double hist3 = macdInd.Diff[barsAgo + 3];

			if (IsMacdTangleAt(barsAgo))
				return false;

			if (sig.IsLong)
			{
				if (hist0 <= 0)
					return false;
				if (IsLongExhaustion(hist0, hist1, hist2, hist3))
					return false;
			}
			else
			{
				if (hist0 >= 0)
					return false;
				if (IsShortExhaustion(hist0, hist1, hist2, hist3))
					return false;
			}

			SignalGrade gradeNow = GetSignalGrade(sig.IsLong, sig.Trigger, hist0, hist1, hist2);
			if (sig.WasReEntry && ReEntryRequireGradeBOrBetter && gradeNow == SignalGrade.C)
				return false;

			if (gradeNow == SignalGrade.C && sig.Grade != SignalGrade.C)
				return false;

			return true;
		}

		private bool IsEntryConfirmedByPrice(PostedSignal sig, int barsAgo)
		{
			double entry = sig.EntryPrice > 0 ? sig.EntryPrice : Close[barsAgo];
			double maxFav = 0;
			double maxAdv = 0;

			for (int i = barsAgo - 1; i >= 0; i--)
			{
				if (sig.IsLong)
				{
					maxFav = Math.Max(maxFav, (High[i] - entry) / TickSize);
					maxAdv = Math.Max(maxAdv, (entry - Low[i]) / TickSize);
				}
				else
				{
					maxFav = Math.Max(maxFav, (entry - Low[i]) / TickSize);
					maxAdv = Math.Max(maxAdv, (High[i] - entry) / TickSize);
				}
			}

			return !(maxAdv >= FalseSignalAdverseTicks && maxFav < SignalConfirmTicks);
		}

		private bool IsApproachStillValid(PostedSignal sig, int barsAgo)
		{
			for (int i = 0; i < postedSignals.Count; i++)
			{
				PostedSignal other = postedSignals[i];
				if (!other.Voided && !other.IsApproach && other.Bar == sig.Bar)
					return false;
			}

			if (barsAgo < 1)
				return true;

			double refPrice = Close[barsAgo];
			double maxAdv = 0;
			for (int i = barsAgo - 1; i >= 0; i--)
			{
				if (sig.ApproachKind == ApproachKind.Buy || sig.ApproachKind == ApproachKind.ReBuy)
					maxAdv = Math.Max(maxAdv, (refPrice - Low[i]) / TickSize);
				else if (sig.ApproachKind == ApproachKind.Sell || sig.ApproachKind == ApproachKind.ReSell)
					maxAdv = Math.Max(maxAdv, (High[i] - refPrice) / TickSize);
			}

			return maxAdv < FalseSignalAdverseTicks;
		}

		private void VoidPostedSignal(PostedSignal sig)
		{
			if (sig.Voided)
				return;

			sig.Voided = true;
			RemoveDrawObject(sig.Tag + "_Sq");
			RemoveDrawObject(sig.Tag + "_Txt");
			RemoveDrawObject(sig.Tag + "_Hint");
			RemoveDrawObject(sig.Tag + "_Arrow");
			RemoveDrawObject(sig.Tag + "_Text");
			RemoveDrawObject(sig.Tag + "_HoldAdvice");
			RemoveDrawObject(sig.Tag + "_HoldHint");

			int barsAgo = CurrentBar - sig.Bar;
			if (!sig.IsApproach && barsAgo >= 0 && barsAgo <= CurrentBar)
			{
				if (sig.IsLong)
					Values[5][barsAgo] = double.NaN;
				else
					Values[6][barsAgo] = double.NaN;
			}
		}

		private void CorrectEntryLabel(PostedSignal sig, int barsAgo, bool asReEntry)
		{
			sig.WasReEntry = asReEntry;
			RemoveDrawObject(sig.Tag + "_Text");

			string label = BuildEntryMainLabel(sig.IsLong, asReEntry, sig.TradeId);
			string subLabel = BuildEntrySubLabel(sig.Trigger, sig.Grade, false);
			string hint = BuildEntryHint(sig.IsLong, sig.Trigger, sig.Grade);

			double arrowY = sig.IsLong
				? Low[barsAgo] - (ArrowOffsetTicks * TickSize)
				: High[barsAgo] + (ArrowOffsetTicks * TickSize);
			double labelY = sig.IsLong
				? arrowY - (LabelOffsetTicks * TickSize)
				: arrowY + (LabelOffsetTicks * TickSize);
			Brush brush = sig.IsLong ? LongSignalBrush : ShortSignalBrush;

			if (ShowSignalLabels)
			{
				string fullLabel = subLabel.Length > 0 ? label + "\n" + subLabel : label;
				Draw.Text(this, sig.Tag + "_Text", false, fullLabel, barsAgo, labelY, 0, brush,
					signalFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				DrawSubHint(sig.Tag + "_Hint", barsAgo, labelY, sig.IsLong, hint, brush);
			}
		}

		private void ValidateAndCorrectRecentSignals()
		{
			PruneOldPostedSignals();
			bool changed = false;

			for (int i = 0; i < postedSignals.Count; i++)
			{
				PostedSignal sig = postedSignals[i];
				if (sig.Voided)
					continue;

				int barsAgo = CurrentBar - sig.Bar;
				if (barsAgo < SignalMinBarsBeforeReview || barsAgo > SignalReviewBars)
					continue;

				if (sig.IsApproach)
				{
					if (!IsApproachStillValid(sig, barsAgo))
					{
						VoidPostedSignal(sig);
						changed = true;
					}
					continue;
				}

				if (tradeState != TradeState.Flat && sig.Bar == openEntryBar && barsInTrade < SignalMinBarsBeforeReview)
					continue;

				bool logicOk = IsEntryLogicStillValid(sig, barsAgo);
				bool priceOk = IsEntryConfirmedByPrice(sig, barsAgo);

				if (!logicOk || !priceOk)
				{
					VoidPostedSignal(sig);
					changed = true;
					continue;
				}

				if (sig.WasReEntry && !sig.QualifiedReEntry)
				{
					CorrectEntryLabel(sig, barsAgo, false);
					changed = true;
				}
			}

			if (changed)
				RequestChartRefresh();
		}

		private bool IsProfitGivebackHit(bool wasLong)
		{
			if (!UseProfitGivebackExit || maxFavorableTicks < MinProfitForGiveback)
				return false;

			return maxFavorableTicks - GetCurrentPnlTicks(wasLong) >= ProfitGivebackTicks;
		}

		private bool IsLongKcBreak(double kcUpper)
		{
			if (Close[0] >= kcUpper)
				return false;

			return UseIntrabarExits || longKcBreakBars >= KcBreakConfirmBars;
		}

		private bool IsShortKcBreak(double kcLower)
		{
			if (Close[0] <= kcLower)
				return false;

			return UseIntrabarExits || shortKcBreakBars >= KcBreakConfirmBars;
		}

		private void OpenTrade(bool isLong, EntryTrigger trigger, int barsAgo = 0, double entryPrice = 0)
		{
			openTradeId = nextTradeId++;
			openEntryBar = CurrentBar - barsAgo;
			openEntryPrice = entryPrice > 0 ? entryPrice : Close[barsAgo];
			openEntryTrigger = trigger;
			openEntryTime = Time[barsAgo];
			barsInTrade = 0;
			longKcBreakBars = 0;
			shortKcBreakBars = 0;
			trailStopPrice = isLong
				? openEntryPrice - (AtrTrailMultiplier * kcAtrInd[barsAgo])
				: openEntryPrice + (AtrTrailMultiplier * kcAtrInd[barsAgo]);
			maxFavorableTicks = 0;
			postPeakMaxTicks = 0;
			lastExitWarningReason = CloseReason.None;
			liveTradeCharacter = TradeCharacter.Normal;
			liveCharacterTag = "EARLY";
			recoveryWatchActive = false;
			RemoveDrawObject("MES500TV36L_Missed");

			int histBarsAgo = Math.Min(barsAgo, CurrentBar);
			double hist0 = macdInd.Diff[histBarsAgo];
			double hist1 = CurrentBar >= histBarsAgo + 1 ? macdInd.Diff[histBarsAgo + 1] : hist0;
			double hist2 = CurrentBar >= histBarsAgo + 2 ? macdInd.Diff[histBarsAgo + 2] : hist1;
			openSignalGrade = GetSignalGrade(isLong, trigger, hist0, hist1, hist2);
			openIsReEntry = isLong ? IsLongReEntryContext() : IsShortReEntryContext();
			tradeState = isLong ? TradeState.Long : TradeState.Short;

			if (isLong)
				DrawLongSignal(openTradeId, trigger, barsAgo);
			else
				DrawShortSignal(openTradeId, trigger, barsAgo);

			RequestChartRefresh();
			PerformChartRefresh();
		}

		private void CloseTrade(int tradeId, bool wasLong, CloseReason reason)
		{
			double pnlTicks = wasLong
				? (Close[0] - openEntryPrice) / TickSize
				: (openEntryPrice - Close[0]) / TickSize;

			statTotalTrades++;
			statTotalTicks += pnlTicks;
			if (pnlTicks >= 0)
			{
				statWins++;
				statGrossProfitTicks += pnlTicks;
				statWinTicksTotal += pnlTicks;
			}
			else
			{
				statLosses++;
				statGrossLossTicks += Math.Abs(pnlTicks);
				statLossTicksTotal += Math.Abs(pnlTicks);
			}

			statPeakNetTicks = Math.Max(statPeakNetTicks, statTotalTicks);
			statMaxDrawdownTicks = Math.Max(statMaxDrawdownTicks, statPeakNetTicks - statTotalTicks);

			int triggerIdx = GetTriggerIndex(openEntryTrigger);
			if (triggerIdx >= 0 && triggerTrades != null)
			{
				triggerTrades[triggerIdx]++;
				triggerNetTicks[triggerIdx] += pnlTicks;
				if (pnlTicks >= 0)
					triggerWins[triggerIdx]++;
			}

			openTradeCharacter = liveTradeCharacter;
			if (liveTradeCharacter == TradeCharacter.Chop || maxFavorableTicks >= ChopQuickPeakTicks)
				StartRecoveryWatch(wasLong, openEntryPrice);

			TradeCharacter characterAtClose = liveTradeCharacter;
			double peakAtClose = maxFavorableTicks;
			double closeHist0 = macdInd.Diff[0];
			double closeHist1 = macdInd.Diff[1];
			double closeHist2 = macdInd.Diff[2];
			double closeKcMid = kcEmaInd[0];
			CloseHoldVerdict holdVerdict = EvaluateCloseHoldVerdict(
				wasLong, reason, pnlTicks, characterAtClose, peakAtClose,
				closeHist0, closeHist1, closeHist2, closeKcMid);

			if (ShowCloseSignals)
				DrawCloseSignal(tradeId, wasLong, reason, openEntryBar, openEntryPrice, pnlTicks, holdVerdict);

			lastCloseReason = reason;
			lastClosePnlTicks = pnlTicks;
			lastCloseTradeCharacter = liveTradeCharacter;
			if (maxFavorableTicks >= ChopQuickPeakTicks && pnlTicks < maxFavorableTicks - (ChopGivebackTicks * 0.5))
				lastCloseTradeCharacter = TradeCharacter.Chop;

			lastClosedWasLong = wasLong;
			hasClosedTrade = true;
			lastCloseBar = CurrentBar;
			ResetTradeTracking();
			RequestChartRefresh();
			PerformChartRefresh();
		}

		private void UpdateLongTrail(double atr)
		{
			if (!UseAtrTrail)
				return;

			double candidate = Close[0] - (AtrTrailMultiplier * atr);
			trailStopPrice = Math.Max(trailStopPrice, candidate);
		}

		private void UpdateShortTrail(double atr)
		{
			if (!UseAtrTrail)
				return;

			double candidate = Close[0] + (AtrTrailMultiplier * atr);
			trailStopPrice = Math.Min(trailStopPrice, candidate);
		}

		private bool CanApplySoftClose(CloseReason reason)
		{
			if (reason == CloseReason.OppositeSignal)
			{
				if (ManualTradeMode && !AllowEarlyFlipClose)
					return barsInTrade >= MinBarsBeforeSoftClose;
				return barsInTrade >= MinBarsInTrade || AllowEarlyFlipClose;
			}

			int minBars = ManualTradeMode
				? Math.Max(MinBarsInTrade, MinBarsBeforeSoftClose)
				: MinBarsInTrade;
			return barsInTrade >= minBars;
		}

		private bool IsInManualHoldWindow()
		{
			return ManualTradeMode && tradeState != TradeState.Flat && barsInTrade < MinBarsBeforeSoftClose;
		}

		private void DrawManualHoldWindow()
		{
			if (!ShowHoldWindowTag || !IsInManualHoldWindow())
			{
				RemoveDrawObject("MES500TV36L_HoldWin");
				return;
			}

			int remaining = MinBarsBeforeSoftClose - barsInTrade;
			string side = tradeState == TradeState.Long ? "LONG" : "SHORT";
			string text = string.Format("Ochranné okno · DRŽ {0}\n{1}/{2} svíček · ZAVŘÍT až za {3}",
				side, barsInTrade, MinBarsBeforeSoftClose, remaining);
			string hint = "Ignoruj drobné korekce — CLOSE přijde až po okně";

			Draw.TextFixed(this, "MES500TV36L_HoldWin", text + "\n" + hint, TextPosition.BottomLeft,
				Brushes.LimeGreen, statsFont, Brushes.Black, Brushes.DarkGreen, 85);
		}

		private bool ShouldShowExitWarning()
		{
			if (!ShowExitWarning)
				return false;
			if (SuppressExitWarningInHold && IsInManualHoldWindow())
				return false;
			return true;
		}

		private bool TryEvaluateLongClose(bool inNoTradeZone, double kcUpper, double hist0, double hist1, double hist2, double hist3, bool shortSignal, out CloseReason reason)
		{
			if (shortSignal)
			{
				reason = CloseReason.OppositeSignal;
				return true;
			}

			if (IsProfitGivebackHit(true))
			{
				reason = CloseReason.ProfitGiveback;
				return CanApplySoftClose(reason);
			}

			if (UseAtrTrail && Close[0] < trailStopPrice)
			{
				reason = CloseReason.AtrTrail;
				return CanApplySoftClose(reason);
			}

			if (inNoTradeZone)
			{
				reason = CloseReason.NoTradeZone;
				return CanApplySoftClose(reason);
			}

			if (UseExhaustionForClose && IsLongExhaustion(hist0, hist1, hist2, hist3))
			{
				reason = CloseReason.Exhaustion;
				return CanApplySoftClose(reason);
			}

			if (IsLongKcBreak(kcUpper))
			{
				reason = CloseReason.KcBreak;
				return CanApplySoftClose(reason);
			}

			if (RequireMomentumFadeForClose ? hist0 <= 0 && hist0 < hist1 : hist0 <= 0)
			{
				reason = CloseReason.MomentumFade;
				return CanApplySoftClose(reason);
			}

			reason = CloseReason.None;
			return false;
		}

		private bool TryEvaluateShortClose(bool inNoTradeZone, double kcLower, double hist0, double hist1, double hist2, double hist3, bool longSignal, out CloseReason reason)
		{
			if (longSignal)
			{
				reason = CloseReason.OppositeSignal;
				return true;
			}

			if (IsProfitGivebackHit(false))
			{
				reason = CloseReason.ProfitGiveback;
				return CanApplySoftClose(reason);
			}

			if (UseAtrTrail && Close[0] > trailStopPrice)
			{
				reason = CloseReason.AtrTrail;
				return CanApplySoftClose(reason);
			}

			if (inNoTradeZone)
			{
				reason = CloseReason.NoTradeZone;
				return CanApplySoftClose(reason);
			}

			if (UseExhaustionForClose && IsShortExhaustion(hist0, hist1, hist2, hist3))
			{
				reason = CloseReason.Exhaustion;
				return CanApplySoftClose(reason);
			}

			if (IsShortKcBreak(kcLower))
			{
				reason = CloseReason.KcBreak;
				return CanApplySoftClose(reason);
			}

			if (RequireMomentumFadeForClose ? hist0 >= 0 && hist0 > hist1 : hist0 >= 0)
			{
				reason = CloseReason.MomentumFade;
				return CanApplySoftClose(reason);
			}

			reason = CloseReason.None;
			return false;
		}

		private bool TryGetLongExitWarning(bool inNoTradeZone, double kcUpper, double hist0, double hist1, double hist2, double hist3, out CloseReason reason)
		{
			if (IsProfitGivebackHit(true))
			{
				reason = CloseReason.ProfitGiveback;
				return true;
			}

			if (UseAtrTrail && Close[0] < trailStopPrice + (2 * TickSize))
			{
				reason = CloseReason.AtrTrail;
				return true;
			}

			if (inNoTradeZone)
			{
				reason = CloseReason.NoTradeZone;
				return true;
			}

			if (UseExhaustionForClose && IsLongExhaustion(hist0, hist1, hist2, hist3))
			{
				reason = CloseReason.Exhaustion;
				return true;
			}

			if (Close[0] < kcUpper && !IsLongKcBreak(kcUpper))
			{
				reason = CloseReason.KcBreak;
				return true;
			}

			if (hist0 > 0 && hist0 < hist1)
			{
				reason = CloseReason.MomentumFade;
				return true;
			}

			reason = CloseReason.None;
			return false;
		}

		private bool TryGetShortExitWarning(bool inNoTradeZone, double kcLower, double hist0, double hist1, double hist2, double hist3, out CloseReason reason)
		{
			if (IsProfitGivebackHit(false))
			{
				reason = CloseReason.ProfitGiveback;
				return true;
			}

			if (UseAtrTrail && Close[0] > trailStopPrice - (2 * TickSize))
			{
				reason = CloseReason.AtrTrail;
				return true;
			}

			if (inNoTradeZone)
			{
				reason = CloseReason.NoTradeZone;
				return true;
			}

			if (UseExhaustionForClose && IsShortExhaustion(hist0, hist1, hist2, hist3))
			{
				reason = CloseReason.Exhaustion;
				return true;
			}

			if (Close[0] > kcLower && !IsShortKcBreak(kcLower))
			{
				reason = CloseReason.KcBreak;
				return true;
			}

			if (hist0 < 0 && hist0 > hist1)
			{
				reason = CloseReason.MomentumFade;
				return true;
			}

			reason = CloseReason.None;
			return false;
		}

		private void DrawExitWarning(int tradeId, bool wasLong, CloseReason reason)
		{
			double currentPnl = GetCurrentPnlTicks(wasLong);
			string pnlText = (currentPnl >= 0 ? "+" : string.Empty) + currentPnl.ToString("F0") + "t";
			string peakText = maxFavorableTicks.ToString("F0") + "t";
			string tag = "MES500TV36L_" + tradeId + "_ExitWarn";
			string label = "BRZY ZAVŘÍT? #" + tradeId + " " + pnlText + " peak " + peakText;
			if (ShowCloseReason)
				label += "\n" + GetCloseReasonText(reason, wasLong);

			CloseHoldVerdict previewVerdict = CloseHoldVerdict.Exit;
			string previewAdvice = string.Empty;
			string previewHint = string.Empty;
			if (ShowCloseHoldAdvice)
			{
				previewVerdict = EvaluateCloseHoldVerdict(
					wasLong, reason, currentPnl, liveTradeCharacter, maxFavorableTicks,
					macdInd.Diff[0], macdInd.Diff[1], macdInd.Diff[2], kcEmaInd[0]);
				previewAdvice = GetCloseHoldAdviceText(previewVerdict, reason, liveTradeCharacter);
				previewHint = GetCloseHoldAdviceHint(previewVerdict, reason, liveTradeCharacter, wasLong);
			}

			double markerY;
			double labelY;
			if (wasLong)
			{
				markerY = High[0] + ((CloseOffsetTicks + 2) * TickSize);
				labelY = markerY + (LabelOffsetTicks * TickSize);
				Draw.TriangleDown(this, tag + "_Mark", false, 0, markerY, ExitWarningBrush);
			}
			else
			{
				markerY = Low[0] - ((CloseOffsetTicks + 2) * TickSize);
				labelY = markerY - (LabelOffsetTicks * TickSize);
				Draw.TriangleUp(this, tag + "_Mark", false, 0, markerY, ExitWarningBrush);
			}

			Draw.Text(this, tag + "_Text", false, label, 0, labelY, 0, ExitWarningBrush,
				warningFont, TextAlignment.Center, Brushes.Black, Brushes.DarkGoldenrod, 100);

			if (ShowCloseReason)
				DrawSubHint(tag + "_ReasonHint", 0, labelY, wasLong, GetCloseReasonHint(reason, wasLong), ExitWarningBrush);

			if (ShowCloseHoldAdvice)
			{
				double adviceY = labelY + (LabelOffsetTicks * TickSize * 0.85);
				Draw.Text(this, tag + "_HoldAdvice", false, previewAdvice, 0, adviceY, 0,
					GetCloseHoldAdviceBrush(previewVerdict), warningFont, TextAlignment.Center, Brushes.Black, Brushes.DimGray, 100);
				DrawSubHint(tag + "_HoldHint", 0, adviceY, wasLong, previewHint, GetCloseHoldAdviceBrush(previewVerdict));
			}

			if (EnableExitWarningAlerts && reason != lastExitWarningReason)
			{
				lastExitWarningReason = reason;
				Alert(tag, Priority.High, "MES500T varování ZAVŘÍT #" + tradeId + " " + pnlText + " " + GetCloseReasonHint(reason, wasLong),
					"Alert4.wav", 10, Brushes.Transparent, Brushes.Black);
				if (EnableTaskbarFlash && FlashOnClose)
					FlashTaskbar();
				if (PopupOnClose)
					TryShowScreenPopup("ExitWarn_" + tradeId + "_" + reason, "BRZY ZAVŘÍT? #" + tradeId, GetCloseReasonHint(reason, wasLong), Colors.Gold);
			}

			RegisterHistoricalMarker(CurrentBar, tag);
		}

		private SqueezeState GetSqueezeState(double bbUpper, double bbLower, double kcUpper, double kcLower)
		{
			bool fullSqueeze = bbUpper <= kcUpper && bbLower >= kcLower;
			if (fullSqueeze)
				return SqueezeState.Full;

			bool bbUpperInsideKc = bbUpper <= kcUpper && bbUpper >= kcLower;
			bool bbLowerInsideKc = bbLower >= kcLower && bbLower <= kcUpper;
			if (bbUpperInsideKc || bbLowerInsideKc)
				return SqueezeState.Partial;

			return SqueezeState.Off;
		}

		private SqueezeReleaseDirection GetSqueezeReleaseDirection(double bbUpper, double bbLower, double kcUpper, double kcLower)
		{
			bool bullRelease = bbUpper > kcUpper;
			bool bearRelease = bbLower < kcLower;

			if (bullRelease && bearRelease)
				return SqueezeReleaseDirection.Both;
			if (bullRelease)
				return SqueezeReleaseDirection.Bullish;
			if (bearRelease)
				return SqueezeReleaseDirection.Bearish;

			return SqueezeReleaseDirection.None;
		}

		private string GetCloseReasonText(CloseReason reason, bool wasLong)
		{
			switch (reason)
			{
				case CloseReason.OppositeSignal:
					return wasLong ? "Přišel PRODEJ proti NÁKUPU" : "Přišel NÁKUP proti PRODEJI";
				case CloseReason.NoTradeZone: return "Trh se zase zúžil";
				case CloseReason.Exhaustion: return "Trend slábne";
				case CloseReason.KcBreak:
					return wasLong ? "Cena klesla pod spodní hranici" : "Cena stoupla nad horní hranici";
				case CloseReason.MomentumFade: return "Síla pohybu ustává";
				case CloseReason.AtrTrail: return "Cena se moc vrátila od zisku";
				case CloseReason.ProfitGiveback: return "Ztráta velké části zisku";
				default: return string.Empty;
			}
		}

		private string GetCloseReasonHint(CloseReason reason, bool wasLong)
		{
			switch (reason)
			{
				case CloseReason.OppositeSignal:
					return wasLong
						? "Máš NÁKUP a přišel PRODEJ — zavři nákup"
						: "Máš PRODEJ a přišel NÁKUP — zavři prodej";
				case CloseReason.NoTradeZone:
					return "Trh je zase úzký a neklidný — bezpečnější je zavřít pozici";
				case CloseReason.Exhaustion:
					return wasLong
						? "Růst ztrácí sílu — zavři NÁKUP"
						: "Pokles ztrácí sílu — zavři PRODEJ";
				case CloseReason.KcBreak:
					return wasLong
						? "Cena spadla pod dolní pásmo — trend proti tobě, zavři NÁKUP"
						: "Cena vystoupila nad horní pásmo — trend proti tobě, zavři PRODEJ";
				case CloseReason.MomentumFade:
					return wasLong
						? "Pohyb nahoru už neposiluje — zavři NÁKUP"
						: "Pohyb dolů už neposiluje — zavři PRODEJ";
				case CloseReason.AtrTrail:
					return "Cena se příliš vrátila od nejlepšího zisku — zavři pozici";
				case CloseReason.ProfitGiveback:
					return "Měl jsi dobrý zisk a trh ho sebral — zavři pozici";
				default: return string.Empty;
			}
		}

		private Brush GetCloseReasonBrush(CloseReason reason)
		{
			switch (reason)
			{
				case CloseReason.OppositeSignal: return Brushes.MediumPurple;
				case CloseReason.NoTradeZone: return Brushes.Goldenrod;
				case CloseReason.Exhaustion: return Brushes.DeepSkyBlue;
				case CloseReason.KcBreak: return Brushes.Orange;
				case CloseReason.MomentumFade: return Brushes.DarkOrange;
				case CloseReason.AtrTrail: return Brushes.MediumSeaGreen;
				case CloseReason.ProfitGiveback: return Brushes.Crimson;
				default: return CloseSignalBrush;
			}
		}

		private bool IsMacdTangle()
		{
			double separation = Math.Abs(macdInd[0] - macdInd.Avg[0]);
			double macdSlope = macdInd[0] - macdInd[1];
			double signalSlope = macdInd.Avg[0] - macdInd.Avg[1];
			double sepThreshold = TangleSeparationTicks * TickSize;
			double slopeThreshold = TangleSlopeTicks * TickSize;

			return separation <= sepThreshold
				&& Math.Abs(macdSlope) <= slopeThreshold
				&& Math.Abs(signalSlope) <= slopeThreshold;
		}

		private bool IsLongExhaustion(double hist0, double hist1, double hist2, double hist3)
		{
			if (hist0 <= 0)
				return false;

			if (hist1 > hist0 && hist1 >= hist2 && hist1 >= hist3)
				return true;

			if (hist0 > hist1 && hist1 > hist2)
			{
				double priorStep = hist1 - hist2;
				double currentStep = hist0 - hist1;
				return currentStep < priorStep * 0.5;
			}

			return false;
		}

		private bool IsShortExhaustion(double hist0, double hist1, double hist2, double hist3)
		{
			if (hist0 >= 0)
				return false;

			if (hist1 < hist0 && hist1 <= hist2 && hist1 <= hist3)
				return true;

			if (hist0 < hist1 && hist1 < hist2)
			{
				double priorStep = hist1 - hist2;
				double currentStep = hist0 - hist1;
				return currentStep > priorStep * 0.5;
			}

			return false;
		}

		private void DrawReleaseMarker(SqueezeReleaseDirection direction)
		{
			if (direction == SqueezeReleaseDirection.None)
				return;

			string tag = "MES500TV36L_Release_" + CurrentBar;

			if (direction == SqueezeReleaseDirection.Bullish || direction == SqueezeReleaseDirection.Both)
			{
				double y = Low[0] - (ArrowOffsetTicks * TickSize);
				Draw.ArrowUp(this, tag + "_Bull", false, 0, y, ReleaseBullBrush);
				Draw.Text(this, tag + "_BullTxt", false, "Trh se rozběhl ↑", 0, y - (LabelOffsetTicks * TickSize * 0.5),
					0, ReleaseBullBrush, new SimpleFont("Arial", 9), TextAlignment.Center,
					Brushes.Transparent, Brushes.Transparent, 0);
				DrawSubHint(tag + "_BullHint", 0, y - (LabelOffsetTicks * TickSize * 0.5), true,
					"Klidný úzký trh skončil — sleduj signál NÁKUP", ReleaseBullBrush);
			}

			if (direction == SqueezeReleaseDirection.Bearish || direction == SqueezeReleaseDirection.Both)
			{
				double y = High[0] + (ArrowOffsetTicks * TickSize);
				Draw.ArrowDown(this, tag + "_Bear", false, 0, y, ReleaseBearBrush);
				Draw.Text(this, tag + "_BearTxt", false, "Trh se rozběhl ↓", 0, y + (LabelOffsetTicks * TickSize * 0.5),
					0, ReleaseBearBrush, new SimpleFont("Arial", 9), TextAlignment.Center,
					Brushes.Transparent, Brushes.Transparent, 0);
				DrawSubHint(tag + "_BearHint", 0, y + (LabelOffsetTicks * TickSize * 0.5), false,
					"Klidný úzký trh skončil — sleduj signál PRODEJ", ReleaseBearBrush);
			}

			RegisterHistoricalMarker(CurrentBar, tag);
		}

		private void DrawLongSignal(int tradeId, EntryTrigger trigger, int barsAgo = 0)
		{
			string tag = "MES500TV36L_" + tradeId + "_Long";
			double arrowY = Low[barsAgo] - (ArrowOffsetTicks * TickSize);
			double labelY = arrowY - (LabelOffsetTicks * TickSize);
			bool chopRisk = ShowTradeCharacter && trigger == EntryTrigger.Continuation && openSignalGrade == SignalGrade.C;
			string label = BuildEntryMainLabel(true, IsLongReEntryEntry(), tradeId);
			string subLabel = BuildEntrySubLabel(trigger, openSignalGrade, chopRisk);
			string hint = BuildEntryHint(true, trigger, openSignalGrade);

			if (ShowSignals)
			{
				Values[5][barsAgo] = arrowY;
				PlotBrushes[5][barsAgo] = LongSignalBrush;
				Draw.ArrowUp(this, tag + "_Arrow", false, barsAgo, arrowY, LongSignalBrush);
			}

			if (ShowSignalLabels)
			{
				string fullLabel = subLabel.Length > 0 ? label + "\n" + subLabel : label;
				Draw.Text(this, tag + "_Text", false, fullLabel, barsAgo, labelY, 0, LongSignalBrush,
					signalFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				DrawSubHint(tag + "_Hint", barsAgo, labelY, true, hint, LongSignalBrush);
			}

			if (EnableAlerts)
			{
				string alertSound = GetEntryAlertSound(openSignalGrade, trigger);
				Alert(tag, Priority.Medium, "MES500T NÁKUP #" + tradeId + " " + GetSignalGradeText(openSignalGrade), alertSound, 10, Brushes.Transparent, Brushes.Black);
				if (FlashOnEntry)
					FlashTaskbar();
			}

			if (PopupOnEntry && barsAgo == 0)
				TryShowScreenPopup("EntryLong_" + tradeId, label, hint, Colors.LimeGreen);

			RegisterHistoricalMarker(CurrentBar - barsAgo, tag, true, false, false);

			RegisterPostedEntry(tradeId, true, barsAgo, trigger, openSignalGrade, IsLongReEntryEntry(), tag,
				barsAgo <= CurrentBar ? Close[barsAgo] : Close[0]);
		}

		private void DrawShortSignal(int tradeId, EntryTrigger trigger, int barsAgo = 0)
		{
			string tag = "MES500TV36L_" + tradeId + "_Short";
			double arrowY = High[barsAgo] + (ArrowOffsetTicks * TickSize);
			double labelY = arrowY + (LabelOffsetTicks * TickSize);
			bool chopRisk = ShowTradeCharacter && trigger == EntryTrigger.Continuation && openSignalGrade == SignalGrade.C;
			string label = BuildEntryMainLabel(false, IsShortReEntryEntry(), tradeId);
			string subLabel = BuildEntrySubLabel(trigger, openSignalGrade, chopRisk);
			string hint = BuildEntryHint(false, trigger, openSignalGrade);

			if (ShowSignals)
			{
				Values[6][barsAgo] = arrowY;
				PlotBrushes[6][barsAgo] = ShortSignalBrush;
				Draw.ArrowDown(this, tag + "_Arrow", false, barsAgo, arrowY, ShortSignalBrush);
			}

			if (ShowSignalLabels)
			{
				string fullLabel = subLabel.Length > 0 ? label + "\n" + subLabel : label;
				Draw.Text(this, tag + "_Text", false, fullLabel, barsAgo, labelY, 0, ShortSignalBrush,
					signalFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				DrawSubHint(tag + "_Hint", barsAgo, labelY, false, hint, ShortSignalBrush);
			}

			if (EnableAlerts)
			{
				string alertSound = GetEntryAlertSound(openSignalGrade, trigger);
				Alert(tag, Priority.Medium, "MES500T PRODEJ #" + tradeId + " " + GetSignalGradeText(openSignalGrade), alertSound, 10, Brushes.Transparent, Brushes.Black);
				if (FlashOnEntry)
					FlashTaskbar();
			}

			if (PopupOnEntry && barsAgo == 0)
				TryShowScreenPopup("EntryShort_" + tradeId, label, hint, Colors.OrangeRed);

			RegisterHistoricalMarker(CurrentBar - barsAgo, tag, false, true, false);

			RegisterPostedEntry(tradeId, false, barsAgo, trigger, openSignalGrade, IsShortReEntryEntry(), tag,
				barsAgo <= CurrentBar ? Close[barsAgo] : Close[0]);
		}

		private CloseHoldVerdict EvaluateCloseHoldVerdict(
			bool wasLong,
			CloseReason reason,
			double pnlTicks,
			TradeCharacter tradeCharacter,
			double peakTicks,
			double hist0,
			double hist1,
			double hist2,
			double kcMid)
		{
			if (reason == CloseReason.OppositeSignal)
				return CloseHoldVerdict.Exit;

			if (tradeCharacter == TradeCharacter.Chop)
				return CloseHoldVerdict.Exit;

			if (reason == CloseReason.ProfitGiveback && peakTicks >= ChopQuickPeakTicks)
				return CloseHoldVerdict.Exit;

			double histStep = MinHistStepTicks * TickSize;
			bool momentumOk = wasLong
				? hist0 > 0 && hist0 >= hist1 - histStep
				: hist0 < 0 && hist0 <= hist1 + histStep;
			bool kcOk = wasLong
				? Close[0] >= kcMid && kcEmaInd[0] >= kcEmaInd[1]
				: Close[0] <= kcMid && kcEmaInd[0] <= kcEmaInd[1];
			bool notTangle = !IsMacdTangle();
			bool wasRunner = peakTicks >= RunnerTargetTicks || tradeCharacter == TradeCharacter.Runner;

			if (wasRunner && momentumOk && kcOk && notTangle)
				return CloseHoldVerdict.Hold;

			if (reason == CloseReason.Exhaustion || reason == CloseReason.MomentumFade)
			{
				if (momentumOk && kcOk && notTangle)
					return CloseHoldVerdict.HoldMaybe;
				return CloseHoldVerdict.Exit;
			}

			if (reason == CloseReason.AtrTrail || reason == CloseReason.KcBreak)
			{
				if (momentumOk && kcOk && notTangle)
					return CloseHoldVerdict.HoldMaybe;
				return CloseHoldVerdict.Exit;
			}

			if (reason == CloseReason.NoTradeZone)
			{
				if (momentumOk && kcOk)
					return CloseHoldVerdict.HoldMaybe;
				return CloseHoldVerdict.Exit;
			}

			if (momentumOk && kcOk && notTangle && pnlTicks >= 0)
				return CloseHoldVerdict.HoldMaybe;

			return CloseHoldVerdict.Exit;
		}

		private string GetCloseHoldAdviceText(CloseHoldVerdict verdict, CloseReason reason, TradeCharacter tradeCharacter)
		{
			switch (verdict)
			{
				case CloseHoldVerdict.Hold:
					return "DRŽ dál";
				case CloseHoldVerdict.HoldMaybe:
					return "DRŽ dál?";
				default:
					return "NEDRŽ";
			}
		}

		private string GetCloseHoldAdviceHint(CloseHoldVerdict verdict, CloseReason reason, TradeCharacter tradeCharacter, bool wasLong)
		{
			switch (verdict)
			{
				case CloseHoldVerdict.Hold:
					return "Silný trend běží dál — signál ZAVŘÍT byl asi předčasný";
				case CloseHoldVerdict.HoldMaybe:
					return "Jen korekce — můžeš pozici ještě chvíli držet";
				default:
					if (tradeCharacter == TradeCharacter.Chop)
						return "Trh těčká sem a tam — nečekej na zázrak, zavři";
					return GetCloseReasonHint(reason, wasLong);
			}
		}

		private Brush GetCloseHoldAdviceBrush(CloseHoldVerdict verdict)
		{
			switch (verdict)
			{
				case CloseHoldVerdict.Hold:
					return Brushes.LimeGreen;
				case CloseHoldVerdict.HoldMaybe:
					return Brushes.Gold;
				default:
					return Brushes.OrangeRed;
			}
		}

		private void DrawCloseSignal(int tradeId, bool wasLong, CloseReason reason, int entryBar, double entryPrice, double pnlTicks, CloseHoldVerdict holdVerdict)
		{
			string tag = "MES500TV36L_" + tradeId + "_Close";
			Brush markerBrush = GetCloseReasonBrush(reason);
			string pnlText = (pnlTicks >= 0 ? "+" : string.Empty) + pnlTicks.ToString("F0") + "t";

			double markerY;
			double labelY;
			if (wasLong)
			{
				markerY = High[0] + (CloseOffsetTicks * TickSize);
				labelY = markerY + (LabelOffsetTicks * TickSize);
			}
			else
			{
				markerY = Low[0] - (CloseOffsetTicks * TickSize);
				labelY = markerY - (LabelOffsetTicks * TickSize);
			}

			Values[7][0] = markerY;
			PlotBrushes[7][0] = markerBrush;

			Draw.Diamond(this, tag + "_Diamond", false, 0, markerY, markerBrush);

			if (ShowTradeLines && entryBar > 0 && entryPrice > 0)
			{
				int barsAgoEntry = CurrentBar - entryBar;
				Draw.Line(this, tag + "_Line", false, barsAgoEntry, entryPrice, 0, Close[0],
					markerBrush, DashStyleHelper.Dash, 1);
			}

			if (ShowCloseLabels)
			{
				string label = "ZAVŘÍT #" + tradeId + " " + pnlText;
				if (ShowCloseReason)
					label += "\n" + GetCloseReasonText(reason, wasLong);

				Draw.Text(this, tag + "_Text", false, label, 0, labelY, 0, markerBrush,
					closeFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);

				if (ShowCloseReason)
					DrawSubHint(tag + "_ReasonHint", 0, labelY, wasLong, GetCloseReasonHint(reason, wasLong), markerBrush);

				if (ShowCloseHoldAdvice)
				{
					string adviceMain = GetCloseHoldAdviceText(holdVerdict, reason, openTradeCharacter);
					string adviceHint = GetCloseHoldAdviceHint(holdVerdict, reason, openTradeCharacter, wasLong);
					double adviceY = labelY + (LabelOffsetTicks * TickSize * 0.9);
					Draw.Text(this, tag + "_HoldAdvice", false, adviceMain, 0, adviceY, 0,
						GetCloseHoldAdviceBrush(holdVerdict), warningFont, TextAlignment.Center, Brushes.Black, Brushes.DimGray, 100);
					DrawSubHint(tag + "_HoldHint", 0, adviceY, false, adviceHint, GetCloseHoldAdviceBrush(holdVerdict));
				}
			}

			if (EnableCloseAlerts)
			{
				string alertText = "MES500T ZAVŘÍT #" + tradeId + " " + pnlText;
				if (ShowCloseReason)
					alertText += " — " + GetCloseReasonText(reason, wasLong);
				if (ShowCloseHoldAdvice)
					alertText += " | " + GetCloseHoldAdviceHint(holdVerdict, reason, openTradeCharacter, wasLong);

				Alert(tag, Priority.Medium, alertText, "Alert3.wav", 10, Brushes.Transparent, Brushes.Black);
				if (FlashOnClose)
					FlashTaskbar();
			}

			if (PopupOnClose)
				TryShowScreenPopup("Close_" + tradeId, "ZAVŘÍT #" + tradeId + " " + pnlText, GetCloseReasonHint(reason, wasLong), Colors.Orange);

			RegisterHistoricalMarker(CurrentBar, tag, false, false, true);
		}

		private string FormatTriggerStatLine(int index)
		{
			if (triggerTrades == null || index < 0 || index >= triggerTrades.Length || triggerTrades[index] == 0)
				return string.Empty;

			double wr = 100.0 * triggerWins[index] / triggerTrades[index];
			return string.Format("{0}: {1} výher / {2} obch.  úspěšnost {3:F0}%  celkem {4}{5:F0} ticků\n",
				GetTriggerStatName(index),
				triggerWins[index],
				triggerTrades[index],
				wr,
				triggerNetTicks[index] >= 0 ? "+" : string.Empty,
				triggerNetTicks[index]);
		}

		private void DrawStatsPanel()
		{
			double winRate = statTotalTrades > 0 ? (100.0 * statWins / statTotalTrades) : 0;
			double profitFactor = statGrossLossTicks > 0 ? statGrossProfitTicks / statGrossLossTicks : (statGrossProfitTicks > 0 ? 99.9 : 0);
			double avgWin = statWins > 0 ? statWinTicksTotal / statWins : 0;
			double avgLoss = statLosses > 0 ? statLossTicksTotal / statLosses : 0;
			double expectancy = statTotalTrades > 0 ? statTotalTicks / statTotalTrades : 0;
			string openLine = string.Empty;

			if (tradeState == TradeState.Long)
			{
				double pnl = GetCurrentPnlTicks(true);
				openLine = string.Format("\nOtevřen NÁKUP #{0}: {1}{2:F0} ticků  nejvyšší zisk {3:F0}  stav: {4}",
					openTradeId, pnl >= 0 ? "+" : string.Empty, pnl, maxFavorableTicks,
					GetTradeCharacterMainLabel(liveCharacterTag));
			}
			else if (tradeState == TradeState.Short)
			{
				double pnl = GetCurrentPnlTicks(false);
				openLine = string.Format("\nOtevřen PRODEJ #{0}: {1}{2:F0} ticků  nejvyšší zisk {3:F0}  stav: {4}",
					openTradeId, pnl >= 0 ? "+" : string.Empty, pnl, maxFavorableTicks,
					GetTradeCharacterMainLabel(liveCharacterTag));
			}

			if (IsInManualHoldWindow())
				openLine += string.Format("\nOchranné okno: {0}/{1} svíček (ZAVŘÍT až poté)", barsInTrade, MinBarsBeforeSoftClose);

			string triggerLines = FormatTriggerStatLine(0)
				+ FormatTriggerStatLine(1)
				+ FormatTriggerStatLine(2)
				+ FormatTriggerStatLine(3)
				+ FormatTriggerStatLine(4);
			if (triggerLines.Length > 0)
				triggerLines = "\n--- podle typu signálu ---\n" + triggerLines.TrimEnd('\n');

			string statsText = string.Format(
				"MES500T Monitor\nObchody: {0}  Výhry/Prohry: {1}/{2}  Úspěšnost: {3:F0}%\nCelkem: {4}{5:F0} ticků  Faktor Z: {6:F1}  Průměr/obchod: {7:F1} ticků\nMax pokles: {8:F0} ticků  Prům. výhra: +{9:F0}  Prům. prohra: -{10:F0}{11}{12}",
				statTotalTrades,
				statWins,
				statLosses,
				winRate,
				statTotalTicks >= 0 ? "+" : string.Empty,
				statTotalTicks,
				profitFactor,
				expectancy,
				statMaxDrawdownTicks,
				avgWin,
				avgLoss,
				openLine,
				triggerLines);

			Draw.TextFixed(this, "MES500TV36L_Stats", statsText, TextPosition.TopRight,
				Brushes.White, statsFont, Brushes.Black, Brushes.DarkSlateGray, 70);
		}

		#region Properties

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "BB Period", Order = 1, GroupName = "1. Bollinger Bands")]
		public int BbPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10.0)]
		[Display(Name = "BB StdDev", Order = 2, GroupName = "1. Bollinger Bands")]
		public double BbStdDev { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "KC Period", Order = 1, GroupName = "2. Keltner Channels")]
		public int KcPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 10.0)]
		[Display(Name = "KC Multiplier", Order = 2, GroupName = "2. Keltner Channels")]
		public double KcMultiplier { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Fast EMA", Order = 1, GroupName = "3. MACD")]
		public int MacdFast { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Slow EMA", Order = 2, GroupName = "3. MACD")]
		public int MacdSlow { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "MACD Signal EMA", Order = 3, GroupName = "3. MACD")]
		public int MacdSignal { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Tangle Separation Ticks", Order = 1, GroupName = "4. Early Entry")]
		public int TangleSeparationTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Tangle Slope Ticks", Order = 2, GroupName = "4. Early Entry")]
		public int TangleSlopeTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require 3-Bar Momentum", Description = "MACD histogram schody (3 svíčky). V3 default OFF.", Order = 3, GroupName = "4. Early Entry")]
		public bool RequireThreeBarMomentum { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Directional Release", Description = "Long jen při bull squeeze release, short jen při bear release.", Order = 4, GroupName = "4. Early Entry")]
		public bool RequireDirectionalRelease { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Entry Buffer Ticks", Description = "Close musí být o X ticků za KC pro breakout vstup.", Order = 5, GroupName = "4. Early Entry")]
		public int EntryBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow KC Mid Entry", Description = "Vstup při průrazu KC středu + histogram ignition (dřív než outer KC).", Order = 6, GroupName = "4. Early Entry")]
		public bool AllowKcMidEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Momentum Ignition", Description = "Vstup při přechodu histogramu přes nulu / první impulz.", Order = 7, GroupName = "4. Early Entry")]
		public bool AllowMomentumIgnition { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow MACD Cross Entry", Description = "Vstup při MACD křížení signal line.", Order = 8, GroupName = "4. Early Entry")]
		public bool AllowMacdCrossEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Bar Break Entry", Description = "Vstup když High/Low prorazí KC dřív než close baru.", Order = 9, GroupName = "4. Early Entry")]
		public bool AllowBarBreakEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Trend Continuation Entry", Description = "Vstup po mini pullbacku v trendu (CNT – tvé zakroužkované shorty).", Order = 10, GroupName = "4. Early Entry")]
		public bool AllowTrendContinuationEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Allow Partial Squeeze Entry", Description = "Vstup i v partial squeeze při správném směru release.", Order = 11, GroupName = "4. Early Entry")]
		public bool AllowPartialSqueezeEntry { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Block Full Squeeze Only", Description = "Blokovat jen plný squeeze, ne partial (žluté pozadí).", Order = 12, GroupName = "4. Early Entry")]
		public bool BlockFullSqueezeOnly { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Intrabar Entries", Description = "Vstup během svíčky při průrazu úrovně.", Order = 13, GroupName = "4. Early Entry")]
		public bool UseIntrabarEntries { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Bounce For Continuation", Description = "CNT až po mini pullbacku (bounce svíčka před vstupem).", Order = 14, GroupName = "4. Early Entry")]
		public bool RequireBounceForContinuation { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require KC Mid Trend", Description = "CNT jen ve směru sklonu KC mid.", Order = 15, GroupName = "4. Early Entry")]
		public bool RequireKcMidTrend { get; set; }

		[NinjaScriptProperty]
		[Range(0, 10)]
		[Display(Name = "Min Hist Step Ticks", Description = "Minimální krok histogramu pro early vstup.", Order = 16, GroupName = "4. Early Entry")]
		public int MinHistStepTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 30)]
		[Display(Name = "Entry Cooldown Bars", Description = "Pauza po CLOSE před dalším vstupem.", Order = 17, GroupName = "4. Early Entry")]
		public int EntryCooldownBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "KC Break Confirm Bars", Description = "Po kolika svíčkách pod/nad KC se uzavře pozice.", Order = 1, GroupName = "5. Exit Filters")]
		public int KcBreakConfirmBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Min Bars In Trade", Description = "Minimální držení pozice (FLIP exit vždy povolen).", Order = 2, GroupName = "5. Exit Filters")]
		public int MinBarsInTrade { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Momentum Fade For Close", Order = 3, GroupName = "5. Exit Filters")]
		public bool RequireMomentumFadeForClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Exhaustion For Close", Order = 4, GroupName = "5. Exit Filters")]
		public bool UseExhaustionForClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use ATR Trail", Description = "Chandelier-style trailing stop podle ATR.", Order = 5, GroupName = "5. Exit Filters")]
		public bool UseAtrTrail { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 5.0)]
		[Display(Name = "ATR Trail Multiplier", Order = 6, GroupName = "5. Exit Filters")]
		public double AtrTrailMultiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Intrabar Exits", Description = "CLOSE signál během svíčky (OnPriceChange), ne až na close baru.", Order = 1, GroupName = "8. Realtime")]
		public bool UseIntrabarExits { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Exit Warning", Description = "Žluté EXIT? varování dříve než finální CLOSE.", Order = 2, GroupName = "8. Realtime")]
		public bool ShowExitWarning { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Exit Warning Alerts", Description = "Zvukové upozornění na EXIT? (Priority High).", Order = 3, GroupName = "8. Realtime")]
		public bool EnableExitWarningAlerts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Profit Giveback Exit", Description = "Zavřít když cena vrátí X ticků od nejvyššího zisku v obchodu.", Order = 4, GroupName = "8. Realtime")]
		public bool UseProfitGivebackExit { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Min Profit For Giveback", Description = "Giveback sledovat až od tohoto zisku v tickech (MES 16t ≈ $20).", Order = 5, GroupName = "8. Realtime")]
		public int MinProfitForGiveback { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Profit Giveback Ticks", Description = "Kolik ticků z maxima spustí GB exit (MES 48t ≈ $60).", Order = 6, GroupName = "8. Realtime")]
		public int ProfitGivebackTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Alerts", Order = 1, GroupName = "6. Display")]
		public bool EnableAlerts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Signals", Order = 2, GroupName = "6. Display")]
		public bool ShowSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Signal Labels", Order = 3, GroupName = "6. Display")]
		public bool ShowSignalLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Close Signals", Order = 4, GroupName = "6. Display")]
		public bool ShowCloseSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Close Labels", Order = 5, GroupName = "6. Display")]
		public bool ShowCloseLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Close Reason", Order = 6, GroupName = "6. Display")]
		public bool ShowCloseReason { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Trade Lines", Order = 7, GroupName = "6. Display")]
		public bool ShowTradeLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Release Marker", Description = "Šipka při prvním výstupu ze squeeze.", Order = 8, GroupName = "6. Display")]
		public bool ShowReleaseMarker { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Stats Panel", Description = "HUD s win rate a net P/L v tickech.", Order = 9, GroupName = "6. Display")]
		public bool ShowStatsPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show KC Mid", Order = 10, GroupName = "6. Display")]
		public bool ShowKcMid { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Close Alerts", Order = 11, GroupName = "6. Display")]
		public bool EnableCloseAlerts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Entry Trigger", Description = "Zkratka typu vstupu (IGN, MID, CNT, BRK, X).", Order = 12, GroupName = "6. Display")]
		public bool ShowEntryTrigger { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Squeeze Background", Order = 13, GroupName = "6. Display")]
		public bool ShowSqueezeBackground { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Arrow Offset Ticks", Order = 1, GroupName = "7. Visual")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 80)]
		[Display(Name = "Label Offset Ticks", Order = 2, GroupName = "7. Visual")]
		public int LabelOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Close Offset Ticks", Order = 3, GroupName = "7. Visual")]
		public int CloseOffsetTicks { get; set; }

		[XmlIgnore]
		[Display(Name = "BB Color", Order = 4, GroupName = "7. Visual")]
		public Brush BbBrush { get; set; }

		[Browsable(false)]
		public string BbBrushSerializable
		{
			get { return Serialize.BrushToString(BbBrush); }
			set { BbBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "KC Color", Order = 5, GroupName = "7. Visual")]
		public Brush KcBrush { get; set; }

		[Browsable(false)]
		public string KcBrushSerializable
		{
			get { return Serialize.BrushToString(KcBrush); }
			set { KcBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "KC Mid Color", Order = 6, GroupName = "7. Visual")]
		public Brush KcMidBrush { get; set; }

		[Browsable(false)]
		public string KcMidBrushSerializable
		{
			get { return Serialize.BrushToString(KcMidBrush); }
			set { KcMidBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Squeeze Full Background", Order = 7, GroupName = "7. Visual")]
		public Brush SqueezeFullBrush { get; set; }

		[Browsable(false)]
		public string SqueezeFullBrushSerializable
		{
			get { return Serialize.BrushToString(SqueezeFullBrush); }
			set { SqueezeFullBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Squeeze Partial Background", Order = 8, GroupName = "7. Visual")]
		public Brush SqueezePartialBrush { get; set; }

		[Browsable(false)]
		public string SqueezePartialBrushSerializable
		{
			get { return Serialize.BrushToString(SqueezePartialBrush); }
			set { SqueezePartialBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Long Signal Color", Order = 9, GroupName = "7. Visual")]
		public Brush LongSignalBrush { get; set; }

		[Browsable(false)]
		public string LongSignalBrushSerializable
		{
			get { return Serialize.BrushToString(LongSignalBrush); }
			set { LongSignalBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Short Signal Color", Order = 10, GroupName = "7. Visual")]
		public Brush ShortSignalBrush { get; set; }

		[Browsable(false)]
		public string ShortSignalBrushSerializable
		{
			get { return Serialize.BrushToString(ShortSignalBrush); }
			set { ShortSignalBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Close Signal Color", Order = 11, GroupName = "7. Visual")]
		public Brush CloseSignalBrush { get; set; }

		[Browsable(false)]
		public string CloseSignalBrushSerializable
		{
			get { return Serialize.BrushToString(CloseSignalBrush); }
			set { CloseSignalBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Release Bull Color", Order = 12, GroupName = "7. Visual")]
		public Brush ReleaseBullBrush { get; set; }

		[Browsable(false)]
		public string ReleaseBullBrushSerializable
		{
			get { return Serialize.BrushToString(ReleaseBullBrush); }
			set { ReleaseBullBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Release Bear Color", Order = 13, GroupName = "7. Visual")]
		public Brush ReleaseBearBrush { get; set; }

		[Browsable(false)]
		public string ReleaseBearBrushSerializable
		{
			get { return Serialize.BrushToString(ReleaseBearBrush); }
			set { ReleaseBearBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Exit Warning Color", Order = 14, GroupName = "7. Visual")]
		public Brush ExitWarningBrush { get; set; }

		[Browsable(false)]
		public string ExitWarningBrushSerializable
		{
			get { return Serialize.BrushToString(ExitWarningBrush); }
			set { ExitWarningBrush = Serialize.StringToBrush(value); }
		}

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MES500TSqueezeMomentumV36Light[] cacheMES500TSqueezeMomentumV36Light;
		public MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			return MES500TSqueezeMomentumV36Light(Input, manualTradeMode, minBarsBeforeSoftClose, allowEarlyFlipClose, showHoldWindowTag, suppressExitWarningInHold, showApproachRing, approachRingMinScore, approachRingSizeTicks, approachRingMinOpacity, approachRingMaxOpacity, enableScreenPopup, popupDurationSec, popupOnApproach, popupOnEntry, popupOnClose, maxMarkerHistoryBars, showCloseHoldAdvice, enableSignalAutoCorrect, signalReviewBars, signalMinBarsBeforeReview, falseSignalAdverseTicks, signalConfirmTicks, blockWeakReEntrySignals, reEntryMinBarsSinceClose, reEntryRequireGradeBOrBetter, blockReEntryAfterChopClose, blockReEntryAfterLossClose, reEntryApproachMinScore, cntMinKcMidSlopeBars, showTrailLine, showSignalGrade, showTradeCharacter, chopQuickPeakTicks, chopGivebackTicks, runnerTargetTicks, recoveryWindowSec, enableTaskbarFlash, flashOnEntry, flashOnClose, flashOnApproach, showApproachHints, showApproachLabels, approachMinScore, approachNearTicks, approachOffsetTicks, reEntryLookbackBars, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, tangleSeparationTicks, tangleSlopeTicks, requireThreeBarMomentum, requireDirectionalRelease, entryBufferTicks, allowKcMidEntry, allowMomentumIgnition, allowMacdCrossEntry, allowBarBreakEntry, allowTrendContinuationEntry, allowPartialSqueezeEntry, blockFullSqueezeOnly, useIntrabarEntries, requireBounceForContinuation, requireKcMidTrend, minHistStepTicks, entryCooldownBars, kcBreakConfirmBars, minBarsInTrade, requireMomentumFadeForClose, useExhaustionForClose, useAtrTrail, atrTrailMultiplier, useIntrabarExits, showExitWarning, enableExitWarningAlerts, useProfitGivebackExit, minProfitForGiveback, profitGivebackTicks, enableAlerts, showSignals, showSignalLabels, showCloseSignals, showCloseLabels, showCloseReason, showTradeLines, showReleaseMarker, showStatsPanel, showKcMid, enableCloseAlerts, showEntryTrigger, showSqueezeBackground, arrowOffsetTicks, labelOffsetTicks, closeOffsetTicks);
		}

		public MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(ISeries<double> input, bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			if (cacheMES500TSqueezeMomentumV36Light != null)
				for (int idx = 0; idx < cacheMES500TSqueezeMomentumV36Light.Length; idx++)
					if (cacheMES500TSqueezeMomentumV36Light[idx] != null && cacheMES500TSqueezeMomentumV36Light[idx].ManualTradeMode == manualTradeMode && cacheMES500TSqueezeMomentumV36Light[idx].MinBarsBeforeSoftClose == minBarsBeforeSoftClose && cacheMES500TSqueezeMomentumV36Light[idx].AllowEarlyFlipClose == allowEarlyFlipClose && cacheMES500TSqueezeMomentumV36Light[idx].ShowHoldWindowTag == showHoldWindowTag && cacheMES500TSqueezeMomentumV36Light[idx].SuppressExitWarningInHold == suppressExitWarningInHold && cacheMES500TSqueezeMomentumV36Light[idx].ShowApproachRing == showApproachRing && cacheMES500TSqueezeMomentumV36Light[idx].ApproachRingMinScore == approachRingMinScore && cacheMES500TSqueezeMomentumV36Light[idx].ApproachRingSizeTicks == approachRingSizeTicks && cacheMES500TSqueezeMomentumV36Light[idx].ApproachRingMinOpacity == approachRingMinOpacity && cacheMES500TSqueezeMomentumV36Light[idx].ApproachRingMaxOpacity == approachRingMaxOpacity && cacheMES500TSqueezeMomentumV36Light[idx].EnableScreenPopup == enableScreenPopup && cacheMES500TSqueezeMomentumV36Light[idx].PopupDurationSec == popupDurationSec && cacheMES500TSqueezeMomentumV36Light[idx].PopupOnApproach == popupOnApproach && cacheMES500TSqueezeMomentumV36Light[idx].PopupOnEntry == popupOnEntry && cacheMES500TSqueezeMomentumV36Light[idx].PopupOnClose == popupOnClose && cacheMES500TSqueezeMomentumV36Light[idx].MaxMarkerHistoryBars == maxMarkerHistoryBars && cacheMES500TSqueezeMomentumV36Light[idx].ShowCloseHoldAdvice == showCloseHoldAdvice && cacheMES500TSqueezeMomentumV36Light[idx].EnableSignalAutoCorrect == enableSignalAutoCorrect && cacheMES500TSqueezeMomentumV36Light[idx].SignalReviewBars == signalReviewBars && cacheMES500TSqueezeMomentumV36Light[idx].SignalMinBarsBeforeReview == signalMinBarsBeforeReview && cacheMES500TSqueezeMomentumV36Light[idx].FalseSignalAdverseTicks == falseSignalAdverseTicks && cacheMES500TSqueezeMomentumV36Light[idx].SignalConfirmTicks == signalConfirmTicks && cacheMES500TSqueezeMomentumV36Light[idx].BlockWeakReEntrySignals == blockWeakReEntrySignals && cacheMES500TSqueezeMomentumV36Light[idx].ReEntryMinBarsSinceClose == reEntryMinBarsSinceClose && cacheMES500TSqueezeMomentumV36Light[idx].ReEntryRequireGradeBOrBetter == reEntryRequireGradeBOrBetter && cacheMES500TSqueezeMomentumV36Light[idx].BlockReEntryAfterChopClose == blockReEntryAfterChopClose && cacheMES500TSqueezeMomentumV36Light[idx].BlockReEntryAfterLossClose == blockReEntryAfterLossClose && cacheMES500TSqueezeMomentumV36Light[idx].ReEntryApproachMinScore == reEntryApproachMinScore && cacheMES500TSqueezeMomentumV36Light[idx].CntMinKcMidSlopeBars == cntMinKcMidSlopeBars && cacheMES500TSqueezeMomentumV36Light[idx].ShowTrailLine == showTrailLine && cacheMES500TSqueezeMomentumV36Light[idx].ShowSignalGrade == showSignalGrade && cacheMES500TSqueezeMomentumV36Light[idx].ShowTradeCharacter == showTradeCharacter && cacheMES500TSqueezeMomentumV36Light[idx].ChopQuickPeakTicks == chopQuickPeakTicks && cacheMES500TSqueezeMomentumV36Light[idx].ChopGivebackTicks == chopGivebackTicks && cacheMES500TSqueezeMomentumV36Light[idx].RunnerTargetTicks == runnerTargetTicks && cacheMES500TSqueezeMomentumV36Light[idx].RecoveryWindowSec == recoveryWindowSec && cacheMES500TSqueezeMomentumV36Light[idx].EnableTaskbarFlash == enableTaskbarFlash && cacheMES500TSqueezeMomentumV36Light[idx].FlashOnEntry == flashOnEntry && cacheMES500TSqueezeMomentumV36Light[idx].FlashOnClose == flashOnClose && cacheMES500TSqueezeMomentumV36Light[idx].FlashOnApproach == flashOnApproach && cacheMES500TSqueezeMomentumV36Light[idx].ShowApproachHints == showApproachHints && cacheMES500TSqueezeMomentumV36Light[idx].ShowApproachLabels == showApproachLabels && cacheMES500TSqueezeMomentumV36Light[idx].ApproachMinScore == approachMinScore && cacheMES500TSqueezeMomentumV36Light[idx].ApproachNearTicks == approachNearTicks && cacheMES500TSqueezeMomentumV36Light[idx].ApproachOffsetTicks == approachOffsetTicks && cacheMES500TSqueezeMomentumV36Light[idx].ReEntryLookbackBars == reEntryLookbackBars && cacheMES500TSqueezeMomentumV36Light[idx].BbPeriod == bbPeriod && cacheMES500TSqueezeMomentumV36Light[idx].BbStdDev == bbStdDev && cacheMES500TSqueezeMomentumV36Light[idx].KcPeriod == kcPeriod && cacheMES500TSqueezeMomentumV36Light[idx].KcMultiplier == kcMultiplier && cacheMES500TSqueezeMomentumV36Light[idx].MacdFast == macdFast && cacheMES500TSqueezeMomentumV36Light[idx].MacdSlow == macdSlow && cacheMES500TSqueezeMomentumV36Light[idx].MacdSignal == macdSignal && cacheMES500TSqueezeMomentumV36Light[idx].TangleSeparationTicks == tangleSeparationTicks && cacheMES500TSqueezeMomentumV36Light[idx].TangleSlopeTicks == tangleSlopeTicks && cacheMES500TSqueezeMomentumV36Light[idx].RequireThreeBarMomentum == requireThreeBarMomentum && cacheMES500TSqueezeMomentumV36Light[idx].RequireDirectionalRelease == requireDirectionalRelease && cacheMES500TSqueezeMomentumV36Light[idx].EntryBufferTicks == entryBufferTicks && cacheMES500TSqueezeMomentumV36Light[idx].AllowKcMidEntry == allowKcMidEntry && cacheMES500TSqueezeMomentumV36Light[idx].AllowMomentumIgnition == allowMomentumIgnition && cacheMES500TSqueezeMomentumV36Light[idx].AllowMacdCrossEntry == allowMacdCrossEntry && cacheMES500TSqueezeMomentumV36Light[idx].AllowBarBreakEntry == allowBarBreakEntry && cacheMES500TSqueezeMomentumV36Light[idx].AllowTrendContinuationEntry == allowTrendContinuationEntry && cacheMES500TSqueezeMomentumV36Light[idx].AllowPartialSqueezeEntry == allowPartialSqueezeEntry && cacheMES500TSqueezeMomentumV36Light[idx].BlockFullSqueezeOnly == blockFullSqueezeOnly && cacheMES500TSqueezeMomentumV36Light[idx].UseIntrabarEntries == useIntrabarEntries && cacheMES500TSqueezeMomentumV36Light[idx].RequireBounceForContinuation == requireBounceForContinuation && cacheMES500TSqueezeMomentumV36Light[idx].RequireKcMidTrend == requireKcMidTrend && cacheMES500TSqueezeMomentumV36Light[idx].MinHistStepTicks == minHistStepTicks && cacheMES500TSqueezeMomentumV36Light[idx].EntryCooldownBars == entryCooldownBars && cacheMES500TSqueezeMomentumV36Light[idx].KcBreakConfirmBars == kcBreakConfirmBars && cacheMES500TSqueezeMomentumV36Light[idx].MinBarsInTrade == minBarsInTrade && cacheMES500TSqueezeMomentumV36Light[idx].RequireMomentumFadeForClose == requireMomentumFadeForClose && cacheMES500TSqueezeMomentumV36Light[idx].UseExhaustionForClose == useExhaustionForClose && cacheMES500TSqueezeMomentumV36Light[idx].UseAtrTrail == useAtrTrail && cacheMES500TSqueezeMomentumV36Light[idx].AtrTrailMultiplier == atrTrailMultiplier && cacheMES500TSqueezeMomentumV36Light[idx].UseIntrabarExits == useIntrabarExits && cacheMES500TSqueezeMomentumV36Light[idx].ShowExitWarning == showExitWarning && cacheMES500TSqueezeMomentumV36Light[idx].EnableExitWarningAlerts == enableExitWarningAlerts && cacheMES500TSqueezeMomentumV36Light[idx].UseProfitGivebackExit == useProfitGivebackExit && cacheMES500TSqueezeMomentumV36Light[idx].MinProfitForGiveback == minProfitForGiveback && cacheMES500TSqueezeMomentumV36Light[idx].ProfitGivebackTicks == profitGivebackTicks && cacheMES500TSqueezeMomentumV36Light[idx].EnableAlerts == enableAlerts && cacheMES500TSqueezeMomentumV36Light[idx].ShowSignals == showSignals && cacheMES500TSqueezeMomentumV36Light[idx].ShowSignalLabels == showSignalLabels && cacheMES500TSqueezeMomentumV36Light[idx].ShowCloseSignals == showCloseSignals && cacheMES500TSqueezeMomentumV36Light[idx].ShowCloseLabels == showCloseLabels && cacheMES500TSqueezeMomentumV36Light[idx].ShowCloseReason == showCloseReason && cacheMES500TSqueezeMomentumV36Light[idx].ShowTradeLines == showTradeLines && cacheMES500TSqueezeMomentumV36Light[idx].ShowReleaseMarker == showReleaseMarker && cacheMES500TSqueezeMomentumV36Light[idx].ShowStatsPanel == showStatsPanel && cacheMES500TSqueezeMomentumV36Light[idx].ShowKcMid == showKcMid && cacheMES500TSqueezeMomentumV36Light[idx].EnableCloseAlerts == enableCloseAlerts && cacheMES500TSqueezeMomentumV36Light[idx].ShowEntryTrigger == showEntryTrigger && cacheMES500TSqueezeMomentumV36Light[idx].ShowSqueezeBackground == showSqueezeBackground && cacheMES500TSqueezeMomentumV36Light[idx].ArrowOffsetTicks == arrowOffsetTicks && cacheMES500TSqueezeMomentumV36Light[idx].LabelOffsetTicks == labelOffsetTicks && cacheMES500TSqueezeMomentumV36Light[idx].CloseOffsetTicks == closeOffsetTicks && cacheMES500TSqueezeMomentumV36Light[idx].EqualsInput(input))
						return cacheMES500TSqueezeMomentumV36Light[idx];
			return CacheIndicator<MES500TSqueezeMomentumV36Light>(new MES500TSqueezeMomentumV36Light(){ ManualTradeMode = manualTradeMode, MinBarsBeforeSoftClose = minBarsBeforeSoftClose, AllowEarlyFlipClose = allowEarlyFlipClose, ShowHoldWindowTag = showHoldWindowTag, SuppressExitWarningInHold = suppressExitWarningInHold, ShowApproachRing = showApproachRing, ApproachRingMinScore = approachRingMinScore, ApproachRingSizeTicks = approachRingSizeTicks, ApproachRingMinOpacity = approachRingMinOpacity, ApproachRingMaxOpacity = approachRingMaxOpacity, EnableScreenPopup = enableScreenPopup, PopupDurationSec = popupDurationSec, PopupOnApproach = popupOnApproach, PopupOnEntry = popupOnEntry, PopupOnClose = popupOnClose, MaxMarkerHistoryBars = maxMarkerHistoryBars, ShowCloseHoldAdvice = showCloseHoldAdvice, EnableSignalAutoCorrect = enableSignalAutoCorrect, SignalReviewBars = signalReviewBars, SignalMinBarsBeforeReview = signalMinBarsBeforeReview, FalseSignalAdverseTicks = falseSignalAdverseTicks, SignalConfirmTicks = signalConfirmTicks, BlockWeakReEntrySignals = blockWeakReEntrySignals, ReEntryMinBarsSinceClose = reEntryMinBarsSinceClose, ReEntryRequireGradeBOrBetter = reEntryRequireGradeBOrBetter, BlockReEntryAfterChopClose = blockReEntryAfterChopClose, BlockReEntryAfterLossClose = blockReEntryAfterLossClose, ReEntryApproachMinScore = reEntryApproachMinScore, CntMinKcMidSlopeBars = cntMinKcMidSlopeBars, ShowTrailLine = showTrailLine, ShowSignalGrade = showSignalGrade, ShowTradeCharacter = showTradeCharacter, ChopQuickPeakTicks = chopQuickPeakTicks, ChopGivebackTicks = chopGivebackTicks, RunnerTargetTicks = runnerTargetTicks, RecoveryWindowSec = recoveryWindowSec, EnableTaskbarFlash = enableTaskbarFlash, FlashOnEntry = flashOnEntry, FlashOnClose = flashOnClose, FlashOnApproach = flashOnApproach, ShowApproachHints = showApproachHints, ShowApproachLabels = showApproachLabels, ApproachMinScore = approachMinScore, ApproachNearTicks = approachNearTicks, ApproachOffsetTicks = approachOffsetTicks, ReEntryLookbackBars = reEntryLookbackBars, BbPeriod = bbPeriod, BbStdDev = bbStdDev, KcPeriod = kcPeriod, KcMultiplier = kcMultiplier, MacdFast = macdFast, MacdSlow = macdSlow, MacdSignal = macdSignal, TangleSeparationTicks = tangleSeparationTicks, TangleSlopeTicks = tangleSlopeTicks, RequireThreeBarMomentum = requireThreeBarMomentum, RequireDirectionalRelease = requireDirectionalRelease, EntryBufferTicks = entryBufferTicks, AllowKcMidEntry = allowKcMidEntry, AllowMomentumIgnition = allowMomentumIgnition, AllowMacdCrossEntry = allowMacdCrossEntry, AllowBarBreakEntry = allowBarBreakEntry, AllowTrendContinuationEntry = allowTrendContinuationEntry, AllowPartialSqueezeEntry = allowPartialSqueezeEntry, BlockFullSqueezeOnly = blockFullSqueezeOnly, UseIntrabarEntries = useIntrabarEntries, RequireBounceForContinuation = requireBounceForContinuation, RequireKcMidTrend = requireKcMidTrend, MinHistStepTicks = minHistStepTicks, EntryCooldownBars = entryCooldownBars, KcBreakConfirmBars = kcBreakConfirmBars, MinBarsInTrade = minBarsInTrade, RequireMomentumFadeForClose = requireMomentumFadeForClose, UseExhaustionForClose = useExhaustionForClose, UseAtrTrail = useAtrTrail, AtrTrailMultiplier = atrTrailMultiplier, UseIntrabarExits = useIntrabarExits, ShowExitWarning = showExitWarning, EnableExitWarningAlerts = enableExitWarningAlerts, UseProfitGivebackExit = useProfitGivebackExit, MinProfitForGiveback = minProfitForGiveback, ProfitGivebackTicks = profitGivebackTicks, EnableAlerts = enableAlerts, ShowSignals = showSignals, ShowSignalLabels = showSignalLabels, ShowCloseSignals = showCloseSignals, ShowCloseLabels = showCloseLabels, ShowCloseReason = showCloseReason, ShowTradeLines = showTradeLines, ShowReleaseMarker = showReleaseMarker, ShowStatsPanel = showStatsPanel, ShowKcMid = showKcMid, EnableCloseAlerts = enableCloseAlerts, ShowEntryTrigger = showEntryTrigger, ShowSqueezeBackground = showSqueezeBackground, ArrowOffsetTicks = arrowOffsetTicks, LabelOffsetTicks = labelOffsetTicks, CloseOffsetTicks = closeOffsetTicks }, input, ref cacheMES500TSqueezeMomentumV36Light);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			return indicator.MES500TSqueezeMomentumV36Light(Input, manualTradeMode, minBarsBeforeSoftClose, allowEarlyFlipClose, showHoldWindowTag, suppressExitWarningInHold, showApproachRing, approachRingMinScore, approachRingSizeTicks, approachRingMinOpacity, approachRingMaxOpacity, enableScreenPopup, popupDurationSec, popupOnApproach, popupOnEntry, popupOnClose, maxMarkerHistoryBars, showCloseHoldAdvice, enableSignalAutoCorrect, signalReviewBars, signalMinBarsBeforeReview, falseSignalAdverseTicks, signalConfirmTicks, blockWeakReEntrySignals, reEntryMinBarsSinceClose, reEntryRequireGradeBOrBetter, blockReEntryAfterChopClose, blockReEntryAfterLossClose, reEntryApproachMinScore, cntMinKcMidSlopeBars, showTrailLine, showSignalGrade, showTradeCharacter, chopQuickPeakTicks, chopGivebackTicks, runnerTargetTicks, recoveryWindowSec, enableTaskbarFlash, flashOnEntry, flashOnClose, flashOnApproach, showApproachHints, showApproachLabels, approachMinScore, approachNearTicks, approachOffsetTicks, reEntryLookbackBars, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, tangleSeparationTicks, tangleSlopeTicks, requireThreeBarMomentum, requireDirectionalRelease, entryBufferTicks, allowKcMidEntry, allowMomentumIgnition, allowMacdCrossEntry, allowBarBreakEntry, allowTrendContinuationEntry, allowPartialSqueezeEntry, blockFullSqueezeOnly, useIntrabarEntries, requireBounceForContinuation, requireKcMidTrend, minHistStepTicks, entryCooldownBars, kcBreakConfirmBars, minBarsInTrade, requireMomentumFadeForClose, useExhaustionForClose, useAtrTrail, atrTrailMultiplier, useIntrabarExits, showExitWarning, enableExitWarningAlerts, useProfitGivebackExit, minProfitForGiveback, profitGivebackTicks, enableAlerts, showSignals, showSignalLabels, showCloseSignals, showCloseLabels, showCloseReason, showTradeLines, showReleaseMarker, showStatsPanel, showKcMid, enableCloseAlerts, showEntryTrigger, showSqueezeBackground, arrowOffsetTicks, labelOffsetTicks, closeOffsetTicks);
		}

		public Indicators.MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(ISeries<double> input , bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			return indicator.MES500TSqueezeMomentumV36Light(input, manualTradeMode, minBarsBeforeSoftClose, allowEarlyFlipClose, showHoldWindowTag, suppressExitWarningInHold, showApproachRing, approachRingMinScore, approachRingSizeTicks, approachRingMinOpacity, approachRingMaxOpacity, enableScreenPopup, popupDurationSec, popupOnApproach, popupOnEntry, popupOnClose, maxMarkerHistoryBars, showCloseHoldAdvice, enableSignalAutoCorrect, signalReviewBars, signalMinBarsBeforeReview, falseSignalAdverseTicks, signalConfirmTicks, blockWeakReEntrySignals, reEntryMinBarsSinceClose, reEntryRequireGradeBOrBetter, blockReEntryAfterChopClose, blockReEntryAfterLossClose, reEntryApproachMinScore, cntMinKcMidSlopeBars, showTrailLine, showSignalGrade, showTradeCharacter, chopQuickPeakTicks, chopGivebackTicks, runnerTargetTicks, recoveryWindowSec, enableTaskbarFlash, flashOnEntry, flashOnClose, flashOnApproach, showApproachHints, showApproachLabels, approachMinScore, approachNearTicks, approachOffsetTicks, reEntryLookbackBars, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, tangleSeparationTicks, tangleSlopeTicks, requireThreeBarMomentum, requireDirectionalRelease, entryBufferTicks, allowKcMidEntry, allowMomentumIgnition, allowMacdCrossEntry, allowBarBreakEntry, allowTrendContinuationEntry, allowPartialSqueezeEntry, blockFullSqueezeOnly, useIntrabarEntries, requireBounceForContinuation, requireKcMidTrend, minHistStepTicks, entryCooldownBars, kcBreakConfirmBars, minBarsInTrade, requireMomentumFadeForClose, useExhaustionForClose, useAtrTrail, atrTrailMultiplier, useIntrabarExits, showExitWarning, enableExitWarningAlerts, useProfitGivebackExit, minProfitForGiveback, profitGivebackTicks, enableAlerts, showSignals, showSignalLabels, showCloseSignals, showCloseLabels, showCloseReason, showTradeLines, showReleaseMarker, showStatsPanel, showKcMid, enableCloseAlerts, showEntryTrigger, showSqueezeBackground, arrowOffsetTicks, labelOffsetTicks, closeOffsetTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			return indicator.MES500TSqueezeMomentumV36Light(Input, manualTradeMode, minBarsBeforeSoftClose, allowEarlyFlipClose, showHoldWindowTag, suppressExitWarningInHold, showApproachRing, approachRingMinScore, approachRingSizeTicks, approachRingMinOpacity, approachRingMaxOpacity, enableScreenPopup, popupDurationSec, popupOnApproach, popupOnEntry, popupOnClose, maxMarkerHistoryBars, showCloseHoldAdvice, enableSignalAutoCorrect, signalReviewBars, signalMinBarsBeforeReview, falseSignalAdverseTicks, signalConfirmTicks, blockWeakReEntrySignals, reEntryMinBarsSinceClose, reEntryRequireGradeBOrBetter, blockReEntryAfterChopClose, blockReEntryAfterLossClose, reEntryApproachMinScore, cntMinKcMidSlopeBars, showTrailLine, showSignalGrade, showTradeCharacter, chopQuickPeakTicks, chopGivebackTicks, runnerTargetTicks, recoveryWindowSec, enableTaskbarFlash, flashOnEntry, flashOnClose, flashOnApproach, showApproachHints, showApproachLabels, approachMinScore, approachNearTicks, approachOffsetTicks, reEntryLookbackBars, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, tangleSeparationTicks, tangleSlopeTicks, requireThreeBarMomentum, requireDirectionalRelease, entryBufferTicks, allowKcMidEntry, allowMomentumIgnition, allowMacdCrossEntry, allowBarBreakEntry, allowTrendContinuationEntry, allowPartialSqueezeEntry, blockFullSqueezeOnly, useIntrabarEntries, requireBounceForContinuation, requireKcMidTrend, minHistStepTicks, entryCooldownBars, kcBreakConfirmBars, minBarsInTrade, requireMomentumFadeForClose, useExhaustionForClose, useAtrTrail, atrTrailMultiplier, useIntrabarExits, showExitWarning, enableExitWarningAlerts, useProfitGivebackExit, minProfitForGiveback, profitGivebackTicks, enableAlerts, showSignals, showSignalLabels, showCloseSignals, showCloseLabels, showCloseReason, showTradeLines, showReleaseMarker, showStatsPanel, showKcMid, enableCloseAlerts, showEntryTrigger, showSqueezeBackground, arrowOffsetTicks, labelOffsetTicks, closeOffsetTicks);
		}

		public Indicators.MES500TSqueezeMomentumV36Light MES500TSqueezeMomentumV36Light(ISeries<double> input , bool manualTradeMode, int minBarsBeforeSoftClose, bool allowEarlyFlipClose, bool showHoldWindowTag, bool suppressExitWarningInHold, bool showApproachRing, int approachRingMinScore, int approachRingSizeTicks, double approachRingMinOpacity, double approachRingMaxOpacity, bool enableScreenPopup, int popupDurationSec, bool popupOnApproach, bool popupOnEntry, bool popupOnClose, int maxMarkerHistoryBars, bool showCloseHoldAdvice, bool enableSignalAutoCorrect, int signalReviewBars, int signalMinBarsBeforeReview, int falseSignalAdverseTicks, int signalConfirmTicks, bool blockWeakReEntrySignals, int reEntryMinBarsSinceClose, bool reEntryRequireGradeBOrBetter, bool blockReEntryAfterChopClose, bool blockReEntryAfterLossClose, int reEntryApproachMinScore, int cntMinKcMidSlopeBars, bool showTrailLine, bool showSignalGrade, bool showTradeCharacter, int chopQuickPeakTicks, int chopGivebackTicks, int runnerTargetTicks, int recoveryWindowSec, bool enableTaskbarFlash, bool flashOnEntry, bool flashOnClose, bool flashOnApproach, bool showApproachHints, bool showApproachLabels, int approachMinScore, int approachNearTicks, int approachOffsetTicks, int reEntryLookbackBars, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int tangleSeparationTicks, int tangleSlopeTicks, bool requireThreeBarMomentum, bool requireDirectionalRelease, int entryBufferTicks, bool allowKcMidEntry, bool allowMomentumIgnition, bool allowMacdCrossEntry, bool allowBarBreakEntry, bool allowTrendContinuationEntry, bool allowPartialSqueezeEntry, bool blockFullSqueezeOnly, bool useIntrabarEntries, bool requireBounceForContinuation, bool requireKcMidTrend, int minHistStepTicks, int entryCooldownBars, int kcBreakConfirmBars, int minBarsInTrade, bool requireMomentumFadeForClose, bool useExhaustionForClose, bool useAtrTrail, double atrTrailMultiplier, bool useIntrabarExits, bool showExitWarning, bool enableExitWarningAlerts, bool useProfitGivebackExit, int minProfitForGiveback, int profitGivebackTicks, bool enableAlerts, bool showSignals, bool showSignalLabels, bool showCloseSignals, bool showCloseLabels, bool showCloseReason, bool showTradeLines, bool showReleaseMarker, bool showStatsPanel, bool showKcMid, bool enableCloseAlerts, bool showEntryTrigger, bool showSqueezeBackground, int arrowOffsetTicks, int labelOffsetTicks, int closeOffsetTicks)
		{
			return indicator.MES500TSqueezeMomentumV36Light(input, manualTradeMode, minBarsBeforeSoftClose, allowEarlyFlipClose, showHoldWindowTag, suppressExitWarningInHold, showApproachRing, approachRingMinScore, approachRingSizeTicks, approachRingMinOpacity, approachRingMaxOpacity, enableScreenPopup, popupDurationSec, popupOnApproach, popupOnEntry, popupOnClose, maxMarkerHistoryBars, showCloseHoldAdvice, enableSignalAutoCorrect, signalReviewBars, signalMinBarsBeforeReview, falseSignalAdverseTicks, signalConfirmTicks, blockWeakReEntrySignals, reEntryMinBarsSinceClose, reEntryRequireGradeBOrBetter, blockReEntryAfterChopClose, blockReEntryAfterLossClose, reEntryApproachMinScore, cntMinKcMidSlopeBars, showTrailLine, showSignalGrade, showTradeCharacter, chopQuickPeakTicks, chopGivebackTicks, runnerTargetTicks, recoveryWindowSec, enableTaskbarFlash, flashOnEntry, flashOnClose, flashOnApproach, showApproachHints, showApproachLabels, approachMinScore, approachNearTicks, approachOffsetTicks, reEntryLookbackBars, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, tangleSeparationTicks, tangleSlopeTicks, requireThreeBarMomentum, requireDirectionalRelease, entryBufferTicks, allowKcMidEntry, allowMomentumIgnition, allowMacdCrossEntry, allowBarBreakEntry, allowTrendContinuationEntry, allowPartialSqueezeEntry, blockFullSqueezeOnly, useIntrabarEntries, requireBounceForContinuation, requireKcMidTrend, minHistStepTicks, entryCooldownBars, kcBreakConfirmBars, minBarsInTrade, requireMomentumFadeForClose, useExhaustionForClose, useAtrTrail, atrTrailMultiplier, useIntrabarExits, showExitWarning, enableExitWarningAlerts, useProfitGivebackExit, minProfitForGiveback, profitGivebackTicks, enableAlerts, showSignals, showSignalLabels, showCloseSignals, showCloseLabels, showCloseReason, showTradeLines, showReleaseMarker, showStatsPanel, showKcMid, enableCloseAlerts, showEntryTrigger, showSqueezeBackground, arrowOffsetTicks, labelOffsetTicks, closeOffsetTicks);
		}
	}
}

#endregion
