# MNQ Microtrend Prop v1.05 — plán backtestů

Plán optimalizace **bez změny kódu** — všechny varianty testuj na **`MnqMicrotrendProp_v105`** v Strategy Analyzer.

---

## Baseline (referenční běh)

Nejdřív ověř, že NT dává stejný výsledek jako export Pullback103 / Grid 10-41.

| Položka | Hodnota |
|---------|---------|
| Strategy | `MnqMicrotrendProp_v105` |
| Instrument | **MNQ** (front month) |
| Graf | **TICK 200** |
| Období fáze 1 | **01.01.2026 – 15.06.2026** |
| Commission fáze 1 | **OFF** (porovnání s baseline) |
| Commission fáze 2 | **$1.90 / RT** |
| Období fáze 3 (OOS) | **01.07.2025 – 15.06.2026** (18M) |

**Baseline cíle (bez comm.):**

| Metrika | Cíl |
|---------|-----|
| Obchody | ~**274** (138L / 136S) |
| Net profit | ~**+$2 050** |
| Profit factor | ~**1.22** (L i S ≥ 1.20) |
| Max DD | ~**−$858** |

V Output okně NT musí být:

```
MnqMicrotrendProp_v105 PRESET_PULLBACK103 | Entry=PullbackBreak L=True S=True SL/PT=130/182 ...
```

Pokud se baseline neshoduje → **Reset** parametrů strategie, smaž starou v104 z NT, znovu **F5**.

---

## Default preset v105 (neměnit v baseline)

| Skupina | Parametr | Hodnota |
|---------|----------|---------|
| Obchod | SL / PT | **130 / 182** ticků |
| Obchod | EMA | **9 / 21** |
| Obchod | Enable Long / Short | **ON / ON** |
| Signál | Entry Mode | **PullbackBreak** |
| Signál | Break Margin | **1** |
| Signál | Pullback Touch | **3** |
| Signál | Min Trend Body | **2** |
| Signál | Min Bars Between Entries | **4** |
| Signál | Session Bias | **VwapAboveOpen** |
| Risk | Max Trades / Day | **3** |
| Risk | Max Consec. Losses / Day | **3** |
| Risk | Daily Loss Limit | **$275** |
| Risk | Cooldown Bars After Loss | **3** |
| Risk | Blocked Hours | **17, 19** |
| Risk | Entry Start / End | **15:30 – 20:30** SEČ |
| Risk | Flat Time | **21:55** SEČ |

---

## Testovací matice (3 varianty)

Testuj **jednu variantu najednou** proti baseline. Každý běh exportuj jako:

```
v105_{variant}_{obdobi}_{commission}_{datum}.csv
```

### Varianta A — Risk (snížení DD)

Cíl: méně sériových ztrát, mírně nižší net.

| Parametr | Baseline | **Varianta A** |
|----------|----------|----------------|
| Max Consec. Losses / Day | 3 | **2** |
| Daily Loss Limit | $275 | **$225** |
| Max Trades / Day | 3 | **2** |
| Cooldown Bars After Loss | 3 | **5** |

### Varianta B — Quality (vyšší PF)

Cíl: méně obchodů, kvalitnější vstupy.

| Parametr | Baseline | **Varianta B** |
|----------|----------|----------------|
| Min Trend Body | 2 | **3** |
| Break Margin | 1 | **2** |
| Min Bars Between Entries | 4 | **5** |
| Entry End | 20:30 | **20:00** |

### Varianta C — Combined (A + B)

Cíl: kompromis DD + PF. **Použij jen pokud A i B zvlášť projdou pass kritérii.**

| Parametr | Hodnota |
|----------|---------|
| Vše z varianty A | ano |
| Vše z varianty B | ano |

---

## Volitelné testy (až po A/B/C)

Spouštěj **samostatně**, ne v kombinaci s A/B/C.

### D1 — Break-even

| Parametr | Baseline | **Test D1** |
|----------|----------|-------------|
| Enable Break-Even | OFF | **ON** |
| BE Trigger (ticks) | 110 | **55** |
| BE Offset (ticks) | 4 | **2** |

> Trigger 110 je u MNQ příliš vysoko (avg MFE ~122 ticků). Testuj 50–60 ticků.

### D2 — Session Bias OFF

| Parametr | Baseline | **Test D2** |
|----------|----------|-------------|
| Session Bias | VwapAboveOpen | **Off** |

> Více obchodů — ověř, zda net/DD nezhorší.

### D3 — Entry Mode PriorBarBreak (long-heavy)

| Parametr | Baseline | **Test D3** |
|----------|----------|-------------|
| Entry Mode | PullbackBreak | **PriorBarBreak** |
| Enable Short | ON | **OFF** (volitelně) |

> Referenční Priorbar103: net +$1 803, long PF 1.24, short PF 1.14.

---

## Pass / fail kritéria

### Fáze 1 — 6M 2026, commission OFF

| Metrika | Pass |
|---------|------|
| Profit factor (All) | ≥ **1.18** |
| Profit factor (Long) | ≥ **1.15** |
| Profit factor (Short) | ≥ **1.10** |
| Max DD | ≥ **−$780** (méně negativní než −$780) |
| Net profit | ≥ **+$1 800** (nebo ≥ baseline − $150 u varianty A) |

### Fáze 2 — 6M 2026, commission $1.90/RT

| Metrika | Pass |
|---------|------|
| Net profit | ≥ **+$1 200** |
| Max DD | ≥ **−$850** |
| PF (All) | ≥ **1.12** |

### Fáze 3 — 18M OOS, commission ON

| Metrika | Pass |
|---------|------|
| Net profit | **> 0** |
| Max DD | ≥ **−$1 200** (pod Apex trailing ~$1 500) |
| PF (All) | ≥ **1.05** |

**Fail → preset nejde do produkce.** Vrať se k baseline v105.

---

## Pořadí testů (doporučené)

```
1. Baseline v105 (comm OFF)     → musí sedět s Pullback103
2. Baseline v105 (comm ON)      → odhad ~+$1 530
3. Varianta A                   → pokud DD ↓ a net OK → pokračuj
4. Varianta B                   → pokud PF ↑ a net OK → pokračuj
5. Varianta C                   → jen pokud A i B OK
6. Vítěz → fáze 3 (18M OOS)
7. Volitelně D1/D2/D3           → jen pro vítězný preset
```

---

## Co exportovat z NT8

Minimálně pro každý běh:

1. **Performance summary** (Grid export) — Strategy Analyzer
2. **Trades list** — záložka Trades → Export (analýza hodin, L/S, exit důvod)

Do názvu souboru vždy: verze, varianta, období, commission, L/S.

---

## Co neměnit (overfit riziko)

- Entry Mode **PullbackBreak** (jádro edge)
- SL / PT **130 / 182** (sedí k avg win/loss $91 / −$65)
- EMA **9 / 21**
- Graf **TICK 200**
- Nepřidávat filtry mimo kód (RequireVwapSide, split session bias — chyba v104)

---

## Po úspěšném testu

1. Zapiš vítězné parametry do `MNQ/README.md` (changelog)
2. Release dle [RELEASE.md](../RELEASE.md): nová verze `.cs` + tag `mnq/microtrend-prop/vX.Y.Z`
