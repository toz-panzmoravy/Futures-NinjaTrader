📋 ZADÁNÍ PRO AI: Vývoj Prop-Trading AOS pro NinjaTrader 8 (MNQ - ORB Strategie)

**Role a Cíl:**
Chovej se jako expert na programování v NinjaScriptu (C#) pro platformu NinjaTrader 8. Tvým úkolem je napsat kompletní a robustní kód pro Automatický Obchodní Systém (AOS). Systém je určen pro splnění prop-trading výzvy.

**1. Základní parametry:**
* Cílový trh: MNQ (Micro E-mini Nasdaq 100).
* Typ grafu: Minutový (1 Minute).

**2. Časové filtry (Time Management):**
* Aktivní hodiny (RTH): Systém smí obchodovat pouze v čase od 15:30 do 21:45 SEČ.
* Flat Before Close: Všechny otevřené pozice musí být nekompromisně uzavřeny ve 21:55 SEČ a další obchody se ten den nesmí otevírat.

**3. Prop-Trading Ochranný štít (Risk Management):**
* Hard Daily Loss Limit: Jakmile denní P&L (zavřené i otevřené pozice) dosáhne hodnoty -500 USD, systém musí zablokovat nové vstupy a vypnout se do konce dne.
* News Filter: Zákaz obchodování (a uzavření otevřených pozic) 15 minut před a po klíčových makro zprávách.

**4. Obchodní logika (Opening Range Breakout - ORB):**
* Setup (Měření rozpětí): Od 15:30 do 16:00 SEČ systém neobchoduje. Místo toho přesně změří nejvyšší (High) a nejnižší (Low) cenu za tuto první půlhodinu (tzv. Opening Range).
* Trigger (Spoušť pro LONG): Jakmile 1-minutová svíčka po 16:00 uzavře NAD změřeným High, AOS otevírá LONG pozici.
* Trigger (Spoušť pro SHORT): Jakmile 1-minutová svíčka po 16:00 uzavře POD změřeným Low, AOS otevírá SHORT pozici.
* Omezení: Povol pouze jeden první průraz denně (max 1 obchod za den).

**5. Trade Management (Výstupy a ochrana zisku):**
* Stop Loss (SL): Fixní Stop Loss nastavený jako uživatelský parametr (např. 160 ticků / 40 bodů, vzhledem k vysoké volatilitě MNQ).
* Profit Target (PT): Nastavený jako uživatelský parametr s RRR 1:2 (např. 320 ticků / 80 bodů).
* Ochrana před Trailing Drawdownem: Implementuj posuvný Stop Loss (Trailing Stop), který se aktivuje, jakmile se pozice dostane do definovaného zisku, aby ochránil otevřený profit pro účely prop-tradingu.

**Požadavky na kód:**
Všechny parametry (SL, PT, časy ORB, Daily Loss) musí být uživatelsky nastavitelné v rozhraní (Properties).