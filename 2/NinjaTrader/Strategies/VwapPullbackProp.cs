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
	/// VWAP Pullback Prop-Trading AOS pro MES.
	/// Obchoduje pullback k session VWAP ve směru trendu (EMA filtr).
	/// Obsahuje prop-trading risk management: denní loss limit, časové filtry, news filter, trailing SL.
	/// </summary>
	public class VwapPullbackProp : Strategy
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
		private bool flatBeforeCloseDone;
		private List<TimeSpan> parsedNewsTimes;
		private string lastParsedNewsTimes;
		#endregion

		#region OnStateChange
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "VWAP Pullback Prop-Trading AOS – trendový pullback k session VWAP s prop risk managementem.";
				Name = "VwapPullbackProp";
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
				BarsRequiredToTrade = 55;
				IsInstantiatedOnEachOptimizationIteration = true;

				// Obchod
				Contracts = 1;
				StopLossTicks = 40;
				ProfitTargetTicks = 80;
				EmaPeriod = 50;
				EnableLong = true;
				EnableShort = true;
				MaxBarsAfterTouch = 5;

				// Trailing
				EnableTrailing = true;
				BreakEvenTriggerTicks = 30;
				BreakEvenOffsetTicks = 4;
				TrailTicks = 25;

				// Risk
				DailyLossLimit = 500;

				// Čas (interpretováno v čase grafu / PC)
				EntryStartTime = DateTime.Parse("15:30", CultureInfo.InvariantCulture);
				EntryEndTime = DateTime.Parse("21:45", CultureInfo.InvariantCulture);
				FlatTime = DateTime.Parse("21:55", CultureInfo.InvariantCulture);

				// News filter
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

			// Flat before close – nekompromisně uzavřít vše ve FlatTime
			if (IsAtOrAfterFlatTime())
			{
				if (Position.MarketPosition != MarketPosition.Flat)
					ExitAllPositions("FlatBeforeClose");

				flatBeforeCloseDone = true;
				ResetSetupState();
				return;
			}

			// News filter – flatten před zprávou
			if (CloseBeforeNews && IsInNewsCloseWindow() && Position.MarketPosition != MarketPosition.Flat)
			{
				ExitAllPositions("NewsClose");
				ResetSetupState();
				return;
			}

			UpdateDailyLossLimit();

			// Trade management pro otevřenou pozici
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				ManageOpenPosition();
				return;
			}

			// Vstupy pouze pokud nejsou aktivní blokace
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
			double barVolume = Volume[0];

			if (barVolume <= 0)
				barVolume = 1;

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
		}

		private double GetDailyPnL()
		{
			double realized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit
				- sessionRealizedStartPnL;

			double unrealized = Position.MarketPosition == MarketPosition.Flat
				? 0
				: Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

			return realized + unrealized;
		}

		private double sessionRealizedStartPnL;

		private void UpdateDailyLossLimit()
		{
			if (dailyLossLimitHit)
				return;

			if (GetDailyPnL() <= -Math.Abs(DailyLossLimit))
				dailyLossLimitHit = true;
		}

		private bool CanEnterNewTrade()
		{
			if (dailyLossLimitHit)
				return false;

			if (flatBeforeCloseDone)
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
			TimeSpan start = EntryStartTime.TimeOfDay;
			TimeSpan end = EntryEndTime.TimeOfDay;
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

		#region Setup state machine
		private bool IsLongTrend()
		{
			return Close[0] > sessionVwap && Close[0] > ema[0];
		}

		private bool IsShortTrend()
		{
			return Close[0] < sessionVwap && Close[0] < ema[0];
		}

		private bool DidTouchVwapFromAbove()
		{
			// Pullback: low se dotkne nebo prorazí VWAP z horní strany trendu
			return Low[0] <= sessionVwap;
		}

		private bool DidTouchVwapFromBelow()
		{
			// Pullback: high se dotkne nebo prorazí VWAP ze spodní strany trendu
			return High[0] >= sessionVwap;
		}

		private bool IsBullishConfirmationBar()
		{
			return Close[0] > Open[0] && Close[0] > sessionVwap;
		}

		private bool IsBearishConfirmationBar()
		{
			return Close[0] < Open[0] && Close[0] < sessionVwap;
		}

		private void UpdateSetupStateMachine()
		{
			// Expirace ozbrojeného setupu
			if (setupState != SetupState.Idle && armedBarIndex >= 0)
			{
				int barsSinceTouch = CurrentBar - armedBarIndex;
				if (barsSinceTouch > MaxBarsAfterTouch)
					ResetSetupState();
			}

			// Rušení setupu při ztrátě trendu
			if (setupState == SetupState.ArmedLong && !IsLongTrend())
				ResetSetupState();

			if (setupState == SetupState.ArmedShort && !IsShortTrend())
				ResetSetupState();

			// LONG: ozbrojení po dotyku VWAP v uptrendu
			if (EnableLong && setupState == SetupState.Idle && IsLongTrend() && DidTouchVwapFromAbove())
			{
				setupState = SetupState.ArmedLong;
				armedBarIndex = CurrentBar;
				return;
			}

			// SHORT: ozbrojení po dotyku VWAP v downtrendu
			if (EnableShort && setupState == SetupState.Idle && IsShortTrend() && DidTouchVwapFromBelow())
			{
				setupState = SetupState.ArmedShort;
				armedBarIndex = CurrentBar;
				return;
			}

			// LONG vstup: první zelená svíčka po dotyku, uzavřená nad VWAP
			if (setupState == SetupState.ArmedLong && armedBarIndex >= 0 && CurrentBar > armedBarIndex)
			{
				if (IsBullishConfirmationBar())
				{
					EnterLong(Contracts, "LongEntry");
					InitializeTradeState(Close[0]);
					ResetSetupState();
				}
			}

			// SHORT vstup: první červená svíčka po dotyku, uzavřená pod VWAP
			if (setupState == SetupState.ArmedShort && armedBarIndex >= 0 && CurrentBar > armedBarIndex)
			{
				if (IsBearishConfirmationBar())
				{
					EnterShort(Contracts, "ShortEntry");
					InitializeTradeState(Close[0]);
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
				InitializeTradeState(price);
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

				newStop = ApplyStopForLong(newStop);
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

				newStop = ApplyStopForShort(newStop);
			}
		}

		private double ApplyStopForLong(double candidateStop)
		{
			if (candidateStop <= 0)
				return currentStopPrice;

			double initialStop = entryPrice - StopLossTicks * TickSize;
			candidateStop = Math.Max(candidateStop, initialStop);

			if (currentStopPrice <= 0 || candidateStop > currentStopPrice)
			{
				SetStopLoss("LongEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}

			return currentStopPrice;
		}

		private double ApplyStopForShort(double candidateStop)
		{
			if (candidateStop <= 0)
				return currentStopPrice;

			double initialStop = entryPrice + StopLossTicks * TickSize;
			candidateStop = Math.Min(candidateStop, initialStop);

			if (currentStopPrice <= 0 || candidateStop < currentStopPrice)
			{
				SetStopLoss("ShortEntry", CalculationMode.Price, candidateStop, false);
				currentStopPrice = candidateStop;
			}

			return currentStopPrice;
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
		[Display(Name = "Contracts", Description = "Počet kontraktů na obchod.", Order = 1, GroupName = "1. Obchod")]
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
		[Display(Name = "Max Bars After Touch", Description = "Max. počet svíček po dotyku VWAP pro potvrzení.", Order = 7, GroupName = "1. Obchod")]
		public int MaxBarsAfterTouch { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Trailing", Order = 1, GroupName = "2. Trailing")]
		public bool EnableTrailing { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Break-Even Trigger (ticks)", Order = 2, GroupName = "2. Trailing")]
		public int BreakEvenTriggerTicks { get; set; }

		[NinjaScriptProperty]
		[Range(0, int.MaxValue)]
		[Display(Name = "Break-Even Offset (ticks)", Description = "Posun SL nad/pod entry pro pokrytí komisí.", Order = 3, GroupName = "2. Trailing")]
		public int BreakEvenOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trail (ticks)", Order = 4, GroupName = "2. Trailing")]
		public int TrailTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Daily Loss Limit ($)", Description = "Při dosažení se blokují nové vstupy do konce dne.", Order = 1, GroupName = "3. Risk")]
		public double DailyLossLimit { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry Start Time", Order = 1, GroupName = "4. Čas")]
		public DateTime EntryStartTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Entry End Time", Order = 2, GroupName = "4. Čas")]
		public DateTime EntryEndTime { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Flat Time", Description = "Všechny pozice se uzavřou v tento čas.", Order = 3, GroupName = "4. Čas")]
		public DateTime FlatTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "News Times", Description = "Časy zpráv oddělené středníkem, např. 14:30;20:00 (čas grafu).", Order = 1, GroupName = "5. News Filter")]
		public string NewsTimes { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Block Minutes Before", Order = 2, GroupName = "5. News Filter")]
		public int NewsBlockMinutesBefore { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "Block Minutes After", Order = 3, GroupName = "5. News Filter")]
		public int NewsBlockMinutesAfter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Close Before News", Description = "Uzavřít otevřené pozice před začátkem news okna.", Order = 4, GroupName = "5. News Filter")]
		public bool CloseBeforeNews { get; set; }
		#endregion
	}
}
