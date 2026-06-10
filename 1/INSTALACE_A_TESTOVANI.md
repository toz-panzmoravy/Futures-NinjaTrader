# AOS VWAP Pullback – instalace, aktivace a testování v NinjaTrader 8

Tento návod popisuje, jak nainstalovat strategii `AOS_VWAPPullbackStrategy`, jak ji správně nastavit pro instrument **MES** a jak ji postupně otestovat od backtestu až po simulované obchodování.

---

## 1. Požadavky

- **NinjaTrader 8** (plná licence nebo trial s povoleným Strategy Analyzer)
- Připojení k datům pro **MES** (Micro E-mini S&P 500)
- Doporučený časový rámec: **5 minut** (vhodný pro VWAP pullback setup)
- Session template: **CME US Index Futures RTH** (Regular Trading Hours)

---

## 2. Instalace strategie do NinjaTrader

### Krok 1 – Zkopírování souboru

1. Otevřete složku projektu a najděte soubor:
   ```
   Strategies\AOS_VWAPPullbackStrategy.cs
   ```
2. Zkopírujte ho do NinjaTrader složky pro vlastní skripty:
   ```
   Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
   Typická cesta na Windows:
   ```
   C:\Users\<Váš_Uživatel>\Documents\NinjaTrader 8\bin\Custom\Strategies\AOS_VWAPPullbackStrategy.cs
   ```

### Krok 2 – Kompilace

1. Spusťte **NinjaTrader 8**.
2. Otevřete **New → NinjaScript Editor** (nebo `Ctrl+Shift+N`).
3. V editoru klikněte pravým tlačítkem na libovolný soubor → **Compile** (nebo stiskněte **F5**).
4. V dolním panelu **Output** ověřte hlášku:
   ```
   NinjaScript successfully compiled
   ```
5. Pokud se objeví chyby, zkontrolujte, že soubor leží ve složce `Strategies` a že používáte NT8 (ne NT7).

### Krok 3 – Ověření v seznamu strategií

1. Otevřete **New → Strategy**.
2. V seznamu by se měla objevit strategie **AOS_VWAPPullbackStrategy**.

---

## 3. Doporučené nastavení parametrů

| Parametr | Výchozí hodnota | Popis |
|---|---|---|
| Daily Loss Limit (USD) | 500 | Po překročení −$500 denní ztráty strategie zastaví obchodování |
| Stop Loss (ticks) | 40 | 10 bodů na MES (= $50 riziko na kontrakt) |
| Profit Target (ticks) | 80 | 20 bodů na MES (= $100 zisk, R:R 1:2) |
| Entry Window Start | 15:30 CET | Začátek okna pro vstupy |
| Entry Window End | 21:45 CET | Konec okna pro vstupy |
| Flatten Time | 21:55 CET | Vynucené uzavření pozice a zrušení orderů |

**Instrument:** MES (např. `MES 09-25` nebo aktuální front-month kontrakt)

**Data series:** 5 Minute, **Trading hours = CME US Index Futures RTH**

**Default quantity:** 1 kontrakt (pro začátek vždy 1)

---

## 4. Testování – doporučený postup (3 fáze)

Testujte vždy postupně: **Backtest → Playback/Sim → Live (až po ověření)**.

### Fáze A – Strategy Analyzer (historický backtest)

Toto je první a nejdůležitější krok – ověří logiku strategie na historických datech.

1. **New → Strategy Analyzer**
2. Klikněte **Add** a vyberte **AOS_VWAPPullbackStrategy**
3. Nastavte:
   - **Instrument:** MES
   - **Type:** Minute
   - **Value:** 5
   - **Trading hours:** CME US Index Futures RTH
   - **Backtest type:** Backtest
   - **Period:** např. posledních 3–6 měsíců
4. V **Settings** strategie nechte výchozí parametry (SL 40 / PT 80 ticků, daily limit $500).
5. Klikněte **Run**.

**Co kontrolovat v Backtestu:**

| Kontrola | Jak ověřit |
|---|---|
| Vstupy jen long | V záložce **Trades** nejsou short obchody |
| SL/PT správně | Každý trade má exit na −40 nebo +80 ticků (± malá odchylka kvůli fill) |
| Časové okno | Vstupy pouze mezi 15:30–21:45 CET |
| EOD flatten | Pozice otevřené po 21:55 CET jsou uzavřeny |
| Daily loss limit | Po sérii ztrát přes −$500 strategie další den neobchoduje |
| Trend filtr | Vstupy nastávají jen když cena byla nad EMA50 a VWAP |

**Tip:** Zapněte **Trades** tab a filtrujte podle času exitu. Otevřete konkrétní obchod na grafu (pravé tlačítko → **View on chart**) a vizuálně ověřte VWAP pullback pattern.

### Fáze B – Market Replay / Sim101 (simulované realtime)

Po úspěšném backtestu ověřte chování v „živém" režimu bez reálných peněz.

#### Varianta 1 – Market Replay

1. **New → Market Replay**
2. Vyberte MES, období s volatilitou (např. den s trendem).
3. Připojte strategii na graf (viz sekce 5).
4. Spusťte replay rychlostí 5×–20×.
5. Sledujte **Output** okno (Ctrl+O) – strategie vypisuje zprávy při denním loss limitu.

#### Varianta 2 – Sim101 účet

1. Připojte se k brokerovi nebo použijte **Sim101** simulační účet.
2. Otevřete **5min chart MES** s RTH daty.
3. Aktivujte strategii (viz sekce 5).
4. Nechte běžet minimálně **2–5 obchodních dnů** v simulaci.
5. Sledujte **Orders**, **Executions** a **Strategies** tab.

**Co kontrolovat v Sim:**

- Bracket ordery (SL + PT) se odesílají jako OCO hned po fill vstupu
- Při 21:55 CET se pozice zavře i když SL/PT nebyly zasaženy
- Po dosažení −$500 denní ztráty strategie zastaví nové vstupy
- Žádné vstupy mimo 15:30–21:45 CET

### Fáze C – Live (až po úspěšné simulaci)

1. Používejte **1 kontrakt MES**.
2. Zapněte strategii na live účtu.
3. První týden pouze monitorujte – nezasahujte ručně do orderů strategie.
4. Mějte připravený plán: co uděláte, pokud se strategie zastaví kvůli daily loss limitu.

---

## 5. Aktivace strategie na grafu

1. Otevřete **New → Chart**.
2. Nastavte instrument **MES**, interval **5 Minute**.
3. V **Data Series** nastavte **Trading hours: CME US Index Futures RTH**.
4. Klikněte pravým tlačítkem na graf → **Strategies → AOS_VWAPPullbackStrategy**.
5. V dialogu nastavte parametry a **Account** (Sim101 pro test, live účet pro produkci).
6. Zaškrtněte **Enabled**.
7. Klikněte **OK**.

Strategie se zobrazí v panelu **Strategies** (dole na grafu). Stav **Active** = běží.

### Důležité při aktivaci

- **Calculate:** On bar close (nastaveno automaticky ve strategii)
- **Start behavior:** Wait until flat – strategie nezačne obchodovat, dokud nemá flat pozici
- **Enable:** musí být zaškrtnuto
- **Account:** správný účet (Sim101 vs. live)

---

## 6. Logika strategie (shrnutí pro testování)

Strategie hledá tento pattern:

```
1. Cena > EMA(50) AND Cena > VWAP     → bullish trend filtr
2. Cena klesne a dotkne se VWAP       → pullback fáze
3. První zelená svíčka zavře ≥ VWAP  → LONG market order
4. Bracket: SL −40 ticků, PT +80 ticků (OCO)
```

**Ochranné mechanismy:**

- Intraday flatten v 21:55 CET
- Daily loss limit −$500 → halt do dalšího dne
- News filter placeholder (zatím vypnutý, připravený pro rozšíření)

---

## 7. Checklist před spuštěním na live

- [ ] Backtest na min. 3 měsících MES 5min RTH proběhl bez chyb kompilace
- [ ] SL = 40 ticků, PT = 80 ticků ověřeny v Trades tabulce
- [ ] EOD flatten funguje v simulaci
- [ ] Daily loss limit zastaví strategii (test v sim s vědomým překročením)
- [ ] Sim101 běžela min. 2 dny bez neočekávaného chování
- [ ] Default quantity = 1 kontrakt
- [ ] Trading hours = RTH
- [ ] Máte přehled o riziku: 40 ticků × $1.25 = **$50 na kontrakt**

---

## 8. Řešení problémů

| Problém | Řešení |
|---|---|
| Strategie se neobjeví v seznamu | Znovu Compile (F5) v NinjaScript Editoru |
| Chyba kompilace VWAP | Ověřte NT8 verzi; VWAP je vestavěný indikátor |
| Žádné obchody v backtestu | Zkontrolujte RTH session, období dat a časové okno 15:30–21:45 CET |
| SL/PT se neodesílají | Ověřte, že `StopTargetHandling = PerEntryExecution` (nastaveno ve strategii) |
| Špatný čas flatten | NT používá čas barů z dat – ověřte, že data mají správnou timezone |
| Strategie obchoduje mimo RTH | V Data Series nastavte **CME US Index Futures RTH** |

---

## 9. Rozšíření – News Filter (placeholder)

Ve strategii je metoda `IsHighImpactNewsTime()` připravená pro budoucí integraci ekonomického kalendáře (FOMC, CPI, NFP). Po implementaci vraťte `true` v časech vysokého dopadu a strategie pozastaví nové vstupy.

---

## 10. Struktura souborů v projektu

```
1/
├── mvp.md                              # Zadání systému
├── INSTALACE_A_TESTOVANI.md            # Tento návod
└── Strategies/
    └── AOS_VWAPPullbackStrategy.cs     # NinjaScript strategie pro NT8
```
