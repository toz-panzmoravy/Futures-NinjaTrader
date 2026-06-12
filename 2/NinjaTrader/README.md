# VWAP Pullback Prop-Trading AOS – NinjaTrader 8

Automatický obchodní systém pro **MES (Micro E-mini S&P 500)** založený na VWAP pullback strategii s prop-trading risk managementem.

## Verze

| Verze | Soubor | Popis |
|---|---|---|
| **v1.00** | `VwapPullbackProp.cs` | Původní implementace dle specifikace |
| **v1.01** | `VwapPullbackProp_v101.cs` | Přísnější filtry signálů, trailing vypnutý defaultně |
| **v1.02** | `VwapPullbackProp_v102.cs` | Win rate preset – příliš málo obchodů, nepoužívat |
| **v1.03** | `VwapPullbackProp_v103.cs` | HIGH FREQUENCY – moc uvolněné, shorty ničí P&L |
| **v1.04** | `VwapPullbackProp_v104.cs` | **MES BALANCED – doporučená pro 1min** |
| **v1.05** | `VwapPullbackProp_v105.cs` | TICK CHART – stejné parametry jako v1.04 |
| **v1.06** | `VwapPullbackProp_v106.cs` | PROP/TICK – end 20:30, blok 17+19 |
| **v1.07** | `VwapPullbackProp_v107.cs` | **Širší okno + SL65/PT60 z optimalizace** |
| **v1.08** | `VwapPullbackProp_v108.cs` | HIGH ACTIVITY – TICK 125, uvolněné filtry |
| **v2.00** | `VwapPullbackProp_v200.cs` | PROP 25k – aktivní preset (selhalo na TICK 125) |
| **v2.01** | `VwapPullbackProp_v201.cs` | Kvalita nad frekvencí (+1652 $ backtest) |
| **v2.03** | `VwapPullbackProp_v203.cs` | Blok 16h + overnight fix – testuj teď |
| **v2.04** | `VwapPullbackProp_v204.cs` | Session-end flat fix |
| **v2.05** | `VwapPullbackProp_v205.cs` | **FINAL – opti preset, používej na live/eval** |

> **Produkce:** v2.05 na **TICK 125**, commission ON. Backtest 01–06/2026: +2 475 $ / PF 2.22 / DD −586 $. **1min:** v1.04.

## Instalace

1. Zkopíruj soubor(y) ze `Strategies/` do:
   ```
   Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
2. Otevři NinjaTrader 8 → **New** → **NinjaScript Editor**.
3. Stiskni **F5** (Compile). Ověř, že kompilace proběhla bez chyb.
4. Strategie: **VwapPullbackProp** (v1.00) nebo **VwapPullbackProp_v101** (v1.01).

## v1.01 – co se změnilo a proč

Backtest v1.00 ukázal: dost obchodů/týden, ale **nízký win rate (~10–15 %)** a průměrný vítěz menší než PT (trailing řezal zisky dřív).

| Změna | Default | Účel |
|---|---|---|
| **VWAP rejection wick** | – | Touch svíčka musí uzavřít na správné straně VWAP (bounce, ne průraz) |
| **Min Trend Bars** | 2 | Trend musí být ustálený před dotykem |
| **Require EMA Slope** | true | LONG jen když EMA roste |
| **Max VWAP Penetration** | 10 ticků | Filtruje hluboké průrazy (= falešný bounce) |
| **Min Confirm Body** | 4 ticky | Bez slabých doji potvrzení |
| **Close Beyond Prior Bar** | true | Potvrzení musí prorazit prior high/low |
| **Enable Trailing** | **false** | Nechá PT 80t doběhnout; zapni až pro live prop ochranu |
| **Skip First Minutes** | 15 | Vyhne se otevíracímu chopu |
| **Cooldown After Loss** | 3 bary | Pauza po ztrátě (sníží overtrading) |
| **Max Trades Per Day** | 6 | Strop obchodů za den |

### Jak testovat v1.01

1. Strategy Analyzer → stejné období jako v1.00 (MES 1min)
2. Porovnej: **% Win**, **Avg. trade**, **Net profit**, **# trades/týden**
3. Pokud je obchodů moc málo: sniž `MinConfirmBodyTicks` na 2 nebo vypni `RequireCloseBeyondPriorBar`
4. Pokud win rate stále nízká: zkus `EnableShort = false` (jen LONG)

## v1.02 – MES win rate preset

Cíl: **win rate 40 %+** při zachování rozumné frekvence obchodů.

| Změna oproti v1.01 | Default | Proč |
|---|---|---|
| **Enable Short** | **false** | MES long bias z backtestu |
| **SL / PT** | **35 / 55** ticků | Bližší PT = častější výhry (R:R ~1:1.57) |
| **Confirm Above Touch Bar** | true | Vstup až po proražení high touch svíčky |
| **Min Extension Before Pullback** | 12 ticků | Musí být reálný pullback, ne chop na VWAP |
| **Touch Bar Pullback** | true | Touch svíčka = prodej do VWAP, ne zelená rally |
| **ADX filter** | min 20 | Jen když je trend |
| **RSI filter** | 40–62 (long) | Vstup v pullback zóně, ne overbought |
| **Volume filter** | ≥ 85 % SMA(20) | Potvrzení s objemem |
| **Max Daily Losses** | 2 | Po 2 SL stop na den → méně revenge trades |
| **Skip First/Last Minutes** | 30 / 75 | Jádro session cca 16:00–20:30 |
| **Min Trend Bars** | 3 | Silnější trend před setupem |

### Jak testovat v1.02

1. Strategy Analyzer → **MES 1min**, stejné období jako v1.01
2. Porovnej:

| Metrika | v1.01 cíl | v1.02 cíl |
|---|---|---|
| Win rate | ~36 % | **40 %+** |
| Net profit | +259 $ | ≥ v1.01 nebo méně DD |
| Max DD | -1211 $ | **pod -800 $** |
| Obchodů | ~197 | mírně méně OK |

3. Pokud **málo obchodů**: sniž `MinAdx` na 18, vypni `UseVolumeFilter`
4. Pokud **win rate OK, málo profitu**: zvyš PT na 60–65
5. Pokud **win rate stále nízký**: zvyš `MinExtensionBeforePullbackTicks` na 16

## v1.03 – HIGH FREQUENCY (řeší málo obchodů)

v1.02 měla **5 obchodů za 5 měsíců** – nepoužitelné. v1.03 vrací frekvenci na úroveň v1.01 (~150–200 obchodů).

| Co v1.02 zabíjela obchody | v1.03 |
|---|---|
| ADX / RSI / Volume filtry | **Odstraněno** |
| Extension 12 ticků | **Odstraněno** |
| Confirm above touch bar | **Odstraněno** |
| Skip 30+75 minut | **0 min skip** |
| Max 2 ztráty/den | **Vypnuto** |
| Cooldown 5 barů | **0** |
| LONG-only | **LONG + SHORT** |
| Same-bar entry | **Zapnuto** |
| Close beyond prior bar | **Vypnuto** |
| Min confirm body 5t | **2 ticky** |
| Max bars after touch 4 | **8 barů** |

**Default:** SL 40 / PT 70, **1 kontrakt**, celé okno 15:30–21:45.

### Očekávané výsledky v1.03 vs v1.02

| | v1.02 | v1.03 cíl |
|---|---|---|
| Obchodů | 5 | **150+** |
| Win rate | 80 % | 35–45 % |
| Obchodů/týden | 0.2 | **7–10** |

## v1.04 – MES BALANCED (po hrůze v1.03)

Backtest v1.03: **355 obchodů** (frekvence OK), ale **-1 417 $** – shorty **-1 347 $**, longy skoro BE.

| | v1.01 | v1.03 | v1.04 cíl |
|---|---|---|---|
| Obchodů | 197 | 355 | **120–200** |
| Win rate | 36 % | 34,7 % | **38 %+** |
| Net P&L | +259 $ | -1 417 $ | **kladný** |
| SHORT | ano | ano (-1347 $) | **vypnuto** |

**v1.04 default:** LONG-only, SL 40 / PT 75, potvrzení na **další** svíčce, EMA slope, close beyond prior bar, skip 10 min po open.

## v1.06 – PROP optimalizace (z analýzy 261 obchodů, 125 tick)

Backtest v1.04 odhalil kde strategie ztrácí:

| Problém | Data | Oprava v v1.06 |
|---|---|---|
| Večerní obchody 19–21h | -491 $ | `Entry End` **20:30** |
| Hodina 17:xx | -464 $ | `Blocked Entry Hours` **17,19** |
| Hodina 19:xx | -295 $ | (viz výše) |
| Série 4–5 SL za den | max -206 $/den | `Max Consecutive Losses/Day` **3** |
| FlatBeforeClose | **+466 $** | Beze změny (neškodí) |

Simulovaný výsledek (s provizí 0,62 $/RT): **~1 548 $** vs **411 $** baseline, max DD **-691 $** vs **-1 159 $**.

**Doporučení:** MES tick graf (80–150), strategie **VwapPullbackProp_v106**, časy v čase grafu (SEČ).

## v1.07 – Širší okno + optimalizované SL/PT

v1.07 = v1.06 s **více obchody** (širší okno) + **SL/PT z Analyzeru** (65 / 60 ticků).

| Parametr | v1.06 | v1.07 |
|---|---|---|
| Entry End | 20:30 | **21:00** |
| Blocked Hours | 17,19 | **17** |
| Skip First Minutes | 10 | **5** |
| Max Consecutive Losses | 3 | **4** |
| Stop Loss (ticks) | 40 | **65** |
| Profit Target (ticks) | 75 | **60** |

### NT8 Strategy Analyzer – doporučená optimalizace v1.07

1. Graf MES **125 tick** (nebo 80–150), období min. 6 měsíců
2. Zapni **commission** (~0,62 $/RT)
3. Optimalizuj tyto parametry (v1.07 default jako střed):

| Parametr | Min | Max | Krok |
|---|---|---|---|
| EntryEndTime | 20:30 | 21:45 | 15 min |
| SkipFirstMinutes | 0 | 10 | 5 |
| BlockedEntryHours | prázdné / 17 / 17,19 | – | discrete |
| MaxConsecutiveLossesPerDay | 3 | 5 | 1 |
| StopLossTicks | 55 | 70 | 5 |
| ProfitTargetTicks | 55 | 70 | 5 |

4. Filtruj výsledky: **Max DD < 500 $**, **PF > 1,5**, **min. 100 obchodů**

> Grid search na existujících 79 obchodech neumí simulovat nové signály – pro frekvenci je nutný plný re-backtest v NT8.

## v1.08 – HIGH ACTIVITY (TICK 125)

v1.08 = v1.07 s **uvolněnými signálovými filtry** pro cíl **7–10 obchodů/týden** při zachování risk mantinelů.

### Timeframe – důležité

| | v1.08 | v1.04 |
|---|---|---|
| **Typ** | **TICK** | Minute (M1) |
| **Hodnota** | **125 tick** | 1 |
| Instrument | MES | MES |
| Session | CME US Index Futures RTH | stejné |

**Nepoužívej M1** – v1.08 je navržená pro tick graf. Na 1min grafu dostaneš jinou frekvenci i jiné výsledky (viz tvůj backtest v107 na M1: 79 obchodů / 6 měsíců).

NT8 nastavení grafu: **MES → Type: Tick → Value: 125**

### Změny oproti v1.07

| Parametr | v1.07 | v1.08 |
|---|---|---|
| Close Beyond Prior Bar | true | **false** |
| Min Confirm Body | 3 | **2** |
| Allow Same Bar Entry | false | **true** |
| Max Bars After Touch | 6 | **8** |
| Max VWAP Penetration | 12 | **16** |
| Entry End | 21:00 | **21:30** |
| Skip First Minutes | 5 | **0** |
| Max Trades/Day | 8 | **12** |
| Max Consecutive Losses | 4 | **5** |
| Cooldown After Loss | 2 bary | **1 bar** |
| Stop Loss / PT | 65 / 60 | **55 / 65** |

### Očekávané výsledky (orientačně, nutný backtest)

| Metrika | v1.07 (M1) | v1.08 cíl (TICK 125) |
|---|---|---|
| Obchodů / týden | ~3,5 | **7–10** |
| Win rate | ~70 % | 40–50 % |
| Profit factor | ~2,0 | **≥ 1,5** |
| Max DD | ~−400 $ | **≤ 500 $** |

### Jak testovat v1.08

1. Strategy Analyzer → MES **125 tick**, min. 6 měsíců, commission ~0,62 $/RT
2. Strategie: **VwapPullbackProp_v108**
3. Filtruj: PF ≥ 1,5, DD ≤ 500 $, min. 120 obchodů
4. Pokud PF OK ale málo obchodů: zkus tick **100** nebo **80**
5. Pokud DD vysoké: vrať `RequireCloseBeyondPriorBar = true`

## v2.00 – PROP 25k (TICK 125) — doporučená verze

Jediná verze navržená jako **kompletní preset pro prop eval** (~$25 000 účet, cíl $1 500, max DD $500).

### Proč v2.00 existuje

| Verze | Problém |
|---|---|
| v1.03 | Moc obchodů, −1 417 $ (shorty) |
| v1.07 | Skvělé PF/DD, ale ~3,5 obchodu/týden (testováno na M1) |
| v1.08 | Moc uvolněné filtry → riziko horší kvality |
| **v2.00** | Střed: kvalita v1.06 + frekvence blíž v1.08 + **prop risk limity** |

### Novinka v2.00: Daily Profit Target

Po dosažení **+$250 za den** se blokují nové vstupy (otevřená pozice doběhne normálně). Chrání zisk před „giveback" odpoledne/večer.

### Default parametry v2.00

| Skupina | Parametr | Hodnota | Proč |
|---|---|---|---|
| **Graf** | Type / Value | **TICK 125** | Více setupů než M1, stabilnější než 80 tick |
| **Obchod** | SL / PT | **52 / 72** | R:R ~1,38:1, ztráta ~$26, zisk ~$36 |
| | EMA Period | **45** | Mezi optimizer (36) a default (50) |
| | Enable Short | **false** | Shorty na MES historicky −1 347 $ |
| **Filtry** | Close Beyond Prior Bar | **true** | Kvalita signálu |
| | Min Confirm Body | **2** | Mírně více obchodů než v1.07 |
| | Max VWAP Penetration | **14** | Kompromis 12↔16 |
| | Max Bars After Touch | **7** | Kompromis 6↔8 |
| | Same Bar Entry | **false** | Potvrzení na další svíčce |
| **Risk** | Daily Loss Limit | **$400** | Pod prop limitem $500 |
| | Daily Profit Target | **$250** | Nové – lock zisku za den |
| | Max Trades/Day | **9** | Dost pro min. obchodní dny |
| | Max Consec. Losses | **3** | Stop po 3 SL (~$80) |
| | Cooldown After Loss | **2 bary** | Anti-revenge |
| **Čas** | Entry Start / End | **15:30 – 20:45** | Před večerním chopu |
| | Blocked Hours | **17,19** | Hodiny −464 $ / −295 $ z analýzy |
| | Skip First Minutes | **5** | Vyhnout open chopu |
| | Flat Time | **21:55** | Žádné přenášení |

### Očekávané metriky (orientačně – ověř backtestem)

| Metrika | v1.07 (M1) | v2.00 cíl (TICK 125) |
|---|---|---|
| Obchodů / týden | ~3,5 | **5–7** |
| Win rate | ~70 % | **42–52 %** |
| Profit factor | ~2,0 | **≥ 1,4** |
| Max DD | ~−400 $ | **≤ 500 $** |
| Čas k $1 500 | ~115 kal. dní | **60–90 kal. dní** |

---

## NT8 – kompletní nastavení pro backtest v2.00

### 1. Data Series (graf)

| Pole | Hodnota |
|---|---|
| Instrument | **MES** (Micro E-mini S&P 500) |
| Type | **Tick** |
| Value | **125** |
| Trading hours | **CME US Index Futures RTH** |
| Break at EOD | **True** |

### 2. Strategy Analyzer – Setup

| Pole | Hodnota |
|---|---|
| Strategy | **VwapPullbackProp_v200** |
| Start date | min. **6 měsíců** zpět (ideálně 12) |
| End date | dnes |
| Include commission | **True** |
| Commission | ~**$0,62** / round turn (nebo tvůj broker template) |
| Slippage | **0** (backtest), live zvaž 1 tick |
| Calculate | On bar close |
| Tick Replay | False (rychlejší backtest) |

### 3. Parametry strategie – neměň při prvním testu

Nech **vše default** z v2.00. Až po baseline backtestu optimalizuj (viz níže).

### 4. Co sledovat ve výsledcích

| Metrika | Pass | Fail → akce |
|---|---|---|
| Total # of trades | **≥ 100** za 6 měs. | Sniž tick na 100, nebo `MinConfirmBody` na 1 |
| Profit factor | **≥ 1,4** | Zapni `RequireCloseBeyondPriorBar`, zúž okno |
| Max. drawdown | **≤ 500 $** | Sniž `DailyLossLimit` na 350, SL na 48 |
| Win rate | **38–55 %** | OK; pod 35 % zpřísnit filtry |
| Avg. # trades/day | **0,5–1,5** | Méně = uvolni filtry; víc = zpřísnit |
| Net profit | **kladný** | Pokud DD OK ale profit malý → zvyš PT na 75 |

### 5. Prop 25k checklist (po backtestu)

- [ ] Cíl **$1 500** dosažen v simulaci?
- [ ] Max DD během cesty k cíli **≤ $500**?
- [ ] Min. **5 obchodních dní** (distinct days with trades)?
- [ ] Žádný den s realizovanou ztrátou **> $400**?
- [ ] Po commission stále **PF ≥ 1,3**?

### 6. Optimalizace (až po baseline) – pouze tyto parametry

| Parametr | Min | Max | Krok |
|---|---|---|---|
| StopLossTicks | 45 | 60 | 4 |
| ProfitTargetTicks | 65 | 80 | 5 |
| EntryEndTime | 20:30 | 21:00 | 15 min |
| BlockedEntryHours | 17 / 17,19 / prázdné | – | discrete |
| DailyProfitTarget | 200 | 300 | 50 |
| MaxVwapPenetrationTicks | 12 | 16 | 2 |

**Filtr výsledků:** Max DD < 500 $, PF > 1,4, min. 100 obchodů.  
**Nepoužívej** `EnableShort = true` na MES.

## v2.01 – Kvalita nad frekvencí (TICK 125)

Reakce na v200 backtest (210 obch., PF 1.0, DD −1413 $): zpřísněné filtry + limit obchodů/den.

| Parametr | v200 | v2.01 |
|---|---|---|
| Min Confirm Body | 2 | **3** |
| Max VWAP Penetration | 14 | **12** |
| Max Trades/Day | 9 | **5** |
| Cooldown After Loss | 2 | **3 bary** |
| Skip First Minutes | 5 | **10** |
| Entry End | 20:45 | **20:30** |
| SL / PT | 52 / 72 | **60 / 65** |
| Daily Profit Target | 250 | **200** |
| EMA Period | 45 | **50** |
| Session-open flat | — | **nové** (no overnight) |

### Cíl v2.01 backtestu

| Metrika | v200 (fail) | v2.01 cíl |
|---|---|---|
| Obchodů / týden | 9,2 | **4–6** |
| Profit factor | 1,0 | **≥ 1,4** |
| Max DD | −1413 $ | **≤ 500 $** |
| Net profit | −24 $ | **kladný** |

> **v2.01 backtest (01–06/2026):** +1 652 $, PF 1,33, DD −879 $, 153 obch.

## v2.03 – Blok 16h + overnight fix (TICK 125)

v2.03 = v201 s **cíleným vynecháním špatných hodin** (ne snížením obchodů/den).

| Parametr | v201 | v2.03 |
|---|---|---|
| Blocked Entry Hours | 17,19 | **16,17,19** |
| Entry End | 20:30 | **20:00** |
| Max Trades/Day | 5 | **5** (beze změny) |
| Overnight flat | slabý | **entrySessionDate** fix |
| SL / PT | 60 / 65 | 60 / 65 |

Data v201: **16:xx = −482 $**, **15:xx = +1 617 $** → blok 16h místo max 3 obch/den.

### Cíl v2.03 backtestu

| Metrika | v201 | v2.03 cíl |
|---|---|---|
| Net profit | +1 652 $ | **≥ v201** |
| Profit factor | 1,33 | **≥ 1,4** |
| Max DD | −879 $ | **≤ 600 $** |

> **v2.03 backtest (01–06/2026):** +1 867 $, PF 1,77, DD −613 $, 85 obch. (2× overnight SL)

## v2.04 – Session-end flat fix (TICK 125)

v2.04 = v203 s **opravou přenášení pozic přes noc** na tick grafu.

### Proč v203 měla overnight ztráty

Na **tick grafu** nemusí existovat bar, který by uzavřel po `FlatTime` (21:55). Poslední bar session často končí dřív → pozice zůstane otevřená → gap stop na open další session.

### Co v2.04 mění (kód, ne parametry)

| Oprava | Popis |
|---|---|
| `IsExitOnSessionCloseStrategy` | NT8 uzavře pozici 30 s před koncem session |
| `Bars.IsLastBarOfSession` | Vynucený flat na posledním baru session |
| `ForceFlatOpenPosition` | Zruší working SL/PT + market exit |
| `SessionOpenFlat` | Záloha na prvním baru nové session |

Default parametry = **stejné jako v203** (SL 60/PT 65, blok 16,17,19, end 20:00).

### Cíl v2.04 backtestu

| Metrika | v203 | v2.04 cíl |
|---|---|---|
| Overnight obchody | 2 (−255 $) | **0** |
| Max DD | −613 $ | **≤ 500 $** |
| Net profit | +1 867 $ | **≥ +1 600 $** |
| PF | 1,77 | **≥ 1,4** |

## v2.05 FINAL – Produkční preset (TICK 125)

v2.05 = v204 s **defaulty z optimalizace krok 1+2** (01–06/2026, commission ON). Používej pro **sim, eval a live** — neměň parametry bez nového backtestu.

### Defaulty v2.05 (opti1 + opti2)

| Skupina | Parametr | v204 | **v2.05** |
|---|---|---|---|
| 1. Obchod | Stop Loss (ticks) | 60 | **65** |
| 1. Obchod | Profit Target (ticks) | 65 | **65** |
| 4. Risk | Daily Loss Limit ($) | 400 | **325** |
| 4. Risk | Daily Profit Target ($) | 200 | **150** |
| 4. Risk | Max Consecutive Losses/Day | 3 | **2** |
| ostatní | filtry, čas, blok hodin | — | beze změny |

Kód (session-end flat) = **stejný jako v204**.

### Backtest v2.05 preset (01–06/2026)

| Metrika | Hodnota |
|---|---|
| Net profit | **+2 475 $** |
| Profit factor | **2,22** |
| Max drawdown | **−586 $** |
| Obchodů | 85 (~3,8/týden) |
| Win rate | 68 % |
| Prop $1 500 | ~obchod #63 (27. 4. 2026) |

### NT8 nastavení (neměň)

```
Instrument:  MES
Chart:       Tick 125
Session:     CME US Index Futures RTH
Commission:  ON
Strategie:   VwapPullbackProp_v205 — vše default
```

---

## NT8 optimalizace (historie v2.04) – postup krok za krokem

> **Pravidlo:** vždy max **2–3 parametry** najedonce. V NT8 u každého parametru nastav **Start / Stop / Increment** ve **správné jednotce** (viz tabulka níže).

### Jednotky parametrů (přesné názvy v NT8)

| Skupina | Parametr | Jednotka | Default |
|---|---|---|---|
| **1. Obchod** | Stop Loss (ticks) | **tick** | 65 |
| **1. Obchod** | Profit Target (ticks) | **tick** | 65 |
| **2. Frekvence / filtry** | Min Confirm Body (ticks) | **tick** | 3 |
| **2. Frekvence / filtry** | Max VWAP Penetration (ticks) | **tick** | 12 |
| **4. Risk** | Daily Loss Limit ($) | **$** | 325 |
| **4. Risk** | Daily Profit Target ($) | **$** | 150 |
| **4. Risk** | Max Consecutive Losses/Day | ks | 2 |

> **MES:** 1 tick = $1,25. SL 60 ticků ≈ $75 riziko na obchod.

### Pevné nastavení (neměň)

```
Instrument:   MES
Chart:        Tick 125
Session:      CME US Index Futures RTH
Období:       01.01.2026 – 10.06.2026 (min. 6 měsíců)
Commission:   ON
Enable Short: false (default)
Strategie:    VwapPullbackProp_v204 – vše ostatní default
```

### Krok 0 – baseline backtest (bez optimalizace)

Spusť backtest s defaulty. Exportuj `204sum.csv`, `204tick.csv`, `204settings.csv`.  
Ověř: **0 overnight** exitů (žádný `Stop loss` s datem exit ≠ den entry přes session break).

---

### Krok 1 – Risk (skupina **4. Risk** — jen **$** a **ks**, ne ticky)

Strategy Analyzer → Parameters → zaškrtni **Optimize** u těchto 3 řádků:

| Parametr v NT8 | Start | Stop | Increment | Jednotka |
|---|---|---|---|---|
| Daily Loss Limit ($) | 300 | 450 | 25 | **$** |
| Daily Profit Target ($) | 150 | 250 | 25 | **$** |
| Max Consecutive Losses/Day | 2 | 3 | 1 | ks |

**Nezaškrtávej** Stop Loss / Profit Target — jsou v **tickách** (skupina 1. Obchod) → krok 2.

**Optimize on:** Max. drawdown (minimize)  
**Filter results:** Profit factor ≥ 1,4 AND Total net profit > 0 AND Total # of trades ≥ 60

Vyber top 3 výsledky s **nejnižším DD** (ne nejvyšším profit!). Zapiš vítěze → nastav jako default pro krok 2.

---

### Krok 2 – SL / PT (skupina **1. Obchod** — jen **ticky**)

Fixní: vítězné hodnoty z kroku 1. Zaškrtni **Optimize** jen u:

| Parametr v NT8 | Start | Stop | Increment | Jednotka |
|---|---|---|---|---|
| Stop Loss (ticks) | 50 | 70 | 5 | **tick** |
| Profit Target (ticks) | 55 | 75 | 5 | **tick** |

**Optimize on:** Profit factor (maximize)  
**Filter:** Max. drawdown ≤ 550 AND Total net profit > 0

---

### Krok 3 – Signálové filtry (skupina **2. Frekvence / filtry** — **ticky**)

Fixní: vítězové z kroků 1–2. Zaškrtni **Optimize** jen u:

| Parametr v NT8 | Start | Stop | Increment | Jednotka |
|---|---|---|---|---|
| Min Confirm Body (ticks) | 2 | 4 | 1 | **tick** |
| Max VWAP Penetration (ticks) | 10 | 14 | 2 | **tick** |

**Optimize on:** Profit factor (maximize)  
**Filter:** Max. drawdown ≤ 500 AND Total # of trades ≥ 50

---

### Krok 4 – Validace (povinné)

Rozděl období:

```
Trénink (optimalizuj):  01.01.2026 – 31.03.2026
Test (nesmíš ladit):    01.04.2026 – 10.06.2026
```

Preset z kroků 1–3 musí na **test období** splnit: PF ≥ 1,3, DD ≤ 550, profit > 0.  
Jinak je to curve-fit → vrať se ke kroku 1 s užšími rozsahy.

---

### Pass / fail tabulka

| Kritérium | Cíl |
|---|---|
| Net profit | > 0 |
| Profit factor | ≥ 1,4 |
| Max drawdown | ≤ 500 $ (strict) nebo ≤ 750 $ (Apex) |
| Obchodů / 6 měs | ≥ 60 |
| Overnight holds | 0 |
| Prop $1 500 | co nejdřív, max run DD pod limitem |

---

### Co NEoptimalizovat

| Parametr | Proč |
|---|---|
| Enable Short | vždy false na MES |
| Blocked Entry Hours | už ověřeno (16,17,19) – měň jen ručně po analýze hodin |
| Entry Start/End | ne v 1. kole – curve-fit risk |
| EMA Period | stabilní na 50, až po SL/PT |
| Max Trades/Day | nech 5 |
| Trailing skupina | Enable Trailing = false |

## Doporučené nastavení grafu

| Parametr | v1.04 | v1.05 |
|---|---|---|
| Instrument | MES | MES |
| Interval | 1 Minute | **80 / 120 / 150 Tick** |
| Session template | CME US Index Futures RTH | stejné |
| Calculate | On bar close | On bar close |

> **Tip:** Nejdřív testuj v **Sim** nebo **Market Replay**, než pustíš strategii na live/prop účet.

## Obchodní logika

### LONG setup
1. Trend: Close > session VWAP **a** Close > EMA(50)
2. Pullback: Low se dotkne VWAP
3. Trigger: první zelená svíčka (Close > Open) uzavřená **nad** VWAP → market LONG

### SHORT setup (zrcadlově)
1. Trend: Close < session VWAP **a** Close < EMA(50)
2. Pullback: High se dotkne VWAP
3. Trigger: první červená svíčka (Close < Open) uzavřená **pod** VWAP → market SHORT

### Výstupy
- **Stop Loss:** 40 ticků (default)
- **Profit Target:** 80 ticků (R:R 1:2)
- **Trailing:** po +30 ticích zisku → SL na break-even +4 ticky, poté trail 25 ticků za extrémem

## Parametry strategie

### 1. Obchod
| Parametr | Default | Popis |
|---|---|---|
| Contracts | 1 | Počet kontraktů |
| Stop Loss (ticks) | 40 | Fixní SL |
| Profit Target (ticks) | 80 | Fixní PT |
| EMA Period | 50 | Filtr trendu |
| Enable Long | true | Povolit long obchody |
| Enable Short | true | Povolit short obchody |
| Max Bars After Touch | 5 | Expirace setupu po dotyku VWAP |

### 2. Trailing
| Parametr | Default | Popis |
|---|---|---|
| Enable Trailing | true | Zapnout BE + trailing |
| Break-Even Trigger (ticks) | 30 | Zisk pro aktivaci BE |
| Break-Even Offset (ticks) | 4 | Posun SL nad/pod entry |
| Trail (ticks) | 25 | Vzdálenost trail SL od extrému |

### 3. Risk
| Parametr | Default | Popis |
|---|---|---|
| Daily Loss Limit ($) | 500 | Při dosažení se **blokují nové vstupy** do konce dne. Otevřená pozice doběhne na SL/PT. |

### 4. Čas (čas grafu / PC)
| Parametr | Default | Popis |
|---|---|---|
| Entry Start Time | 15:30 | Začátek okna pro vstupy (SEČ) |
| Entry End Time | 21:45 | Konec okna pro vstupy |
| Flat Time | 21:55 | Nekompromisní uzavření všech pozic |

### 5. News Filter
| Parametr | Default | Popis |
|---|---|---|
| News Times | *(prázdné)* | Časy zpráv oddělené `;` např. `14:30;20:00` |
| Block Minutes Before | 5 | Blokace vstupů X minut před zprávou |
| Block Minutes After | 5 | Blokace vstupů X minut po zprávě |
| Close Before News | true | Uzavřít otevřené pozice před news oknem |

> **News filter:** NT8 nemá vestavěný ekonomický kalendář. Před obchodním dnem si časy zpráv (FOMC, CPI apod.) doplň ručně z ForexFactory nebo jiného kalendáře. Časy zadávej v čase grafu.

## Session VWAP

Strategie počítá **session-anchored VWAP** interně (typická cena × volume / kumulativní volume, reset na začátku session). Nepotřebuješ placený OrderFlow VWAP indikátor.

## Strategy Analyzer

Všechny klíčové parametry jsou vystavené jako Properties – lze je optimalizovat ve **Strategy Analyzer** (Stop Loss, PT, EMA perioda, trailing, daily loss limit, časy).

## Poznámky k prop-tradingu

- **Daily loss limit** sleduje realizovaný + nerealizovaný P&L za aktuální session.
- **Flat before close** (21:55) vždy tržně uzavře všechny pozice – žádné přenášení přes noc.
- **Trailing SL** chrání otevřený zisk proti trailing drawdown pravidlům prop firem.
- Pro přesnější trailing v live obchodování lze v kódu změnit `Calculate = Calculate.OnEachTick` (pomalejší backtest).
