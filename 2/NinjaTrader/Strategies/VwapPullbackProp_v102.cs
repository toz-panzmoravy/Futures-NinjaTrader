#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// VWAP Pullback Prop AOS v1.02 – optimalizace pro MES a vyšší win rate.
	/// LONG-only default, bližší PT, ADX/RSI/volume filtry, potvrzení nad touch barem,
	/// extension před pullbacke, limit ztrát za den.
	/// </summary>
	public class VwapPullbackProp_v102 : Strategy
	{
		#region Enums
		private enum SetupState
		{
			Idle,
			ArmedLong,
			ArmedShort
		}
		#endregion

		#region Private fields
		private EMA ema;
		private RSI rsi;
		private ADX adx;
		private SMA volumeSma;

		private double cumulativeTypicalPriceVolume;
		private double cumulativeVolume;
		private double sessionVwap;

		private SetupState setupState;
		private int armedBarIndex;
		private double armedTouchBarHigh;
		private double armedTouchBarLow;

		private double entryPrice;
		private double highestPriceSinceEntry;
		private double lowestPriceSinceEntry;
		private bool breakEvenApplied;
		private double currentStopPrice;

		private DateTime lastSessionDate;
		private bool dailyLossLimitHit;
		private bool flatBeforeCloseDone;
		private List<TimeSpan> parsedNewsTimes;
		private string lastParsedNewsTimes;

		private int dailyTradeCount;
		private int dailyLossCount;
		private int cooldownUntilBar;
		private double sessionRealizedStartPnL;
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "VWAP Pullback Prop AOS v1.02 – MES win rate preset (LONG, PT 55, ADX/RSI filtry).";
				Name = "VwapPullbackProp_v102";
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
				BarsRequiredToTrade = 60;
				IsInstantiatedOnEachOptimizationIteration = true;

				// Obchod – bližší PT = vyšší win rate (MES: 35 SL / 55 PT ≈ 1:1.57)
				Contracts = 1;
				StopLossTicks = 35;
				ProfitTargetTicks = 55;
				EmaPeriod = 50;
				EnableLong = true;
				EnableShort = false;
				MaxBarsAfterTouch = 4;

				// Kvalita signálu (přísnější než v1.01)
				MinTrendBars = 3;
				RequireEmaSlope = true;
				MaxVwapPenetrationTicks = 8;
				MinConfirmBodyTicks = 5;
				RequireConfirmAboveTouchBar = true;
				MinExtensionBeforePullbackTicks = 12;
				ExtensionLookbackBars = 5;
				RequireTouchBarPullback = true;

				// Filtry trendu / momentum
				UseAdxFilter = true;
				MinAdx = 20;
				AdxPeriod = 14;
				UseRsiFilter = true;
				RsiPeriod = 14;
				RsiMinLong = 40;
				RsiMaxLong = 62;
				RsiMinShort = 38;
				RsiMaxShort = 60;
				UseVolumeFilter = true;
				VolumeSmaPeriod = 20;
				MinVolumeMultiplier = 0.85;

				// Trailing vypnuto – nechává PT doběhnout
				EnableTrailing = false;
				BreakEvenTriggerTicks = 40;
				BreakEvenOffsetTicks = 4;
				TrailTicks = 18;

				// Risk
				DailyLossLimit = 500;
				MaxTradesPerDay = 5;
				MaxDailyLosses = 2;
				CooldownBarsAfterLoss = 5;
				SkipFirstMinutes = 30;
				SkipLastMinutes = 75;

				// Čas – jádro US session (cca 16:00–20:30 SEČ)
				EntryStartTime = DateTime.Parse("15:30", CultureInfo.InvariantCulture);
				EntryEndTime = DateTime.Parse("21:45", CultureInfo.InvariantCulture);
				FlatTime = DateTime.Parse("21:55", CultureInfo.InvariantCulture);

				NewsTimes = string.Empty;
				NewsBlockMinutesBefore = 5;
				NewsBlockMinutesAfter = 5;
				CloseBeforeNews = true;
			}
			else if (State == State.Configure)
			{
				SetStopLoss("LongEntry", CalculationMode.Ticks, StopLossTicks, false);
				SetProfitTarget("LongEntry", CalculationMode.Ticks, ProfitTargetTicks);
				SetStopLoss("ShortEntry", CalculationMode.Ticks, StopLossTicks, false);
				SetProfitTarget("ShortEntry", CalculationMode.Ticks, ProfitTargetTicks);
			}
			else if (State == State.DataLoaded)
			{
				ema = EMA(Close, EmaPeriod);
				rsi = RSI(Close, RsiPeriod, 3);
				adx = ADX(AdxPeriod);
				volumeSma = SMA(Volume, VolumeSmaPeriod);
				ResetSessionState(true);
				ParseNewsTimes();
			}
		}
		#endregion

		#region OnBarUpdate
		protected override void OnBarUpdate()
		{
			if (CurrentBar < BarsRequiredToTrade)
				return;

			UpdateSessionVwap();
			HandleNewSession();

			if (Bars.IsFirstBarOfSession)
				ResetDailyRiskState();

			if (IsAtOrAfterFlatTime())
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					ExitAllPositions("FlatBeforeClose");

				flatBeforeCloseDone = true;
				ResetSetupState();
				return;
			}

			if (CloseBeforeNews && IsInNewsCloseWindow() && Position.MarketPosition != MarketPosition.Flat)
			{
				ExitAllPositions("NewsClose");
				ResetSetupState();
				return;
			}

			UpdateDailyLossLimit();

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageOpenPosition();
				return;
			}

			if (!CanEnterNewTrade())
				return;

			UpdateSetupStateMachine();
		}
		#endregion

		#region VWAP
		private void UpdateSessionVwap()
		{
			if (Bars.IsFirstBarOfSession)
			{
				cumulativeTypicalPriceVolume = 0;
				cumulativeVolume = 0;
			}

			double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
			double barVolume = Volume[0] <= 0 ? 1 : Volume[0];

			cumulativeTypicalPriceVolume += typicalPrice * barVolume;
			cumulativeVolume += barVolume;
			sessionVwap = cumulativeTypicalPriceVolume / cumulativeVolume;
		}
		#endregion

		#region Session / risk helpers
		private void HandleNewSession()
		{
			DateTime sessionDate = Time[0].Date;
			if (sessionDate != lastSessionDate)
			{
				lastSessionDate = sessionDate;
				ResetDailyRiskState();
			}
		}

		private void ResetDailyRiskState()
		{
			dailyLossLimitHit = false;
			flatBeforeCloseDone = false;
			dailyTradeCount = 0;
			dailyLossCount = 0;
			cooldownUntilBar = 0;
			sessionRealizedStartPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			ResetSetupState();
		}

		private void ResetSessionState(bool fullReset)
		{
			setupState = SetupState.Idle;
			armedBarIndex = -1;
			armedTouchBarHigh = 0;
			armedTouchBarLow = 0;
			entryPrice = 0;
			highestPriceSinceEntry = 0;
			lowestPriceSinceEntry = 0;
			breakEvenApplied = false;
			currentStopPrice = 0;
			cumulativeTypicalPriceVolume = 0;
			cumulativeVolume = 0;
			sessionVwap = 0;
			dailyTradeCount = 0;
			dailyLossCount = 0;
			cooldownUntilBar = 0;

			if (fullReset)
			{
				lastSessionDate = DateTime.MinValue;
				dailyLossLimitHit = false;
				flatBeforeCloseDone = false;
			}
		}

		private void ResetSetupState()
		{
			setupState = SetupState.Idle;
			armedBarIndex = -1;
			armedTouchBarHigh = 0;
			armedTouchBarLow = 0;
		}

		private double GetDailyPnL()
		{
			double realized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionRealizedStartPnL;
			double unrealized = Position.MarketPosition == MarketPosition.Flat
				? 0
				: Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
			return realized + unrealized;
		}

		private void UpdateDailyLossLimit()
		{
			if (!dailyLossLimitHit && GetDailyPnL() <= -Math.Abs(DailyLossLimit))
				dailyLossLimitHit = true;
		}

		private bool CanEnterNewTrade()
		{
			if (dailyLossLimitHit || flatBeforeCloseDone)
				return false;

			if (MaxTradesPerDay > 0 && dailyTradeCount >= MaxTradesPerDay)
				return false;

			if (MaxDailyLosses > 0 && dailyLossCount >= MaxDailyLosses)
				return false;

			if (CooldownBarsAfterLoss > 0 && CurrentBar < cooldownUntilBar)
				return false;

			if (!IsWithinEntryWindow())
				return false;

			if (IsInNewsBlockWindow())
				return false;

			return true;
		}

		private bool IsWithinEntryWindow()
		{
			TimeSpan now = Time[0].TimeOfDay;
			TimeSpan start = EntryStartTime.TimeOfDay.Add(TimeSpan.FromMinutes(SkipFirstMinutes));
			TimeSpan end = EntryEndTime.TimeOfDay.Subtract(TimeSpan.FromMinutes(SkipLastMinutes));

			if (end < start)
				end = EntryEndTime.TimeOfDay;

			return now >= start && now <= end;
		}

		private bool IsAtOrAfterFlatTime()
		{
			return Time[0].TimeOfDay >= FlatTime.TimeOfDay;
		}
		#endregion

		#region News filter
		private void ParseNewsTimes()
		{
			if (NewsTimes == lastParsedNewsTimes)
				return;

			lastParsedNewsTimes = NewsTimes;
			parsedNewsTimes = new List<TimeSpan>();

			if (string.IsNullOrWhiteSpace(NewsTimes))
				return;

			string[] parts = NewsTimes.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string part in parts)
			{
				string trimmed = part.Trim();
				if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan ts))
					parsedNewsTimes.Add(ts);
				else if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
					parsedNewsTimes.Add(dt.TimeOfDay);
			}
		}

		private bool IsInNewsBlockWindow()
		{
			ParseNewsTimes();
			if (parsedNewsTimes == null || parsedNewsTimes.Count == 0)
				return false;

			TimeSpan now = Time[0].TimeOfDay;
			foreach (TimeSpan newsTime in parsedNewsTimes)
			{
				TimeSpan blockStart = newsTime.Subtract(TimeSpan.FromMinutes(NewsBlockMinutesBefore));
				TimeSpan blockEnd = newsTime.Add(TimeSpan.FromMinutes(NewsBlockMinutesAfter));
				if (now >= blockStart && now <= blockEnd)
					return true;
			}
			return false;
		}

		private bool IsInNewsCloseWindow()
		{
			ParseNewsTimes();
			if (parsedNewsTimes == null || parsedNewsTimes.Count == 0)
				return false;

			TimeSpan now = Time[0].TimeOfDay;
			foreach (TimeSpan newsTime in parsedNewsTimes)
			{
				TimeSpan closeStart = newsTime.Subtract(TimeSpan.FromMinutes(NewsBlockMinutesBefore));
				if (now >= closeStart && now < newsTime)
					return true;
			}
			return false;
		}
		#endregion

		#region Filtry signálu v1.02
		private bool PassesTrendStrengthFilter()
		{
			if (!UseAdxFilter)
				return true;
			return adx[0] >= MinAdx;
		}

		private bool PassesLongRsiFilter()
		{
			if (!UseRsiFilter)
				return true;
			return rsi[0] >= RsiMinLong && rsi[0] <= RsiMaxLong;
		}

		private bool PassesShortRsiFilter()
		{
			if (!UseRsiFilter)
				return true;
			return rsi[0] >= RsiMinShort && rsi[0] <= RsiMaxShort;
		}

		private bool PassesVolumeFilter()
		{
			if (!UseVolumeFilter)
				return true;

			double avgVolume = volumeSma[0];
			if (avgVolume <= 0)
				return true;

			return Volume[0] >= avgVolume * MinVolumeMultiplier;
		}

		private bool HasSustainedLongTrend()
		{
			if (CurrentBar < MinTrendBars)
				return false;

			for (int i = 1; i <= MinTrendBars; i++)
			{
				if (Close[i] <= ema[i])
					return false;
			}

			return Close[0] > sessionVwap && Close[0] > ema[0];
		}

		private bool HasSustainedShortTrend()
		{
			if (CurrentBar < MinTrendBars)
				return false;

			for (int i = 1; i <= MinTrendBars; i++)
			{
				if (Close[i] >= ema[i])
					return false;
			}

			return Close[0] < sessionVwap && Close[0] < ema[0];
		}

		private bool HasLongEmaSlope()
		{
			if (!RequireEmaSlope)
				return true;
			return ema[0] > ema[1] && ema[1] >= ema[2];
		}

		private bool HasShortEmaSlope()
		{
			if (!RequireEmaSlope)
				return true;
			return ema[0] < ema[1] && ema[1] <= ema[2];
		}

		private bool HasPriorExtensionForLong()
		{
			if (MinExtensionBeforePullbackTicks <= 0)
				return true;

			double threshold = sessionVwap + MinExtensionBeforePullbackTicks * TickSize;
			int lookback = Math.Min(ExtensionLookbackBars, CurrentBar);

			for (int i = 1; i <= lookback; i++)
			{
				if (High[i] >= threshold)
					return true;
			}
			return false;
		}

		private bool HasPriorExtensionForShort()
		{
			if (MinExtensionBeforePullbackTicks <= 0)
				return true;

			double threshold = sessionVwap - MinExtensionBeforePullbackTicks * TickSize;
			int lookback = Math.Min(ExtensionLookbackBars, CurrentBar);

			for (int i = 1; i <= lookback; i++)
			{
				if (Low[i] <= threshold)
					return true;
			}
			return false;
		}

		private bool IsValidLongVwapTouch()
		{
			if (Low[0] > sessionVwap || Close[0] <= sessionVwap)
				return false;

			double penetration = (sessionVwap - Low[0]) / TickSize;
			if (penetration > MaxVwapPenetrationTicks)
				return false;

			// Touch bar = pullback (červená nebo doji, ne agresivní zelená rally do VWAP)
			if (RequireTouchBarPullback && Close[0] >= Open[0])
			{
				double bodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;
				if (bodyTicks >= MinConfirmBodyTicks)
					return false;
			}

			return true;
		}

		private bool IsValidShortVwapTouch()
		{
			if (High[0] < sessionVwap || Close[0] >= sessionVwap)
				return false;

			double penetration = (High[0] - sessionVwap) / TickSize;
			if (penetration > MaxVwapPenetrationTicks)
				return false;

			if (RequireTouchBarPullback && Close[0] <= Open[0])
			{
				double bodyTicks = Math.Abs(Close[0] - Open[0]) / TickSize;
				if (bodyTicks >= MinConfirmBodyTicks)
					return false;
			}

			return true;
		}

		private bool IsBullishConfirmationBar()
		{
			if (Close[0] <= Open[0] || Close[0] <= sessionVwap)
				return false;

			double bodyTicks = (Close[0] - Open[0]) / TickSize;
			if (bodyTicks < MinConfirmBodyTicks)
				return false;

			if (RequireConfirmAboveTouchBar && Close[0] <= armedTouchBarHigh)
				return false;

			if (!PassesLongRsiFilter() || !PassesVolumeFilter())
				return false;

			return true;
		}

		private bool IsBearishConfirmationBar()
		{
			if (Close[0] >= Open[0] || Close[0] >= sessionVwap)
				return false;

			double bodyTicks = (Open[0] - Close[0]) / TickSize;
			if (bodyTicks < MinConfirmBodyTicks)
				return false;

			if (RequireConfirmAboveTouchBar && Close[0] >= armedTouchBarLow)
				return false;

			if (!PassesShortRsiFilter() || !PassesVolumeFilter())
				return false;

			return true;
		}
		#endregion

		#region Setup state machine
		private bool IsLongTrend()
		{
			return Close[0] > sessionVwap && Close[0] > ema[0];
		}

		private bool IsShortTrend()
		{
			return Close[0] < sessionVwap && Close[0] < ema[0];
		}

		private void UpdateSetupStateMachine()
		{
			if (setupState != SetupState.Idle && armedBarIndex >= 0)
			{
				if (CurrentBar - armedBarIndex > MaxBarsAfterTouch)
					ResetSetupState();
			}

			if (setupState == SetupState.ArmedLong && !IsLongTrend())
				ResetSetupState();

			if (setupState == SetupState.ArmedShort && !IsShortTrend())
				ResetSetupState();

			if (!PassesTrendStrengthFilter())
				return;

			if (EnableLong && setupState == SetupState.Idle
				&& IsLongTrend()
				&& HasSustainedLongTrend()
				&& HasLongEmaSlope()
				&& HasPriorExtensionForLong()
				&& IsValidLongVwapTouch())
			{
				setupState = SetupState.ArmedLong;
				armedBarIndex = CurrentBar;
				armedTouchBarHigh = High[0];
				armedTouchBarLow = Low[0];
				return;
			}

			if (EnableShort && setupState == SetupState.Idle
				&& IsShortTrend()
				&& HasSustainedShortTrend()
				&& HasShortEmaSlope()
				&& HasPriorExtensionForShort()
				&& IsValidShortVwapTouch())
			{
				setupState = SetupState.ArmedShort;
				armedBarIndex = CurrentBar;
				armedTouchBarHigh = High[0];
				armedTouchBarLow = Low[0];
				return;
			}

			if (setupState == SetupState.ArmedLong && armedBarIndex >= 0 && CurrentBar > armedBarIndex)
			{
				if (IsBullishConfirmationBar())
				{
					EnterLong(Contracts, "LongEntry");
					InitializeTradeState(Close[0]);
					dailyTradeCount++;
					ResetSetupState();
				}
			}

			if (setupState == SetupState.ArmedShort && armedBarIndex >= 0 && CurrentBar > armedBarIndex)
			{
				if (IsBearishConfirmationBar())
				{
					EnterShort(Contracts, "ShortEntry");
					InitializeTradeState(Close[0]);
					dailyTradeCount++;
					ResetSetupState();
				}
			}
		}
		#endregion

		#region Trade management
		private void InitializeTradeState(double price)
		{
			entryPrice = price;
			highestPriceSinceEntry = price;
			lowestPriceSinceEntry = price;
			breakEvenApplied = false;
			currentStopPrice = 0;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null || execution.Order.OrderState != OrderState.Filled)
				return;

			if (execution.Order.Name == "LongEntry" || execution.Order.Name == "ShortEntry")
			{
				InitializeTradeState(price);
				return;
			}

			bool isExit = execution.Order.Name == "Stop loss"
				|| execution.Order.Name == "Profit target"
				|| execution.Order.Name == "FlatBeforeClose"
				|| execution.Order.Name == "NewsClose";

			if (!isExit)
				return;

			bool isLoss = false;

			if (execution.Order.OrderAction == OrderAction.Sell && marketPosition == MarketPosition.Flat)
				isLoss = price < entryPrice;
			else if (execution.Order.OrderAction == OrderAction.BuyToCover && marketPosition == MarketPosition.Flat)
				isLoss = price > entryPrice;

			if (isLoss)
			{
				dailyLossCount++;
				if (CooldownBarsAfterLoss > 0)
					cooldownUntilBar = CurrentBar + CooldownBarsAfterLoss;
			}
		}

		private void ManageOpenPosition()
		{
			if (!EnableTrailing)
				return;

			highestPriceSinceEntry = Math.Max(highestPriceSinceEntry, High[0]);
			lowestPriceSinceEntry = Math.Min(lowestPriceSinceEntry, Low[0]);

			double tickSize = TickSize;
			double newStop = currentStopPrice;

			if (Position.MarketPosition == MarketPosition.Long)
			{
				double profitTicks = (Close[0] - entryPrice) / tickSize;

				if (!breakEvenApplied && profitTicks >= BreakEvenTriggerTicks)
				{
					newStop = entryPrice + BreakEvenOffsetTicks * tickSize;
					breakEvenApplied = true;
				}

				if (breakEvenApplied)
				{
					double trailStop = highestPriceSinceEntry - TrailTicks * tickSize;
					newStop = Math.Max(newStop, trailStop);
				}

				ApplyStopForLong(newStop);
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				double profitTicks = (entryPrice - Close[0]) / tickSize;

				if (!breakEvenApplied && profitTicks >= BreakEvenTriggerTicks)
				{
					newStop = entryPrice - BreakEvenOffsetTicks * tickSize;
					breakEvenApplied = true;
				}

				if (breakEvenApplied)
				{
					double trailStop = lowestPriceSinceEntry + TrailTicks * tickSize;
					newStop = newStop == 0 ? trailStop : Math.Min(newStop, trailStop);
				}

				ApplyStopForShort(newStop);
			}
		}

		private void ApplyStopForLong(double candidateStop)
		{
			if (candidateStop <= 0)
				return;

			double initialStop = entryPrice - StopLossTicks * TickSize;
			candidateStop = Math.Max(candidateStop, initialStop);

			if (currentStopPrice <= 0 || candidateStop > currentStopPrice)
			{
				SetStopLoss("LongEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}
		}

		private void ApplyStopForShort(double candidateStop)
		{
			if (candidateStop <= 0)
				return;

			double initialStop = entryPrice + StopLossTicks * TickSize;
			candidateStop = Math.Min(candidateStop, initialStop);

			if (currentStopPrice <= 0 || candidateStop < currentStopPrice)
			{
				SetStopLoss("ShortEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}
		}

		private void ExitAllPositions(string signalName)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong(signalName, "LongEntry");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(signalName, "ShortEntry");
		}
		#endregion

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Contracts", Order = 1, GroupName = "1. Obchod")]
		public int Contracts { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "1. Obchod")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "1. Obchod")]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "EMA Period", Order = 4, GroupName = "1. Obchod")]
		public int EmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Long", Order = 5, GroupName = "1. Obchod")]
		public bool EnableLong { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Short", Order = 6, GroupName = "1. Obchod")]
		public bool EnableShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Max Bars After Touch", Order = 7, GroupName = "1. Obchod")]
		public int MaxBarsAfterTouch { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Min Trend Bars", Order = 1, GroupName = "2. Kvalita signálu")]
		public int MinTrendBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require EMA Slope", Order = 2, GroupName = "2. Kvalita signálu")]
		public bool RequireEmaSlope { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Max VWAP Penetration (ticks)", Order = 3, GroupName = "2. Kvalita signálu")]
		public int MaxVwapPenetrationTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 50)]
		[Display(Name = "Min Confirm Body (ticks)", Order = 4, GroupName = "2. Kvalita signálu")]
		public int MinConfirmBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Confirm Above Touch Bar", Description = "LONG: close nad high touch svíčky.", Order = 5, GroupName = "2. Kvalita signálu")]
		public bool RequireConfirmAboveTouchBar { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Min Extension Before Pullback (ticks)", Order = 6, GroupName = "2. Kvalita signálu")]
		public int MinExtensionBeforePullbackTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, 20)]
		[Display(Name = "Extension Lookback Bars", Order = 7, GroupName = "2. Kvalita signálu")]
		public int ExtensionLookbackBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Touch Bar Must Be Pullback", Order = 8, GroupName = "2. Kvalita signálu")]
		public bool RequireTouchBarPullback { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use ADX Filter", Order = 1, GroupName = "3. Momentum filtry")]
		public bool UseAdxFilter { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "Min ADX", Order = 2, GroupName = "3. Momentum filtry")]
		public int MinAdx { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "ADX Period", Order = 3, GroupName = "3. Momentum filtry")]
		public int AdxPeriod { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use RSI Filter", Order = 4, GroupName = "3. Momentum filtry")]
		public bool UseRsiFilter { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Period", Order = 5, GroupName = "3. Momentum filtry")]
		public int RsiPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Min Long", Order = 6, GroupName = "3. Momentum filtry")]
		public int RsiMinLong { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Max Long", Order = 7, GroupName = "3. Momentum filtry")]
		public int RsiMaxLong { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Min Short", Order = 8, GroupName = "3. Momentum filtry")]
		public int RsiMinShort { get; set; }

		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name = "RSI Max Short", Order = 9, GroupName = "3. Momentum filtry")]
		public int RsiMaxShort { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use Volume Filter", Order = 10, GroupName = "3. Momentum filtry")]
		public bool UseVolumeFilter { get; set; }

		[NinjaScriptProperty]
		[Range(1, 200)]
		[Display(Name = "Volume SMA Period", Order = 11, GroupName = "3. Momentum filtry")]
		public int VolumeSmaPeriod { get; set; }

		[NinjaScriptProperty]
		[Range(0.1, 5.0)]
		[Display(Name = "Min Volume Multiplier", Order = 12, GroupName = "3. Momentum filtry")]
		public double MinVolumeMultiplier { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Trailing", Order = 1, GroupName = "4. Trailing")]
		public bool EnableTrailing { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Break-Even Trigger (ticks)", Order = 2, GroupName = "4. Trailing")]
		public int BreakEvenTriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Break-Even Offset (ticks)", Order = 3, GroupName = "4. Trailing")]
		public int BreakEvenOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trail (ticks)", Order = 4, GroupName = "4. Trailing")]
		public int TrailTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Daily Loss Limit ($)", Order = 1, GroupName = "5. Risk")]
		public double DailyLossLimit { get; set; }

		[NinjaScriptProperty]
		[Range(0, 50)]
		[Display(Name = "Max Trades Per Day", Order = 2, GroupName = "5. Risk")]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty]
		[Range(0, 20)]
		[Display(Name = "Max Daily Losses", Description = "Po X ztrátách v daný den se neobchoduje.", Order = 3, GroupName = "5. Risk")]
		public int MaxDailyLosses { get; set; }

		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name = "Cooldown Bars After Loss", Order = 4, GroupName = "5. Risk")]
		public int CooldownBarsAfterLoss { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Skip First Minutes", Order = 5, GroupName = "5. Risk")]
		public int SkipFirstMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Skip Last Minutes", Description = "Konec entry okna před Entry End.", Order = 6, GroupName = "5. Risk")]
		public int SkipLastMinutes { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry Start Time", Order = 1, GroupName = "6. Čas")]
		public DateTime EntryStartTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry End Time", Order = 2, GroupName = "6. Čas")]
		public DateTime EntryEndTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Flat Time", Order = 3, GroupName = "6. Čas")]
		public DateTime FlatTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "News Times", Order = 1, GroupName = "7. News Filter")]
		public string NewsTimes { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Block Minutes Before", Order = 2, GroupName = "7. News Filter")]
		public int NewsBlockMinutesBefore { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Block Minutes After", Order = 3, GroupName = "7. News Filter")]
		public int NewsBlockMinutesAfter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Close Before News", Order = 4, GroupName = "7. News Filter")]
		public bool CloseBeforeNews { get; set; }
		#endregion
	}
}
