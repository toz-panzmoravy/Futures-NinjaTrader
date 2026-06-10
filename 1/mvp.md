System Specification for NinjaTrader 8 Automated Trading System (AOS)

1. Platform and Instrument
- Platform: NinjaTrader 8[cite: 218].
- Language: C# (NinjaScript)[cite: 218].
- Target Instrument: MES (Micro E-mini S&P 500)[cite: 219].
- Data Execution: Calculate on bar close (OnBarUpdate triggered by bar close)[cite: 84].

2. Trading Hours (Time Filters)
- Allowed Session: Regular Trading Hours (RTH) only[cite: 220]. 
- Trading Window: Enable entry signals only between 15:30 and 21:45 SEČ (Central European Time)[cite: 221].
- Intraday Flattening: If any position is still open at 21:55 SEČ, close it immediately at market price (Flat before close) and cancel all working orders[cite: 221].

3. Risk Management & Prop-Firm Protection
- Hard Daily Loss Limit: Implement a strict daily loss monitoring mechanism[cite: 222]. After every closed trade, check the daily realized P&L[cite: 223]. If the cumulative daily loss exceeds -$500 USD, immediately flatten any open positions, cancel all pending orders, and halt the strategy execution until the next trading day[cite: 224].
- News Filter: Include a placeholder function/filter to suspend trading during high-impact macroeconomic news (e.g., FOMC, CPI)[cite: 225].

4. Strategy Logic: VWAP Pullback (Long Only Setup)
- Indicators used: Standard VWAP (Volume Weighted Average Price) [cite: 238] and EMA 50 (Exponential Moving Average)[cite: 241].
- Trend Setup: The system actively monitors the market only when the current closing price is ABOVE the EMA 50 AND ABOVE the VWAP line (confirming a strong bullish daily trend)[cite: 240, 241].
- Entry Trigger: 
  1. The price must decline and touch or cross below the VWAP line (the pullback phase)[cite: 242, 243].
  2. After touching the VWAP line, the system waits for the first bullish (green) bar to close back above or at the VWAP level[cite: 243].
  3. Action: As soon as this bullish confirmation bar closes, enter a LONG position immediately via Market Order[cite: 243].

5. Order Management (Exit Strategy)
- Stop Loss: Attach a mandatory protective Stop Loss order exactly at 40 ticks (equivalent to 10 full points on MES) from the entry price[cite: 245].
- Profit Target: Attach a mandatory Profit Target order exactly at 80 ticks (equivalent to 20 full points on MES) from the entry price to maintain a strict 1:2 Risk/Reward ratio[cite: 247, 248].
- Execution: Orders must be submitted as OCO (One-Cancels-Other) bracket orders.
