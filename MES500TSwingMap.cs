#region Using declarations
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
	/// MES500TSwingMap — swing map pro MES 500 tick.
	/// Barví price bary: zelená BUY impulse, červená SELL impulse, růžová KOREKCE.
	/// Mezi dvěma signály stejného směru označí protipohyb jako KOREKCE, pokud je menší než CorrectionRatio × prior impulse.
	/// </summary>
	public class MES500TSwingMap : Indicator
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
		private SimpleFont labelFont;
		private SimpleFont actionFont;

		// Entry / panel state
		private string actionHeadline;
		private string actionDetail;
		private Brush actionBrush;
		private bool entrySignalThisBar;
		private SwingDir entrySignalDir;

		// Peak state
		private bool peakSignalThisBar;
		private SwingDir peakSignalDir;
		private bool peakAlreadyDrawnThisLeg;

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
				Description = "MES500TSwingMap — BUY/SELL/KOREKCE mapa z swing struktury pro MES 500 tick.";
				Name        = "MES500TSwingMap";
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
				ShowSwingLines    = true;
				ShowStrengthPanel = true;
				ShowActionPanel   = true;
				ShowEntryMarkers  = true;
				ShowEntryConnector = true;
				ShowPeakMarkers   = true;
				LabelOffsetTicks  = 4;
				PeakMinTicks      = 30;

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

				labelFont = new SimpleFont("Arial", 9) { Bold = true };
				actionFont = new SimpleFont("Arial", 11) { Bold = true };
			}
			else if (State == State.DataLoaded)
			{
				barTypeSeries = new Series<int>(this);
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
			peakSignalThisBar        = false;
			peakSignalDir            = SwingDir.None;
			peakAlreadyDrawnThisLeg  = false;
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
			peakSignalThisBar       = false;
			peakSignalDir           = SwingDir.None;
			UpdateSwingLogic(minMove);
			DetectPeak();
			UpdateStrengthPlot();
			UpdateActionPanel();
			if (ShowEntryMarkers && entrySignalThisBar)
				DrawEntryMarker(entrySignalDir);
			if (ShowPeakMarkers && peakSignalThisBar)
				DrawPeakMarker(peakSignalDir);
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
			peakAlreadyDrawnThisLeg = false;

			return GetTentativeDownColor();
		}

		private SwingType ProcessSeekingLow(double minMove)
		{
			if (Low[0] < runningExtreme)
			{
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
			peakAlreadyDrawnThisLeg = false;

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
			peakAlreadyDrawnThisLeg = false;

			if (dir == SwingDir.Long)
				SetAction("🟢 VSTUP BUY", detail, brushBuy);
			else if (dir == SwingDir.Short)
				SetAction("🔴 VSTUP SELL", detail, brushSell);
		}

		private void DetectPeak()
		{
			if (!ShowPeakMarkers || peakAlreadyDrawnThisLeg)
				return;

			double minPeakMove = PeakMinTicks * TickSize;

			// PEAK pouze pro čistou BUY nebo SELL leg — ne pro korekce ani konsolidaci
			if (phase == TrendPhase.SeekingHigh && lastPivotKind == PivotKind.Low)
			{
				double moveSinceBase = runningExtreme - lastPivotPrice;
				if (moveSinceBase < minPeakMove)
					return;

				bool atExtreme = High[0] >= runningExtreme - 2 * TickSize;
				bool reversalCandle = Close[0] < Open[0]
					|| Close[0] < (High[0] + Low[0]) / 2.0;
				bool bodyNearHigh = Close[0] < High[0] - (High[0] - Low[0]) * 0.35;

				if (atExtreme && (reversalCandle || bodyNearHigh))
				{
					peakSignalThisBar = true;
					peakSignalDir = SwingDir.Long;
					SetAction("◆ PEAK BUY — UZAVŘÍT", "Obrat svíčka na vrcholu — nejlepší moment k exitu", brushPeak);
					peakAlreadyDrawnThisLeg = true;
				}
			}
			else if (phase == TrendPhase.SeekingLow && lastPivotKind == PivotKind.High)
			{
				double moveSinceBase = lastPivotPrice - runningExtreme;
				if (moveSinceBase < minPeakMove)
					return;

				bool atExtreme = Low[0] <= runningExtreme + 2 * TickSize;
				bool reversalCandle = Close[0] > Open[0]
					|| Close[0] > (High[0] + Low[0]) / 2.0;
				bool bodyNearLow = Close[0] > Low[0] + (High[0] - Low[0]) * 0.35;

				if (atExtreme && (reversalCandle || bodyNearLow))
				{
					peakSignalThisBar = true;
					peakSignalDir = SwingDir.Short;
					SetAction("◆ PEAK SELL — UZAVŘÍT", "Obrat svíčka na dnu — nejlepší moment k exitu", brushPeak);
					peakAlreadyDrawnThisLeg = true;
				}
			}
			// Korekce (CorrectionAfterBuy / CorrectionAfterSell) → PEAK se nezobrazuje
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

			if (!entrySignalThisBar)
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
				+ "\nPravidlo: VSTUP = ↑/↓, VÝSTUP = ◆ PEAK";

			Draw.TextFixed(this, "MES500TSM_Action", text,
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

			string barTag = "MES500TSM_Entry_" + CurrentBar;

			if (ShowEntryConnector)
			{
				Draw.Line(this, barTag + "_Conn", false,
					0, knotY, 0, labelY,
					brush, DashStyleHelper.Solid, 2);
			}

			Draw.Text(this, barTag + "_Txt", false, text, 0, labelY, 0, brush, labelFont,
				TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
		}

		private void DrawPeakMarker(SwingDir dir)
		{
			// PEAK kotví se na extremním knotu — High pro BUY leg, Low pro SELL leg
			// Offset je 2× větší než VSTUP — nikdy se nepřekrývají
			double knotY = dir == SwingDir.Long ? High[0] : Low[0];
			double labelOffset = LabelOffsetTicks * TickSize * 6;
			double labelY = dir == SwingDir.Long ? knotY + labelOffset : knotY - labelOffset;

			string barTag = "MES500TSM_Peak_" + CurrentBar;

			// Čára od knotu k textu
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
			string tag = "MES500TSM_Line_" + barA + "_" + barB;
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
		[Display(Name = "Show Peak Markers", Description = "Zlatý ◆ PEAK na nejlepším momentu k výstupu.", Order = 6, GroupName = "3. Zobrazení")]
		public bool ShowPeakMarkers { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Label Offset Ticks", Order = 7, GroupName = "3. Zobrazení")]
		public int LabelOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(10, 80)]
		[Display(Name = "Peak Min Ticks", Description = "Minimální pohyb trendu aby se PEAK mohl zobrazit.", Order = 1, GroupName = "4. PEAK")]
		public int PeakMinTicks { get; set; }

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
		private MES500TSwingMap[] cacheMES500TSwingMap;
		public MES500TSwingMap MES500TSwingMap(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			return MES500TSwingMap(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, labelOffsetTicks, peakMinTicks);
		}

		public MES500TSwingMap MES500TSwingMap(ISeries<double> input, int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			if (cacheMES500TSwingMap != null)
				for (int idx = 0; idx < cacheMES500TSwingMap.Length; idx++)
					if (cacheMES500TSwingMap[idx] != null && cacheMES500TSwingMap[idx].MinSwingTicks == minSwingTicks && cacheMES500TSwingMap[idx].CorrectionRatio == correctionRatio && cacheMES500TSwingMap[idx].ConfirmBars == confirmBars && cacheMES500TSwingMap[idx].AtrPeriod == atrPeriod && cacheMES500TSwingMap[idx].AtrMultiplier == atrMultiplier && cacheMES500TSwingMap[idx].ShowSwingLines == showSwingLines && cacheMES500TSwingMap[idx].ShowStrengthPanel == showStrengthPanel && cacheMES500TSwingMap[idx].ShowActionPanel == showActionPanel && cacheMES500TSwingMap[idx].ShowEntryMarkers == showEntryMarkers && cacheMES500TSwingMap[idx].ShowEntryConnector == showEntryConnector && cacheMES500TSwingMap[idx].ShowPeakMarkers == showPeakMarkers && cacheMES500TSwingMap[idx].LabelOffsetTicks == labelOffsetTicks && cacheMES500TSwingMap[idx].PeakMinTicks == peakMinTicks && cacheMES500TSwingMap[idx].EqualsInput(input))
						return cacheMES500TSwingMap[idx];
			return CacheIndicator<MES500TSwingMap>(new MES500TSwingMap(){ MinSwingTicks = minSwingTicks, CorrectionRatio = correctionRatio, ConfirmBars = confirmBars, AtrPeriod = atrPeriod, AtrMultiplier = atrMultiplier, ShowSwingLines = showSwingLines, ShowStrengthPanel = showStrengthPanel, ShowActionPanel = showActionPanel, ShowEntryMarkers = showEntryMarkers, ShowEntryConnector = showEntryConnector, ShowPeakMarkers = showPeakMarkers, LabelOffsetTicks = labelOffsetTicks, PeakMinTicks = peakMinTicks }, input, ref cacheMES500TSwingMap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TSwingMap MES500TSwingMap(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			return indicator.MES500TSwingMap(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, labelOffsetTicks, peakMinTicks);
		}

		public Indicators.MES500TSwingMap MES500TSwingMap(ISeries<double> input , int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			return indicator.MES500TSwingMap(input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, labelOffsetTicks, peakMinTicks);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TSwingMap MES500TSwingMap(int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			return indicator.MES500TSwingMap(Input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, labelOffsetTicks, peakMinTicks);
		}

		public Indicators.MES500TSwingMap MES500TSwingMap(ISeries<double> input , int minSwingTicks, double correctionRatio, int confirmBars, int atrPeriod, double atrMultiplier, bool showSwingLines, bool showStrengthPanel, bool showActionPanel, bool showEntryMarkers, bool showEntryConnector, bool showPeakMarkers, int labelOffsetTicks, int peakMinTicks)
		{
			return indicator.MES500TSwingMap(input, minSwingTicks, correctionRatio, confirmBars, atrPeriod, atrMultiplier, showSwingLines, showStrengthPanel, showActionPanel, showEntryMarkers, showEntryConnector, showPeakMarkers, labelOffsetTicks, peakMinTicks);
		}
	}
}

#endregion
