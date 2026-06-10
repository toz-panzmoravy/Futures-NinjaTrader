#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// AOS VWAP Pullback – high win-rate profile (~65% target), long-only MES

namespace NinjaTrader.NinjaScript.Strategies
{
	public class AOS_VWAPPullbackStrategy : Strategy
	{
		private const string EntrySignal = "VWAPPullbackLong";

		private EMA ema50;
		private ATR atr14;
		private ADX adx14;
		private RSI rsi14;
		private SMA volumeSma20;

		private double cumulativeTypicalPriceVolume;
		private double cumulativeVolume;
		private double sessionVwap;

		private bool pullbackDetected;
		private int pullbackDetectedBar;
		private double maxCloseAboveVwapTicks;
		private int sessionBarCount;
		private bool tradingHaltedForDay;
		private int tradesToday;
		private DateTime lastTradingDay;

		private TimeZoneInfo cetTimeZone;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AOS VWAP Pullback – high win-rate profile, long-only MES.";
				Name = "AOS_VWAPPullbackStrategy";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
				OrderFillResolution = OrderFillResolution.Standard;
				Slippage = 0;
				StartBehavior = StartBehavior.WaitUntilFlat;
				TimeInForce = TimeInForce.Gtc;
				TraceOrders = false;
				RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				BarsRequiredToTrade = 55;
				IsInstantiatedOnEachOptimizationIteration = true;

				DailyLossLimit = 500;
				StopLossTicks = 40;
				ProfitTargetTicks = 32;
				MaxTradesPerDay = 1;

				MinEmaDistanceTicks = 28;
				MinAtrTicks = 10;
				MinAdx = 22;
				EmaSlopeLookback = 5;
				MinRsi = 48;
				MaxRsi = 68;
				MinConfirmationBodyPercent = 55;
				MinCloseAboveVwapTicks = 4;
				MinPrePullbackExtensionTicks = 16;
				MinSessionBars = 6;
				PullbackTimeoutBars = 4;

				EntryWindowStartHour = 16;
				EntryWindowStartMinute = 0;
				EntryWindowEndHour = 20;
				EntryWindowEndMinute = 30;
				FlattenHour = 21;
				FlattenMinute = 55;

				AddPlot(Brushes.DodgerBlue, "SessionVWAP");
			}
			else if (State == State.Configure)
			{
				cetTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
			}
			else if (State == State.DataLoaded)
			{
				ema50 = EMA(50);
				atr14 = ATR(14);
				adx14 = ADX(14);
				rsi14 = RSI(14, 3);
				volumeSma20 = SMA(Volume, 20);
				AddChartIndicator(ema50);
			}
		}

		protected override void OnBarUpdate()
		{
			UpdateSessionVwap();
			UpdateSessionBarCount();

			if (CurrentBar < BarsRequiredToTrade)
				return;

			DateTime barTimeCet = TimeZoneInfo.ConvertTime(Time[0], Globals.GeneralOptions.TimeZoneInfo, cetTimeZone);
			ResetDailyStateIfNewDay(barTimeCet);

			if (tradingHaltedForDay)
				return;

			if (IsHighImpactNewsTime(barTimeCet))
				return;

			HandleEndOfDayFlatten(barTimeCet);

			if (tradingHaltedForDay)
				return;

			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			if (tradesToday >= MaxTradesPerDay)
				return;

			if (sessionBarCount < MinSessionBars)
			{
				ResetPullbackState();
				return;
			}

			if (!IsWithinEntryWindow(barTimeCet))
			{
				ResetPullbackState();
				return;
			}

			double close = Close[0];
			double open = Open[0];
			double high = High[0];
			double low = Low[0];
			double ema = ema50[0];
			double vwapValue = sessionVwap;
			double atrTicks = atr14[0] / TickSize;
			double adxValue = adx14[0];
			double rsiValue = rsi14[0];
			double barRange = high - low;

			if (atrTicks < MinAtrTicks || adxValue < MinAdx)
			{
				ResetPullbackState();
				return;
			}

			if (CurrentBar < EmaSlopeLookback || ema50[0] <= ema50[EmaSlopeLookback])
			{
				ResetPullbackState();
				return;
			}

			bool bullishTrend = close > ema && close > vwapValue;
			double emaDistanceTicks = (close - ema) / TickSize;

			if (!bullishTrend || emaDistanceTicks < MinEmaDistanceTicks)
			{
				ResetPullbackState();
				return;
			}

			if (!pullbackDetected)
			{
				maxCloseAboveVwapTicks = Math.Max(maxCloseAboveVwapTicks, (close - vwapValue) / TickSize);

				bool touchedVwap = low <= vwapValue;
				bool declinedIntoVwap = touchedVwap && (open > vwapValue || (CurrentBar > 0 && Close[1] > vwapValue));

				if (declinedIntoVwap && maxCloseAboveVwapTicks >= MinPrePullbackExtensionTicks)
				{
					pullbackDetected = true;
					pullbackDetectedBar = CurrentBar;
				}

				return;
			}

			if (CurrentBar - pullbackDetectedBar > PullbackTimeoutBars)
			{
				ResetPullbackState();
				return;
			}

			if (!IsHighQualityConfirmationBar(open, close, high, low, vwapValue, barRange, rsiValue))
				return;

			SetStopLoss(CalculationMode.Ticks, StopLossTicks);
			SetProfitTarget(CalculationMode.Ticks, ProfitTargetTicks);
			EnterLong(DefaultQuantity, EntrySignal);
			ResetPullbackState();
		}

		private bool IsHighQualityConfirmationBar(double open, double close, double high, double low, double vwapValue, double barRange, double rsiValue)
		{
			if (close <= open || close < vwapValue)
				return false;

			if (low < vwapValue)
				return false;

			if (rsiValue < MinRsi || rsiValue > MaxRsi)
				return false;

			if (Volume[0] <= volumeSma20[0])
				return false;

			double closeAboveVwapTicks = (close - vwapValue) / TickSize;
			if (closeAboveVwapTicks < MinCloseAboveVwapTicks)
				return false;

			if (barRange <= TickSize)
				return false;

			double bodyPercent = ((close - open) / barRange) * 100.0;
			return bodyPercent >= MinConfirmationBodyPercent;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null || execution.Order.OrderState != OrderState.Filled)
				return;

			if (execution.Order.Name == EntrySignal && execution.Order.OrderAction == OrderAction.Buy)
				tradesToday++;

			if (SystemPerformance.AllTrades.Count == 0)
				return;

			Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
			if (lastTrade.Exit == null || lastTrade.Exit.Time != time)
				return;

			double dailyRealizedPnL = GetDailyRealizedPnL(time);

			if (dailyRealizedPnL <= -DailyLossLimit)
				HaltTradingForDay("Daily loss limit reached.");
		}

		private void ResetDailyStateIfNewDay(DateTime barTimeCet)
		{
			DateTime tradingDay = barTimeCet.Date;

			if (lastTradingDay == tradingDay)
				return;

			lastTradingDay = tradingDay;
			tradingHaltedForDay = false;
			tradesToday = 0;
			ResetPullbackState();
		}

		private void HandleEndOfDayFlatten(DateTime barTimeCet)
		{
			TimeSpan flattenTime = new TimeSpan(FlattenHour, FlattenMinute, 0);
			bool shouldFlatten = barTimeCet.TimeOfDay >= flattenTime || Bars.IsLastBarOfSession;

			if (!shouldFlatten)
				return;

			if (Position.MarketPosition != MarketPosition.Flat)
				ExitLong("EOD Flatten", EntrySignal);

			CancelAllWorkingOrders();
			ResetPullbackState();
		}

		private void HaltTradingForDay(string reason)
		{
			if (Position.MarketPosition != MarketPosition.Flat)
				ExitLong("Daily Loss Halt", EntrySignal);

			CancelAllWorkingOrders();
			tradingHaltedForDay = true;
			ResetPullbackState();

			Print(string.Format("{0} | {1}", Time[0], reason));
		}

		private void ResetPullbackState()
		{
			pullbackDetected = false;
			pullbackDetectedBar = -1;
			maxCloseAboveVwapTicks = 0;
		}

		private void UpdateSessionBarCount()
		{
			if (Bars.IsFirstBarOfSession)
				sessionBarCount = 0;

			sessionBarCount++;
		}

		private void UpdateSessionVwap()
		{
			if (Bars.IsFirstBarOfSession)
			{
				cumulativeTypicalPriceVolume = 0;
				cumulativeVolume = 0;
			}

			double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
			cumulativeTypicalPriceVolume += typicalPrice * Volume[0];
			cumulativeVolume += Volume[0];
			sessionVwap = cumulativeVolume > 0 ? cumulativeTypicalPriceVolume / cumulativeVolume : Close[0];

			Values[0][0] = sessionVwap;
		}

		private void CancelAllWorkingOrders()
		{
			if (Account == null)
				return;

			Account.CancelAllOrders(Instrument);
		}

		private bool IsWithinEntryWindow(DateTime barTimeCet)
		{
			TimeSpan current = barTimeCet.TimeOfDay;
			TimeSpan start = new TimeSpan(EntryWindowStartHour, EntryWindowStartMinute, 0);
			TimeSpan end = new TimeSpan(EntryWindowEndHour, EntryWindowEndMinute, 0);

			return current >= start && current <= end;
		}

		private double GetDailyRealizedPnL(DateTime referenceTime)
		{
			DateTime referenceCet = TimeZoneInfo.ConvertTime(referenceTime, Globals.GeneralOptions.TimeZoneInfo, cetTimeZone);
			DateTime dayStart = referenceCet.Date;
			DateTime dayEnd = dayStart.AddDays(1);

			double total = 0;

			foreach (Trade trade in SystemPerformance.AllTrades)
			{
				if (trade.Exit == null)
					continue;

				DateTime exitCet = TimeZoneInfo.ConvertTime(trade.Exit.Time, Globals.GeneralOptions.TimeZoneInfo, cetTimeZone);

				if (exitCet >= dayStart && exitCet < dayEnd)
					total += trade.ProfitCurrency;
			}

			return total;
		}

		private bool IsHighImpactNewsTime(DateTime barTimeCet)
		{
			return false;
		}

		#region Properties

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Daily Loss Limit (USD)", Order = 1, GroupName = "Risk Management")]
		public int DailyLossLimit { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "Risk Management")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "Risk Management")]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10)]
		[Display(Name = "Max Trades Per Day", Order = 4, GroupName = "Risk Management")]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Min EMA Distance (ticks)", Order = 1, GroupName = "Entry Filters")]
		public int MinEmaDistanceTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Min ATR (ticks)", Order = 2, GroupName = "Entry Filters")]
		public int MinAtrTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min ADX", Order = 3, GroupName = "Entry Filters")]
		public int MinAdx { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "EMA Slope Lookback", Order = 4, GroupName = "Entry Filters")]
		public int EmaSlopeLookback { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min RSI", Order = 5, GroupName = "Entry Filters")]
		public int MinRsi { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Max RSI", Order = 6, GroupName = "Entry Filters")]
		public int MaxRsi { get; set; }

		[NinjaScriptProperty]
		[Range(30, 90)]
		[Display(Name = "Min Confirmation Body %", Order = 7, GroupName = "Entry Filters")]
		public int MinConfirmationBodyPercent { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Min Close Above VWAP (ticks)", Order = 8, GroupName = "Entry Filters")]
		public int MinCloseAboveVwapTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Min Pre-Pullback Extension (ticks)", Order = 9, GroupName = "Entry Filters")]
		public int MinPrePullbackExtensionTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Min Session Bars", Order = 10, GroupName = "Entry Filters")]
		public int MinSessionBars { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Pullback Timeout (bars)", Order = 11, GroupName = "Entry Filters")]
		public int PullbackTimeoutBars { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Entry Window Start Hour (CET)", Order = 1, GroupName = "Time Filters")]
		public int EntryWindowStartHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Entry Window Start Minute", Order = 2, GroupName = "Time Filters")]
		public int EntryWindowStartMinute { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Entry Window End Hour (CET)", Order = 3, GroupName = "Time Filters")]
		public int EntryWindowEndHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Entry Window End Minute", Order = 4, GroupName = "Time Filters")]
		public int EntryWindowEndMinute { get; set; }

		[NinjaScriptProperty]
		[Range(0, 23)]
		[Display(Name = "Flatten Hour (CET)", Order = 5, GroupName = "Time Filters")]
		public int FlattenHour { get; set; }

		[NinjaScriptProperty]
		[Range(0, 59)]
		[Display(Name = "Flatten Minute", Order = 6, GroupName = "Time Filters")]
		public int FlattenMinute { get; set; }

		#endregion
	}
}
