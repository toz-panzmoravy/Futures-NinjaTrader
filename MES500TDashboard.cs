#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
	/// MES500T Dashboard — jeden aktivní trend najednou (BUY nebo SELL, nikdy oba).
	/// TrendPhase sloupec = kontinuální síla 0–100 na každé svíčce (barva = fáze vývoje).
	/// </summary>
	public class MES500TDashboard : Indicator
	{
		private enum TrendPhase
		{
			Flat,
			Forming,
			Active,
			Mature,
			Fading
		}

		private enum TrendDirection
		{
			None,
			Long,
			Short
		}

		private enum MarketRegime
		{
			Flat,
			TrendUp,
			TrendDown,
			Chop,
			SqueezeWait
		}

		private enum KcZone
		{
			Neutral,
			UpperBand,
			LowerBand,
			MidTrend
		}

		private enum ActionAdvice
		{
			WaitSqueeze,
			NoTradeTangle,
			WaitFlat,
			WatchEntry,
			EntryOk,
			Hold,
			HoldCaution,
			ConsiderClose,
			CloseNow
		}

		private const int MAX_APPROACH = 7;
		private const int PEAK_LOOKBACK = 5;

		private EMA    bbEmaInd;
		private StdDev bbStdDevInd;
		private EMA    kcEmaInd;
		private ATR    kcAtrInd;
		private MACD   macdInd;

		private Brush brushBullStrong;
		private Brush brushBullWeak;
		private Brush brushBearStrong;
		private Brush brushBearWeak;
		private Brush brushNeutral;
		private Brush brushForming;
		private Brush brushFading;
		private Brush brushEntryGood;
		private Brush brushEntryMid;
		private Brush brushEntryLow;
		private Brush brushExitWarn;
		private Brush brushExitStrong;
		private Brush brushZero;
		private SimpleFont labelFont;
		private SimpleFont actionFont;
		private SimpleFont markerFont;
		private SimpleFont phaseLabelFont;

		private TrendPhase priorTrendPhase;
		private TrendDirection priorTrendDirection;
		private TrendDirection lockedDirection;
		private double priorDisplayStrength;

		private Series<double> entryScoreHistory;
		private Series<double> strengthHistory;

		// Plot 0: TrendStrength — signed bars
		// Plot 1: EntryScore — signed line
		// Plot 2: ExitPressure — signed (proti směru vstupu = tlak na zavření)
		// Plot 3: Nula

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500T Dashboard v2 — síla trendu, Entry/Exit score, akční panel, režim trhu.";
				Name        = "MES500TDashboard";
				Calculate   = Calculate.OnBarClose;
				IsOverlay   = false;
				DisplayInDataBox = true;
				DrawOnPricePanel = false;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot = 25;

				BbPeriod       = 20;
				BbStdDev       = 2.0;
				KcPeriod       = 20;
				KcMultiplier   = 1.5;
				MacdFast       = 6;
				MacdSlow       = 13;
				MacdSignal     = 9;

				EntryBufferTicks        = 1;
				ApproachNearTicks         = 10;
				RequireThreeBarMomentum   = false;
				ShowStatusText            = true;
				ShowPhaseLabels           = true;
				ShowTrendStartArrows      = true;
				ShowPeakMarkers           = true;
				MomentumFadeBars          = 2;
				PeakLookbackBars          = 5;
				ExitCloseThreshold        = 55;
				ExitStrongThreshold       = 72;

				AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Bar,  "TrendSila");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "EntryScore");
				AddPlot(new Stroke(Brushes.Orange,      2), PlotStyle.Line, "ExitPressure");
				AddPlot(new Stroke(Brushes.Gray,        1), PlotStyle.Line, "Nula");

				AddLine(new Stroke(Brushes.DimGray, 1), 0,   "Nula");
				AddLine(new Stroke(Brushes.DimGray, 1), 70,  "EntryBuySilny");
				AddLine(new Stroke(Brushes.DimGray, 1), 40,  "EntryBuySlaby");
				AddLine(new Stroke(Brushes.DimGray, 1), -70, "EntrySellSilny");
				AddLine(new Stroke(Brushes.DimGray, 1), -40, "EntrySellSlaby");
			}
			else if (State == State.Configure)
			{
				bbEmaInd    = EMA(BbPeriod);
				bbStdDevInd = StdDev(BbPeriod);
				kcEmaInd    = EMA(KcPeriod);
				kcAtrInd    = ATR(KcPeriod);
				macdInd     = MACD(MacdFast, MacdSlow, MacdSignal);

				brushBullStrong = new SolidColorBrush(Color.FromRgb(0, 220, 80));
				brushBullStrong.Freeze();
				brushBullWeak   = new SolidColorBrush(Color.FromRgb(0, 140, 50));
				brushBullWeak.Freeze();
				brushBearStrong = new SolidColorBrush(Color.FromRgb(220, 50, 30));
				brushBearStrong.Freeze();
				brushBearWeak   = new SolidColorBrush(Color.FromRgb(140, 40, 20));
				brushBearWeak.Freeze();
				brushNeutral    = new SolidColorBrush(Color.FromRgb(100, 100, 100));
				brushNeutral.Freeze();
				brushForming    = new SolidColorBrush(Color.FromRgb(255, 200, 0));
				brushForming.Freeze();
				brushFading     = new SolidColorBrush(Color.FromRgb(255, 120, 0));
				brushFading.Freeze();
				brushEntryGood  = new SolidColorBrush(Color.FromRgb(0, 200, 80));
				brushEntryGood.Freeze();
				brushEntryMid   = new SolidColorBrush(Color.FromRgb(255, 200, 0));
				brushEntryMid.Freeze();
				brushEntryLow   = new SolidColorBrush(Color.FromRgb(120, 120, 120));
				brushEntryLow.Freeze();
				brushExitWarn = new SolidColorBrush(Color.FromRgb(255, 140, 0));
				brushExitWarn.Freeze();
				brushExitStrong = new SolidColorBrush(Color.FromRgb(255, 60, 40));
				brushExitStrong.Freeze();
				brushZero       = new SolidColorBrush(Color.FromRgb(60, 60, 60));
				brushZero.Freeze();

				labelFont      = new SimpleFont("Arial", 9);
				actionFont     = new SimpleFont("Arial", 11) { Bold = true };
				markerFont     = new SimpleFont("Arial", 8) { Bold = true };
				phaseLabelFont = new SimpleFont("Arial", 7);
			}
			else if (State == State.DataLoaded)
			{
				entryScoreHistory     = new Series<double>(this);
				strengthHistory       = new Series<double>(this);
				priorTrendPhase       = TrendPhase.Flat;
				priorTrendDirection   = TrendDirection.None;
				lockedDirection       = TrendDirection.None;
				priorDisplayStrength  = 0;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToPlot)
				return;

			double bbMid   = bbEmaInd[0];
			double stdDev  = bbStdDevInd[0];
			double bbUpper = bbMid + BbStdDev * stdDev;
			double bbLower = bbMid - BbStdDev * stdDev;

			double kcMid   = kcEmaInd[0];
			double kcUpper = kcMid + KcMultiplier * kcAtrInd[0];
			double kcLower = kcMid - KcMultiplier * kcAtrInd[0];
			double near    = ApproachNearTicks * TickSize;

			double hist0 = macdInd.Diff[0];
			double hist1 = CurrentBar >= 1 ? macdInd.Diff[1] : hist0;
			double hist2 = CurrentBar >= 2 ? macdInd.Diff[2] : hist1;
			double hist3 = CurrentBar >= 3 ? macdInd.Diff[3] : hist2;

			bool fullSqueeze    = bbUpper <= kcUpper && bbLower >= kcLower;
			bool partialSqueeze = !fullSqueeze && ((bbUpper <= kcUpper && bbUpper >= kcLower)
			                                     || (bbLower >= kcLower && bbLower <= kcUpper));
			bool macdTangle     = IsMacdTangle();
			bool longExhaustion  = IsLongExhaustion(hist0, hist1, hist2, hist3);
			bool shortExhaustion = IsShortExhaustion(hist0, hist1, hist2, hist3);

			int bullSlopeCount = CountKcSlopeBars(true);
			int bearSlopeCount = CountKcSlopeBars(false);
			int longFadeBars   = CountMomentumFadeBars(true);
			int shortFadeBars  = CountMomentumFadeBars(false);

			int approachLongRaw  = CountApproachScore(true, fullSqueeze, macdTangle, longExhaustion, bullSlopeCount, kcMid, kcUpper, kcLower, near, hist0, hist1, hist2);
			int approachShortRaw = CountApproachScore(false, fullSqueeze, macdTangle, shortExhaustion, bearSlopeCount, kcMid, kcUpper, kcLower, near, hist0, hist1, hist2);

			double bullStrength = ComputeDirectionalStrength(
				true, bullSlopeCount, approachLongRaw, fullSqueeze, partialSqueeze,
				macdTangle, longExhaustion, hist0, hist1, kcMid);

			double bearStrength = ComputeDirectionalStrength(
				false, bearSlopeCount, approachShortRaw, fullSqueeze, partialSqueeze,
				macdTangle, shortExhaustion, hist0, hist1, kcMid);

			TrendDirection direction = SelectLockedDirection(bullStrength, bearStrength, hist0);

			double rawStrength = direction == TrendDirection.Long ? bullStrength
				: direction == TrendDirection.Short ? bearStrength
				: 0;

			double displayStrength = SmoothDisplayStrength(direction, rawStrength);

			TrendPhase phase = DeriveTrendPhase(
				direction, displayStrength, priorTrendPhase, priorTrendDirection,
				direction == TrendDirection.Long ? longExhaustion : shortExhaustion,
				direction == TrendDirection.Long ? longFadeBars : shortFadeBars,
				hist0, hist1);

			int activeSlope = direction == TrendDirection.Long ? bullSlopeCount : bearSlopeCount;
			int activeApproach = direction == TrendDirection.Long ? approachLongRaw : approachShortRaw;

			double entryMagnitude = GetEntryScore(
				direction, phase, displayStrength, activeSlope, activeApproach,
				macdTangle, partialSqueeze, fullSqueeze);

			double entrySigned = direction == TrendDirection.Long ? entryMagnitude
				: direction == TrendDirection.Short ? -entryMagnitude
				: 0;

			bool divergence = direction == TrendDirection.Long
				? IsPriceMomentumDivergence(true, hist0, hist1)
				: direction == TrendDirection.Short
					? IsPriceMomentumDivergence(false, hist0, hist1)
					: false;

			double exitMagnitude = ComputeExitPressure(
				direction, phase, displayStrength, priorDisplayStrength,
				direction == TrendDirection.Long ? longFadeBars : shortFadeBars,
				direction == TrendDirection.Long ? longExhaustion : shortExhaustion,
				divergence, partialSqueeze, hist0, hist1);

			double exitSigned = direction == TrendDirection.Long ? -exitMagnitude
				: direction == TrendDirection.Short ? exitMagnitude
				: 0;

			MarketRegime regime = GetMarketRegime(
				direction, phase, fullSqueeze, macdTangle, bullStrength, bearStrength);

			KcZone kcZone = GetKcZone(kcUpper, kcLower, kcMid, near);

			ActionAdvice advice = GetActionAdvice(
				direction, phase, fullSqueeze, macdTangle,
				Math.Abs(entrySigned), exitMagnitude);

			double phasePlot = direction == TrendDirection.Short ? -displayStrength
				: direction == TrendDirection.Long ? displayStrength
				: 0;

			entryScoreHistory[0] = entrySigned;
			strengthHistory[0]   = displayStrength;

			Values[0][0] = phasePlot;
			Values[1][0] = entrySigned;
			Values[2][0] = exitSigned;
			Values[3][0] = 0;

			PlotBrushes[0][0] = GetPhaseBrush(direction, phase, displayStrength);
			PlotBrushes[1][0] = GetEntryBrush(entrySigned);
			PlotBrushes[2][0] = GetExitBrush(exitMagnitude);
			PlotBrushes[3][0] = brushZero;

			if (ShowPhaseLabels && phase != priorTrendPhase && phase != TrendPhase.Flat)
				DrawPhaseLabel(direction, phase, phasePlot);

			if (ShowTrendStartArrows)
				DrawTrendStartArrow(priorTrendDirection, priorTrendPhase, direction, phase, phasePlot);

			if (ShowPeakMarkers && direction != TrendDirection.None
				&& priorTrendDirection == direction
				&& priorTrendPhase == TrendPhase.Active
				&& (phase == TrendPhase.Mature || phase == TrendPhase.Fading))
			{
				DrawPeakMarker(direction == TrendDirection.Long, phase);
			}

			if (ShowStatusText)
				DrawActionPanel(advice, direction, phase, displayStrength,
					Math.Abs(entrySigned), exitMagnitude, regime, kcZone,
					fullSqueeze, partialSqueeze, approachLongRaw, approachShortRaw);

			priorTrendPhase       = phase;
			priorTrendDirection   = direction;
			priorDisplayStrength  = displayStrength;
			if (direction != TrendDirection.None)
				lockedDirection = direction;
			else if (displayStrength < 8)
				lockedDirection = TrendDirection.None;
		}

		private double ComputeDirectionalStrength(
			bool isLong,
			int slopeCount,
			int approachRaw,
			bool fullSqueeze,
			bool partialSqueeze,
			bool macdTangle,
			bool exhaustion,
			double hist0,
			double hist1,
			double kcMid)
		{
			if (fullSqueeze)
				return 0;

			double strength = Math.Min(8, slopeCount) * 5.0;

			if (isLong)
			{
				if (hist0 > 0) strength += 12;
				if (hist0 > hist1) strength += 10;
				if (Close[0] > kcMid) strength += 12;
			}
			else
			{
				if (hist0 < 0) strength += 12;
				if (hist0 < hist1) strength += 10;
				if (Close[0] < kcMid) strength += 12;
			}

			if (!exhaustion)
				strength += 8;

			strength += approachRaw * (15.0 / MAX_APPROACH);

			if (macdTangle)
				strength *= 0.35;
			else if (partialSqueeze)
				strength *= 0.75;

			if (exhaustion)
				strength *= 0.65;

			return Math.Min(100, Math.Max(0, strength));
		}

		private TrendDirection SelectLockedDirection(double bullStrength, double bearStrength, double hist0)
		{
			if (lockedDirection == TrendDirection.Long)
			{
				if (bearStrength > bullStrength + 18 && bearStrength >= 30)
					return TrendDirection.Short;
				if (bullStrength < 8 && bearStrength >= 22)
					return TrendDirection.Short;
				if (bullStrength >= 8 || priorDisplayStrength >= 12)
					return TrendDirection.Long;
			}

			if (lockedDirection == TrendDirection.Short)
			{
				if (bullStrength > bearStrength + 18 && bullStrength >= 30)
					return TrendDirection.Long;
				if (bearStrength < 8 && bullStrength >= 22)
					return TrendDirection.Long;
				if (bearStrength >= 8 || priorDisplayStrength >= 12)
					return TrendDirection.Short;
			}

			if (bullStrength < 10 && bearStrength < 10)
				return TrendDirection.None;

			if (bullStrength > bearStrength + 8)
				return TrendDirection.Long;
			if (bearStrength > bullStrength + 8)
				return TrendDirection.Short;

			if (hist0 > 0 && bullStrength >= 10)
				return TrendDirection.Long;
			if (hist0 < 0 && bearStrength >= 10)
				return TrendDirection.Short;

			return TrendDirection.None;
		}

		private double SmoothDisplayStrength(TrendDirection direction, double rawStrength)
		{
			if (direction == TrendDirection.None)
				return Math.Max(0, priorDisplayStrength * 0.7);

			if (priorTrendDirection == direction && rawStrength < priorDisplayStrength)
				return Math.Max(rawStrength, priorDisplayStrength * 0.88);

			return rawStrength;
		}

		private TrendPhase DeriveTrendPhase(
			TrendDirection direction,
			double strength,
			TrendPhase priorPhase,
			TrendDirection priorDirection,
			bool exhaustion,
			int fadeBars,
			double hist0,
			double hist1)
		{
			if (direction == TrendDirection.None || strength < 10)
				return TrendPhase.Flat;

			bool sameTrend = priorDirection == direction && priorPhase != TrendPhase.Flat;
			bool momentumGrowing = direction == TrendDirection.Long
				? hist0 > hist1
				: hist0 < hist1;

			if (fadeBars >= MomentumFadeBars || (exhaustion && strength < 45))
			{
				if (sameTrend && priorPhase >= TrendPhase.Active)
					return TrendPhase.Fading;
				return strength >= 20 ? TrendPhase.Mature : TrendPhase.Flat;
			}

			if (strength >= 55 && momentumGrowing && !exhaustion)
				return TrendPhase.Active;

			if (strength >= 30 && momentumGrowing)
				return sameTrend && priorPhase >= TrendPhase.Forming
					? (priorPhase == TrendPhase.Fading ? TrendPhase.Mature : priorPhase)
					: TrendPhase.Forming;

			if (sameTrend && priorPhase >= TrendPhase.Active)
				return TrendPhase.Mature;

			if (strength >= 15)
				return TrendPhase.Forming;

			return TrendPhase.Flat;
		}

		private Brush GetEntryBrush(double entrySigned)
		{
			double abs = Math.Abs(entrySigned);
			if (abs >= 70)
				return entrySigned > 0 ? brushEntryGood : brushBearStrong;
			if (abs >= 40)
				return brushEntryMid;
			return brushEntryLow;
		}

		private Brush GetExitBrush(double exitMagnitude)
		{
			if (exitMagnitude >= ExitStrongThreshold)
				return brushExitStrong;
			if (exitMagnitude >= ExitCloseThreshold)
				return brushExitWarn;
			if (exitMagnitude >= 25)
				return brushFading;
			return brushEntryLow;
		}

		private double ComputeExitPressure(
			TrendDirection direction,
			TrendPhase phase,
			double strength,
			double priorStrength,
			int fadeBars,
			bool exhaustion,
			bool divergence,
			bool partialSqueeze,
			double hist0,
			double hist1)
		{
			if (direction == TrendDirection.None || strength < 12)
				return 0;

			double score = 0;

			switch (phase)
			{
				case TrendPhase.Fading:  score += 38; break;
				case TrendPhase.Mature:  score += 22; break;
				case TrendPhase.Active:  score += 6;  break;
				case TrendPhase.Forming: score += 2;  break;
			}

			score += fadeBars * 14;

			if (exhaustion)
				score += 18;

			if (divergence)
				score += 16;

			if (priorStrength > strength + 6)
				score += 12;

			bool momentumAgainst = direction == TrendDirection.Long
				? hist0 < hist1
				: hist0 > hist1;
			if (momentumAgainst)
				score += 10;

			if (partialSqueeze)
				score += 6;

			return Math.Min(100, Math.Max(0, score));
		}

		private MarketRegime GetMarketRegime(
			TrendDirection direction,
			TrendPhase phase,
			bool fullSqueeze,
			bool macdTangle,
			double bullStrength,
			double bearStrength)
		{
			if (fullSqueeze)
				return MarketRegime.SqueezeWait;

			if (macdTangle)
				return MarketRegime.Chop;

			if (bullStrength >= 18 && bearStrength >= 18
				&& Math.Abs(bullStrength - bearStrength) < 15)
				return MarketRegime.Chop;

			if (direction == TrendDirection.Long && phase != TrendPhase.Flat)
				return MarketRegime.TrendUp;

			if (direction == TrendDirection.Short && phase != TrendPhase.Flat)
				return MarketRegime.TrendDown;

			return MarketRegime.Flat;
		}

		private KcZone GetKcZone(double kcUpper, double kcLower, double kcMid, double near)
		{
			if (Close[0] >= kcUpper - near)
				return KcZone.UpperBand;
			if (Close[0] <= kcLower + near)
				return KcZone.LowerBand;
			if (Close[0] > kcMid + near * 0.5 || Close[0] < kcMid - near * 0.5)
				return KcZone.MidTrend;
			return KcZone.Neutral;
		}

		private ActionAdvice GetActionAdvice(
			TrendDirection direction,
			TrendPhase phase,
			bool fullSqueeze,
			bool macdTangle,
			double entryAbs,
			double exitPressure)
		{
			if (fullSqueeze)
				return ActionAdvice.WaitSqueeze;

			if (macdTangle)
				return ActionAdvice.NoTradeTangle;

			if (direction == TrendDirection.None)
				return ActionAdvice.WaitFlat;

			if (exitPressure >= ExitStrongThreshold)
				return ActionAdvice.CloseNow;

			if (exitPressure >= ExitCloseThreshold || phase == TrendPhase.Fading)
				return ActionAdvice.ConsiderClose;

			if (phase == TrendPhase.Mature)
				return ActionAdvice.HoldCaution;

			if (entryAbs >= 70 && (phase == TrendPhase.Forming || phase == TrendPhase.Active))
				return ActionAdvice.EntryOk;

			if (entryAbs >= 40 && phase == TrendPhase.Forming)
				return ActionAdvice.WatchEntry;

			if (phase == TrendPhase.Active)
				return ActionAdvice.Hold;

			return ActionAdvice.WatchEntry;
		}

		private bool IsPriceMomentumDivergence(bool isLong, double hist0, double hist1)
		{
			if (CurrentBar < 1)
				return false;

			if (isLong)
				return Close[0] >= High[1] && hist0 < hist1 && hist0 > 0;
			return Close[0] <= Low[1] && hist0 > hist1 && hist0 < 0;
		}

		private void DrawPhaseLabel(TrendDirection direction, TrendPhase phase, double phasePlot)
		{
			string label;
			switch (phase)
			{
				case TrendPhase.Forming: label = "FORM"; break;
				case TrendPhase.Active:  label = "ACT";  break;
				case TrendPhase.Mature:  label = "MAT";  break;
				case TrendPhase.Fading:  label = "FADE"; break;
				default: return;
			}

			double y = phasePlot >= 0 ? phasePlot + 4 : phasePlot - 4;
			Brush brush = GetPhaseBrush(direction, phase, Math.Abs(phasePlot));
			Draw.Text(this, "MES500TDB_Ph_" + CurrentBar, label, 0, y, brush);
		}

		private void DrawActionPanel(
			ActionAdvice advice,
			TrendDirection direction,
			TrendPhase phase,
			double strength,
			double entryAbs,
			double exitPressure,
			MarketRegime regime,
			KcZone kcZone,
			bool fullSqueeze,
			bool partialSqueeze,
			int appLong,
			int appShort)
		{
			string actionLine = GetActionText(advice, direction);
			Brush actionBrush = GetActionBrush(advice, direction);

			string dirText = direction == TrendDirection.Long ? "NÁKUP ↑"
				: direction == TrendDirection.Short ? "PRODEJ ↓"
				: "—";

			string detailLine = phase + " · " + dirText + " · síla " + strength.ToString("0")
				+ "/100 · vstup " + entryAbs.ToString("0") + "% · exit " + exitPressure.ToString("0") + "%";

			string contextLine = GetRegimeText(regime) + " · " + GetKcZoneText(kcZone)
				+ " · " + GetSqueezeText(fullSqueeze, partialSqueeze);

			string approachLine = "Approach BUY " + appLong + "/7 · SELL " + appShort + "/7";

			string fullText = actionLine + "\n" + detailLine + "\n" + contextLine + "\n" + approachLine;

			Draw.TextFixed(this, "MES500TDB_Action", fullText,
				TextPosition.BottomLeft, actionBrush, actionFont,
				Brushes.Black, Brushes.Transparent, 0);
		}

		private static string GetActionText(ActionAdvice advice, TrendDirection direction)
		{
			string side = direction == TrendDirection.Long ? "NÁKUP"
				: direction == TrendDirection.Short ? "PRODEJ" : string.Empty;

			switch (advice)
			{
				case ActionAdvice.WaitSqueeze:    return "⏸ ČEKEJ — squeeze FULL";
				case ActionAdvice.NoTradeTangle:  return "⛔ NEVSTUPOVAT — tangle";
				case ActionAdvice.WaitFlat:       return "⏸ Čekej — bez trendu";
				case ActionAdvice.WatchEntry:     return "👁 Sleduj — trend začíná" + (side.Length > 0 ? " (" + side + ")" : string.Empty);
				case ActionAdvice.EntryOk:        return "🟢 VSTUP OK — " + side;
				case ActionAdvice.Hold:           return "🔵 DRŽ — " + side;
				case ActionAdvice.HoldCaution:    return "🟡 DRŽ — trend slábne (" + side + ")";
				case ActionAdvice.ConsiderClose:  return "⚠ ZVAŽ ZAVŘENÍ — " + side;
				case ActionAdvice.CloseNow:       return "🔴 ZAVŘÍT — " + side;
				default:                          return "—";
			}
		}

		private Brush GetActionBrush(ActionAdvice advice, TrendDirection direction)
		{
			switch (advice)
			{
				case ActionAdvice.EntryOk:
					return direction == TrendDirection.Long ? brushBullStrong : brushBearStrong;
				case ActionAdvice.Hold:
					return direction == TrendDirection.Long ? brushBullWeak : brushBearWeak;
				case ActionAdvice.HoldCaution:
				case ActionAdvice.WatchEntry:
					return brushForming;
				case ActionAdvice.ConsiderClose:
					return brushExitWarn;
				case ActionAdvice.CloseNow:
					return brushExitStrong;
				case ActionAdvice.NoTradeTangle:
				case ActionAdvice.WaitSqueeze:
					return brushForming;
				default:
					return brushNeutral;
			}
		}

		private static string GetRegimeText(MarketRegime regime)
		{
			switch (regime)
			{
				case MarketRegime.TrendUp:     return "Režim: TREND ↑";
				case MarketRegime.TrendDown:   return "Režim: TREND ↓";
				case MarketRegime.Chop:        return "Režim: CHOP";
				case MarketRegime.SqueezeWait: return "Režim: SQUEEZE";
				default:                       return "Režim: FLAT";
			}
		}

		private static string GetKcZoneText(KcZone zone)
		{
			switch (zone)
			{
				case KcZone.UpperBand: return "Zóna: horní pásmo (reversal SELL?)";
				case KcZone.LowerBand: return "Zóna: dolní pásmo (reversal BUY?)";
				case KcZone.MidTrend:  return "Zóna: KC mid (trend leg)";
				default:               return "Zóna: neutrální";
			}
		}

		private static string GetSqueezeText(bool full, bool partial)
		{
			if (full) return "Squeeze: FULL";
			if (partial) return "Squeeze: partial";
			return "Squeeze: off";
		}
		{
			double abs = Math.Abs(entrySigned);
			if (abs >= 70)
				return entrySigned > 0 ? brushEntryGood : brushBearStrong;
			if (abs >= 40)
				return brushEntryMid;
			return brushEntryLow;
		}

		private double GetEntryScore(
			TrendDirection direction,
			TrendPhase phase,
			double displayStrength,
			int slopeCount,
			int approachRaw,
			bool macdTangle,
			bool partialSqueeze,
			bool fullSqueeze)
		{
			if (direction == TrendDirection.None || fullSqueeze || displayStrength < 10)
				return 0;

			double score = displayStrength * 0.45;

			switch (phase)
			{
				case TrendPhase.Active:
					score += 25;
					break;
				case TrendPhase.Forming:
					score += 10;
					break;
				case TrendPhase.Mature:
					score += 4;
					break;
				case TrendPhase.Fading:
					return Math.Max(0, displayStrength * 0.15);
			}

			score += Math.Min(4, slopeCount) * 3;
			score += approachRaw * (12.0 / MAX_APPROACH);

			if (!macdTangle)
				score += 6;

			if (!partialSqueeze)
				score += 3;

			return Math.Min(100, Math.Max(0, score));
		}

		private Brush GetPhaseBrush(TrendDirection direction, TrendPhase phase, double strength)
		{
			if (phase == TrendPhase.Flat || direction == TrendDirection.None || strength < 10)
				return brushNeutral;

			switch (phase)
			{
				case TrendPhase.Forming:
					return brushForming;
				case TrendPhase.Active:
					if (strength >= 70)
						return direction == TrendDirection.Long ? brushBullStrong : brushBearStrong;
					return direction == TrendDirection.Long ? brushBullWeak : brushBearWeak;
				case TrendPhase.Mature:
					return direction == TrendDirection.Long ? brushBullWeak : brushBearWeak;
				case TrendPhase.Fading:
					return brushFading;
				default:
					return brushNeutral;
			}
		}

		private void DrawTrendStartArrow(
			TrendDirection priorDirection,
			TrendPhase priorPhase,
			TrendDirection direction,
			TrendPhase phase,
			double phasePlot)
		{
			bool wasFlat = priorDirection == TrendDirection.None || priorPhase == TrendPhase.Flat;
			if (!wasFlat || (phase != TrendPhase.Forming && phase != TrendPhase.Active))
				return;

			if (direction == TrendDirection.Long)
			{
				double y = Math.Max(35, Math.Abs(phasePlot) + 5);
				Draw.ArrowUp(this, "MES500TDB_TUp_" + CurrentBar, false, 0, y, Brushes.LimeGreen);
			}
			else if (direction == TrendDirection.Short)
			{
				double y = Math.Min(-35, phasePlot - 5);
				Draw.ArrowDown(this, "MES500TDB_TDn_" + CurrentBar, false, 0, y, Brushes.OrangeRed);
			}
		}

		private void DrawPeakMarker(bool isLong, TrendPhase newPhase)
		{
			int lookback = Math.Max(3, Math.Min(10, PeakLookbackBars));
			int bestBarsAgo = 0;
			double bestAbs = -1;

			for (int i = 1; i <= lookback && CurrentBar >= i; i++)
			{
				double score = entryScoreHistory[i];
				double str = strengthHistory[i];
				bool sameSide = isLong ? score > 0 : score < 0;
				if (!sameSide)
					continue;

				double metric = Math.Abs(score) + str * 0.5;
				if (metric > bestAbs)
				{
					bestAbs = metric;
					bestBarsAgo = i;
				}
			}

			if (bestAbs < 0)
				return;

			string tag = "MES500TDB_Peak_" + (CurrentBar - bestBarsAgo);
			double entryAtPeak = entryScoreHistory[bestBarsAgo];
			double strAtPeak   = strengthHistory[bestBarsAgo];
			double phaseAtPeak = Values[0][bestBarsAgo];

			if (isLong)
			{
				double y = Math.Max(Math.Max(Math.Abs(entryAtPeak), Math.Abs(phaseAtPeak)), strAtPeak) + 8;
				Draw.ArrowDown(this, tag + "_Arr", false, bestBarsAgo, y, Brushes.Gold);
				Draw.Text(this, tag + "_Txt", "PEAK", bestBarsAgo, y + 6, Brushes.Gold);
			}
			else
			{
				double y = Math.Min(entryAtPeak, phaseAtPeak) - 8;
				Draw.ArrowUp(this, tag + "_Arr", false, bestBarsAgo, y, Brushes.Gold);
				Draw.Text(this, tag + "_Txt", "PEAK", bestBarsAgo, y - 6, Brushes.Gold);
			}
		}

		private int CountKcSlopeBars(bool isLong)
		{
			int count = 0;
			for (int i = 0; i < 8 && i + 1 <= CurrentBar; i++)
			{
				if (isLong)
				{
					if (kcEmaInd[i] > kcEmaInd[i + 1])
						count++;
					else
						break;
				}
				else
				{
					if (kcEmaInd[i] < kcEmaInd[i + 1])
						count++;
					else
						break;
				}
			}
			return count;
		}

		private int CountMomentumFadeBars(bool isLong)
		{
			int barsToCheck = Math.Max(MomentumFadeBars, 2);
			int count = 0;

			for (int i = 0; i < barsToCheck && CurrentBar >= i + 1; i++)
			{
				double h0 = macdInd.Diff[i];
				double h1 = macdInd.Diff[i + 1];
				if (isLong)
				{
					if (h0 < h1 || h0 <= 0)
						count++;
					else
						break;
				}
				else
				{
					if (h0 > h1 || h0 >= 0)
						count++;
					else
						break;
				}
			}

			return count;
		}

		private int CountApproachScore(
			bool isLong,
			bool fullSqueeze,
			bool macdTangle,
			bool exhaustion,
			int slopeCount,
			double kcMid,
			double kcUpper,
			double kcLower,
			double near,
			double hist0,
			double hist1,
			double hist2)
		{
			int score = 0;
			if (!fullSqueeze)
				score++;

			if (isLong)
			{
				if (hist0 > 0) score++;
				if (RequireThreeBarMomentum ? hist0 > hist1 && hist1 > hist2 : hist0 > hist1) score++;
				if (!exhaustion) score++;
				if (slopeCount >= 1) score++;
				if (Close[0] > kcUpper - near || Close[0] > kcMid - near) score++;
			}
			else
			{
				if (hist0 < 0) score++;
				if (RequireThreeBarMomentum ? hist0 < hist1 && hist1 < hist2 : hist0 < hist1) score++;
				if (!exhaustion) score++;
				if (slopeCount >= 1) score++;
				if (Close[0] < kcLower + near || Close[0] < kcMid + near) score++;
			}

			if (!macdTangle)
				score++;

			return score;
		}

		private bool IsLongExhaustion(double h0, double h1, double h2, double h3)
		{
			if (h0 <= 0) return false;
			if (h1 > h0 && h1 >= h2 && h1 >= h3) return true;
			if (h0 > h1 && h1 > h2)
			{
				double priorStep   = h1 - h2;
				double currentStep = h0 - h1;
				return currentStep < priorStep * 0.5;
			}
			return false;
		}

		private bool IsShortExhaustion(double h0, double h1, double h2, double h3)
		{
			if (h0 >= 0) return false;
			if (h1 < h0 && h1 <= h2 && h1 <= h3) return true;
			if (h0 < h1 && h1 < h2)
			{
				double priorStep   = h1 - h2;
				double currentStep = h0 - h1;
				return currentStep > priorStep * 0.5;
			}
			return false;
		}

		private bool IsMacdTangle()
		{
			double separation  = Math.Abs(macdInd[0] - macdInd.Avg[0]);
			double macdSlope   = macdInd[0]     - macdInd[1];
			double signalSlope = macdInd.Avg[0] - macdInd.Avg[1];
			double sepThr      = TangleSeparationTicks * TickSize;
			double slopeThr    = TangleSlopeTicks * TickSize;
			return separation <= sepThr
				&& Math.Abs(macdSlope) <= slopeThr
				&& Math.Abs(signalSlope) <= slopeThr;
		}

		[NinjaScriptProperty]
		[Range(5, 50)]
		[Display(Name = "BB Period", Order = 1, GroupName = "1. Bollinger Bands")]
		public int BbPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1.0, 4.0)]
		[Display(Name = "BB Std Dev", Order = 2, GroupName = "1. Bollinger Bands")]
		public double BbStdDev { get; set; }

		[NinjaScriptProperty]
		[Range(5, 50)]
		[Display(Name = "KC Period", Order = 1, GroupName = "2. Keltner Channel")]
		public int KcPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.5, 4.0)]
		[Display(Name = "KC Multiplier", Order = 2, GroupName = "2. Keltner Channel")]
		public double KcMultiplier { get; set; }

		[NinjaScriptProperty]
		[Range(1, 30)]
		[Display(Name = "MACD Fast", Order = 1, GroupName = "3. MACD")]
		public int MacdFast { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "MACD Slow", Order = 2, GroupName = "3. MACD")]
		public int MacdSlow { get; set; }

		[NinjaScriptProperty]
		[Range(1, 30)]
		[Display(Name = "MACD Signal", Order = 3, GroupName = "3. MACD")]
		public int MacdSignal { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Entry Buffer Ticks", Order = 1, GroupName = "4. Filtry")]
		public int EntryBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Range(2, 30)]
		[Display(Name = "Approach Near Ticks", Order = 2, GroupName = "4. Filtry")]
		public int ApproachNearTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require 3-bar Momentum", Order = 3, GroupName = "4. Filtry")]
		public bool RequireThreeBarMomentum { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Tangle Separation Ticks", Order = 4, GroupName = "4. Filtry")]
		public int TangleSeparationTicks { get; set; } = 4;

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Tangle Slope Ticks", Order = 5, GroupName = "4. Filtry")]
		public int TangleSlopeTicks { get; set; } = 1;

		[NinjaScriptProperty]
		[Display(Name = "Show Action Panel", Description = "Akční panel: Čekej / Vstup OK / Drž / Zavři.", Order = 1, GroupName = "5. Zobrazení")]
		public bool ShowStatusText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Phase Labels", Description = "FORM/ACT/MAT/FADE na sloupci při změně fáze.", Order = 2, GroupName = "5. Zobrazení")]
		public bool ShowPhaseLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Trend Start Arrows", Order = 3, GroupName = "5. Zobrazení")]
		public bool ShowTrendStartArrows { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Peak Markers", Order = 4, GroupName = "5. Zobrazení")]
		public bool ShowPeakMarkers { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "Momentum Fade Bars", Order = 1, GroupName = "6. Trend fáze")]
		public int MomentumFadeBars { get; set; }

		[NinjaScriptProperty]
		[Range(3, 10)]
		[Display(Name = "Peak Lookback Bars", Order = 2, GroupName = "6. Trend fáze")]
		public int PeakLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Range(30, 90)]
		[Display(Name = "Exit Close Threshold", Description = "Exit % pro 'Zvaž zavření'.", Order = 1, GroupName = "7. Exit pravidla")]
		public int ExitCloseThreshold { get; set; }

		[NinjaScriptProperty]
		[Range(40, 100)]
		[Display(Name = "Exit Strong Threshold", Description = "Exit % pro 'Zavřít'.", Order = 2, GroupName = "7. Exit pravidla")]
		public int ExitStrongThreshold { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> TrendStrengthPlot => Values[0];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> EntryScorePlot => Values[1];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ExitPressurePlot => Values[2];
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MES500TDashboard[] cacheMES500TDashboard;
		public MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			return MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showPhaseLabels, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars, exitCloseThreshold, exitStrongThreshold);
		}

		public MES500TDashboard MES500TDashboard(ISeries<double> input, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			if (cacheMES500TDashboard != null)
				for (int idx = 0; idx < cacheMES500TDashboard.Length; idx++)
					if (cacheMES500TDashboard[idx] != null && cacheMES500TDashboard[idx].BbPeriod == bbPeriod && cacheMES500TDashboard[idx].BbStdDev == bbStdDev && cacheMES500TDashboard[idx].KcPeriod == kcPeriod && cacheMES500TDashboard[idx].KcMultiplier == kcMultiplier && cacheMES500TDashboard[idx].MacdFast == macdFast && cacheMES500TDashboard[idx].MacdSlow == macdSlow && cacheMES500TDashboard[idx].MacdSignal == macdSignal && cacheMES500TDashboard[idx].EntryBufferTicks == entryBufferTicks && cacheMES500TDashboard[idx].ApproachNearTicks == approachNearTicks && cacheMES500TDashboard[idx].RequireThreeBarMomentum == requireThreeBarMomentum && cacheMES500TDashboard[idx].TangleSeparationTicks == tangleSeparationTicks && cacheMES500TDashboard[idx].TangleSlopeTicks == tangleSlopeTicks && cacheMES500TDashboard[idx].ShowStatusText == showStatusText && cacheMES500TDashboard[idx].ShowPhaseLabels == showPhaseLabels && cacheMES500TDashboard[idx].ShowTrendStartArrows == showTrendStartArrows && cacheMES500TDashboard[idx].ShowPeakMarkers == showPeakMarkers && cacheMES500TDashboard[idx].MomentumFadeBars == momentumFadeBars && cacheMES500TDashboard[idx].PeakLookbackBars == peakLookbackBars && cacheMES500TDashboard[idx].ExitCloseThreshold == exitCloseThreshold && cacheMES500TDashboard[idx].ExitStrongThreshold == exitStrongThreshold && cacheMES500TDashboard[idx].EqualsInput(input))
						return cacheMES500TDashboard[idx];
			return CacheIndicator<MES500TDashboard>(new MES500TDashboard(){ BbPeriod = bbPeriod, BbStdDev = bbStdDev, KcPeriod = kcPeriod, KcMultiplier = kcMultiplier, MacdFast = macdFast, MacdSlow = macdSlow, MacdSignal = macdSignal, EntryBufferTicks = entryBufferTicks, ApproachNearTicks = approachNearTicks, RequireThreeBarMomentum = requireThreeBarMomentum, TangleSeparationTicks = tangleSeparationTicks, TangleSlopeTicks = tangleSlopeTicks, ShowStatusText = showStatusText, ShowPhaseLabels = showPhaseLabels, ShowTrendStartArrows = showTrendStartArrows, ShowPeakMarkers = showPeakMarkers, MomentumFadeBars = momentumFadeBars, PeakLookbackBars = peakLookbackBars, ExitCloseThreshold = exitCloseThreshold, ExitStrongThreshold = exitStrongThreshold }, input, ref cacheMES500TDashboard);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showPhaseLabels, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars, exitCloseThreshold, exitStrongThreshold);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showPhaseLabels, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars, exitCloseThreshold, exitStrongThreshold);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showPhaseLabels, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars, exitCloseThreshold, exitStrongThreshold);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showPhaseLabels, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars, int exitCloseThreshold, int exitStrongThreshold)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showPhaseLabels, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars, exitCloseThreshold, exitStrongThreshold);
		}
	}
}

#endregion
