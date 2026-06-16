# Release workflow – Futures-NinjaTrader

> **Pro AI agenty:** Před každým commitem, tagem nebo pushem do GitHubu **přečti a dodržuj tento dokument**.  
> Cílový repozitář: [toz-panzmoravy/Futures-NinjaTrader](https://github.com/toz-panzmoravy/Futures-NinjaTrader)

---

## 1. Účel

Tento repozitář sdružuje všechny NinjaTrader 8 AOS strategie pro futures (MES, MNQ, ZN, …).  
Každá nová verze strategie v **jakékoliv složce** se verzuje samostatně, označí git tagem a pushne na GitHub.

---

## 2. Struktura repozitáře

| Složka | Instrument | Strategie | Aktuální soubor | Poslední verze |
|--------|------------|-----------|-----------------|----------------|
| `MES/` | MES | Microtrend Prop (TICK 200) | `MES/NinjaTrader/Strategies/MesMicrotrendProp_v104.cs` | `v1.0.4` |

> **Po každém release aktualizuj sloupec „Poslední verze" v této tabulce.**

Každá složka obsahuje:
- `mvp.md` – zadání / specifikace strategie
- `NinjaTrader/Strategies/*.cs` – zdrojový kód (nebo `Strategies/` u starších verzí)
- volitelně `NinjaTrader/README.md` – dokumentace strategie

---

## 3. Konvence verzování (SemVer)

Formát: `MAJOR.MINOR.PATCH` (např. `1.0.5`)

| Typ změny | Bump | Příklad |
|-----------|------|---------|
| Breaking change (změna signálu, odstranění parametru) | MAJOR | `1.4.0` → `2.0.0` |
| Nová funkce, nový filtr, změna defaultů | MINOR | `1.4.0` → `1.5.0` |
| Bugfix, kosmetika, komentáře | PATCH | `1.4.0` → `1.4.1` |

**Mapování na název souboru (složka `MES/`):**
- `v104` v názvu souboru = semver `v1.0.4`

Při vytvoření nové verze ve složce `MES/`:
1. Zkopíruj předchozí soubor → `MesMicrotrendProp_v105.cs` (pokud MINOR/PATCH)
2. Aktualizuj `Name`, `Description` a XML komentář verze uvnitř třídy
3. Aktualizuj tabulku verzí v `MES/README.md` a v §2 tohoto souboru

---

## 4. Konvence git tagů

### Formát tagu

```
{instrument}/{strategy-slug}/v{MAJOR}.{MINOR}.{PATCH}
```

| Instrument | strategy-slug | Příklad tagu |
|------------|---------------|--------------|
| `mes` | `microtrend-prop` | `mes/microtrend-prop/v1.0.4` |

- Tagy jsou **annotated** (s popisem), ne lightweight.
- Jeden tag = jedna verze jedné strategie.
- Tag může ukazovat na commit, který obsahuje i jiné změny v repu – v popisu tagu vždy uveď, co se mění.

### Popis tagu (annotation message)

```
{instrument}/{strategy} v{X.Y.Z}

- Shrnutí změn (1–5 odrážek)
- Soubor: cesta/k/souboru.cs
- Instrument: MES / MNQ / ZN
```

---

## 5. Konvence commit messages

```
{type}({scope}): {krátký popis}

{volitelné tělo – co a proč}
```

| type | Kdy použít |
|------|------------|
| `feat` | Nová strategie nebo nová verze s novou logikou |
| `fix` | Oprava bugu ve strategii |
| `docs` | Změna mvp.md, README, RELEASE.md |
| `chore` | .gitignore, CI, reorganizace bez změny logiky |

| scope | Hodnota |
|-------|---------|
| `mes` | složka `MES/` |
| `repo` | kořenové soubory (RELEASE.md, README, .gitignore) |

**Příklady:**
```
feat(zn): add mean reversion prop strategy v1.0.0
feat(mes): vwap pullback prop v1.0.6 – tighten ADX filter
fix(mnq): correct flat-before-close time in SEČ
docs(mes): update version table in README
```

---

## 6. Checklist před každým release (povinný)

Postupuj **v tomto pořadí**. Nevynechávej kroky.

### Krok 1 – Identifikace

- [ ] Urči **složku**, **instrument** a **strategy-slug** (viz tabulka v §2 a §4)
- [ ] Urči typ změny → zvol MAJOR / MINOR / PATCH bump
- [ ] Zkontroluj, že kód kompiluje v NinjaScript Editoru (F5) – pokud je to možné

### Krok 2 – Příprava souborů

- [ ] U verzovaných souborů: vytvoř nový `.cs` s odpovídajícím číslem verze v názvu
- [ ] Aktualizuj README ve složce strategie (tabulka verzí, changelog)
- [ ] Aktualizuj tabulku „Poslední verze" v **tomto souboru** (`RELEASE.md` §2)

### Krok 3 – Git commit

```powershell
cd "c:\GitHub@Panzmoravy\Futures@Trading"
git status
git add <relevantní soubory>
git commit -m "feat(zn): mean reversion prop strategy v1.0.0"
```

**Nepřidávej:**
- `**/Exports/**` (backtest CSV) – jsou v `.gitignore`
- `.env`, credentials, API klíče

### Krok 4 – Git tag

```powershell
git tag -a zn/mean-reversion-prop/v1.0.0 -m "zn/mean-reversion-prop v1.0.0

- Initial release: Bollinger + RSI mean reversion
- File: ZN/NinjaTrader/Strategies/ZN_MeanReversionPropStrategy.cs
- Instrument: ZN (10-Year T-Note)"
```

### Krok 5 – Push

```powershell
git push origin main
git push origin zn/mean-reversion-prop/v1.0.0
```

Nebo push všech tagů najednou:
```powershell
git push origin main --follow-tags
```

### Krok 6 – GitHub Release (doporučeno u MINOR+ a MAJOR)

```powershell
gh release create zn/mean-reversion-prop/v1.0.0 `
  --title "ZN Mean Reversion Prop v1.0.0" `
  --notes "## ZN Mean Reversion Prop v1.0.0

- Bollinger Bands (20, 2) + RSI (14) mean reversion
- Prop risk: daily loss -500 USD, news filter, flat 21:55 SEČ
- File: \`ZN/NinjaTrader/Strategies/ZN_MeanReversionPropStrategy.cs\`"
```

U PATCH verzí stačí tag bez GitHub Release.

---

## 7. Co se nesmí pushovat

Definováno v `.gitignore`:

| Vzor | Důvod |
|------|-------|
| `**/Exports/**` | Backtest CSV exporty z NT8 |
| `*.csv` | Grid exporty, výsledky optimalizace |
| `.env`, `*credentials*` | Citlivé údaje |
| `bin/`, `obj/` | Build artefakty |

---

## 8. První nastavení repozitáře (jednorázově)

```powershell
cd "c:\GitHub@Panzmoravy\Futures@Trading"
git init
git remote add origin https://github.com/toz-panzmoravy/Futures-NinjaTrader.git
git branch -M main
git add .
git status   # ověř, že nejsou CSV/Exports
git commit -m "chore(repo): initial import of all futures AOS strategies"
git push -u origin main
```

Pokud remote repo už obsahuje `mvp.md` (starý MES spec), při prvním pushi použij:

```powershell
git pull origin main --rebase --allow-unrelated-histories
# vyřeš konflikty (ponech oba mvp.md v příslušných složkách)
git push -u origin main
```

---

## 9. Rychlý přehled tagů

```powershell
git tag -l "mes/*"
git tag -l "mnq/*"
git tag -l "zn/*"
git tag -l --sort=-version:refname
```

---

## 10. Příklad: release nové verze MES v1.0.6

1. Zkopíruj `VwapPullbackProp_v105.cs` → `VwapPullbackProp_v106.cs`
2. Uprav logiku, `Name = "VwapPullbackProp_v106"`, popis verze
3. Aktualizuj `2/NinjaTrader/README.md` – přidej řádek v tabulce verzí
4. Aktualizuj §2 v tomto souboru → `v1.0.6`
5. Commit: `feat(mes): vwap pullback prop v1.0.6 – popis změny`
6. Tag: `mes/vwap-pullback-prop/v1.0.6`
7. Push branch + tag
8. `gh release create mes/vwap-pullback-prop/v1.0.6 --title "MES VWAP Pullback Prop v1.0.6" --notes "..."`

---

## 11. Kontakt / remote

| | |
|---|---|
| **GitHub** | https://github.com/toz-panzmoravy/Futures-NinjaTrader |
| **Remote** | `origin` → `https://github.com/toz-panzmoravy/Futures-NinjaTrader.git` |
| **Default branch** | `main` |
