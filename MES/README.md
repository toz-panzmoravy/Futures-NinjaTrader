# MES Microtrend Prop v1.04

Strategie pro **Micro E-mini S&P 500 (MES)** — jízda mikrotrendem na **TICK 200** grafu.  
Cíl: prop trading (Apex 25K EOD), long + short, řízené denní riziko.

**Soubor strategie:** `NinjaTrader/Strategies/MesMicrotrendProp_v104.cs`

---

## Logika (stručně)

| Podmínka | Long | Short |
|----------|------|-------|
| Trend | EMA 9 > EMA 21, cena nad EMA 9 | EMA 9 < EMA 21, cena pod EMA 9 |
| Entry (PriorBarBreak) | Close breakne **high předchozího** baru | Close breakne **low předchozího** baru |
| Bar | Bullish bar (close > open), min. 1 tick tělo | Bearish bar, min. 1 tick tělo |

---

## Instalace v NinjaTrader 8

1. Zkopíruj soubor:
   ```
   MesMicrotrendProp_v104.cs
   → Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
2. Otevři **NinjaScript Editor** (Control Center → New → NinjaScript Editor).
3. Stiskni **F5** (Compile). Ověř, že není chyba.
4. Strategie se objeví jako **MesMicrotrendProp_v104**.

---

## Nastavení grafu

| Položka | Hodnota |
|---------|---------|
| Instrument | **MES** (aktuální front month, např. MES 09-26) |
| Typ grafu | **TICK** |
| Tick count | **200** |
| Session template | CME US Index Futures RTH (nebo ekvivalent s US session) |
| Časová zóna NT | **SEČ** (Central European Standard Time) |

> Strategie počítá s časy v **SEČ**. Entry 15:30 = open US cash (~9:30 ET).

---

## Připojení strategie

1. Otevři TICK 200 graf MES.
2. Pravý klik → **Strategies** → **MesMicrotrendProp_v104**.
3. Nastav parametry dle tabulky níže (defaulty jsou produkční preset).
4. **Enable** = zaškrtnuto.
5. Pro backtest: **Tools → Strategy Analyzer** (stejný instrument a TICK 200).

---

## Doporučené parametry (produkční preset)

### 1. Obchod

| Parametr | Hodnota | Poznámka |
|----------|---------|----------|
| Contracts | **1** | 1 MES kontrakt |
| Stop Loss (ticks) | **40** | ≈ $50 riziko |
| Profit Target (ticks) | **56** | ≈ $70 cíl, R:R ~1,4 |
| EMA Fast | **9** | |
| EMA Slow | **21** | |
| Enable Long | **Ano** | |
| Enable Short | **Ano** | |

### 2. Signál

| Parametr | Hodnota | Poznámka |
|----------|---------|----------|
| Entry Mode | **PriorBarBreak** | nejlepší výsledky v backtestu |
| Swing Lookback Bars | 4 | jen pro SwingBreak / PullbackBreak |
| Break Margin (ticks) | **0** | |
| Require Trend Bar | **Ano** | |
| Min Trend Body (ticks) | **1** | |
| Min Bars Between Entries | **2** | |
| Session Bias | **Off** | volitelně VwapAboveOpen pro ochranu longů |

**Alternativní entry módy:**

- **SwingBreak** — break swing high/low za N barů (pomalejší, méně obchodů)
- **PullbackBreak** — dotyk EMA 9 + break prior bar (entry po pullbacku)

### 3. Výstupy

| Parametr | Hodnota |
|----------|---------|
| Enable Break-Even | **Ne** (default) |

### 4. Prop risk

| Parametr | Hodnota | Poznámka |
|----------|---------|----------|
| Daily Loss Limit ($) | **300** | stop obchodování na den |
| Max Trades / Day | **5** | |
| Max Consec. Losses / Day | **3** | |
| Cooldown Bars After Loss | **3** | tick bary |
| Skip First Minutes | **3** | po 15:30 SEČ |
| Blocked Hours | **17,19** | SEČ, bez entry |
| Entry Start | **15:30** | |
| Entry End | **21:00** | |
| Flat Time | **21:55** | zavřít vše před koncem session |
| News Times | *(prázdné)* | doplň např. `14:30;16:00` pro CPI/FOMC |
| Close Before News | **Ano** | |

---

## Backtest (Strategy Analyzer)

| Položka | Hodnota |
|---------|---------|
| Instrument | MES |
| Data series | **TICK 200** |
| Commission | **$1.90** per round turn (doporučeno) |
| Slippage | 0 (nebo 1 tick pro konzervativní odhad) |
| Období | min. **18 měsíců** (2025 + 2026) pro validaci |

**Referenční výsledek (6M 2026, PriorBarBreak, bez commission):**

- Net ≈ **+$1 661**, PF **1.11**, Max DD **−$858**
- S commission ~$1.90/RT odhad net **≈ +$600**

---

## Apex 25K EOD — poznámky

- Trailing max DD ~**$1 500** — backtest DD −$858 nechává rezervu.
- Denní limit **$300** chrání před prop pravidly i sérií ztrát.
- Před funded účtem ověř **18M backtest s commission ON**.
- Pokud short selže v bear období (2025): vypni **Enable Short** nebo zapni **Session Bias = VwapAboveOpen**.

---

## Verze

| Verze | Popis |
|-------|-------|
| v1.04 | Ride the microtrend, PriorBarBreak preset, L+S |

Release workflow a tagy: viz [RELEASE.md](../RELEASE.md) v kořeni repozitáře.
