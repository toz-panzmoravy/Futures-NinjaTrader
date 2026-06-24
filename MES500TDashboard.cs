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
	/// MES500T Dashboard — sub-panel pod grafem.
	/// Zobrazuje v 7 pruzích: sílu nákupního trendu, sílu prodejního trendu,
	/// MACD histogram (momentum), squeeze komprese, approach skóre (BUY/SELL),
	/// exit korekce a jistotu exitu — vše odvozeno ze stejných parametrů jako V39.
	/// </summary>
	public class MES500TDashboard : Indicator
	{
		// ── indikátory ──────────────────────────────────────────────────────────
		private EMA    bbEmaInd;
		private StdDev bbStdDevInd;
		private EMA    kcEmaInd;
		private ATR    kcAtrInd;
		private MACD   macdInd;

		// ── konstanty pásem ─────────────────────────────────────────────────────
		private const int MAX_SCORE    = 7;   // max approach score ve V39
		private const int HIST_SCALE   = 40;  // body pro normalizaci histogramu → ±100

		// ── barvy pruhů ─────────────────────────────────────────────────────────
		private Brush brushBullStrong;
		private Brush brushBullWeak;
		private Brush brushBearStrong;
		private Brush brushBearWeak;
		private Brush brushNeutral;
		private Brush brushSqueeze;
		private Brush brushExitWarn;
		private Brush brushZero;
		private SimpleFont labelFont;

		// ────────────────────────────────────────────────────────────────────────
		//  PLOTS — pořadí musí souhlasit s AddPlot() v OnStateChange
		// ────────────────────────────────────────────────────────────────────────
		// 0  TrendSilaBuy    — síla nákupního trendu  0‥100
		// 1  TrendSilaSell   — síla prodejního trendu 0‥100  (kladná = silný SELL)
		// 2  MomentumHist    — MACD histogram normalizovaný  –100‥+100
		// 3  SqueezeLine     — komprese BB/KC  0 = off, 50 = partial, 100 = full
		// 4  ApproachBuy     — approach skóre BUY   0‥100
		// 5  ApproachSell    — approach skóre SELL  0‥100 (kladná)
		// 6  Nulová linka    — stálá 0

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500T Dashboard — sub-panel: síla trendu, momentum, squeeze, approach skóre.";
				Name        = "MES500TDashboard";
				Calculate   = Calculate.OnBarClose;
				IsOverlay   = false;    // ← vlastní sub-panel
				DisplayInDataBox    = true;
				DrawOnPricePanel    = false;
				IsSuspendedWhileInactive = true;
				BarsRequiredToPlot  = 25;

				// parametry pásem — musí odpovídat V39
				BbPeriod       = 20;
				BbStdDev       = 2.0;
				KcPeriod       = 20;
				KcMultiplier   = 1.5;
				MacdFast       = 6;
				MacdSlow       = 13;
				MacdSignal     = 9;

			EntryBufferTicks      = 1;
			ApproachNearTicks     = 10;
			RequireThreeBarMomentum = false;

			AddPlot(new Stroke(Brushes.LimeGreen,    2), PlotStyle.Bar,    "SílaNákup");
				AddPlot(new Stroke(Brushes.OrangeRed,    2), PlotStyle.Bar,    "SílaProdej");
				AddPlot(new Stroke(Brushes.DodgerBlue,   1), PlotStyle.Bar,    "Momentum");
				AddPlot(new Stroke(Brushes.Gold,         2), PlotStyle.Bar,    "Squeeze");
				AddPlot(new Stroke(Brushes.SpringGreen,  1), PlotStyle.Line,   "ApproachBuy");
				AddPlot(new Stroke(Brushes.Tomato,       1), PlotStyle.Line,   "ApproachSell");
				AddPlot(new Stroke(Brushes.Gray,         1), PlotStyle.Line,   "Nula");

			AddLine(new Stroke(Brushes.DimGray,  1), 0,   "Nula");
			AddLine(new Stroke(Brushes.DimGray,  1), 50,  "Střed");
			AddLine(new Stroke(Brushes.DimGray,  1), -50, "StředNeg");
			}
			else if (State == State.Configure)
			{
				bbEmaInd    = EMA(BbPeriod);
				bbStdDevInd = StdDev(BbPeriod);
				kcEmaInd    = EMA(KcPeriod);
				kcAtrInd    = ATR(KcPeriod);
				macdInd     = MACD(MacdFast, MacdSlow, MacdSignal);

				brushBullStrong = new SolidColorBrush(Color.FromRgb(0,  220, 80));
				brushBullStrong.Freeze();
				brushBullWeak   = new SolidColorBrush(Color.FromRgb(0,  140, 50));
				brushBullWeak.Freeze();
				brushBearStrong = new SolidColorBrush(Color.FromRgb(220, 50, 30));
				brushBearStrong.Freeze();
				brushBearWeak   = new SolidColorBrush(Color.FromRgb(140, 40, 20));
				brushBearWeak.Freeze();
				brushNeutral    = new SolidColorBrush(Color.FromRgb(100, 100, 100));
				brushNeutral.Freeze();
				brushSqueeze    = new SolidColorBrush(Color.FromRgb(255, 200, 0));
				brushSqueeze.Freeze();
				brushExitWarn   = new SolidColorBrush(Color.FromRgb(255, 100, 0));
				brushExitWarn.Freeze();
				brushZero       = new SolidColorBrush(Color.FromRgb(60,  60,  60));
				brushZero.Freeze();

				labelFont = new SimpleFont("Arial", 9);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToPlot)
				return;

			// ── pásma ────────────────────────────────────────────────────────────
			double bbMid   = bbEmaInd[0];
			double stdDev  = bbStdDevInd[0];
			double bbUpper = bbMid + BbStdDev * stdDev;
			double bbLower = bbMid - BbStdDev * stdDev;

			double kcMid   = kcEmaInd[0];
			double atr     = kcAtrInd[0];
			double kcUpper = kcMid + KcMultiplier * atr;
			double kcLower = kcMid - KcMultiplier * atr;
			double buf     = EntryBufferTicks * TickSize;
			double near    = ApproachNearTicks * TickSize;

			// ── MACD histogram ───────────────────────────────────────────────────
			double hist0 = macdInd.Diff[0];
			double hist1 = CurrentBar >= 1 ? macdInd.Diff[1] : hist0;
			double hist2 = CurrentBar >= 2 ? macdInd.Diff[2] : hist1;
			double hist3 = CurrentBar >= 3 ? macdInd.Diff[3] : hist2;

			// ── squeeze ──────────────────────────────────────────────────────────
			bool fullSqueeze    = bbUpper <= kcUpper && bbLower >= kcLower;
			bool partialSqueeze = !fullSqueeze && ((bbUpper <= kcUpper && bbUpper >= kcLower)
			                                     || (bbLower >= kcLower && bbLower <= kcUpper));

			// ── KC slope — počet svíček po sobě jdoucích ─────────────────────────
			int bullSlopeCount = 0;
			int bearSlopeCount = 0;
			for (int i = 0; i < 8 && i + 1 <= CurrentBar; i++)
			{
				if (kcEmaInd[i] > kcEmaInd[i + 1])
					bullSlopeCount++;
				else
					break;
			}
			for (int i = 0; i < 8 && i + 1 <= CurrentBar; i++)
			{
				if (kcEmaInd[i] < kcEmaInd[i + 1])
					bearSlopeCount++;
				else
					break;
			}

			// ── momentum síla (exhaustion check) ─────────────────────────────────
			bool longExhaustion  = IsLongExhaustion(hist0, hist1, hist2, hist3);
			bool shortExhaustion = IsShortExhaustion(hist0, hist1, hist2, hist3);
			bool macdTangle      = IsMacdTangle();

			// ── Síla nákupního trendu 0-100 ──────────────────────────────────────
			double buyStrength = 0;
			// KC mid roste → základ trendu
			buyStrength += Math.Min(8, bullSlopeCount) * 8.0;          // max 64
			// cena nad KC mid
			if (Close[0] > kcMid)   buyStrength += 12;
			// MACD kladný
			if (hist0 > 0)           buyStrength += 10;
			// momentum roste
			if (hist0 > hist1)       buyStrength += 8;
			// bez exhauscion
			if (!longExhaustion)     buyStrength += 6;
			// Celkem max cca 100; zkrátíme
			buyStrength = Math.Min(100, buyStrength);
			// Pokud squeeze nebo tangle → snížit
			if (fullSqueeze || macdTangle) buyStrength *= 0.5;
			if (shortExhaustion)           buyStrength *= 0.3;

			// ── Síla prodejního trendu 0-100 (zobrazena záporně) ─────────────────
			double sellStrength = 0;
			sellStrength += Math.Min(8, bearSlopeCount) * 8.0;
			if (Close[0] < kcMid)    sellStrength += 12;
			if (hist0 < 0)            sellStrength += 10;
			if (hist0 < hist1)        sellStrength += 8;
			if (!shortExhaustion)     sellStrength += 6;
			sellStrength = Math.Min(100, sellStrength);
			if (fullSqueeze || macdTangle) sellStrength *= 0.5;
			if (longExhaustion)            sellStrength *= 0.3;

			// ── Normalizovaný MACD histogram ─────────────────────────────────────
			double histNorm = atr > 0
				? Math.Max(-100, Math.Min(100, hist0 / (atr * 0.5) * 100))
				: 0;

			// ── Squeeze hodnota ───────────────────────────────────────────────────
			double squeezeVal = fullSqueeze ? 100 : (partialSqueeze ? 50 : 0);

			// ── Approach skóre BUY ────────────────────────────────────────────────
			int approachBuyRaw = 0;
			if (!fullSqueeze)                                            approachBuyRaw++;
			if (hist0 > 0)                                               approachBuyRaw++;
			if (RequireThreeBarMomentum ? hist0 > hist1 && hist1 > hist2 : hist0 > hist1)
				approachBuyRaw++;
			if (!longExhaustion)                                         approachBuyRaw++;
			if (bullSlopeCount >= 1)                                     approachBuyRaw++;
			if (Close[0] > kcUpper - near || Close[0] > kcMid - near)  approachBuyRaw++;
			if (!macdTangle)                                             approachBuyRaw++;
			double approachBuy = approachBuyRaw * (100.0 / MAX_SCORE);

			// ── Approach skóre SELL (záporné) ────────────────────────────────────
			int approachSellRaw = 0;
			if (!fullSqueeze)                                            approachSellRaw++;
			if (hist0 < 0)                                               approachSellRaw++;
			if (RequireThreeBarMomentum ? hist0 < hist1 && hist1 < hist2 : hist0 < hist1)
				approachSellRaw++;
			if (!shortExhaustion)                                        approachSellRaw++;
			if (bearSlopeCount >= 1)                                     approachSellRaw++;
			if (Close[0] < kcLower + near || Close[0] < kcMid + near)  approachSellRaw++;
			if (!macdTangle)                                             approachSellRaw++;
			double approachSell = -(approachSellRaw * (100.0 / MAX_SCORE));

			// ── zápis do plotů ────────────────────────────────────────────────────
			Values[0][0] = buyStrength;
			Values[1][0] = -sellStrength;           // záporné = pod nulou
			Values[2][0] = histNorm;
			Values[3][0] = squeezeVal;
			Values[4][0] = approachBuy;
			Values[5][0] = approachSell;
			Values[6][0] = 0;

			// ── barvy ─────────────────────────────────────────────────────────────
			PlotBrushes[0][0] = buyStrength  >= 60 ? brushBullStrong : (buyStrength  >= 30 ? brushBullWeak  : brushNeutral);
			PlotBrushes[1][0] = sellStrength >= 60 ? brushBearStrong : (sellStrength >= 30 ? brushBearWeak  : brushNeutral);
			PlotBrushes[2][0] = hist0 > 0
				? (hist0 > hist1 ? brushBullStrong : brushBullWeak)
				: (hist0 < hist1 ? brushBearStrong : brushBearWeak);
			PlotBrushes[3][0] = fullSqueeze ? brushSqueeze : (partialSqueeze ? brushExitWarn : brushZero);
			PlotBrushes[4][0] = approachBuyRaw >= 5 ? brushBullStrong : brushBullWeak;
			PlotBrushes[5][0] = approachSellRaw >= 5 ? brushBearStrong : brushBearWeak;
			PlotBrushes[6][0] = brushZero;

			// ── textový popis aktuálního stavu (vykreslí se na poslední svíčce) ──
			if (ShowStatusText && IsFirstTickOfBar && CurrentBar > 0)
				DrawStatusText(hist0, hist1, buyStrength, sellStrength,
					squeezeVal, approachBuyRaw, approachSellRaw, bullSlopeCount, bearSlopeCount,
					longExhaustion, shortExhaustion, macdTangle);
		}

		// ────────────────────────────────────────────────────────────────────────
		//  Pomocné metody — přepsány ze V39 (bez závislosti na interním stavu)
		// ────────────────────────────────────────────────────────────────────────

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
			double slopeThr    = TangleSlopeTicks       * TickSize;
			return separation  <= sepThr
			    && Math.Abs(macdSlope)   <= slopeThr
			    && Math.Abs(signalSlope) <= slopeThr;
		}

		// ── textové popisy na grafu ────────────────────────────────────────────
		private void DrawStatusText(
			double hist0, double hist1,
			double buyStr, double sellStr,
			double sqz, int appBuy, int appSell,
			int bullSlope, int bearSlope,
			bool longExh, bool shortExh, bool tangle)
		{
			// Trend
			string trendText;
			Brush  trendBrush;
			if (buyStr >= 60 && buyStr > sellStr * 1.3)
			{
				trendText  = "↑ Trend NÁKUP  (" + bullSlope + " sv.)";
				trendBrush = Brushes.LimeGreen;
			}
			else if (sellStr >= 60 && sellStr > buyStr * 1.3)
			{
				trendText  = "↓ Trend PRODEJ  (" + bearSlope + " sv.)";
				trendBrush = Brushes.OrangeRed;
			}
			else
			{
				trendText  = "→ Bez jasného trendu";
				trendBrush = Brushes.Gray;
			}

			// Momentum
			string momText;
			Brush  momBrush;
			if (tangle)
			{
				momText  = "Momentum: TANGLE (nevstupovat)";
				momBrush = Brushes.Gold;
			}
			else if (hist0 > 0 && hist0 > hist1)
			{
				momText  = "Momentum: ↑ roste" + (longExh  ? " · EXHAUST!" : string.Empty);
				momBrush = longExh ? Brushes.Gold : Brushes.LimeGreen;
			}
			else if (hist0 < 0 && hist0 < hist1)
			{
				momText  = "Momentum: ↓ klesá" + (shortExh ? " · EXHAUST!" : string.Empty);
				momBrush = shortExh ? Brushes.Gold : Brushes.OrangeRed;
			}
			else if (hist0 > 0)
			{
				momText  = "Momentum: ↑ slabne" + (longExh  ? " · EXHAUST!" : string.Empty);
				momBrush = longExh ? Brushes.Gold : Brushes.CornflowerBlue;
			}
			else if (hist0 < 0)
			{
				momText  = "Momentum: ↓ slabne" + (shortExh ? " · EXHAUST!" : string.Empty);
				momBrush = shortExh ? Brushes.Gold : Brushes.CornflowerBlue;
			}
			else
			{
				momText  = "Momentum: neutrální";
				momBrush = Brushes.Gray;
			}

			// Squeeze
			string sqzText;
			Brush  sqzBrush;
			if (sqz >= 100)
			{
				sqzText  = "Squeeze: ● FULL — čekej na výstřel";
				sqzBrush = Brushes.Gold;
			}
			else if (sqz >= 50)
			{
				sqzText  = "Squeeze: ○ partial";
				sqzBrush = Brushes.Orange;
			}
			else
			{
				sqzText  = "Squeeze: off";
				sqzBrush = Brushes.Gray;
			}

			// Approach
			string appText;
			Brush  appBrush;
			if (appBuy >= 5)
			{
				appText  = "Approach: ↑ NÁKUP silný  (" + appBuy + "/7)";
				appBrush = Brushes.LimeGreen;
			}
			else if (appSell >= 5)
			{
				appText  = "Approach: ↓ PRODEJ silný  (" + appSell + "/7)";
				appBrush = Brushes.OrangeRed;
			}
			else if (appBuy >= 3)
			{
				appText  = "Approach: ↑ BUY slabý  (" + appBuy + "/7)";
				appBrush = Brushes.CornflowerBlue;
			}
			else if (appSell >= 3)
			{
				appText  = "Approach: ↓ SELL slabý  (" + appSell + "/7)";
				appBrush = Brushes.CornflowerBlue;
			}
			else
			{
				appText  = "Approach: čekání";
				appBrush = Brushes.Gray;
			}

			string fullText = trendText + "\n" + momText + "\n" + sqzText + "\n" + appText;
			Draw.TextFixed(this, "MES500TDB_Status", fullText,
				TextPosition.BottomLeft, trendBrush, labelFont,
				Brushes.Black, Brushes.Transparent, 0);
		}

		// ────────────────────────────────────────────────────────────────────────
		//  PROPERTIES
		// ────────────────────────────────────────────────────────────────────────

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
		[Display(Name = "Entry Buffer Ticks", Description = "Stejné jako ve V39.", Order = 1, GroupName = "4. Filtry")]
		public int EntryBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Range(2, 30)]
		[Display(Name = "Approach Near Ticks", Description = "Stejné jako ve V39.", Order = 2, GroupName = "4. Filtry")]
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
		[Display(Name = "Show Status Text", Description = "Text vpravo dole — aktuální trend / momentum / squeeze / approach.", Order = 1, GroupName = "5. Zobrazení")]
		public bool ShowStatusText { get; set; } = true;

		// ── přístupné série pro jiné indikátory ────────────────────────────────
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SilaNakup    => Values[0];
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SilaProdej   => Values[1];
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Momentum     => Values[2];
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SqueezeValue => Values[3];
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ApproachBuy  => Values[4];
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ApproachSell => Values[5];
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MES500TDashboard[] cacheMES500TDashboard;
		public MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			return MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText);
		}

		public MES500TDashboard MES500TDashboard(ISeries<double> input, int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			if (cacheMES500TDashboard != null)
				for (int idx = 0; idx < cacheMES500TDashboard.Length; idx++)
					if (cacheMES500TDashboard[idx] != null && cacheMES500TDashboard[idx].BbPeriod == bbPeriod && cacheMES500TDashboard[idx].BbStdDev == bbStdDev && cacheMES500TDashboard[idx].KcPeriod == kcPeriod && cacheMES500TDashboard[idx].KcMultiplier == kcMultiplier && cacheMES500TDashboard[idx].MacdFast == macdFast && cacheMES500TDashboard[idx].MacdSlow == macdSlow && cacheMES500TDashboard[idx].MacdSignal == macdSignal && cacheMES500TDashboard[idx].EntryBufferTicks == entryBufferTicks && cacheMES500TDashboard[idx].ApproachNearTicks == approachNearTicks && cacheMES500TDashboard[idx].RequireThreeBarMomentum == requireThreeBarMomentum && cacheMES500TDashboard[idx].TangleSeparationTicks == tangleSeparationTicks && cacheMES500TDashboard[idx].TangleSlopeTicks == tangleSlopeTicks && cacheMES500TDashboard[idx].ShowStatusText == showStatusText && cacheMES500TDashboard[idx].EqualsInput(input))
						return cacheMES500TDashboard[idx];
			return CacheIndicator<MES500TDashboard>(new MES500TDashboard(){ BbPeriod = bbPeriod, BbStdDev = bbStdDev, KcPeriod = kcPeriod, KcMultiplier = kcMultiplier, MacdFast = macdFast, MacdSlow = macdSlow, MacdSignal = macdSignal, EntryBufferTicks = entryBufferTicks, ApproachNearTicks = approachNearTicks, RequireThreeBarMomentum = requireThreeBarMomentum, TangleSeparationTicks = tangleSeparationTicks, TangleSlopeTicks = tangleSlopeTicks, ShowStatusText = showStatusText }, input, ref cacheMES500TDashboard);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MES500TDashboard MES500TDashboard(int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			return indicator.MES500TDashboard(Input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText);
		}

		public Indicators.MES500TDashboard MES500TDashboard(ISeries<double> input , int bbPeriod, double bbStdDev, int kcPeriod, double kcMultiplier, int macdFast, int macdSlow, int macdSignal, int entryBufferTicks, int approachNearTicks, bool requireThreeBarMomentum, int tangleSeparationTicks, int tangleSlopeTicks, bool showStatusText)
		{
			return indicator.MES500TDashboard(input, bbPeriod, bbStdDev, kcPeriod, kcMultiplier, macdFast, macdSlow, macdSignal, entryBufferTicks, approachNearTicks, requireThreeBarMomentum, tangleSeparationTicks, tangleSlopeTicks, showStatusText);
		}
	}
}

#endregion
