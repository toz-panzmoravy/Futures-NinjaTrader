# MNQ Microtrend Prop v1.05

Strategie pro **Micro E-mini Nasdaq (MNQ)** — jízda mikrotrendem na **TICK 200** grafu.  
Cíl: prop trading (Apex 25K EOD), **PullbackBreak long + short**, řízené denní riziko.

**Soubor strategie:** `NinjaTrader/Strategies/MnqMicrotrendProp_v105.cs`  
**Plán backtestů:** [BACKTEST_PLAN_v105.md](BACKTEST_PLAN_v105.md)

---

## Verze

| Verze | Soubor | Stav |
|-------|--------|------|
| **v1.05** | `MnqMicrotrendProp_v105.cs` | **Aktuální** — Pullback103 preset + diagnostika |
| v1.04 | `MnqMicrotrendProp_v104.cs` | Deprecated — použij v105 (NT cache) |
| v1.00–v1.03 | — | Lokální experimenty, ne v repu |

---

## Baseline backtest (6M 2026, bez commission)

| Metrika | All | Long | Short |
|---------|-----|------|-------|
| Net profit | **+$2 050** | +$1 062 | +$988 |
| Profit factor | **1.22** | 1.22 | 1.21 |
| Max DD | **−$858** | −$858 | −$858 |
| Obchody | **274** | 138 | 136 |

Odhad s commission **$1.90/RT**: ~**+$1 530** / 6M.

---

## Logika

| Podmínka | Long | Short |
|----------|------|-------|
| Trend | EMA 9 > EMA 21, cena nad EMA 9 | EMA 9 < EMA 21, cena pod EMA 9 |
| Pullback | Dotyk EMA 9 (tolerance 3 ticky) | Dotyk EMA 9 |
| Entry | Break **high** předchozího baru | Break **low** předchozího baru |
| Bar | Bullish, min. 2 ticky tělo | Bearish, min. 2 ticky tělo |
| Session bias | `VwapAboveOpen` (stejná brána pro L i S) | |

Entry módy v kódu: **PullbackBreak** (default), PriorBarBreak, SwingBreak.

---

## Instalace v NinjaTrader 8

1. Zkopíruj soubor:
   ```
   MnqMicrotrendProp_v105.cs
   → Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
2. Smaž starou `MnqMicrotrendProp_v104.cs`, pokud obsahuje parametry `Session Long Bias` / `Require VWAP Side`.
3. **NinjaScript Editor** → **F5** (Compile).
4. Strategie: **MnqMicrotrendProp_v105**.

---

## Nastavení grafu

| Položka | Hodnota |
|---------|---------|
| Instrument | **MNQ** (front month) |
| Typ grafu | **TICK** |
| Tick count | **200** |
| Časová zóna NT | **SEČ** |

---

## Produkční preset (defaulty v105)

| Parametr | Hodnota |
|----------|---------|
| Entry Mode | **PullbackBreak** |
| Enable Long / Short | **ON / ON** |
| Stop Loss / Profit Target | **130 / 182** ticků (~$65 / $91) |
| EMA Fast / Slow | **9 / 21** |
| Break Margin | **1** |
| Pullback Touch | **3** |
| Min Trend Body | **2** |
| Min Bars Between Entries | **4** |
| Session Bias | **VwapAboveOpen** |
| Max Trades / Day | **3** |
| Max Consec. Losses / Day | **3** |
| Daily Loss Limit | **$275** |
| Cooldown Bars After Loss | **3** |
| Blocked Hours | **17, 19** |
| Entry Start / End | **15:30 – 20:30** SEČ |
| Flat Time | **21:55** SEČ |

---

## Další optimalizace

Varianty A/B/C (risk, quality, combined) a pass kritéria: **[BACKTEST_PLAN_v105.md](BACKTEST_PLAN_v105.md)**

---

## Portfolio

| Instrument | Verze | Role |
|------------|-------|------|
| **MES** | v1.04 PriorBarBreak L+S | hlavní |
| **MNQ** | v1.05 PullbackBreak L+S | doplněk obou směrů |

---

Release: [RELEASE.md](../RELEASE.md)
