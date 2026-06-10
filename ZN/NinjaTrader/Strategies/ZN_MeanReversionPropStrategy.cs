#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class ZN_MeanReversionPropStrategy : Strategy
	{
		private Bollinger bollinger;
		private RSI rsi;

		private bool longSetupActive;
		private bool shortSetupActive;
		private double entryPrice;
		private double targetPrice;
		private bool trailingStopActive;

		private DateTime currentSessionDate;
		private double sessionStartCumProfit;
		private bool dailyLossLimitHit;

		private List<TimeSpan> newsEventTimes;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"ZN Mean Reversion prop-trading AOS (Bollinger Bands + RSI).";
				Name = "ZN_MeanReversionPropStrategy";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = false;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 25;
				IsInstantiatedOnEachOptimizationIteration = true;

				BandPeriod = 20;
				BandDeviation = 2.0;
				RsiPeriod = 14;
				RsiOversold = 30;
				RsiOverbought = 70;

				StopLossTicks = 8;
				TrailingStopTicks = 4;

				SessionStartHour = 15;
				SessionStartMinute = 30;
				SessionEndHour = 21;
				SessionEndMinute = 45;
				FlatBeforeCloseHour = 21;
				FlatBeforeCloseMinute = 55;

				DailyLossLimitUsd = 500;
				NewsBufferMinutes = 15;
				NewsEventTimes = "14:30,16:00";
				UseSečTimeZone = true;
			}
			else if (State == State.Configure)
			{
			}
			else if (State == State.DataLoaded)
			{
				bollinger = Bollinger(BandDeviation, BandPeriod);
				rsi = RSI(RsiPeriod, 1);

				AddChartIndicator(bollinger);
				AddChartIndicator(rsi);

				ParseNewsEventTimes();
				ResetSessionState(Time[0].Date);
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			DateTime barTime = GetStrategyTime();
			UpdateSessionTracking(barTime);

			if (IsFlatBeforeClose(barTime))
			{
				FlattenAll("FlatBeforeClose");
				return;
			}

			if (IsNewsBlackout(barTime))
			{
				FlattenAll("NewsFilter");
				return;
			}

			if (dailyLossLimitHit)
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					FlattenAll("DailyLossLimit");
				return;
			}

			if (GetDailyPnL() <= -DailyLossLimitUsd)
			{
				dailyLossLimitHit = true;
				FlattenAll("DailyLossLimit");
				return;
			}

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageOpenPosition();
				return;
			}

			trailingStopActive = false;

			if (!IsWithinTradingHours(barTime))
			{
				longSetupActive = false;
				shortSetupActive = false;
				return;
			}

			double lowerBand = bollinger.Lower[0];
			double upperBand = bollinger.Upper[0];
			double middleBand = bollinger.Middle[0];
			double rsiValue = rsi[0];

			bool closedBelowLower = Close[0] < lowerBand;
			bool closedAboveUpper = Close[0] > upperBand;
			bool closedInsideBands = Close[0] >= lowerBand && Close[0] <= upperBand;

			bool bullishReversal = Close[0] > Open[0];
			bool bearishReversal = Close[0] < Open[0];

			if (closedBelowLower && rsiValue < RsiOversold)
				longSetupActive = true;

			if (closedAboveUpper && rsiValue > RsiOverbought)
				shortSetupActive = true;

			if (longSetupActive && closedInsideBands && bullishReversal)
			{
				entryPrice = Close[0];
				targetPrice = middleBand;
				longSetupActive = false;
				shortSetupActive = false;

				SetStopLoss(CalculationMode.Ticks, StopLossTicks);
				EnterLong(DefaultQuantity, "LongMR");
			}
			else if (shortSetupActive && closedInsideBands && bearishReversal)
			{
				entryPrice = Close[0];
				targetPrice = middleBand;
				longSetupActive = false;
				shortSetupActive = false;

				SetStopLoss(CalculationMode.Ticks, StopLossTicks);
				EnterShort(DefaultQuantity, "ShortMR");
			}
		}

		private void ManageOpenPosition()
		{
			double middleBand = bollinger.Middle[0];

			if (Position.MarketPosition == MarketPosition.Long)
			{
				if (High[0] >= middleBand)
				{
					ExitLong("PT_MiddleBand", "LongMR");
					return;
				}

				TryActivateTrailingStop(true);
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				if (Low[0] <= middleBand)
				{
					ExitShort("PT_MiddleBand", "ShortMR");
					return;
				}

				TryActivateTrailingStop(false);
			}
		}

		private void TryActivateTrailingStop(bool isLong)
		{
			if (trailingStopActive || targetPrice == 0 || entryPrice == 0)
				return;

			double halfwayPrice = isLong
				? entryPrice + 0.5 * (targetPrice - entryPrice)
				: entryPrice - 0.5 * (entryPrice - targetPrice);

			bool reachedHalfway = isLong
				? Close[0] >= halfwayPrice
				: Close[0] <= halfwayPrice;

			if (!reachedHalfway)
				return;

			trailingStopActive = true;
			SetTrailStop(CalculationMode.Ticks, TrailingStopTicks);
		}

		private void FlattenAll(string signalName)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong(signalName, "LongMR");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(signalName, "ShortMR");

			longSetupActive = false;
			shortSetupActive = false;
		}

		private void UpdateSessionTracking(DateTime barTime)
		{
			DateTime sessionDate = barTime.Date;
			if (sessionDate != currentSessionDate)
				ResetSessionState(sessionDate);
		}

		private void ResetSessionState(DateTime sessionDate)
		{
			currentSessionDate = sessionDate;
			sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			dailyLossLimitHit = false;
			longSetupActive = false;
			shortSetupActive = false;
			trailingStopActive = false;
		}

		private double GetDailyPnL()
		{
			double realized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
			double unrealized = Position.MarketPosition == MarketPosition.Flat
				? 0
				: Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

			return realized + unrealized;
		}

		private DateTime GetStrategyTime()
		{
			if (!UseSečTimeZone)
				return Time[0];

			try
			{
				TimeZoneInfo sečZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
				return TimeZoneInfo.ConvertTime(Time[0], sečZone);
			}
			catch
			{
				return Time[0];
			}
		}

		private bool IsWithinTradingHours(DateTime time)
		{
			TimeSpan sessionStart = new TimeSpan(SessionStartHour, SessionStartMinute, 0);
			TimeSpan sessionEnd = new TimeSpan(SessionEndHour, SessionEndMinute, 0);
			TimeSpan now = time.TimeOfDay;

			return now >= sessionStart && now <= sessionEnd;
		}

		private bool IsFlatBeforeClose(DateTime time)
		{
			TimeSpan flatTime = new TimeSpan(FlatBeforeCloseHour, FlatBeforeCloseMinute, 0);
			return time.TimeOfDay >= flatTime;
		}

		private bool IsNewsBlackout(DateTime time)
		{
			if (newsEventTimes == null || newsEventTimes.Count == 0)
				return false;

			TimeSpan now = time.TimeOfDay;
			TimeSpan buffer = TimeSpan.FromMinutes(NewsBufferMinutes);

			return newsEventTimes.Any(newsTime =>
				now >= newsTime - buffer && now <= newsTime + buffer);
		}

		private void ParseNewsEventTimes()
		{
			newsEventTimes = new List<TimeSpan>();

			if (string.IsNullOrWhiteSpace(NewsEventTimes))
				return;

			string[] parts = NewsEventTimes.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string part in parts)
			{
				string trimmed = part.Trim();
				if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan parsed))
					newsEventTimes.Add(parsed);
			}
		}

		#region Properties

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Band Period", Order = 1, GroupName = "1. Indicators")]
		public int BandPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, double.MaxValue)]
		[Display(Name = "Band Deviation", Order = 2, GroupName = "1. Indicators")]
		public double BandDeviation { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "RSI Period", Order = 3, GroupName = "1. Indicators")]
		public int RsiPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Oversold", Order = 4, GroupName = "1. Indicators")]
		public int RsiOversold { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Overbought", Order = 5, GroupName = "1. Indicators")]
		public int RsiOverbought { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (Ticks)", Order = 1, GroupName = "2. Trade Management")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trailing Stop (Ticks)", Order = 2, GroupName = "2. Trade Management")]
		public int TrailingStopTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Session Start Hour (SEČ)", Order = 1, GroupName = "3. Time Filters")]
		public int SessionStartHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Session Start Minute", Order = 2, GroupName = "3. Time Filters")]
		public int SessionStartMinute { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Session End Hour (SEČ)", Order = 3, GroupName = "3. Time Filters")]
		public int SessionEndHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Session End Minute", Order = 4, GroupName = "3. Time Filters")]
		public int SessionEndMinute { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Flat Before Close Hour (SEČ)", Order = 5, GroupName = "3. Time Filters")]
		public int FlatBeforeCloseHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Flat Before Close Minute", Order = 6, GroupName = "3. Time Filters")]
		public int FlatBeforeCloseMinute { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use SEČ Time Zone", Order = 7, GroupName = "3. Time Filters")]
		public bool UseSečTimeZone { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Daily Loss Limit (USD)", Order = 1, GroupName = "4. Risk Management")]
		public double DailyLossLimitUsd { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "News Buffer (Minutes)", Order = 2, GroupName = "4. Risk Management")]
		public int NewsBufferMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "News Event Times (SEČ, HH:mm)", Order = 3, GroupName = "4. Risk Management")]
		public string NewsEventTimes { get; set; }

		#endregion
	}
}
