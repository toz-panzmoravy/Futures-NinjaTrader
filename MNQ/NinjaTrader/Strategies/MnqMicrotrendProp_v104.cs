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
	/// MNQ Microtrend Prop v1.04 – v1.03 + PullbackBreak + L+S (viz v105, pokud NT cache starou v104).
	/// Backtest Pullback103 6M2026: PF 1.22, net +$2 050, ~274 trades (bez comm.).
	/// </summary>
	public class MnqMicrotrendProp_v104 : Strategy
	{
		public enum TrendRideEntryMode
		{
			PriorBarBreak,
			SwingBreak,
			PullbackBreak
		}

		public enum SessionBiasMode
		{
			Off,
			VwapAboveOpen,
			VwapBelowOpen
		}

		#region Private fields
		private EMA emaFast;
		private EMA emaSlow;

		private double cumulativeTPV;
		private double cumulativeVol;
		private double sessionVwap;
		private double sessionOpen;

		private int lastEntryBar;
		private double entryPrice;
		private bool breakEvenApplied;

		private DateTime lastSessionDate;
		private bool dailyLossLimitHit;
		private bool flatDoneToday;
		private double sessionStartPnL;
		private int dailyTradeCount;
		private int dailyConsecLosses;
		private int cooldownUntilBar;

		private List<int> blockedHours;
		private string lastParsedBlockedHours;
		private List<TimeSpan> newsTimes;
		private string lastParsedNewsTimes;
		private bool tickWarnShown;
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MNQ Microtrend v1.04 – PullbackBreak L+S, SL 130/PT 182, entry do 20:30 SEČ.";
				Name = "MnqMicrotrendProp_v104";
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
				BarsRequiredToTrade = 25;
				IsInstantiatedOnEachOptimizationIteration = true;

				Contracts         = 1;
				StopLossTicks     = 130;
				ProfitTargetTicks = 182;
				EmaFastPeriod     = 9;
				EmaSlowPeriod     = 21;

				EnableLong  = true;
				EnableShort = true;

				EntryMode              = TrendRideEntryMode.PullbackBreak;
				SwingLookbackBars      = 4;
				PriorBarMarginTicks    = 1;
				PullbackTouchTicks     = 3;
				RequireTrendBar        = true;
				MinTrendBodyTicks      = 2;
				MinBarsBetweenEntries  = 4;
				SessionBias            = SessionBiasMode.VwapAboveOpen;

				EnableBreakEven       = false;
				BreakEvenTriggerTicks = 110;
				BreakEvenOffsetTicks  = 4;

				DailyLossLimit        = 275;
				MaxTradesPerDay       = 3;
				MaxConsecLossesPerDay = 3;
				CooldownBarsAfterLoss = 3;
				SkipFirstMinutes      = 3;
				BlockedEntryHours     = "17,19";

				EntryStartTime = DateTime.Parse("15:30", CultureInfo.InvariantCulture);
				EntryEndTime   = DateTime.Parse("20:30", CultureInfo.InvariantCulture);
				FlatTime       = DateTime.Parse("21:55", CultureInfo.InvariantCulture);

				NewsTimes          = string.Empty;
				NewsBlockMinBefore = 5;
				NewsBlockMinAfter  = 5;
				CloseBeforeNews    = true;
			}
			else if (State == State.Configure)
			{
				SetStopLoss("Long",     CalculationMode.Ticks, StopLossTicks,     false);
				SetProfitTarget("Long", CalculationMode.Ticks, ProfitTargetTicks);
				SetStopLoss("Short",    CalculationMode.Ticks, StopLossTicks,     false);
				SetProfitTarget("Short", CalculationMode.Ticks, ProfitTargetTicks);
			}
			else if (State == State.DataLoaded)
			{
				emaFast = EMA(Close, EmaFastPeriod);
				emaSlow = EMA(Close, EmaSlowPeriod);
				FullReset();
			}
		}
		#endregion

		#region OnBarUpdate
		protected override void OnBarUpdate()
		{
			WarnSetup();
			if (CurrentBar < BarsRequiredToTrade) return;

			UpdateSessionVwap();
			HandleDayRollover();
			if (Bars.IsFirstBarOfSession) NewDayRisk();

			if (Time[0].TimeOfDay >= FlatTime.TimeOfDay)
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					ExitAll("FlatBeforeClose");
				flatDoneToday = true;
				return;
			}

			if (CloseBeforeNews && InNewsCloseWindow() && Position.MarketPosition != MarketPosition.Flat)
			{
				ExitAll("NewsClose");
				return;
			}

			CheckDailyLoss();

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageBreakEven();
				return;
			}

			if (!CanEnter()) return;
			if (MinBarsBetweenEntries > 0 && lastEntryBar >= 0
				&& CurrentBar - lastEntryBar < MinBarsBetweenEntries)
				return;

			TryEntry();
		}

		private void WarnSetup()
		{
			if (tickWarnShown) return;
			if (BarsPeriod.BarsPeriodType != BarsPeriodType.Tick)
			{
				Print(string.Format("{0}: Navržen MNQ TICK 200 graf.", Name));
				tickWarnShown = true;
			}
		}
		#endregion

		#region Session VWAP (volitelný bias)
		private void UpdateSessionVwap()
		{
			if (Bars.IsFirstBarOfSession)
			{
				cumulativeTPV = 0;
				cumulativeVol = 0;
				sessionOpen   = Open[0];
			}
			double tp  = (High[0] + Low[0] + Close[0]) / 3.0;
			double vol = Volume[0] > 0 ? Volume[0] : 1;
			cumulativeTPV += tp * vol;
			cumulativeVol += vol;
			sessionVwap = cumulativeTPV / cumulativeVol;
		}

		private bool AllowsLong()
		{
			if (SessionBias == SessionBiasMode.Off) return true;
			if (SessionBias == SessionBiasMode.VwapAboveOpen) return sessionVwap > sessionOpen;
			return sessionVwap < sessionOpen;
		}

		private bool AllowsShort()
		{
			if (SessionBias == SessionBiasMode.Off) return true;
			if (SessionBias == SessionBiasMode.VwapBelowOpen) return sessionVwap < sessionOpen;
			return sessionVwap > sessionOpen;
		}
		#endregion

		#region Signály – ride the microtrend
		private bool IsUptrend()
		{
			return emaFast[0] > emaSlow[0] && Close[0] > emaFast[0];
		}

		private bool IsDowntrend()
		{
			return emaFast[0] < emaSlow[0] && Close[0] < emaFast[0];
		}

		private bool IsTrendBarLong()
		{
			if (!RequireTrendBar) return true;
			if (Close[0] <= Open[0]) return false;
			return MinTrendBodyTicks <= 0
				|| (Close[0] - Open[0]) / TickSize >= MinTrendBodyTicks;
		}

		private bool IsTrendBarShort()
		{
			if (!RequireTrendBar) return true;
			if (Close[0] >= Open[0]) return false;
			return MinTrendBodyTicks <= 0
				|| (Open[0] - Close[0]) / TickSize >= MinTrendBodyTicks;
		}

		private bool BreaksPriorBarHigh()
		{
			return Close[0] > High[1] + PriorBarMarginTicks * TickSize;
		}

		private bool BreaksPriorBarLow()
		{
			return Close[0] < Low[1] - PriorBarMarginTicks * TickSize;
		}

		private bool BreaksSwingHigh()
		{
			if (CurrentBar < SwingLookbackBars) return false;
			double swingHigh = High[1];
			for (int i = 2; i <= SwingLookbackBars; i++)
				swingHigh = Math.Max(swingHigh, High[i]);
			return Close[0] > swingHigh + PriorBarMarginTicks * TickSize;
		}

		private bool BreaksSwingLow()
		{
			if (CurrentBar < SwingLookbackBars) return false;
			double swingLow = Low[1];
			for (int i = 2; i <= SwingLookbackBars; i++)
				swingLow = Math.Min(swingLow, Low[i]);
			return Close[0] < swingLow - PriorBarMarginTicks * TickSize;
		}

		private bool HadPullbackToEmaLong()
		{
			if (CurrentBar < 2) return false;
			double tol = PullbackTouchTicks * TickSize;
			return Low[1] <= emaFast[1] + tol || Low[2] <= emaFast[2] + tol;
		}

		private bool HadPullbackToEmaShort()
		{
			if (CurrentBar < 2) return false;
			double tol = PullbackTouchTicks * TickSize;
			return High[1] >= emaFast[1] - tol || High[2] >= emaFast[2] - tol;
		}

		private bool IsLongSignal()
		{
			if (!AllowsLong() || !IsUptrend() || !IsTrendBarLong()) return false;

			switch (EntryMode)
			{
				case TrendRideEntryMode.PriorBarBreak:
					return BreaksPriorBarHigh();
				case TrendRideEntryMode.SwingBreak:
					return BreaksSwingHigh();
				case TrendRideEntryMode.PullbackBreak:
					return HadPullbackToEmaLong() && BreaksPriorBarHigh();
				default:
					return false;
			}
		}

		private bool IsShortSignal()
		{
			if (!AllowsShort() || !IsDowntrend() || !IsTrendBarShort()) return false;

			switch (EntryMode)
			{
				case TrendRideEntryMode.PriorBarBreak:
					return BreaksPriorBarLow();
				case TrendRideEntryMode.SwingBreak:
					return BreaksSwingLow();
				case TrendRideEntryMode.PullbackBreak:
					return HadPullbackToEmaShort() && BreaksPriorBarLow();
				default:
					return false;
			}
		}

		private void TryEntry()
		{
			if (EnableLong && IsLongSignal())
			{
				EnterLong(Contracts, "Long");
				RecordEntry();
				return;
			}

			if (EnableShort && IsShortSignal())
			{
				EnterShort(Contracts, "Short");
				RecordEntry();
			}
		}

		private void RecordEntry()
		{
			lastEntryBar = CurrentBar;
			dailyTradeCount++;
		}
		#endregion

		#region Trade management
		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null || execution.Order.OrderState != OrderState.Filled) return;

			if (execution.Order.Name == "Long" || execution.Order.Name == "Short")
			{
				entryPrice       = price;
				breakEvenApplied = false;
				return;
			}

			bool isExit = execution.Order.Name == "Stop loss"
				|| execution.Order.Name == "Profit target"
				|| execution.Order.Name == "FlatBeforeClose"
				|| execution.Order.Name == "NewsClose";
			if (!isExit || marketPosition != MarketPosition.Flat) return;

			bool isLoss = (execution.Order.OrderAction == OrderAction.Sell && price < entryPrice)
			           || (execution.Order.OrderAction == OrderAction.BuyToCover && price > entryPrice);
			if (isLoss)
			{
				dailyConsecLosses++;
				if (CooldownBarsAfterLoss > 0)
					cooldownUntilBar = CurrentBar + CooldownBarsAfterLoss;
			}
			else
			{
				dailyConsecLosses = 0;
			}
		}

		private void ManageBreakEven()
		{
			if (!EnableBreakEven || breakEvenApplied) return;
			double pt = Position.MarketPosition == MarketPosition.Long
				? (Close[0] - entryPrice) / TickSize
				: (entryPrice - Close[0]) / TickSize;
			if (pt < BreakEvenTriggerTicks) return;

			string sig = Position.MarketPosition == MarketPosition.Long ? "Long" : "Short";
			double be = Position.MarketPosition == MarketPosition.Long
				? entryPrice + BreakEvenOffsetTicks * TickSize
				: entryPrice - BreakEvenOffsetTicks * TickSize;
			SetStopLoss(sig, CalculationMode.Price, be, false);
			breakEvenApplied = true;
		}

		private void ExitAll(string signal)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong(signal, "Long");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(signal, "Short");
		}
		#endregion

		#region Risk
		private void HandleDayRollover()
		{
			if (Time[0].Date == lastSessionDate) return;
			lastSessionDate = Time[0].Date;
			NewDayRisk();
		}

		private void NewDayRisk()
		{
			dailyLossLimitHit = false;
			flatDoneToday     = false;
			dailyTradeCount   = 0;
			dailyConsecLosses = 0;
			cooldownUntilBar  = 0;
			sessionStartPnL   = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
		}

		private void FullReset()
		{
			lastSessionDate  = DateTime.MinValue;
			lastEntryBar     = -1;
			entryPrice       = 0;
			breakEvenApplied = false;
			tickWarnShown    = false;
		}

		private void CheckDailyLoss()
		{
			if (dailyLossLimitHit) return;
			double pnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartPnL;
			if (Position.MarketPosition != MarketPosition.Flat)
				pnl += Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
			if (pnl <= -Math.Abs(DailyLossLimit)) dailyLossLimitHit = true;
		}

		private bool CanEnter()
		{
			if (dailyLossLimitHit || flatDoneToday) return false;
			if (MaxTradesPerDay > 0 && dailyTradeCount >= MaxTradesPerDay) return false;
			if (cooldownUntilBar > 0 && CurrentBar < cooldownUntilBar) return false;
			if (MaxConsecLossesPerDay > 0 && dailyConsecLosses >= MaxConsecLossesPerDay) return false;
			TimeSpan now = Time[0].TimeOfDay;
			if (now < EntryStartTime.TimeOfDay.Add(TimeSpan.FromMinutes(SkipFirstMinutes))) return false;
			if (now > EntryEndTime.TimeOfDay) return false;
			if (InBlockedHour()) return false;
			if (InNewsBlockWindow()) return false;
			return true;
		}

		private void ParseBlockedHours()
		{
			if (BlockedEntryHours == lastParsedBlockedHours) return;
			lastParsedBlockedHours = BlockedEntryHours;
			blockedHours = new List<int>();
			if (string.IsNullOrWhiteSpace(BlockedEntryHours)) return;
			foreach (string p in BlockedEntryHours.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
				if (int.TryParse(p.Trim(), out int h) && h >= 0 && h <= 23) blockedHours.Add(h);
		}

		private bool InBlockedHour()
		{
			ParseBlockedHours();
			return blockedHours != null && blockedHours.Contains(Time[0].Hour);
		}

		private void ParseNewsTimes()
		{
			if (NewsTimes == lastParsedNewsTimes) return;
			lastParsedNewsTimes = NewsTimes;
			newsTimes = new List<TimeSpan>();
			if (string.IsNullOrWhiteSpace(NewsTimes)) return;
			foreach (string p in NewsTimes.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (TimeSpan.TryParse(p.Trim(), out TimeSpan ts)) newsTimes.Add(ts);
				else if (DateTime.TryParse(p.Trim(), out DateTime dt)) newsTimes.Add(dt.TimeOfDay);
			}
		}

		private bool InNewsBlockWindow()
		{
			ParseNewsTimes();
			if (newsTimes == null || newsTimes.Count == 0) return false;
			TimeSpan now = Time[0].TimeOfDay;
			foreach (TimeSpan nt in newsTimes)
				if (now >= nt.Subtract(TimeSpan.FromMinutes(NewsBlockMinBefore))
				 && now <= nt.Add(TimeSpan.FromMinutes(NewsBlockMinAfter))) return true;
			return false;
		}

		private bool InNewsCloseWindow()
		{
			ParseNewsTimes();
			if (newsTimes == null || newsTimes.Count == 0) return false;
			TimeSpan now = Time[0].TimeOfDay;
			foreach (TimeSpan nt in newsTimes)
				if (now >= nt.Subtract(TimeSpan.FromMinutes(NewsBlockMinBefore)) && now < nt) return true;
			return false;
		}
		#endregion

		#region Properties – 1. Obchod
		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Contracts", Order = 1, GroupName = "1. Obchod")]
		public int Contracts { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "1. Obchod")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "Profit Target (ticks)", Order = 3, GroupName = "1. Obchod")]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty][Range(2, 50)]
		[Display(Name = "EMA Fast", Order = 4, GroupName = "1. Obchod")]
		public int EmaFastPeriod { get; set; }

		[NinjaScriptProperty][Range(5, 100)]
		[Display(Name = "EMA Slow", Order = 5, GroupName = "1. Obchod")]
		public int EmaSlowPeriod { get; set; }

		[NinjaScriptProperty][Display(Name = "Enable Long", Order = 6, GroupName = "1. Obchod")]
		public bool EnableLong { get; set; }

		[NinjaScriptProperty][Display(Name = "Enable Short", Order = 7, GroupName = "1. Obchod")]
		public bool EnableShort { get; set; }
		#endregion

		#region Properties – 2. Signál
		[NinjaScriptProperty]
		[Display(Name = "Entry Mode", Description = "PriorBarBreak = break high/low předchozího baru ve směru trendu.", Order = 1, GroupName = "2. Signál")]
		public TrendRideEntryMode EntryMode { get; set; }

		[NinjaScriptProperty][Range(2, 12)]
		[Display(Name = "Swing Lookback Bars", Order = 2, GroupName = "2. Signál")]
		public int SwingLookbackBars { get; set; }

		[NinjaScriptProperty][Range(0, 5)]
		[Display(Name = "Break Margin (ticks)", Order = 3, GroupName = "2. Signál")]
		public int PriorBarMarginTicks { get; set; }

		[NinjaScriptProperty][Range(1, 10)]
		[Display(Name = "Pullback Touch (ticks)", Description = "PullbackBreak: dotyk EMA9 tolerance.", Order = 4, GroupName = "2. Signál")]
		public int PullbackTouchTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Require Trend Bar", Description = "Bar ve směru obchodu (close > open pro long).", Order = 5, GroupName = "2. Signál")]
		public bool RequireTrendBar { get; set; }

		[NinjaScriptProperty][Range(0, 5)]
		[Display(Name = "Min Trend Body (ticks)", Order = 6, GroupName = "2. Signál")]
		public int MinTrendBodyTicks { get; set; }

		[NinjaScriptProperty][Range(0, 20)]
		[Display(Name = "Min Bars Between Entries", Order = 7, GroupName = "2. Signál")]
		public int MinBarsBetweenEntries { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Session Bias", Description = "Off = jeď čistý mikrotrend bez VWAP brány.", Order = 8, GroupName = "2. Signál")]
		public SessionBiasMode SessionBias { get; set; }
		#endregion

		#region Properties – 3. Výstupy
		[NinjaScriptProperty][Display(Name = "Enable Break-Even", Order = 1, GroupName = "3. Výstupy")]
		public bool EnableBreakEven { get; set; }

		[NinjaScriptProperty][Range(1, int.MaxValue)]
		[Display(Name = "BE Trigger (ticks)", Order = 2, GroupName = "3. Výstupy")]
		public int BreakEvenTriggerTicks { get; set; }

		[NinjaScriptProperty][Range(0, int.MaxValue)]
		[Display(Name = "BE Offset (ticks)", Order = 3, GroupName = "3. Výstupy")]
		public int BreakEvenOffsetTicks { get; set; }
		#endregion

		#region Properties – 4. Prop risk
		[NinjaScriptProperty][Range(0, double.MaxValue)]
		[Display(Name = "Daily Loss Limit ($)", Order = 1, GroupName = "4. Prop risk")]
		public double DailyLossLimit { get; set; }

		[NinjaScriptProperty][Range(0, 20)]
		[Display(Name = "Max Trades / Day", Order = 2, GroupName = "4. Prop risk")]
		public int MaxTradesPerDay { get; set; }

		[NinjaScriptProperty][Range(0, 10)]
		[Display(Name = "Max Consec. Losses / Day", Order = 3, GroupName = "4. Prop risk")]
		public int MaxConsecLossesPerDay { get; set; }

		[NinjaScriptProperty][Range(0, 50)]
		[Display(Name = "Cooldown Bars After Loss", Order = 4, GroupName = "4. Prop risk")]
		public int CooldownBarsAfterLoss { get; set; }

		[NinjaScriptProperty][Range(0, 60)]
		[Display(Name = "Skip First Minutes", Order = 5, GroupName = "4. Prop risk")]
		public int SkipFirstMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Blocked Hours", Order = 6, GroupName = "4. Prop risk")]
		public string BlockedEntryHours { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry Start", Order = 7, GroupName = "4. Prop risk")]
		public DateTime EntryStartTime { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry End", Order = 8, GroupName = "4. Prop risk")]
		public DateTime EntryEndTime { get; set; }

		[NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Flat Time", Order = 9, GroupName = "4. Prop risk")]
		public DateTime FlatTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "News Times", Order = 10, GroupName = "4. Prop risk")]
		public string NewsTimes { get; set; }

		[NinjaScriptProperty][Range(0, 120)]
		[Display(Name = "News Block Before (min)", Order = 11, GroupName = "4. Prop risk")]
		public int NewsBlockMinBefore { get; set; }

		[NinjaScriptProperty][Range(0, 120)]
		[Display(Name = "News Block After (min)", Order = 12, GroupName = "4. Prop risk")]
		public int NewsBlockMinAfter { get; set; }

		[NinjaScriptProperty][Display(Name = "Close Before News", Order = 13, GroupName = "4. Prop risk")]
		public bool CloseBeforeNews { get; set; }
		#endregion
	}
}
