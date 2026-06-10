#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	/// <summary>
	/// MNQ Opening Range Breakout – prop-trading AOS dle specifikace ORB.
	/// Graf: MNQ 1 minuta. Časy v SEČ.
	/// </summary>
	public class MNQ_OrbPropStrategy : Strategy
	{
		private TimeZoneInfo cetZone;

		// Opening Range
		private double orHigh;
		private double orLow;
		private bool orReady;

		// Denní stav
		private DateTime currentDay;
		private bool tradedToday;
		private bool haltedToday;
		private bool flatDoneToday;
		private double dayStartRealizedPnL;

		// Trailing
		private double fillPrice;
		private double extremeSinceFill;
		private bool trailEngaged;
		private double activeStopPrice;

		// News
		private List<TimeSpan> newsSchedule;
		private string cachedNewsInput;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "MNQ ORB Prop AOS – Opening Range Breakout (15:30–16:00 SEČ), max 1 obchod/den.";
				Name = "MNQ_OrbPropStrategy";
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
				BarsRequiredToTrade = 1;
				IsInstantiatedOnEachOptimizationIteration = true;

				Quantity = 1;
				StopLossTicks = 160;
				ProfitTargetTicks = 320;

				OrbStart = ParseTime("15:30");
				OrbEnd = ParseTime("16:00");
				TradeStart = ParseTime("15:30");
				TradeEnd = ParseTime("21:45");
				ForceFlatTime = ParseTime("21:55");

				DailyLossLimitUsd = 500;

				NewsTimesCet = "";
				NewsBufferMinutes = 15;

				TrailActivationTicks = 80;
				TrailOffsetTicks = 40;
			}
			else if (State == State.Configure)
			{
				cetZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

				SetStopLoss("ORB_Long", CalculationMode.Ticks, StopLossTicks, false);
				SetProfitTarget("ORB_Long", CalculationMode.Ticks, ProfitTargetTicks);
				SetStopLoss("ORB_Short", CalculationMode.Ticks, StopLossTicks, false);
				SetProfitTarget("ORB_Short", CalculationMode.Ticks, ProfitTargetTicks);
			}
			else if (State == State.DataLoaded)
			{
				ResetDayState(DateTime.MinValue);
				RebuildNewsSchedule();
			}
		}

		protected override void OnBarUpdate()
		{
			DateTime cet = ToCet(Time[0]);
			EnsureDayRollover(cet);

			if (IsForceFlatTime(cet))
			{
				ClosePosition("EOD_Flat");
				flatDoneToday = true;
				return;
			}

			if (IsNewsWindow(cet))
			{
				ClosePosition("News_Exit");
				return;
			}

			RefreshDailyHalt();

			MeasureOpeningRange(cet);

			if (Position.MarketPosition != MarketPosition.Flat)
			{
				RunTrailingStop();
				return;
			}

			if (haltedToday || flatDoneToday || tradedToday)
				return;

			if (!IsTradeWindow(cet))
				return;

			if (!orReady || !IsAfterOrb(cet))
				return;

			if (Close[0] > orHigh)
			{
				EnterLong(Quantity, "ORB_Long");
				tradedToday = true;
			}
			else if (Close[0] < orLow)
			{
				EnterShort(Quantity, "ORB_Short");
				tradedToday = true;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
			MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null || execution.Order.OrderState != OrderState.Filled)
				return;

			if (execution.Order.Name != "ORB_Long" && execution.Order.Name != "ORB_Short")
				return;

			fillPrice = price;
			extremeSinceFill = price;
			trailEngaged = false;
			activeStopPrice = 0;
		}

		#region Opening Range
		private void MeasureOpeningRange(DateTime cet)
		{
			TimeSpan t = cet.TimeOfDay;

			if (t >= OrbStart.TimeOfDay && t < OrbEnd.TimeOfDay)
			{
				if (orHigh == 0 && orLow == 0)
				{
					orHigh = High[0];
					orLow = Low[0];
				}
				else
				{
					orHigh = Math.Max(orHigh, High[0]);
					orLow = Math.Min(orLow, Low[0]);
				}
				orReady = false;
				return;
			}

			if (!orReady && t >= OrbEnd.TimeOfDay && orHigh > orLow)
				orReady = true;
		}

		private bool IsAfterOrb(DateTime cet)
		{
			return cet.TimeOfDay >= OrbEnd.TimeOfDay;
		}
		#endregion

		#region Trailing Stop
		private void RunTrailingStop()
		{
			if (Position.MarketPosition == MarketPosition.Long)
			{
				extremeSinceFill = Math.Max(extremeSinceFill, High[0]);
				double profitTicks = (Close[0] - fillPrice) / TickSize;

				if (!trailEngaged && profitTicks >= TrailActivationTicks)
					trailEngaged = true;

				if (!trailEngaged)
					return;

				double candidate = extremeSinceFill - TrailOffsetTicks * TickSize;
				double floor = fillPrice - StopLossTicks * TickSize;
				candidate = Math.Max(candidate, floor);

				if (activeStopPrice <= 0 || candidate > activeStopPrice)
				{
					SetStopLoss("ORB_Long", CalculationMode.Price, candidate, false);
					activeStopPrice = candidate;
				}
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				extremeSinceFill = Math.Min(extremeSinceFill, Low[0]);
				double profitTicks = (fillPrice - Close[0]) / TickSize;

				if (!trailEngaged && profitTicks >= TrailActivationTicks)
					trailEngaged = true;

				if (!trailEngaged)
					return;

				double candidate = extremeSinceFill + TrailOffsetTicks * TickSize;
				double ceiling = fillPrice + StopLossTicks * TickSize;
				candidate = Math.Min(candidate, ceiling);

				if (activeStopPrice <= 0 || candidate < activeStopPrice)
				{
					SetStopLoss("ORB_Short", CalculationMode.Price, candidate, false);
					activeStopPrice = candidate;
				}
			}
		}
		#endregion

		#region Risk & time
		private void EnsureDayRollover(DateTime cet)
		{
			if (cet.Date == currentDay)
				return;

			ResetDayState(cet.Date);
		}

		private void ResetDayState(DateTime day)
		{
			currentDay = day;
			orHigh = 0;
			orLow = 0;
			orReady = false;
			tradedToday = false;
			haltedToday = false;
			flatDoneToday = false;
			dayStartRealizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			fillPrice = 0;
			extremeSinceFill = 0;
			trailEngaged = false;
			activeStopPrice = 0;
		}

		private void RefreshDailyHalt()
		{
			if (haltedToday)
				return;

			double realized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - dayStartRealizedPnL;
			double unrealized = Position.MarketPosition == MarketPosition.Flat
				? 0
				: Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

			if (realized + unrealized <= -Math.Abs(DailyLossLimitUsd))
				haltedToday = true;
		}

		private bool IsTradeWindow(DateTime cet)
		{
			TimeSpan t = cet.TimeOfDay;
			return t >= TradeStart.TimeOfDay && t <= TradeEnd.TimeOfDay;
		}

		private bool IsForceFlatTime(DateTime cet)
		{
			return cet.TimeOfDay >= ForceFlatTime.TimeOfDay;
		}

		private DateTime ToCet(DateTime barTime)
		{
			return TimeZoneInfo.ConvertTime(barTime, Globals.GeneralOptions.TimeZoneInfo, cetZone);
		}

		private static DateTime ParseTime(string hhmm)
		{
			return DateTime.Parse(hhmm, CultureInfo.InvariantCulture);
		}
		#endregion

		#region News filter
		private void RebuildNewsSchedule()
		{
			if (NewsTimesCet == cachedNewsInput && newsSchedule != null)
				return;

			cachedNewsInput = NewsTimesCet;
			newsSchedule = new List<TimeSpan>();

			if (string.IsNullOrWhiteSpace(NewsTimesCet))
				return;

			foreach (string token in NewsTimesCet.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string s = token.Trim();
				if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out TimeSpan ts))
					newsSchedule.Add(ts);
				else if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
					newsSchedule.Add(dt.TimeOfDay);
			}
		}

		private bool IsNewsWindow(DateTime cet)
		{
			RebuildNewsSchedule();
			if (newsSchedule == null || newsSchedule.Count == 0)
				return false;

			TimeSpan now = cet.TimeOfDay;
			TimeSpan buffer = TimeSpan.FromMinutes(NewsBufferMinutes);

			foreach (TimeSpan eventTime in newsSchedule)
			{
				if (now >= eventTime.Subtract(buffer) && now <= eventTime.Add(buffer))
					return true;
			}
			return false;
		}
		#endregion

		#region Orders
		private void ClosePosition(string reason)
		{
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong(reason, "ORB_Long");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(reason, "ORB_Short");
		}
		#endregion

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Quantity", Order = 1, GroupName = "Obchod")]
		public int Quantity { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop Loss (ticks)", Order = 2, GroupName = "Obchod")]
		public int StopLossTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Profit Target (ticks)", Description = "Default 320 = RRR 1:2 k SL 160.", Order = 3, GroupName = "Obchod")]
		public int ProfitTargetTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trail Activation (ticks)", Description = "Zisk v ticích pro aktivaci trailing SL.", Order = 4, GroupName = "Obchod")]
		public int TrailActivationTicks { get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Trail Offset (ticks)", Description = "Vzdálenost trailing SL od extrému.", Order = 5, GroupName = "Obchod")]
		public int TrailOffsetTicks { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "ORB Start (SEČ)", Order = 1, GroupName = "Časy")]
		public DateTime OrbStart { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "ORB End (SEČ)", Order = 2, GroupName = "Časy")]
		public DateTime OrbEnd { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Obchod Od (SEČ)", Order = 3, GroupName = "Časy")]
		public DateTime TradeStart { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Obchod Do (SEČ)", Order = 4, GroupName = "Časy")]
		public DateTime TradeEnd { get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name = "Force Flat (SEČ)", Order = 5, GroupName = "Časy")]
		public DateTime ForceFlatTime { get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name = "Daily Loss Limit (USD)", Order = 1, GroupName = "Risk")]
		public double DailyLossLimitUsd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "News Times (SEČ)", Description = "Časy makro zpráv oddělené středníkem, např. 14:30;16:00;20:00", Order = 1, GroupName = "News")]
		public string NewsTimesCet { get; set; }

		[NinjaScriptProperty]
		[Range(0, 120)]
		[Display(Name = "News Buffer (min)", Description = "Blokace a uzavření pozic X minut před i po zprávě.", Order = 2, GroupName = "News")]
		public int NewsBufferMinutes { get; set; }
		#endregion
	}
}
