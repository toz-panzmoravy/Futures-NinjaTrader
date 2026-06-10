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
| **v1.06** | `VwapPullbackProp_v106.cs` | **PROP/TICK – doporučeno po analýze backtestu** |

> **1min:** v1.04. **Tick (80–150):** v1.06. **v1.06** = konec vstupů 20:30, blok hodin 17+19, stop po 3 ztrátách/den.

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
