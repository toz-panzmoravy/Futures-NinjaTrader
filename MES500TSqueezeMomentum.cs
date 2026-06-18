#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
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
	public class MES500TSqueezeMomentum : Indicator
	{
		private EMA bbEmaInd;
		private StdDev bbStdDevInd;
		private EMA kcEmaInd;
		private ATR kcAtrInd;
		private MACD macdInd;
		private SimpleFont signalFont;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MES500T Squeeze and Momentum - BB/KC squeeze filter, MACD momentum entries.";
				Name = "MES500TSqueezeMomentum";
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
				RequireThreeBarMomentum = true;
				EnableAlerts = true;
				ShowSignals = true;
				ShowSignalLabels = true;
				ShowSqueezeBackground = true;
				ArrowOffsetTicks = 8;
				LabelOffsetTicks = 12;

				BbBrush = Brushes.DodgerBlue;
				KcBrush = Brushes.Gray;
				SqueezeBackgroundBrush = Brushes.Goldenrod;
				LongSignalBrush = Brushes.LimeGreen;
				ShortSignalBrush = Brushes.Red;

				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "BB Upper");
				AddPlot(new Stroke(Brushes.DodgerBlue, 2), PlotStyle.Line, "BB Lower");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 2), PlotStyle.Line, "KC Upper");
				AddPlot(new Stroke(Brushes.Gray, DashStyleHelper.Dash, 2), PlotStyle.Line, "KC Lower");
				AddPlot(new Stroke(Brushes.LimeGreen, 3), PlotStyle.TriangleUp, "Long Marker");
				AddPlot(new Stroke(Brushes.Red, 3), PlotStyle.TriangleDown, "Short Marker");
			}
			else if (State == State.Configure)
			{
				Plots[4].AutoWidth = true;
				Plots[5].AutoWidth = true;
			}
			else if (State == State.DataLoaded)
			{
				signalFont = new SimpleFont("Arial", 14) { Bold = true };
				bbEmaInd = EMA(BbPeriod);
				bbStdDevInd = StdDev(BbPeriod);
				kcEmaInd = EMA(KcPeriod);
				kcAtrInd = ATR(KcPeriod);
				macdInd = MACD(MacdFast, MacdSlow, MacdSignal);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToPlot)
				return;

			double bbMid = bbEmaInd[0];
			double stdDev = bbStdDevInd[0];
			double bbUpper = bbMid + (BbStdDev * stdDev);
			double bbLower = bbMid - (BbStdDev * stdDev);

			double kcMid = kcEmaInd[0];
			double atr = kcAtrInd[0];
			double kcUpper = kcMid + (KcMultiplier * atr);
			double kcLower = kcMid - (KcMultiplier * atr);

			Values[0][0] = bbUpper;
			Values[1][0] = bbLower;
			Values[2][0] = kcUpper;
			Values[3][0] = kcLower;
			Values[4][0] = double.NaN;
			Values[5][0] = double.NaN;

			PlotBrushes[0][0] = BbBrush;
			PlotBrushes[1][0] = BbBrush;
			PlotBrushes[2][0] = KcBrush;
			PlotBrushes[3][0] = KcBrush;

			bool inNoTradeZone = IsInNoTradeZone(bbUpper, bbLower, kcUpper, kcLower);
			bool squeezeOff = IsSqueezeOff(bbUpper, bbLower, kcUpper, kcLower);
			BackBrush = ShowSqueezeBackground && inNoTradeZone ? SqueezeBackgroundBrush : null;

			if (!ShowSignals)
				return;

			double hist0 = macdInd.Diff[0];
			double hist1 = macdInd.Diff[1];
			double hist2 = macdInd.Diff[2];
			double hist3 = CurrentBar >= 3 ? macdInd.Diff[3] : hist2;

			bool macdTangle = IsMacdTangle();
			bool histRising = RequireThreeBarMomentum
				? hist0 > hist1 && hist1 > hist2
				: hist0 > hist1;
			bool histFalling = RequireThreeBarMomentum
				? hist0 < hist1 && hist1 < hist2
				: hist0 < hist1;

			bool longSignal = !inNoTradeZone
				&& squeezeOff
				&& !macdTangle
				&& Close[0] > kcUpper
				&& hist0 > 0
				&& histRising
				&& !IsLongExhaustion(hist0, hist1, hist2, hist3);

			bool shortSignal = !inNoTradeZone
				&& squeezeOff
				&& !macdTangle
				&& Close[0] < kcLower
				&& hist0 < 0
				&& histFalling
				&& !IsShortExhaustion(hist0, hist1, hist2, hist3);

			if (longSignal)
				DrawLongSignal();

			if (shortSignal)
				DrawShortSignal();
		}

		private bool IsInNoTradeZone(double bbUpper, double bbLower, double kcUpper, double kcLower)
		{
			bool fullSqueeze = bbUpper <= kcUpper && bbLower >= kcLower;
			bool bbUpperInsideKc = bbUpper <= kcUpper && bbUpper >= kcLower;
			bool bbLowerInsideKc = bbLower >= kcLower && bbLower <= kcUpper;

			return fullSqueeze || bbUpperInsideKc || bbLowerInsideKc;
		}

		private bool IsSqueezeOff(double bbUpper, double bbLower, double kcUpper, double kcLower)
		{
			return bbUpper > kcUpper || bbLower < kcLower;
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

			// Lokální peak na předchozí svíčce – histogram se láme dolů
			if (hist1 > hist0 && hist1 >= hist2 && hist1 >= hist3)
				return true;

			// Blow-off: schody stále rostou, ale momentum výrazně slábne
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

		private void DrawLongSignal()
		{
			string tag = "MES500TLong_" + CurrentBar;
			double arrowY = Low[0] - (ArrowOffsetTicks * TickSize);
			double labelY = arrowY - (LabelOffsetTicks * TickSize);

			Values[4][0] = arrowY;
			PlotBrushes[4][0] = LongSignalBrush;

			Draw.ArrowUp(this, tag + "_Arrow", false, 0, arrowY, LongSignalBrush);

			if (ShowSignalLabels)
			{
				Draw.Text(this, tag + "_Text", false, "LONG", 0, labelY, 0, LongSignalBrush,
					signalFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				Draw.Text(this, tag + "_Simple", "LONG", 0, labelY, LongSignalBrush);
			}

			if (EnableAlerts)
				Alert(tag, Priority.Medium, "MES500T LONG", "Alert1.wav", 10, Brushes.Transparent, Brushes.Black);
		}

		private void DrawShortSignal()
		{
			string tag = "MES500TShort_" + CurrentBar;
			double arrowY = High[0] + (ArrowOffsetTicks * TickSize);
			double labelY = arrowY + (LabelOffsetTicks * TickSize);

			Values[5][0] = arrowY;
			PlotBrushes[5][0] = ShortSignalBrush;

			Draw.ArrowDown(this, tag + "_Arrow", false, 0, arrowY, ShortSignalBrush);

			if (ShowSignalLabels)
			{
				Draw.Text(this, tag + "_Text", false, "SHORT", 0, labelY, 0, ShortSignalBrush,
					signalFont, TextAlignment.Center, Brushes.Black, Brushes.White, 100);
				Draw.Text(this, tag + "_Simple", "SHORT", 0, labelY, ShortSignalBrush);
			}

			if (EnableAlerts)
				Alert(tag, Priority.Medium, "MES500T SHORT", "Alert1.wav", 10, Brushes.Transparent, Brushes.Black);
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
		[Display(Name = "Tangle Separation Ticks", Order = 1, GroupName = "4. Filters")]
		public int TangleSeparationTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Tangle Slope Ticks", Order = 2, GroupName = "4. Filters")]
		public int TangleSlopeTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require 3-Bar Momentum", Description = "MACD histogram schody (3 svíčky).", Order = 3, GroupName = "4. Filters")]
		public bool RequireThreeBarMomentum { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Alerts", Order = 4, GroupName = "4. Filters")]
		public bool EnableAlerts { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Signals", Order = 5, GroupName = "4. Filters")]
		public bool ShowSignals { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Signal Labels", Order = 6, GroupName = "4. Filters")]
		public bool ShowSignalLabels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Squeeze Background", Order = 7, GroupName = "4. Filters")]
		public bool ShowSqueezeBackground { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Arrow Offset Ticks", Order = 1, GroupName = "5. Visual")]
		public int ArrowOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 80)]
		[Display(Name = "Label Offset Ticks", Order = 2, GroupName = "5. Visual")]
		public int LabelOffsetTicks { get; set; }

		[XmlIgnore]
		[Display(Name = "BB Color", Order = 3, GroupName = "5. Visual")]
		public Brush BbBrush { get; set; }

		[Browsable(false)]
		public string BbBrushSerializable
		{
			get { return Serialize.BrushToString(BbBrush); }
			set { BbBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "KC Color", Order = 4, GroupName = "5. Visual")]
		public Brush KcBrush { get; set; }

		[Browsable(false)]
		public string KcBrushSerializable
		{
			get { return Serialize.BrushToString(KcBrush); }
			set { KcBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Squeeze Background", Order = 5, GroupName = "5. Visual")]
		public Brush SqueezeBackgroundBrush { get; set; }

		[Browsable(false)]
		public string SqueezeBackgroundBrushSerializable
		{
			get { return Serialize.BrushToString(SqueezeBackgroundBrush); }
			set { SqueezeBackgroundBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Long Signal Color", Order = 6, GroupName = "5. Visual")]
		public Brush LongSignalBrush { get; set; }

		[Browsable(false)]
		public string LongSignalBrushSerializable
		{
			get { return Serialize.BrushToString(LongSignalBrush); }
			set { LongSignalBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(Name = "Short Signal Color", Order = 7, GroupName = "5. Visual")]
		public Brush ShortSignalBrush { get; set; }

		[Browsable(false)]
		public string ShortSignalBrushSerializable
		{
			get { return Serialize.BrushToString(ShortSignalBrush); }
			set { ShortSignalBrush = Serialize.StringToBrush(value); }
		}

		#endregion
	}
}
