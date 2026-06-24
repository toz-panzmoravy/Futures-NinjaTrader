#region Using declarations
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// MES500TSwingMapV4Light — V4 pro slabší PC: statistiky jen za poslední 1h, méně kreslení.
	/// </summary>
	public class MES500TSwingMapV4Light : Indicator
	{
		private enum SwingDir
		{
			None,
			Long,
			Short
		}

		private enum SwingType
		{
			Neutral,
			BuyImpulse,
			SellImpulse,
			Correction,
			TentativeBuy,
			TentativeSell
		}

		private enum PivotKind
		{
			None,
			High,
			Low
		}

		private enum TrendPhase
		{
			SeekingHigh,
			SeekingLow,
			CorrectionAfterBuy,
			CorrectionAfterSell
		}

		private ATR atrInd;

		private Brush brushBuy;
		private Brush brushSell;
		private Brush brushBuyTentative;
		private Brush brushSellTentative;
		private Brush brushCorrection;
		private Brush brushNeutral;
		private Brush brushPeak;
		private Brush brushPeakSoft;
		private SimpleFont labelFont;
		private SimpleFont peakPctFont;
		private SimpleFont actionFont;
		private SimpleFont statsFont;

		private struct CompletedTrade
		{
			public DateTime EntryTime;
			public int TicksPnl;
			public bool IsWin;
		}

		private List<CompletedTrade> completedTrades;

		// Simulovaný obchod VSTUP → ◆ PEAK
		private bool openTradeActive;
		private SwingDir openTradeDir;
		private double openEntryPrice;
		private double openEntryCandleLow;
		private double openEntryCandleHigh;
		private DateTime openEntryTime;
		private string actionHeadline;
		private string actionDetail;
		private Brush actionBrush;
		private bool entrySignalThisBar;
		private SwingDir entrySignalDir;

		// Peak state — dvoustupňový: PEAK? (soft) a ◆ PEAK (confirmed)
		private bool peakSoftSignalThisBar;
		private bool peakConfirmedSignalThisBar;
		private SwingDir peakSignalDir;
		private bool peakSoftDrawnThisLeg;
		private bool peakConfirmedDrawnThisLeg;
		private double peakSoftExtremePrice;
		private int peakSoftExtremeBar;
		private int peakSoftProbability;

		// Last confirmed pivot
		private double lastPivotPrice;
		private int lastPivotBar;
		private PivotKind lastPivotKind;

		// Running extreme since last pivot
		private double runningExtreme;
		private int runningExtremeBar;

		// Current segment
		private int segmentStartBar;
		private SwingType segmentType;
		private TrendPhase phase;

		// Last completed impulse
		private SwingDir lastImpulseDir;
		private double lastImpulseSizeTicks;

		// Correction tracking
		private int correctionStartBar;
		private double correctionStartPrice;

		// ConfirmBars delay
		private int reversalBarCount;
		private bool reversalPending;

		private Series<int> barTypeSeries;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500TSwingMapV4Light — statistiky za 1h, bez swing čar a strength panelu.";
				Name        = "MES500TSwingMapV4Light";
				Calculate   = Calculate.OnBarClose;
				IsOverlay   = false;
				DrawOnPricePanel = true;
				DisplayInDataBox = true;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot = 20;

				MinSwingTicks     = 20;
				CorrectionRatio   = 0.62;
				ConfirmBars       = 1;
				AtrPeriod         = 14;
				AtrMultiplier     = 1.2;
				ShowSwingLines    = false;
				ShowStrengthPanel = false;
				ShowActionPanel   = true;
				ShowEntryMarkers  = true;
				ShowEntryConnector = true;
				ShowPeakMarkers     = true;
				ShowPeakSoftMarkers = true;
				LabelOffsetTicks    = 4;
				PeakMinTicks        = 30;
				PeakPullbackTicks   = 12;
				PeakRequireBreak    = true;
				ShowStatsPanel      = true;
				ContractCount       = 10;
				StatsLookbackHours  = 1;
				MaxStatsTrades      = 80;

				AddPlot(new Stroke(Brushes.DimGray, 2), PlotStyle.Bar, "MoveStrength");
				Plots[0].AutoWidth = true;
			}
			else if (State == State.Configure)
			{
				atrInd = ATR(AtrPeriod);

				brushBuy = new SolidColorBrush(Color.FromRgb(0, 200, 70));
				brushBuy.Freeze();
				brushSell = new SolidColorBrush(Color.FromRgb(220, 50, 40));
				brushSell.Freeze();
				brushBuyTentative = new SolidColorBrush(Color.FromRgb(100, 180, 120));
				brushBuyTentative.Freeze();
				brushSellTentative = new SolidColorBrush(Color.FromRgb(200, 120, 100));
				brushSellTentative.Freeze();
				brushCorrection = new SolidColorBrush(Color.FromRgb(255, 105, 180));
				brushCorrection.Freeze();
				brushNeutral = new SolidColorBrush(Color.FromRgb(120, 120, 120));
				brushNeutral.Freeze();
				brushPeak = new SolidColorBrush(Color.FromRgb(255, 215, 0));
				brushPeak.Freeze();
				brushPeakSoft = new SolidColorBrush(Color.FromRgb(255, 160, 60));
				brushPeakSoft.Freeze();

				labelFont = new SimpleFont("Arial", 9) { Bold = true };
				peakPctFont = new SimpleFont("Arial", 7) { Bold = false };
				actionFont = new SimpleFont("Arial", 11) { Bold = true };
				statsFont = new SimpleFont("Consolas", 9) { Bold = false };
			}
			else if (State == State.DataLoaded)
			{
				barTypeSeries = new Series<int>(this);
				completedTrades = new List<CompletedTrade>();
				ResetState();
			}
		}

		private void ResetState()
		{
			lastPivotPrice      = 0;
			lastPivotBar        = 0;
			lastPivotKind       = PivotKind.None;
			runningExtreme      = 0;
			runningExtremeBar   = 0;
			segmentStartBar     = 0;
			segmentType         = SwingType.Neutral;
			phase               = TrendPhase.SeekingHigh;
			lastImpulseDir      = SwingDir.None;
			lastImpulseSizeTicks = 0;
			correctionStartBar  = 0;
			correctionStartPrice = 0;
			reversalBarCount    = 0;
			reversalPending     = false;
			actionHeadline           = "■ ČEKEJ";
			actionDetail             = "Potvrzený signál až po uzavření svíčky";
			actionBrush              = brushNeutral;
			entrySignalThisBar       = false;
			entrySignalDir           = SwingDir.None;
			peakSoftSignalThisBar       = false;
			peakConfirmedSignalThisBar  = false;
			peakSignalDir               = SwingDir.None;
			peakSoftDrawnThisLeg        = false;
			peakConfirmedDrawnThisLeg   = false;
			peakSoftExtremePrice        = 0;
			peakSoftExtremeBar          = 0;
			peakSoftProbability         = 0;
			openTradeActive             = false;
			openTradeDir                = SwingDir.None;
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToPlot)
			{
				ApplyBarColor(0, SwingType.Neutral);
				barTypeSeries[0] = (int)SwingType.Neutral;
				Values[0][0] = 0;
				return;
			}

			if (CurrentBar == BarsRequiredToPlot)
				InitializeFirstPivot();

			double minMove = GetMinMove();
			entrySignalThisBar      = false;
			entrySignalDir          = SwingDir.None;
			peakSoftSignalThisBar      = false;
			peakConfirmedSignalThisBar = false;
			peakSignalDir              = SwingDir.None;
			UpdateSwingLogic(minMove);
			DetectPeak();
			UpdateStrengthPlot();
			UpdateActionPanel();
			UpdateStatsPanel();
			if (ShowEntryMarkers && entrySignalThisBar)
				DrawEntryMarker(entrySignalDir);
			if (ShowPeakSoftMarkers && peakSoftSignalThisBar)
				DrawPeakSoftMarker(peakSignalDir);
			if (ShowPeakMarkers && peakConfirmedSignalThisBar)
				DrawPeakConfirmedMarker(peakSignalDir);
		}

		private void ResetPeakLegState()
		{
			peakSoftDrawnThisLeg      = false;
			peakConfirmedDrawnThisLeg = false;
			peakSoftExtremePrice      = 0;
			peakSoftExtremeBar        = 0;
			peakSoftProbability       = 0;
		}

		private void InitializeFirstPivot()
		{
			lastPivotPrice = Low[0];
			lastPivotBar   = CurrentBar;
			lastPivotKind  = PivotKind.Low;
			runningExtreme = High[0];
			runningExtremeBar = CurrentBar;
			segmentStartBar = CurrentBar;
			segmentType = SwingType.Neutral;
			phase = TrendPhase.SeekingHigh;
		}

		private double GetMinMove()
		{
			double atrMove = atrInd[0] * AtrMultiplier;
			double tickMove = MinSwingTicks * TickSize;
			return Math.Max(tickMove, atrMove);
		}

		private void UpdateSwingLogic(double minMove)
		{
			SwingType barType = SwingType.Neutral;

			if (lastPivotKind == PivotKind.Low)
			{
				barType = ProcessSeekingHigh(minMove);
			}
			else if (lastPivotKind == PivotKind.High)
			{
				barType = ProcessSeekingLow(minMove);
			}

			ApplyBarColor(0, barType);
			barTypeSeries[0] = (int)barType;
		}

		private SwingType ProcessSeekingHigh(double minMove)
		{
			if (High[0] > runningExtreme)
			{
				if (peakSoftDrawnThisLeg && !peakConfirmedDrawnThisLeg
					&& High[0] > peakSoftExtremePrice + TickSize)
				{
					SetAction("✓ Falešný PEAK? — DRŽ", "Nové high — trend pokračuje", brushBuy);
					peakSoftDrawnThisLeg = false;
				}

				runningExtreme = High[0];
				runningExtremeBar = CurrentBar;
				reversalPending = false;
				reversalBarCount = 0;
			}

			double drop = runningExtreme - Low[0];
			if (drop < minMove)
			{
				reversalPending = false;
				reversalBarCount = 0;
				return GetTentativeUpColor();
			}

			if (!reversalPending)
			{
				reversalPending = true;
				reversalBarCount = 1;
			}
			else
				reversalBarCount++;

			if (ConfirmBars > 0 && reversalBarCount <= ConfirmBars)
				return GetTentativeDownColor();

			double upLegTicks = (runningExtreme - lastPivotPrice) / TickSize;
			SwingType completedType = ClassifyUpLeg(upLegTicks);

			// Žádné zpětné přebarvování — pouze swing čáry
			DrawSwingLine(lastPivotBar, lastPivotPrice, runningExtremeBar, runningExtreme);

			if (completedType == SwingType.BuyImpulse)
			{
				lastImpulseDir = SwingDir.Long;
				lastImpulseSizeTicks = upLegTicks;
				SignalEntry(SwingDir.Long, "↑ BUY potvrzeno — VSTUP na další bar", lastPivotPrice, upLegTicks);
			}
			else if (completedType == SwingType.Correction)
				SetAction("↩ KOREKCE — NEVSTUPOVAT", "Odraz po SELL — čekej obnovení trendu", brushCorrection);

			lastPivotPrice = runningExtreme;
			lastPivotBar   = runningExtremeBar;
			lastPivotKind  = PivotKind.High;
			runningExtreme = Low[0];
			runningExtremeBar = CurrentBar;
			segmentStartBar = CurrentBar;
			phase = TrendPhase.SeekingLow;
			segmentType = SwingType.SellImpulse;
			reversalPending = false;
			reversalBarCount = 0;
			ResetPeakLegState();

			return GetTentativeDownColor();
		}

		private SwingType ProcessSeekingLow(double minMove)
		{
			if (Low[0] < runningExtreme)
			{
				if (peakSoftDrawnThisLeg && !peakConfirmedDrawnThisLeg
					&& Low[0] < peakSoftExtremePrice - TickSize)
				{
					SetAction("✓ Falešný PEAK? — DRŽ", "Nové low — trend pokračuje", brushSell);
					peakSoftDrawnThisLeg = false;
				}

				runningExtreme = Low[0];
				runningExtremeBar = CurrentBar;
				reversalPending = false;
				reversalBarCount = 0;
			}

			double rise = High[0] - runningExtreme;
			if (rise < minMove)
			{
				reversalPending = false;
				reversalBarCount = 0;
				return GetTentativeDownColor();
			}

			if (!reversalPending)
			{
				reversalPending = true;
				reversalBarCount = 1;
			}
			else
				reversalBarCount++;

			if (ConfirmBars > 0 && reversalBarCount <= ConfirmBars)
				return GetTentativeUpColor();

			double downLegTicks = (lastPivotPrice - runningExtreme) / TickSize;
			SwingType completedType = ClassifyDownLeg(downLegTicks);

			// Žádné zpětné přebarvování — pouze swing čáry
			DrawSwingLine(lastPivotBar, lastPivotPrice, runningExtremeBar, runningExtreme);

			if (completedType == SwingType.SellImpulse)
			{
				lastImpulseDir = SwingDir.Short;
				lastImpulseSizeTicks = downLegTicks;
				SignalEntry(SwingDir.Short, "↓ SELL potvrzeno — VSTUP na další bar", lastPivotPrice, downLegTicks);
			}
			else if (completedType == SwingType.Correction)
				SetAction("↩ KOREKCE — NEVSTUPOVAT", "Odraz po BUY — čekej obnovení trendu", brushCorrection);

			lastPivotPrice = runningExtreme;
			lastPivotBar   = runningExtremeBar;
			lastPivotKind  = PivotKind.Low;
			runningExtreme = High[0];
			runningExtremeBar = CurrentBar;
			segmentStartBar = CurrentBar;
			phase = TrendPhase.SeekingHigh;
			segmentType = SwingType.BuyImpulse;
			reversalPending = false;
			reversalBarCount = 0;
			ResetPeakLegState();

			return GetTentativeUpColor();
		}

		private SwingType ClassifyUpLeg(double upLegTicks)
		{
			if (lastImpulseDir == SwingDir.Short && lastImpulseSizeTicks > 0
				&& upLegTicks < CorrectionRatio * lastImpulseSizeTicks)
			{
				phase = TrendPhase.CorrectionAfterSell;
				correctionStartBar = lastPivotBar;
				correctionStartPrice = lastPivotPrice;
				return SwingType.Correction;
			}

			if (upLegTicks >= MinSwingTicks)
				return SwingType.BuyImpulse;

			return SwingType.Neutral;
		}

		private SwingType ClassifyDownLeg(double downLegTicks)
		{
			if (lastImpulseDir == SwingDir.Long && lastImpulseSizeTicks > 0
				&& downLegTicks < CorrectionRatio * lastImpulseSizeTicks)
			{
				phase = TrendPhase.CorrectionAfterBuy;
				correctionStartBar = lastPivotBar;
				correctionStartPrice = lastPivotPrice;
				return SwingType.Correction;
			}

			if (downLegTicks >= MinSwingTicks)
				return SwingType.SellImpulse;

			return SwingType.Neutral;
		}

		private SwingType GetTentativeUpColor()
		{
			// Korekce po SELL — pokud vzestup překročí CorrectionRatio → obnoví se BUY impuls
			if (lastPivotKind == PivotKind.Low && lastImpulseDir == SwingDir.Short && lastImpulseSizeTicks > 0)
			{
				double riseTicks = (runningExtreme - lastPivotPrice) / TickSize;
				if (riseTicks >= CorrectionRatio * lastImpulseSizeTicks)
				{
					// Pouze aktuální bar dostane barvu BuyImpulse — žádné přebarvení historie
					lastImpulseDir = SwingDir.Long;
					lastImpulseSizeTicks = riseTicks;
					phase = TrendPhase.SeekingHigh;
					SignalEntry(SwingDir.Long, "Korekce → BUY — VSTUP", lastPivotPrice, riseTicks);
					return SwingType.BuyImpulse;
				}
				SetAction("↩ KOREKCE — NEVSTUPOVAT", "Odraz v SELL trendu", brushCorrection);
				return SwingType.Correction;
			}

			// Aktivní BUY leg — stabilní zelená od uzavření každé svíčky
			if (phase == TrendPhase.SeekingHigh || segmentType == SwingType.BuyImpulse)
			{
				SetAction("▶ BUY běží", "Zelená = aktivní BUY leg", brushBuy);
				return SwingType.BuyImpulse;
			}

			return SwingType.Neutral;
		}

		private SwingType GetTentativeDownColor()
		{
			// Korekce po BUY — pokud pokles překročí CorrectionRatio → obnoví se SELL impuls
			if (lastPivotKind == PivotKind.High && lastImpulseDir == SwingDir.Long && lastImpulseSizeTicks > 0)
			{
				double dropTicks = (lastPivotPrice - runningExtreme) / TickSize;
				if (dropTicks >= CorrectionRatio * lastImpulseSizeTicks)
				{
					// Pouze aktuální bar dostane barvu SellImpulse — žádné přebarvení historie
					lastImpulseDir = SwingDir.Short;
					lastImpulseSizeTicks = dropTicks;
					phase = TrendPhase.SeekingLow;
					SignalEntry(SwingDir.Short, "Korekce → SELL — VSTUP", lastPivotPrice, dropTicks);
					return SwingType.SellImpulse;
				}
				SetAction("↩ KOREKCE — NEVSTUPOVAT", "Odraz v BUY trendu", brushCorrection);
				return SwingType.Correction;
			}

			// Aktivní SELL leg — stabilní červená od uzavření každé svíčky
			if (phase == TrendPhase.SeekingLow || segmentType == SwingType.SellImpulse)
			{
				SetAction("▶ SELL běží", "Červená = aktivní SELL leg", brushSell);
				return SwingType.SellImpulse;
			}

			return SwingType.Neutral;
		}

		private void RepaintSegment(int startBar, int endBar, SwingType type)
		{
			for (int bar = startBar; bar <= endBar; bar++)
			{
				int barsAgo = CurrentBar - bar;
				if (barsAgo < 0)
					continue;
				ApplyBarColor(barsAgo, type);
				barTypeSeries[barsAgo] = (int)type;
			}
		}

		private void PaintSegment(int startBar, int endBar, SwingType type)
		{
			if (endBar < startBar)
				return;

			for (int bar = startBar; bar <= endBar; bar++)
			{
				int barsAgo = CurrentBar - bar;
				if (barsAgo < 0 || barsAgo > CurrentBar)
					continue;
				ApplyBarColor(barsAgo, type);
				barTypeSeries[barsAgo] = (int)type;
			}
		}

		private void ApplyBarColor(int barsAgo, SwingType type)
		{
			Brush brush = GetBrush(type);
			BarBrushes[barsAgo] = brush;
			CandleOutlineBrushes[barsAgo] = brush;
		}

		private Brush GetBrush(SwingType type)
		{
			switch (type)
			{
				case SwingType.BuyImpulse:      return brushBuy;
				case SwingType.SellImpulse:     return brushSell;
				case SwingType.TentativeBuy:    return brushBuyTentative;
				case SwingType.TentativeSell:   return brushSellTentative;
				case SwingType.Correction:      return brushCorrection;
				default:                        return brushNeutral;
			}
		}

		private void SignalEntry(SwingDir dir, string detail, double structureStop, double impulseTicks)
		{
			entrySignalThisBar = true;
			entrySignalDir = dir;
			ResetPeakLegState();
			OpenTrade(dir);

			if (dir == SwingDir.Long)
				SetAction("🟢 VSTUP BUY", detail, brushBuy);
			else if (dir == SwingDir.Short)
				SetAction("🔴 VSTUP SELL", detail, brushSell);
		}

		private void OpenTrade(SwingDir dir)
		{
			openTradeActive = true;
			openTradeDir = dir;
			openEntryPrice = Close[0];
			openEntryCandleLow = Low[0];
			openEntryCandleHigh = High[0];
			openEntryTime = Time[0];
		}

		private void RecordConfirmedPeak(SwingDir dir, double peakLevel, double exitClose)
		{
			if (!openTradeActive || openTradeDir != dir)
				return;

			int ticksPnl = dir == SwingDir.Long
				? (int)Math.Round((exitClose - openEntryPrice) / TickSize)
				: (int)Math.Round((openEntryPrice - exitClose) / TickSize);

			bool isWin = ticksPnl > 0;

			completedTrades.Add(new CompletedTrade
			{
				EntryTime = openEntryTime,
				TicksPnl = ticksPnl,
				IsWin = isWin
			});

			TrimTradeHistory();
			openTradeActive = false;
			openTradeDir = SwingDir.None;
		}

		private void TrimTradeHistory()
		{
			if (MaxStatsTrades <= 0)
				return;

			while (completedTrades.Count > MaxStatsTrades)
				completedTrades.RemoveAt(0);
		}

		private IEnumerable<CompletedTrade> GetFilteredTrades()
		{
			if (StatsLookbackHours <= 0)
			{
				foreach (CompletedTrade t in completedTrades)
					yield return t;
				yield break;
			}

			DateTime cutoff = Time[0].AddHours(-StatsLookbackHours);
			foreach (CompletedTrade t in completedTrades)
			{
				if (t.EntryTime >= cutoff)
					yield return t;
			}
		}

		private void UpdateStatsPanel()
		{
			if (!ShowStatsPanel)
			{
				Draw.TextFixed(this, "MES500TSMV4Light_Stats", string.Empty,
					TextPosition.TopLeft, Brushes.Transparent, statsFont,
					Brushes.Black, Brushes.Transparent, 0);
				return;
			}

			int trades = 0;
			int wins = 0;
			int losses = 0;
			int totalTicks = 0;

			foreach (CompletedTrade t in GetFilteredTrades())
			{
				trades++;
				totalTicks += t.TicksPnl;
				if (t.IsWin)
					wins++;
				else
					losses++;
			}

			double winRate = trades > 0 ? 100.0 * wins / trades : 0;
			double dollarsPerTick = Instrument.MasterInstrument.PointValue * TickSize;
			double totalUsd = totalTicks * dollarsPerTick * ContractCount;

			string windowLabel = StatsLookbackHours > 0
				? string.Format("posledních {0}h", StatsLookbackHours)
				: "celá historie";

			string openLine = openTradeActive
				? string.Format("\nOtevřeno: {0} od {1:HH:mm}", openTradeDir == SwingDir.Long ? "BUY" : "SELL", openEntryTime)
				: string.Empty;

			string text = string.Format(
				"══ STATISTIKY ({0}) ══\n" +
				"Obchody: {1}   W:{2}  L:{3}\n" +
				"Win rate: {4:F0}%\n" +
				"Ticků celkem: {5:+0;-0;0}\n" +
				"USD ({6} kon): ${7:N0}" +
				"{8}\n" +
				"─ VSTUP → ◆ PEAK only",
				windowLabel, trades, wins, losses, winRate, totalTicks, ContractCount, totalUsd, openLine);

			Brush statsBrush = totalTicks >= 0 ? brushBuy : brushSell;
			if (trades == 0)
				statsBrush = brushNeutral;

			Draw.TextFixed(this, "MES500TSMV4Light_Stats", text,
				TextPosition.TopLeft, statsBrush, statsFont,
				Brushes.Black, new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 0);
		}

		private void DetectPeak()
		{
			if (peakConfirmedDrawnThisLeg)
				return;

			double minPeakMove = PeakMinTicks * TickSize;

			if (phase == TrendPhase.SeekingHigh && lastPivotKind == PivotKind.Low)
				DetectPeakLong(minPeakMove);
			else if (phase == TrendPhase.SeekingLow && lastPivotKind == PivotKind.High)
				DetectPeakShort(minPeakMove);
		}

		private void DetectPeakLong(double minPeakMove)
		{
			double moveSinceBase = runningExtreme - lastPivotPrice;
			if (moveSinceBase < minPeakMove)
				return;

			double pullbackTicks = (runningExtreme - Close[0]) / TickSize;
			bool structureBreak = !PeakRequireBreak || (CurrentBar > 0 && Low[0] < Low[1]);

			// ◆ PEAK — potvrzený exit: skutečný pullback od vrcholu trendu
			if (ShowPeakMarkers && pullbackTicks >= PeakPullbackTicks && structureBreak)
			{
				peakConfirmedSignalThisBar = true;
				peakSignalDir = SwingDir.Long;
				peakConfirmedDrawnThisLeg = true;
				int pct = ComputePeakProbabilityLong(pullbackTicks, moveSinceBase, true);
				RecordConfirmedPeak(SwingDir.Long, runningExtreme, Close[0]);
				SetAction("◆ PEAK BUY — UZAVŘÍT",
					string.Format("{0}% · pullback {1}t od vrcholu", pct, (int)Math.Round(pullbackTicks)), brushPeak);
				return;
			}

			// PEAK? — varování u extrému (může být falešný — drž pokud přijde nové high)
			if (ShowPeakSoftMarkers && !peakSoftDrawnThisLeg)
			{
				bool atExtreme = High[0] >= runningExtreme - 2 * TickSize;
				bool reversalCandle = Close[0] < Open[0]
					|| Close[0] < (High[0] + Low[0]) / 2.0;
				bool bodyNearHigh = Close[0] < High[0] - (High[0] - Low[0]) * 0.35;

				if (atExtreme && (reversalCandle || bodyNearHigh))
				{
					peakSoftProbability = ComputePeakProbabilityLong(pullbackTicks, moveSinceBase, false);
					peakSoftSignalThisBar = true;
					peakSignalDir = SwingDir.Long;
					peakSoftDrawnThisLeg = true;
					peakSoftExtremePrice = runningExtreme;
					peakSoftExtremeBar = runningExtremeBar;
					SetAction(string.Format("⚠ PEAK? {0}% — POZOR", peakSoftProbability),
						PeakProbabilityHint(peakSoftProbability), brushPeakSoft);
				}
			}
		}

		private void DetectPeakShort(double minPeakMove)
		{
			double moveSinceBase = lastPivotPrice - runningExtreme;
			if (moveSinceBase < minPeakMove)
				return;

			double pullbackTicks = (Close[0] - runningExtreme) / TickSize;
			bool structureBreak = !PeakRequireBreak || (CurrentBar > 0 && High[0] > High[1]);

			if (ShowPeakMarkers && pullbackTicks >= PeakPullbackTicks && structureBreak)
			{
				peakConfirmedSignalThisBar = true;
				peakSignalDir = SwingDir.Short;
				peakConfirmedDrawnThisLeg = true;
				int pct = ComputePeakProbabilityShort(pullbackTicks, moveSinceBase, true);
				RecordConfirmedPeak(SwingDir.Short, runningExtreme, Close[0]);
				SetAction("◆ PEAK SELL — UZAVŘÍT",
					string.Format("{0}% · pullback {1}t od dna", pct, (int)Math.Round(pullbackTicks)), brushPeak);
				return;
			}

			if (ShowPeakSoftMarkers && !peakSoftDrawnThisLeg)
			{
				bool atExtreme = Low[0] <= runningExtreme + 2 * TickSize;
				bool reversalCandle = Close[0] > Open[0]
					|| Close[0] > (High[0] + Low[0]) / 2.0;
				bool bodyNearLow = Close[0] > Low[0] + (High[0] - Low[0]) * 0.35;

				if (atExtreme && (reversalCandle || bodyNearLow))
				{
					peakSoftProbability = ComputePeakProbabilityShort(pullbackTicks, moveSinceBase, false);
					peakSoftSignalThisBar = true;
					peakSignalDir = SwingDir.Short;
					peakSoftDrawnThisLeg = true;
					peakSoftExtremePrice = runningExtreme;
					peakSoftExtremeBar = runningExtremeBar;
					SetAction(string.Format("⚠ PEAK? {0}% — POZOR", peakSoftProbability),
						PeakProbabilityHint(peakSoftProbability), brushPeakSoft);
				}
			}
		}

		private string PeakProbabilityHint(int pct)
		{
			if (pct < 40)
				return "Slabé — pravděpodobně falešný, DRŽ pokud trend pokračuje";
			if (pct < 60)
				return "Střední — sleduj další svíčku, nové high/low = drž";
			if (pct < 75)
				return "Vyšší — zvaž zúžení stopu nebo částečný exit";
			return "Vysoké — silné známky vrcholu swingu";
		}

		private int ComputePeakProbabilityLong(double pullbackTicks, double moveSinceBase, bool confirmed)
		{
			double range = High[0] - Low[0];
			if (range < TickSize)
				range = TickSize;

			double score = confirmed ? 55 : 0;

			// Obratová svíčka u vrcholu (0–25)
			double bearishBody = Close[0] < Open[0] ? (Open[0] - Close[0]) / range : 0;
			double closeFromHigh = (High[0] - Close[0]) / range;
			double upperWick = (High[0] - Math.Max(Open[0], Close[0])) / range;
			score += 25 * Math.Min(1.0, Math.Max(bearishBody, closeFromHigh * 0.9) + upperWick * 0.25);

			// Pullback od running extreme (0–30)
			score += 30 * Math.Min(1.0, pullbackTicks / Math.Max(1, PeakPullbackTicks));

			// Strukturální průraz (0–20)
			if (CurrentBar > 0 && Low[0] < Low[1])
				score += 20;
			else if (CurrentBar > 0 && Close[0] < Close[1])
				score += 8;

			// Vyčerpání — délka legu vs typický impuls (0–15)
			double moveTicks = moveSinceBase / TickSize;
			double refMove = Math.Max(PeakMinTicks, lastImpulseSizeTicks > 0 ? lastImpulseSizeTicks * 0.85 : PeakMinTicks);
			score += 15 * Math.Min(1.0, Math.Max(0, (moveTicks / refMove - 0.65) / 0.85));

			// Slábnoucí momentum oproti předchozí svíčce (0–10)
			if (CurrentBar > 0)
			{
				double curUp = (Close[0] - Low[0]) / TickSize;
				double prevUp = (Close[1] - Low[1]) / TickSize;
				if (prevUp > 2 && curUp < prevUp * 0.55)
					score += 10;
				else if (prevUp > 2 && curUp < prevUp * 0.8)
					score += 5;
			}

			return (int)Math.Round(Math.Min(99, Math.Max(confirmed ? 50 : 5, score)));
		}

		private int ComputePeakProbabilityShort(double pullbackTicks, double moveSinceBase, bool confirmed)
		{
			double range = High[0] - Low[0];
			if (range < TickSize)
				range = TickSize;

			double score = confirmed ? 55 : 0;

			double bullishBody = Close[0] > Open[0] ? (Close[0] - Open[0]) / range : 0;
			double closeFromLow = (Close[0] - Low[0]) / range;
			double lowerWick = (Math.Min(Open[0], Close[0]) - Low[0]) / range;
			score += 25 * Math.Min(1.0, Math.Max(bullishBody, (1.0 - closeFromLow) * 0.9) + lowerWick * 0.25);

			score += 30 * Math.Min(1.0, pullbackTicks / Math.Max(1, PeakPullbackTicks));

			if (CurrentBar > 0 && High[0] > High[1])
				score += 20;
			else if (CurrentBar > 0 && Close[0] > Close[1])
				score += 8;

			double moveTicks = moveSinceBase / TickSize;
			double refMove = Math.Max(PeakMinTicks, lastImpulseSizeTicks > 0 ? lastImpulseSizeTicks * 0.85 : PeakMinTicks);
			score += 15 * Math.Min(1.0, Math.Max(0, (moveTicks / refMove - 0.65) / 0.85));

			if (CurrentBar > 0)
			{
				double curDown = (High[0] - Close[0]) / TickSize;
				double prevDown = (High[1] - Close[1]) / TickSize;
				if (prevDown > 2 && curDown < prevDown * 0.55)
					score += 10;
				else if (prevDown > 2 && curDown < prevDown * 0.8)
					score += 5;
			}

			return (int)Math.Round(Math.Min(99, Math.Max(confirmed ? 50 : 5, score)));
		}

		private void SetAction(string headline, string detail, Brush brush)
		{
			actionHeadline = headline;
			actionDetail = detail;
			actionBrush = brush;
		}

		private void UpdateActionPanel()
		{
			if (!ShowActionPanel)
				return;

			SwingType current = (SwingType)barTypeSeries[0];

			if (!entrySignalThisBar && !peakSoftSignalThisBar && !peakConfirmedSignalThisBar)
			{
				if (current == SwingType.BuyImpulse)
					SetAction("▶ BUY běží — DRŽ", "Vstup jen po značce VSTUP ↑", brushBuy);
				else if (current == SwingType.SellImpulse)
					SetAction("▶ SELL běží — DRŽ", "Vstup jen po značce VSTUP ↓", brushSell);
				else if (current == SwingType.Correction)
					SetAction("↩ KOREKCE — NEVSTUPOVAT", "Růžová = odraz, ne nový trend", brushCorrection);
				else if (current == SwingType.TentativeBuy || current == SwingType.TentativeSell)
				{ /* already set in GetTentative* */ }
				else if (current == SwingType.Neutral)
					SetAction("■ BEZ SIGNÁLU", "Šedá = příliš malý pohyb — neobchodovat", brushNeutral);
			}

			string text = actionHeadline + "\n" + actionDetail
				+ "\nPravidlo: VSTUP = ↑/↓ | PEAK? + % = síla vrcholu | ◆ PEAK = zavřít";

			Draw.TextFixed(this, "MES500TSMV4Light_Action", text,
				TextPosition.BottomRight, actionBrush, actionFont,
				Brushes.Black, Brushes.Transparent, 0);
		}

		private void DrawEntryMarker(SwingDir dir)
		{
			string text = dir == SwingDir.Long ? "VSTUP ↑" : "VSTUP ↓";
			Brush brush = dir == SwingDir.Long ? brushBuy : brushSell;

			double knotY = dir == SwingDir.Long ? Low[0] : High[0];
			double labelOffset = LabelOffsetTicks * TickSize * 3;
			double labelY = dir == SwingDir.Long ? knotY - labelOffset : knotY + labelOffset;

			string barTag = "MES500TSMV4Light_Entry_" + CurrentBar;

			if (ShowEntryConnector)
			{
				Draw.Line(this, barTag + "_Conn", false,
					0, knotY, 0, labelY,
					brush, DashStyleHelper.Solid, 2);
			}

			Draw.Text(this, barTag + "_Txt", false, text, 0, labelY, 0, brush, labelFont,
				TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawPeakSoftMarker(SwingDir dir)
		{
			double knotY = dir == SwingDir.Long ? High[0] : Low[0];
			double labelOffset = LabelOffsetTicks * TickSize * 4;
			double labelY = dir == SwingDir.Long ? knotY + labelOffset : knotY - labelOffset;

			string barTag = "MES500TSMV4Light_PeakSoft_" + CurrentBar;

			if (ShowEntryConnector)
			{
				Draw.Line(this, barTag + "_Conn", false,
					0, knotY, 0, labelY,
					brushPeakSoft, DashStyleHelper.Dot, 1);
			}

			Draw.Text(this, barTag + "_Txt", false, "PEAK?",
				0, labelY, 0, brushPeakSoft, labelFont,
				TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);

			double pctOffset = LabelOffsetTicks * TickSize * 0.9;
			double pctY = dir == SwingDir.Long ? labelY + pctOffset : labelY - pctOffset;
			Draw.Text(this, barTag + "_Pct", false, peakSoftProbability + "%",
				0, pctY, 0, brushPeakSoft, peakPctFont,
				TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawPeakConfirmedMarker(SwingDir dir)
		{
			double knotY = dir == SwingDir.Long ? High[0] : Low[0];
			double labelOffset = LabelOffsetTicks * TickSize * 6;
			double labelY = dir == SwingDir.Long ? knotY + labelOffset : knotY - labelOffset;

			string barTag = "MES500TSMV4Light_Peak_" + CurrentBar;

			if (ShowEntryConnector)
			{
				Draw.Line(this, barTag + "_Conn", false,
					0, knotY, 0, labelY,
					brushPeak, DashStyleHelper.Dash, 1);
			}

			Draw.Text(this, barTag + "_Txt", false, "◆ PEAK",
				0, labelY, 0, brushPeak, labelFont,
				TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawSwingLine(int barA, double priceA, int barB, double priceB)
		{
			if (!ShowSwingLines)
				return;

			Brush lineBrush = priceB > priceA ? brushBuy : brushSell;
			string tag = "MES500TSMV4Light_Line_" + barA + "_" + barB;
			Draw.Line(this, tag, false,
				CurrentBar - barA, priceA,
				CurrentBar - barB, priceB,
				lineBrush, DashStyleHelper.Solid, 1);
		}

		private void UpdateStrengthPlot()
		{
			if (!ShowStrengthPanel)
			{
				Values[0][0] = 0;
				PlotBrushes[0][0] = Brushes.Transparent;
				return;
			}

			double strength;
			SwingType current = (SwingType)barTypeSeries[0];

			switch (current)
			{
				case SwingType.BuyImpulse:
				case SwingType.TentativeBuy:
					strength = (Close[0] - lastPivotPrice) / TickSize;
					PlotBrushes[0][0] = current == SwingType.TentativeBuy ? brushBuyTentative : brushBuy;
					break;
				case SwingType.SellImpulse:
				case SwingType.TentativeSell:
					strength = -(lastPivotPrice - Close[0]) / TickSize;
					PlotBrushes[0][0] = current == SwingType.TentativeSell ? brushSellTentative : brushSell;
					break;
				case SwingType.Correction:
					if (phase == TrendPhase.CorrectionAfterBuy || phase == TrendPhase.SeekingLow)
						strength = -(lastPivotPrice - Close[0]) / TickSize * 0.5;
					else
						strength = (Close[0] - lastPivotPrice) / TickSize * 0.5;
					PlotBrushes[0][0] = brushCorrection;
					break;
				default:
					strength = 0;
					PlotBrushes[0][0] = brushNeutral;
					break;
			}

			Values[0][0] = strength;
		}

		#region Properties

		[NinjaScriptProperty]
		[Range(8, 80)]
		[Display(Name = "Min Swing Ticks", Description = "Min. velikost swingu v tickách (filtr šumu).", Order = 1, GroupName = "1. Swing")]
		public int MinSwingTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0.30, 0.90)]
		[Display(Name = "Correction Ratio", Description = "Korekce = pohyb menší než tento podíl předchozího impulsu.", Order = 2, GroupName = "1. Swing")]
		public double CorrectionRatio { get; set; }

		[NinjaScriptProperty]
		[Range(0, 3)]
		[Display(Name = "Confirm Bars", Description = "Počet barů pro potvrzení pivotu (0 = ihned).", Order = 3, GroupName = "1. Swing")]
		public int ConfirmBars { get; set; }

		[NinjaScriptProperty]
		[Range(5, 50)]
		[Display(Name = "ATR Period", Order = 1, GroupName = "2. ATR")]
		public int AtrPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 3.0)]
		[Display(Name = "ATR Multiplier", Description = "MinSwing = max(MinSwingTicks, ATR × multiplier).", Order = 2, GroupName = "2. ATR")]
		public double AtrMultiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Swing Lines", Description = "Čáry mezi pivot body.", Order = 1, GroupName = "3. Zobrazení")]
		public bool ShowSwingLines { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Strength Panel", Description = "Sub-panel: síla pohybu v tickách.", Order = 2, GroupName = "3. Zobrazení")]
		public bool ShowStrengthPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Action Panel", Description = "Panel: VSTUP / DRŽ / NEVSTUPOVAT / ČEKEJ.", Order = 3, GroupName = "3. Zobrazení")]
		public bool ShowActionPanel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Entry Markers", Description = "Label VSTUP ↑/↓ jen na potvrzeném signálu.", Order = 4, GroupName = "3. Zobrazení")]
		public bool ShowEntryMarkers { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Entry Connector", Description = "Čára od knotu svíčky k labelu VSTUP / PEAK.", Order = 5, GroupName = "3. Zobrazení")]
		public bool ShowEntryConnector { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Peak Markers", Description = "Zlatý ◆ PEAK — potvrzený exit po pullbacku.", Order = 6, GroupName = "3. Zobrazení")]
		public bool ShowPeakMarkers { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Peak Soft Markers", Description = "Oranžový PEAK? — varování, může být falešný.", Order = 7, GroupName = "3. Zobrazení")]
		public bool ShowPeakSoftMarkers { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Label Offset Ticks", Order = 8, GroupName = "3. Zobrazení")]
		public int LabelOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(10, 80)]
		[Display(Name = "Peak Min Ticks", Description = "Minimální pohyb trendu před PEAK signálem.", Order = 1, GroupName = "4. PEAK")]
		public int PeakMinTicks { get; set; }

		[NinjaScriptProperty]
		[Range(4, 40)]
		[Display(Name = "Peak Pullback Ticks", Description = "◆ PEAK až po tomto pullbacku od vrcholu/dna (filtr falešných peaků).", Order = 2, GroupName = "4. PEAK")]
		public int PeakPullbackTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Peak Require Break", Description = "◆ PEAK vyžaduje průraz low/high předchozí svíčky.", Order = 3, GroupName = "4. PEAK")]
		public bool PeakRequireBreak { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Stats Panel", Description = "Tabulka win rate / ticky / USD (VSTUP → ◆ PEAK).", Order = 1, GroupName = "5. Statistiky")]
		public bool ShowStatsPanel { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Contract Count", Description = "Počet kontraktů pro výpočet USD.", Order = 2, GroupName = "5. Statistiky")]
		public int ContractCount { get; set; }

		[NinjaScriptProperty]
		[Range(0, 24)]
		[Display(Name = "Stats Lookback Hours", Description = "0 = celá historie, jinak jen obchody z posledních N hodin.", Order = 3, GroupName = "5. Statistiky")]
		public int StatsLookbackHours { get; set; }

		[NinjaScriptProperty]
		[Range(20, 2000)]
		[Display(Name = "Max Stats Trades", Description = "Max. počet obchodů v paměti (výkon).", Order = 4, GroupName = "5. Statistiky")]
		public int MaxStatsTrades { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> MoveStrength => Values[0];

		[Browsable(false)]
		[XmlIgnore]
		public Series<int> BarType => barTypeSeries;

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MES500TSwingMapV4Light[] cacheMES500TSwingMapV4Light;
		public MES500TSwingMapV4Light MES500TSwingMapV4Light(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			return MES500TSwingMapV4Light(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, showPeakSoftMarkers, labelOffsetTicks, peakMinTicks, peakPullbackTicks, peakRequireBreak, showStatsPanel, contractCount, statsLookbackHours, maxStatsTrades);
		}

		public MES500TSwingMapV4Light MES500TSwingMapV4Light(ISeries<double> input, int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			if (cacheMES500TSwingMapV4Light != null)
				for (int idx = 0; idx < cacheMES500TSwingMapV4Light.Length; idx++)
					if (cacheMES500TSwingMapV4Light[idx] != null && cacheMES500TSwingMapV4Light[idx].MinSwingTicks == minSwingTicks && cacheMES500TSwingMapV4Light[idx].CorrectionRatio == correctionRatio && cacheMES500TSwingMapV4Light[idx].ConfirmBars == confirmBars && cacheMES500TSwingMapV4Light[idx].AtrPeriod == atrPeriod && cacheMES500TSwingMapV4Light[idx].AtrMultiplier == atrMultiplier && cacheMES500TSwingMapV4Light[idx].ShowSwingLines == showSwingLines && cacheMES500TSwingMapV4Light[idx].ShowStrengthPanel == showStrengthPanel && cacheMES500TSwingMapV4Light[idx].ShowActionPanel == showActionPanel && cacheMES500TSwingMapV4Light[idx].ShowEntryMarkers == showEntryMarkers && cacheMES500TSwingMapV4Light[idx].ShowEntryConnector == showEntryConnector && cacheMES500TSwingMapV4Light[idx].ShowPeakMarkers == showPeakMarkers && cacheMES500TSwingMapV4Light[idx].ShowPeakSoftMarkers == showPeakSoftMarkers && cacheMES500TSwingMapV4Light[idx].LabelOffsetTicks == labelOffsetTicks && cacheMES500TSwingMapV4Light[idx].PeakMinTicks == peakMinTicks && cacheMES500TSwingMapV4Light[idx].PeakPullbackTicks == peakPullbackTicks && cacheMES500TSwingMapV4Light[idx].PeakRequireBreak == peakRequireBreak && cacheMES500TSwingMapV4Light[idx].ShowStatsPanel == showStatsPanel && cacheMES500TSwingMapV4Light[idx].ContractCount == contractCount && cacheMES500TSwingMapV4Light[idx].StatsLookbackHours == statsLookbackHours && cacheMES500TSwingMapV4Light[idx].MaxStatsTrades == maxStatsTrades && cacheMES500TSwingMapV4Light[idx].EqualsInput(input))
						return cacheMES500TSwingMapV4Light[idx];
			return CacheIndicator<MES500TSwingMapV4Light>(new MES500TSwingMapV4Light(){ MinSwingTicks = minSwingTicks, CorrectionRatio = correctionRatio, ConfirmBars = confirmBars, AtrPeriod = atrPeriod, AtrMultiplier = atrMultiplier, ShowSwingLines = showSwingLines, ShowStrengthPanel = showStrengthPanel, ShowActionPanel = showActionPanel, ShowEntryMarkers = showEntryMarkers, ShowEntryConnector = showEntryConnector, ShowPeakMarkers = showPeakMarkers, ShowPeakSoftMarkers = showPeakSoftMarkers, LabelOffsetTicks = labelOffsetTicks, PeakMinTicks = peakMinTicks, PeakPullbackTicks = peakPullbackTicks, PeakRequireBreak = peakRequireBreak, ShowStatsPanel = showStatsPanel, ContractCount = contractCount, StatsLookbackHours = statsLookbackHours, MaxStatsTrades = maxStatsTrades }, input, ref cacheMES500TSwingMapV4Light);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TSwingMapV4Light MES500TSwingMapV4Light(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			return indicator.MES500TSwingMapV4Light(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, showPeakSoftMarkers, labelOffsetTicks, peakMinTicks, peakPullbackTicks, peakRequireBreak, showStatsPanel, contractCount, statsLookbackHours, maxStatsTrades);
		}

		public Indicators.MES500TSwingMapV4Light MES500TSwingMapV4Light(ISeries<double> input , int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			return indicator.MES500TSwingMapV4Light(input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, showPeakSoftMarkers, labelOffsetTicks, peakMinTicks, peakPullbackTicks, peakRequireBreak, showStatsPanel, contractCount, statsLookbackHours, maxStatsTrades);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TSwingMapV4Light MES500TSwingMapV4Light(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			return indicator.MES500TSwingMapV4Light(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, showPeakSoftMarkers, labelOffsetTicks, peakMinTicks, peakPullbackTicks, peakRequireBreak, showStatsPanel, contractCount, statsLookbackHours, maxStatsTrades);
		}

		public Indicators.MES500TSwingMapV4Light MES500TSwingMapV4Light(ISeries<double> input , int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, bool showPeakSoftMarkers, int labelOffsetTicks, int peakMinTicks, int peakPullbackTicks, bool peakRequireBreak, bool showStatsPanel, int contractCount, int statsLookbackHours, int maxStatsTrades)
		{
			return indicator.MES500TSwingMapV4Light(input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, showPeakSoftMarkers, labelOffsetTicks, peakMinTicks, peakPullbackTicks, peakRequireBreak, showStatsPanel, contractCount, statsLookbackHours, maxStatsTrades);
		}
	}
}

#endregion
