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
	/// TrendPhase sloupec + signed EntryScore linka + PEAK markery.
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
		private Brush brushZero;
		private SimpleFont labelFont;
		private SimpleFont markerFont;

		private TrendPhase priorTrendPhase;
		private TrendDirection priorTrendDirection;
		private TrendDirection lockedDirection;

		private Series<double> entryScoreHistory;

		// Plot 0: TrendPhase — signed bar (nad 0 = BUY trend, pod 0 = SELL trend, jen jeden směr)
		// Plot 1: EntryScore — signed line (+ = BUY entry, − = SELL entry)
		// Plot 2: Zero line

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500T Dashboard — jeden trend (BUY/SELL), fáze vývoje, signed Entry Score, PEAK.";
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
				ShowTrendStartArrows      = true;
				ShowPeakMarkers           = true;
				MomentumFadeBars          = 2;
				PeakLookbackBars          = 5;

				AddPlot(new Stroke(Brushes.LimeGreen, 2), PlotStyle.Bar,  "TrendPhase");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "EntryScore");
				AddPlot(new Stroke(Brushes.Gray,       1), PlotStyle.Line, "Nula");

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
				brushZero       = new SolidColorBrush(Color.FromRgb(60, 60, 60));
				brushZero.Freeze();

				labelFont  = new SimpleFont("Arial", 9);
				markerFont = new SimpleFont("Arial", 8) { Bold = true };
			}
			else if (State == State.DataLoaded)
			{
				entryScoreHistory   = new Series<double>(this);
				priorTrendPhase     = TrendPhase.Flat;
				priorTrendDirection = TrendDirection.None;
				lockedDirection     = TrendDirection.None;
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

			TrendPhase bullRaw = GetRawTrendPhase(
				true, priorTrendDirection == TrendDirection.Long ? priorTrendPhase : TrendPhase.Flat,
				fullSqueeze, macdTangle, bullSlopeCount, hist0, hist1, longExhaustion, longFadeBars);

			TrendPhase bearRaw = GetRawTrendPhase(
				false, priorTrendDirection == TrendDirection.Short ? priorTrendPhase : TrendPhase.Flat,
				fullSqueeze, macdTangle, bearSlopeCount, hist0, hist1, shortExhaustion, shortFadeBars);

			TrendDirection direction = SelectLockedDirection(
				bullRaw, bearRaw, hist0, bullSlopeCount, bearSlopeCount);

			TrendPhase rawPhase = direction == TrendDirection.Long ? bullRaw
				: direction == TrendDirection.Short ? bearRaw
				: TrendPhase.Flat;

			TrendPhase priorPhase = direction == priorTrendDirection ? priorTrendPhase : TrendPhase.Flat;
			TrendPhase phase = AdvancePhase(priorPhase, rawPhase);

			int activeSlope = direction == TrendDirection.Long ? bullSlopeCount : bearSlopeCount;
			int activeApproach = direction == TrendDirection.Long ? approachLongRaw : approachShortRaw;

			double entryMagnitude = GetEntryScore(
				direction, phase, activeSlope, activeApproach,
				macdTangle, partialSqueeze, fullSqueeze);

			double entrySigned = direction == TrendDirection.Long ? entryMagnitude
				: direction == TrendDirection.Short ? -entryMagnitude
				: 0;

			entryScoreHistory[0] = entrySigned;

			double phaseHeight = GetPhaseHeight(phase);
			double phasePlot = direction == TrendDirection.Short ? -phaseHeight
				: direction == TrendDirection.Long ? phaseHeight
				: 0;

			Values[0][0] = phasePlot;
			Values[1][0] = entrySigned;
			Values[2][0] = 0;

			PlotBrushes[0][0] = GetPhaseBrush(direction, phase);
			PlotBrushes[1][0] = GetEntryBrush(entrySigned);
			PlotBrushes[2][0] = brushZero;

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
				DrawStatusText(direction, phase, entrySigned, bullSlopeCount, bearSlopeCount,
					approachLongRaw, approachShortRaw, fullSqueeze, partialSqueeze, macdTangle,
					longExhaustion, shortExhaustion);

			priorTrendPhase     = phase;
			priorTrendDirection = direction;
			lockedDirection     = direction;
		}

		private TrendPhase GetRawTrendPhase(
			bool isLong,
			TrendPhase priorPhase,
			bool fullSqueeze,
			bool macdTangle,
			int slopeCount,
			double hist0,
			double hist1,
			bool exhaustion,
			int fadeBars)
		{
			if (fullSqueeze || macdTangle)
				return TrendPhase.Flat;

			if (isLong)
			{
				if (slopeCount == 0 && hist0 <= 0)
					return TrendPhase.Flat;

				if (fadeBars >= MomentumFadeBars || (hist0 < 0 && priorPhase >= TrendPhase.Active))
					return TrendPhase.Fading;

				if (slopeCount >= 2 && hist0 > 0 && (hist0 < hist1 || exhaustion))
					return TrendPhase.Mature;

				if (slopeCount >= 3 && hist0 > 0 && hist0 > hist1 && !exhaustion)
					return TrendPhase.Active;

				if (slopeCount >= 1 && hist0 > 0 && hist0 > hist1 && !exhaustion)
					return TrendPhase.Forming;

				return TrendPhase.Flat;
			}

			if (slopeCount == 0 && hist0 >= 0)
				return TrendPhase.Flat;

			if (fadeBars >= MomentumFadeBars || (hist0 > 0 && priorPhase >= TrendPhase.Active))
				return TrendPhase.Fading;

			if (slopeCount >= 2 && hist0 < 0 && (hist0 > hist1 || exhaustion))
				return TrendPhase.Mature;

			if (slopeCount >= 3 && hist0 < 0 && hist0 < hist1 && !exhaustion)
				return TrendPhase.Active;

			if (slopeCount >= 1 && hist0 < 0 && hist0 < hist1 && !exhaustion)
				return TrendPhase.Forming;

			return TrendPhase.Flat;
		}

		private TrendPhase AdvancePhase(TrendPhase prior, TrendPhase candidate)
		{
			if (candidate == TrendPhase.Flat)
				return TrendPhase.Flat;

			if (prior == TrendPhase.Flat)
				return candidate;

			int priorRank    = (int)prior;
			int candidateRank = (int)candidate;

			if (candidateRank >= priorRank)
				return candidate;

			if (prior == TrendPhase.Active && (candidate == TrendPhase.Mature || candidate == TrendPhase.Fading))
				return candidate;

			if (prior == TrendPhase.Mature && candidate == TrendPhase.Fading)
				return TrendPhase.Fading;

			return prior;
		}

		private TrendDirection SelectLockedDirection(
			TrendPhase bullRaw,
			TrendPhase bearRaw,
			double hist0,
			int bullSlope,
			int bearSlope)
		{
			int bullRank = (int)bullRaw;
			int bearRank = (int)bearRaw;

			if (bullRank == 0 && bearRank == 0)
				return TrendDirection.None;

			if (lockedDirection == TrendDirection.Long && bullRank > 0)
			{
				if (bearRank >= (int)TrendPhase.Active && bullRank <= (int)TrendPhase.Mature)
					return TrendDirection.Short;
				if (bullRank == 0 && bearRank >= (int)TrendPhase.Forming)
					return TrendDirection.Short;
				return TrendDirection.Long;
			}

			if (lockedDirection == TrendDirection.Short && bearRank > 0)
			{
				if (bullRank >= (int)TrendPhase.Active && bearRank <= (int)TrendPhase.Mature)
					return TrendDirection.Long;
				if (bearRank == 0 && bullRank >= (int)TrendPhase.Forming)
					return TrendDirection.Long;
				return TrendDirection.Short;
			}

			if (bullRank != bearRank)
				return bullRank > bearRank ? TrendDirection.Long : TrendDirection.Short;

			if (hist0 > 0)
				return TrendDirection.Long;
			if (hist0 < 0)
				return TrendDirection.Short;
			if (bullSlope > bearSlope)
				return TrendDirection.Long;
			if (bearSlope > bullSlope)
				return TrendDirection.Short;

			return TrendDirection.None;
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

		private double GetEntryScore(
			TrendDirection direction,
			TrendPhase phase,
			int slopeCount,
			int approachRaw,
			bool macdTangle,
			bool partialSqueeze,
			bool fullSqueeze)
		{
			if (direction == TrendDirection.None || fullSqueeze)
				return 0;

			if (phase != TrendPhase.Forming && phase != TrendPhase.Active)
				return 0;

			double score = 0;
			if (phase == TrendPhase.Active)
				score += 50;
			else if (phase == TrendPhase.Forming)
				score += 20;

			score += Math.Min(4, slopeCount) * 5;
			score += approachRaw * (20.0 / MAX_APPROACH);

			if (!macdTangle)
				score += 8;

			if (!partialSqueeze)
				score += 4;

			return Math.Min(100, score);
		}

		private static double GetPhaseHeight(TrendPhase phase)
		{
			switch (phase)
			{
				case TrendPhase.Forming: return 30;
				case TrendPhase.Active:  return 85;
				case TrendPhase.Mature:  return 50;
				case TrendPhase.Fading:  return 20;
				default:                 return 0;
			}
		}

		private Brush GetPhaseBrush(TrendDirection direction, TrendPhase phase)
		{
			if (phase == TrendPhase.Flat || direction == TrendDirection.None)
				return brushNeutral;

			switch (phase)
			{
				case TrendPhase.Forming:
					return brushForming;
				case TrendPhase.Active:
					return direction == TrendDirection.Long ? brushBullStrong : brushBearStrong;
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
				bool sameSide = isLong ? score > 0 : score < 0;
				if (!sameSide)
					continue;

				double abs = Math.Abs(score);
				if (abs > bestAbs)
				{
					bestAbs = abs;
					bestBarsAgo = i;
				}
			}

			if (bestAbs < 0)
				return;

			string tag = "MES500TDB_Peak_" + (CurrentBar - bestBarsAgo);
			double entryAtPeak = entryScoreHistory[bestBarsAgo];
			double phaseAtPeak = Values[0][bestBarsAgo];

			if (isLong)
			{
				double y = Math.Max(entryAtPeak, Math.Abs(phaseAtPeak)) + 8;
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

		private void DrawStatusText(
			TrendDirection direction,
			TrendPhase phase,
			double entryScore,
			int bullSlope,
			int bearSlope,
			int appLong,
			int appShort,
			bool fullSqueeze,
			bool partialSqueeze,
			bool tangle,
			bool longExh,
			bool shortExh)
		{
			string phaseText = GetPhaseText(direction, phase);
			double entryAbs = Math.Abs(entryScore);
			string entryText = direction == TrendDirection.Long
				? "Entry BUY: +" + entryAbs.ToString("0") + "%"
				: direction == TrendDirection.Short
					? "Entry SELL: -" + entryAbs.ToString("0") + "%"
					: "Entry Score: 0";
			entryText += entryAbs >= 70 ? " — vhodné vstoupit"
				: entryAbs >= 40 ? " — slabší setup"
				: direction != TrendDirection.None ? " — nevhodné" : string.Empty;

			string sqzText;
			if (fullSqueeze)
				sqzText = "Squeeze: FULL — čekej";
			else if (partialSqueeze)
				sqzText = "Squeeze: partial";
			else
				sqzText = "Squeeze: off";

			string warnText = string.Empty;
			if (tangle)
				warnText = "TANGLE — nevstupovat";
			else if (phase == TrendPhase.Fading)
				warnText = "Trend slábne — zvaž zavření";
			else if (phase == TrendPhase.Mature)
				warnText = "Trend zralý — momentum slábne";
			else if (longExh && direction == TrendDirection.Long)
				warnText = "Long EXHAUST";
			else if (shortExh && direction == TrendDirection.Short)
				warnText = "Short EXHAUST";

			string slopeText = "KC slope: bull " + bullSlope + " / bear " + bearSlope;
			string appText = "Approach: BUY " + appLong + "/7 · SELL " + appShort + "/7";

			string fullText = phaseText + "\n" + entryText + "\n" + sqzText;
			if (warnText.Length > 0)
				fullText += "\n" + warnText;
			fullText += "\n" + slopeText + "\n" + appText;

			Brush textBrush = direction == TrendDirection.Long ? brushBullStrong
				: direction == TrendDirection.Short ? brushBearStrong
				: brushNeutral;

			Draw.TextFixed(this, "MES500TDB_Status", fullText,
				TextPosition.BottomLeft, textBrush, labelFont,
				Brushes.Black, Brushes.Transparent, 0);
		}

		private static string GetPhaseText(TrendDirection direction, TrendPhase phase)
		{
			string dir = direction == TrendDirection.Long ? "NÁKUP"
				: direction == TrendDirection.Short ? "PRODEJ"
				: "—";

			switch (phase)
			{
				case TrendPhase.Forming: return "FORMING ↑ " + dir + " — trend začíná";
				case TrendPhase.Active:  return "ACTIVE ↑ " + dir + " — silný trend";
				case TrendPhase.Mature:  return "MATURE ↑ " + dir + " — trend pokračuje, slábne";
				case TrendPhase.Fading:  return "FADING ↓ " + dir + " — trend končí";
				default:                 return "FLAT — bez trendu";
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
		[Display(Name = "Show Status Text", Order = 1, GroupName = "5. Zobrazení")]
		public bool ShowStatusText { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Trend Start Arrows", Description = "Šipka při FLAT → FORMING/ACTIVE.", Order = 2, GroupName = "5. Zobrazení")]
		public bool ShowTrendStartArrows { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Peak Markers", Description = "PEAK značka při ACTIVE → MATURE/FADING.", Order = 3, GroupName = "5. Zobrazení")]
		public bool ShowPeakMarkers { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "Momentum Fade Bars", Order = 1, GroupName = "6. Trend fáze")]
		public int MomentumFadeBars { get; set; }

		[NinjaScriptProperty]
		[Range(3, 10)]
		[Display(Name = "Peak Lookback Bars", Order = 2, GroupName = "6. Trend fáze")]
		public int PeakLookbackBars { get; set; }

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> TrendPhasePlot => Values[0];

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> EntryScorePlot => Values[1];
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MES500TDashboard[] cacheMES500TDashboard;
		public MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			return MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars);
		}

		public MES500TDashboard MES500TDashboard(ISeries<double> input, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			if (cacheMES500TDashboard != null)
				for (int idx = 0; idx < cacheMES500TDashboard.Length; idx++)
					if (cacheMES500TDashboard[idx] != null && cacheMES500TDashboard[idx].BbPeriod == bbPeriod && cacheMES500TDashboard[idx].BbStdDev == bbStdDev && cacheMES500TDashboard[idx].KcPeriod == kcPeriod && cacheMES500TDashboard[idx].KcMultiplier == kcMultiplier && cacheMES500TDashboard[idx].MacdFast == macdFast && cacheMES500TDashboard[idx].MacdSlow == macdSlow && cacheMES500TDashboard[idx].MacdSignal == macdSignal && cacheMES500TDashboard[idx].EntryBufferTicks == entryBufferTicks && cacheMES500TDashboard[idx].ApproachNearTicks == approachNearTicks && cacheMES500TDashboard[idx].RequireThreeBarMomentum == requireThreeBarMomentum && cacheMES500TDashboard[idx].TangleSeparationTicks == tangleSeparationTicks && cacheMES500TDashboard[idx].TangleSlopeTicks == tangleSlopeTicks && cacheMES500TDashboard[idx].ShowStatusText == showStatusText && cacheMES500TDashboard[idx].ShowTrendStartArrows == showTrendStartArrows && cacheMES500TDashboard[idx].ShowPeakMarkers == showPeakMarkers && cacheMES500TDashboard[idx].MomentumFadeBars == momentumFadeBars && cacheMES500TDashboard[idx].PeakLookbackBars == peakLookbackBars && cacheMES500TDashboard[idx].EqualsInput(input))
						return cacheMES500TDashboard[idx];
			return CacheIndicator<MES500TDashboard>(new MES500TDashboard(){ BbPeriod = bbPeriod, BbStdDev = bbStdDev, KcPeriod = kcPeriod, KcMultiplier = kcMultiplier, MacdFast = macdFast, MacdSlow = macdSlow, MacdSignal = macdSignal, EntryBufferTicks = entryBufferTicks, ApproachNearTicks = approachNearTicks, RequireThreeBarMomentum = requireThreeBarMomentum, TangleSeparationTicks = tangleSeparationTicks, TangleSlopeTicks = tangleSlopeTicks, ShowStatusText = showStatusText, ShowTrendStartArrows = showTrendStartArrows, ShowPeakMarkers = showPeakMarkers, MomentumFadeBars = momentumFadeBars, PeakLookbackBars = peakLookbackBars }, input, ref cacheMES500TDashboard);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText, bool showTrendStartArrows, bool showPeakMarkers, int momentumFadeBars, int peakLookbackBars)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText, showTrendStartArrows, showPeakMarkers, momentumFadeBars, peakLookbackBars);
		}
	}
}

#endregion
