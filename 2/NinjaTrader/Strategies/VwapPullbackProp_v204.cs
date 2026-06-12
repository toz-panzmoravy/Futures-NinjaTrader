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
	/// VWAP Pullback Prop AOS v2.04 – v203 + session-end flat (tick chart fix).
	/// IsLastBarOfSession + ExitOnSessionClose; cancel SL/PT before forced exits.
	/// Max 5 obch/den, SL 60 / PT 65, TICK 125.
	/// </summary>
	public class VwapPullbackProp_v204 : Strategy
	{
		#region Enums
		private enum SetupState { Idle, ArmedLong, ArmedShort }
		#endregion

		#region Private fields
		private EMA ema;
		private double cumulativeTypicalPriceVolume;
		private double cumulativeVolume;
		private double sessionVwap;

		private SetupState setupState;
		private int armedBarIndex;
		private double entryPrice;
		private double highestPriceSinceEntry;
		private double lowestPriceSinceEntry;
		private bool breakEvenApplied;
		private double currentStopPrice;

		private DateTime lastSessionDate;
		private bool dailyLossLimitHit;
		private bool dailyProfitTargetHit;
		private bool flatBeforeCloseDone;
		private List<TimeSpan> parsedNewsTimes;
		private string lastParsedNewsTimes;

		private int dailyTradeCount;
		private int dailyConsecutiveLosses;
		private int cooldownUntilBar;
		private double sessionRealizedStartPnL;
		private bool tickChartWarningShown;
		private List<int> blockedEntryHours;
		private string lastParsedBlockedHours;
		private DateTime entrySessionDate;
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "VWAP Pullback Prop v2.04 – session-end flat fix, blok 16,17,19h, SL 60 / PT 65.";
				Name = "VwapPullbackProp_v204";
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

				Contracts = 1;
				StopLossTicks = 60;
				ProfitTargetTicks = 65;
				EmaPeriod = 50;
				EnableLong = true;
				EnableShort = false;
				MaxBarsAfterTouch = 6;

				MinTrendBars = 2;
				RequireEmaSlope = true;
				MaxVwapPenetrationTicks = 12;
				MinConfirmBodyTicks = 3;
				RequireCloseBeyondPriorBar = true;
				AllowSameBarEntry = false;
				AllowLooseVwapTouch = false;
				LooseVwapCloseTicks = 0;

				EnableTrailing = false;
				BreakEvenTriggerTicks = 50;
				BreakEvenOffsetTicks = 4;
				TrailTicks = 20;

				DailyLossLimit = 400;
				DailyProfitTarget = 200;
				MaxTradesPerDay = 5;
				MaxConsecutiveLossesPerDay = 3;
				CooldownBarsAfterLoss = 3;
				SkipFirstMinutes = 10;
				BlockedEntryHours = "16,17,19";

				EntryStartTime = DateTime.Parse("15:30", CultureInfo.InvariantCulture);
				EntryEndTime = DateTime.Parse("20:00", CultureInfo.InvariantCulture);
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
				ResetSessionState(true);
				ParseNewsTimes();
				ParseBlockedEntryHours();
			}
		}
		#endregion

		#region OnBarUpdate
		protected override void OnBarUpdate()
		{
			ValidateTickChart();

			if (CurrentBar < BarsRequiredToTrade)
				return;

			UpdateSessionVwap();
			HandleNewSession();

			if (Bars.IsFirstBarOfSession)
			{
				if (Position.MarketPosition != MarketPosition.Flat
					&& entrySessionDate != DateTime.MinValue
					&& entrySessionDate < Time[0].Date)
				{
					ForceFlatOpenPosition("SessionOpenFlat");
					ResetSetupState();
					ResetDailyRiskState();
					return;
				}
				ResetDailyRiskState();
			}

			if (Bars.IsLastBarOfSession && Position.MarketPosition != MarketPosition.Flat)
			{
				ForceFlatOpenPosition("SessionEndFlat");
				flatBeforeCloseDone = true;
				ResetSetupState();
				return;
			}

			if (IsAtOrAfterFlatTime())
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					ForceFlatOpenPosition("FlatBeforeClose");
				flatBeforeCloseDone = true;
				ResetSetupState();
				return;
			}

			if (CloseBeforeNews && IsInNewsCloseWindow() && Position.MarketPosition != MarketPosition.Flat)
			{
				ForceFlatOpenPosition("NewsClose");
				ResetSetupState();
				return;
			}

			UpdateDailyLossLimit();
			UpdateDailyProfitTarget();

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageOpenPosition();
				return;
			}

			if (!CanEnterNewTrade())
				return;

			UpdateSetupStateMachine();
		}

		private void ValidateTickChart()
		{
			if (tickChartWarningShown)
				return;

			if (BarsPeriod.BarsPeriodType != BarsPeriodType.Tick)
				Print(string.Format("{0}: Navrzeno pro TICK 125 graf, aktuálně: {1}.", Name, BarsPeriod.BarsPeriodType));
			else if (BarsPeriod.Value != 125)
				Print(string.Format("{0}: Doporučeno TICK 125, aktuálně: {1} tick.", Name, BarsPeriod.Value));

			tickChartWarningShown = true;
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

		#region Session / risk
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
			dailyProfitTargetHit = false;
			flatBeforeCloseDone = false;
			dailyTradeCount = 0;
			dailyConsecutiveLosses = 0;
			cooldownUntilBar = 0;
			sessionRealizedStartPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			ResetSetupState();
		}

		private void ResetSessionState(bool fullReset)
		{
			setupState = SetupState.Idle;
			armedBarIndex = -1;
			entryPrice = 0;
			highestPriceSinceEntry = 0;
			lowestPriceSinceEntry = 0;
			breakEvenApplied = false;
			currentStopPrice = 0;
			cumulativeTypicalPriceVolume = 0;
			cumulativeVolume = 0;
			sessionVwap = 0;
			dailyTradeCount = 0;
			dailyConsecutiveLosses = 0;
			cooldownUntilBar = 0;
			entrySessionDate = DateTime.MinValue;

			if (fullReset)
			{
				lastSessionDate = DateTime.MinValue;
				dailyLossLimitHit = false;
				dailyProfitTargetHit = false;
				flatBeforeCloseDone = false;
				tickChartWarningShown = false;
			}
		}

		private void ResetSetupState()
		{
			setupState = SetupState.Idle;
			armedBarIndex = -1;
		}

		private double GetDailyPnL()
		{
			double realized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionRealizedStartPnL;
			double unrealized = Position.MarketPosition == MarketPosition.Flat
				? 0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
			return realized + unrealized;
		}

		private void UpdateDailyLossLimit()
		{
			if (!dailyLossLimitHit && GetDailyPnL() <= -Math.Abs(DailyLossLimit))
				dailyLossLimitHit = true;
		}

		private void UpdateDailyProfitTarget()
		{
			if (DailyProfitTarget <= 0 || dailyProfitTargetHit)
				return;
			if (GetDailyPnL() >= DailyProfitTarget)
				dailyProfitTargetHit = true;
		}

		private bool CanEnterNewTrade()
		{
			if (dailyLossLimitHit || dailyProfitTargetHit || flatBeforeCloseDone)
				return false;
			if (MaxTradesPerDay > 0 && dailyTradeCount >= MaxTradesPerDay)
				return false;
			if (CooldownBarsAfterLoss > 0 && CurrentBar < cooldownUntilBar)
				return false;
			if (MaxConsecutiveLossesPerDay > 0 && dailyConsecutiveLosses >= MaxConsecutiveLossesPerDay)
				return false;
			if (!IsWithinEntryWindow())
				return false;
			if (IsBlockedEntryHour())
				return false;
			if (IsInNewsBlockWindow())
				return false;
			return true;
		}

		private void ParseBlockedEntryHours()
		{
			if (BlockedEntryHours == lastParsedBlockedHours)
				return;
			lastParsedBlockedHours = BlockedEntryHours;
			blockedEntryHours = new List<int>();
			if (string.IsNullOrWhiteSpace(BlockedEntryHours))
				return;
			foreach (string part in BlockedEntryHours.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part.Trim(), out int hour) && hour >= 0 && hour <= 23)
					blockedEntryHours.Add(hour);
			}
		}

		private bool IsBlockedEntryHour()
		{
			ParseBlockedEntryHours();
			if (blockedEntryHours == null || blockedEntryHours.Count == 0)
				return false;
			return blockedEntryHours.Contains(Time[0].Hour);
		}

		private bool IsWithinEntryWindow()
		{
			TimeSpan now = Time[0].TimeOfDay;
			TimeSpan start = EntryStartTime.TimeOfDay.Add(TimeSpan.FromMinutes(SkipFirstMinutes));
			TimeSpan end = EntryEndTime.TimeOfDay;
			return now >= start && now <= end;
		}

		private bool IsAtOrAfterFlatTime() => Time[0].TimeOfDay >= FlatTime.TimeOfDay;
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
			foreach (string part in NewsTimes.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
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

		#region Signály
		private bool IsLongTrend() => Close[0] > sessionVwap && Close[0] > ema[0];
		private bool IsShortTrend() => Close[0] < sessionVwap && Close[0] < ema[0];

		private bool HasSustainedLongTrend()
		{
			if (MinTrendBars <= 0)
				return IsLongTrend();
			if (CurrentBar < MinTrendBars)
				return false;
			for (int i = 1; i <= MinTrendBars; i++)
				if (Close[i] <= ema[i])
					return false;
			return IsLongTrend();
		}

		private bool HasSustainedShortTrend()
		{
			if (MinTrendBars <= 0)
				return IsShortTrend();
			if (CurrentBar < MinTrendBars)
				return false;
			for (int i = 1; i <= MinTrendBars; i++)
				if (Close[i] >= ema[i])
					return false;
			return IsShortTrend();
		}

		private bool HasLongEmaSlope() => !RequireEmaSlope || ema[0] > ema[1];
		private bool HasShortEmaSlope() => !RequireEmaSlope || ema[0] < ema[1];

		private bool IsValidLongVwapTouch()
		{
			if (Low[0] > sessionVwap)
				return false;

			double minClose = AllowLooseVwapTouch
				? sessionVwap - LooseVwapCloseTicks * TickSize
				: sessionVwap;

			if (Close[0] < minClose)
				return false;

			double penetration = (sessionVwap - Low[0]) / TickSize;
			return penetration <= MaxVwapPenetrationTicks;
		}

		private bool IsValidShortVwapTouch()
		{
			if (High[0] < sessionVwap)
				return false;

			double maxClose = AllowLooseVwapTouch
				? sessionVwap + LooseVwapCloseTicks * TickSize
				: sessionVwap;

			if (Close[0] > maxClose)
				return false;

			double penetration = (High[0] - sessionVwap) / TickSize;
			return penetration <= MaxVwapPenetrationTicks;
		}

		private bool IsBullishConfirmationBar()
		{
			if (Close[0] <= Open[0] || Close[0] <= sessionVwap)
				return false;
			if ((Close[0] - Open[0]) / TickSize < MinConfirmBodyTicks)
				return false;
			if (RequireCloseBeyondPriorBar && CurrentBar >= 1 && Close[0] <= High[1])
				return false;
			return true;
		}

		private bool IsBearishConfirmationBar()
		{
			if (Close[0] >= Open[0] || Close[0] >= sessionVwap)
				return false;
			if ((Open[0] - Close[0]) / TickSize < MinConfirmBodyTicks)
				return false;
			if (RequireCloseBeyondPriorBar && CurrentBar >= 1 && Close[0] >= Low[1])
				return false;
			return true;
		}

		private bool TryEnterLong()
		{
			EnterLong(Contracts, "LongEntry");
			InitializeTradeState(Close[0]);
			dailyTradeCount++;
			ResetSetupState();
			return true;
		}

		private bool TryEnterShort()
		{
			EnterShort(Contracts, "ShortEntry");
			InitializeTradeState(Close[0]);
			dailyTradeCount++;
			ResetSetupState();
			return true;
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

			if (EnableLong && setupState == SetupState.Idle
				&& IsLongTrend() && HasSustainedLongTrend() && HasLongEmaSlope() && IsValidLongVwapTouch())
			{
				if (AllowSameBarEntry && IsBullishConfirmationBar())
				{
					TryEnterLong();
					return;
				}
				setupState = SetupState.ArmedLong;
				armedBarIndex = CurrentBar;
				return;
			}

			if (EnableShort && setupState == SetupState.Idle
				&& IsShortTrend() && HasSustainedShortTrend() && HasShortEmaSlope() && IsValidShortVwapTouch())
			{
				if (AllowSameBarEntry && IsBearishConfirmationBar())
				{
					TryEnterShort();
					return;
				}
				setupState = SetupState.ArmedShort;
				armedBarIndex = CurrentBar;
				return;
			}

			if (setupState == SetupState.ArmedLong && armedBarIndex >= 0 && CurrentBar > armedBarIndex && IsBullishConfirmationBar())
				TryEnterLong();

			if (setupState == SetupState.ArmedShort && armedBarIndex >= 0 && CurrentBar > armedBarIndex && IsBearishConfirmationBar())
				TryEnterShort();
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
				entrySessionDate = time.Date;
				return;
			}

			bool isExit = execution.Order.Name == "Stop loss" || execution.Order.Name == "Profit target"
				|| execution.Order.Name == "FlatBeforeClose" || execution.Order.Name == "NewsClose"
				|| execution.Order.Name == "SessionOpenFlat" || execution.Order.Name == "SessionEndFlat";
			if (!isExit || marketPosition != MarketPosition.Flat)
				return;

			entrySessionDate = DateTime.MinValue;

			bool isLoss = (execution.Order.OrderAction == OrderAction.Sell && price < entryPrice)
				|| (execution.Order.OrderAction == OrderAction.BuyToCover && price > entryPrice);

			if (isLoss)
			{
				dailyConsecutiveLosses++;
				if (CooldownBarsAfterLoss > 0)
					cooldownUntilBar = CurrentBar + CooldownBarsAfterLoss;
			}
			else
			{
				dailyConsecutiveLosses = 0;
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
					newStop = Math.Max(newStop, highestPriceSinceEntry - TrailTicks * tickSize);
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
			if (candidateStop <= 0) return;
			candidateStop = Math.Max(candidateStop, entryPrice - StopLossTicks * TickSize);
			if (currentStopPrice <= 0 || candidateStop > currentStopPrice)
			{
				SetStopLoss("LongEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}
		}

		private void ApplyStopForShort(double candidateStop)
		{
			if (candidateStop <= 0) return;
			candidateStop = Math.Min(candidateStop, entryPrice + StopLossTicks * TickSize);
			if (currentStopPrice <= 0 || candidateStop < currentStopPrice)
			{
				SetStopLoss("ShortEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}
		}

		private void ForceFlatOpenPosition(string signalName)
		{
			if (Position.MarketPosition == MarketPosition.Flat)
				return;

			CancelWorkingOrders();
			ExitAllPositions(signalName);
		}

		private void CancelWorkingOrders()
		{
			foreach (Order order in Account.Orders)
			{
				if (order == null || order.Instrument != Instrument)
					continue;

				if (order.OrderState == OrderState.Working
					|| order.OrderState == OrderState.Accepted
					|| order.OrderState == OrderState.Submitted
					|| order.OrderState == OrderState.PartFilled)
					CancelOrder(order);
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
		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Contracts", Order = 1, GroupName = "1. Obchod")]
		public int Contracts { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "1. Obchod")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "1. Obchod")]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "EMA Period", Order = 4, GroupName = "1. Obchod")]
		public int EmaPeriod { get; set; }

		[NinjaScriptProperty][Display(Name = "Enable Long", Order = 5, GroupName = "1. Obchod")]
		public bool EnableLong { get; set; }

		[NinjaScriptProperty][Display(Name = "Enable Short", Order = 6, GroupName = "1. Obchod")]
		public bool EnableShort { get; set; }

		[NinjaScriptProperty][Range(1, 50)]
		[Display(Name = "Max Bars After Touch", Order = 7, GroupName = "1. Obchod")]
		public int MaxBarsAfterTouch { get; set; }

		[NinjaScriptProperty][Range(0, 20)]
		[Display(Name = "Min Trend Bars", Order = 1, GroupName = "2. Frekvence / filtry")]
		public int MinTrendBars { get; set; }

		[NinjaScriptProperty][Display(Name = "Require EMA Slope", Order = 2, GroupName = "2. Frekvence / filtry")]
		public bool RequireEmaSlope { get; set; }

		[NinjaScriptProperty][Range(1, 50)]
		[Display(Name = "Max VWAP Penetration (ticks)", Order = 3, GroupName = "2. Frekvence / filtry")]
		public int MaxVwapPenetrationTicks { get; set; }

		[NinjaScriptProperty][Range(1, 50)]
		[Display(Name = "Min Confirm Body (ticks)", Order = 4, GroupName = "2. Frekvence / filtry")]
		public int MinConfirmBodyTicks { get; set; }

		[NinjaScriptProperty][Display(Name = "Close Beyond Prior Bar", Order = 5, GroupName = "2. Frekvence / filtry")]
		public bool RequireCloseBeyondPriorBar { get; set; }

		[NinjaScriptProperty][Display(Name = "Allow Same Bar Entry", Description = "Vstup na touch svíčce, pokud je zároveň potvrzení.", Order = 6, GroupName = "2. Frekvence / filtry")]
		public bool AllowSameBarEntry { get; set; }

		[NinjaScriptProperty][Display(Name = "Allow Loose VWAP Touch", Order = 7, GroupName = "2. Frekvence / filtry")]
		public bool AllowLooseVwapTouch { get; set; }

		[NinjaScriptProperty][Range(0, 20)]
		[Display(Name = "Loose VWAP Close (ticks)", Order = 8, GroupName = "2. Frekvence / filtry")]
		public int LooseVwapCloseTicks { get; set; }

		[NinjaScriptProperty][Display(Name = "Enable Trailing", Order = 1, GroupName = "3. Trailing")]
		public bool EnableTrailing { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Break-Even Trigger (ticks)", Order = 2, GroupName = "3. Trailing")]
		public int BreakEvenTriggerTicks { get; set; }

		[NinjaScriptProperty][Range(0, int.MaxValue)]
		[Display(Name = "Break-Even Offset (ticks)", Order = 3, GroupName = "3. Trailing")]
		public int BreakEvenOffsetTicks { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Trail (ticks)", Order = 4, GroupName = "3. Trailing")]
		public int TrailTicks { get; set; }

		[NinjaScriptProperty][Range(1, double.MaxValue)]
		[Display(Name = "Daily Loss Limit ($)", Order = 1, GroupName = "4. Risk")]
		public double DailyLossLimit { get; set; }

		[NinjaScriptProperty][Range(0, double.MaxValue)]
		[Display(Name = "Daily Profit Target ($)", Description = "0 = vypnuto. Po dosažení se blokují nové vstupy do konce dne.", Order = 2, GroupName = "4. Risk")]
		public double DailyProfitTarget { get; set; }

		[NinjaScriptProperty][Range(0, 50)]
		[Display(Name = "Max Trades Per Day", Description = "0 = bez limitu.", Order = 3, GroupName = "4. Risk")]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty][Range(0, 10)]
		[Display(Name = "Max Consecutive Losses/Day", Description = "0 = vypnuto. Po N ztrátách stop vstupů do konce dne.", Order = 4, GroupName = "4. Risk")]
		public int MaxConsecutiveLossesPerDay { get; set; }

		[NinjaScriptProperty][Range(0, 100)]
		[Display(Name = "Cooldown Bars After Loss", Order = 5, GroupName = "4. Risk")]
		public int CooldownBarsAfterLoss { get; set; }

		[NinjaScriptProperty][Range(0, 120)]
		[Display(Name = "Skip First Minutes", Order = 6, GroupName = "4. Risk")]
		public int SkipFirstMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Blocked Entry Hours", Description = "Hodiny bez vstupů, např. 17,19 (čas grafu).", Order = 7, GroupName = "4. Risk")]
		public string BlockedEntryHours { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry Start Time", Order = 1, GroupName = "5. Čas")]
		public DateTime EntryStartTime { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry End Time", Order = 2, GroupName = "5. Čas")]
		public DateTime EntryEndTime { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Flat Time", Order = 3, GroupName = "5. Čas")]
		public DateTime FlatTime { get; set; }

		[NinjaScriptProperty][Display(Name = "News Times", Order = 1, GroupName = "6. News Filter")]
		public string NewsTimes { get; set; }

		[NinjaScriptProperty][Range(0, 120)]
		[Display(Name = "Block Minutes Before", Order = 2, GroupName = "6. News Filter")]
		public int NewsBlockMinutesBefore { get; set; }

		[NinjaScriptProperty][Range(0, 120)]
		[Display(Name = "Block Minutes After", Order = 3, GroupName = "6. News Filter")]
		public int NewsBlockMinutesAfter { get; set; }

		[NinjaScriptProperty][Display(Name = "Close Before News", Order = 4, GroupName = "6. News Filter")]
		public bool CloseBeforeNews { get; set; }
		#endregion
	}
}
