# SouthParkDeaths - краткий контекст

Актуально на 2026-05-03.

Мод для RimWorld 1.6.4566 rev571.

Рабочая папка:

`C:\Users\svmrabota\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\SouthParkDeaths`

Вторая установленная копия:

`C:\Program Files (x86)\RimWorld\Mods\SouthParkDeaths`

AppData-копия считается основным источником. После правок runtime-файлы нужно синхронизировать во вторую копию, если RimWorld загружает мод оттуда.

Полный старый журнал правок сохранен здесь:

`.tools/.store/context_backups/CONTEXT_full_before_trim_20260503.md`

## Что делает мод

1. Когда смерть колониста совпадает с vanilla-условием Man in Black, мод проигрывает звук Kenny death и переодевает труп в Kenny outfit.
2. При использовании resurrector mech serum мод показывает PNG-картинку Иисуса по центру экрана.
3. Vanilla Stranger in Black превращается в Мистерио.
4. Мистерио получает усиления: имя, костюм, гранаты, Melee 20, черту Nimble и 4 самовоскрешения.

## Зависимости

`About/About.xml`:

- `packageId`: `local.southpark.deaths`
- зависимость: Harmony
- Harmony packageId: `brrainz.harmony`
- поддерживаемая версия RimWorld: `1.6`

Порядок модов в игре:

1. Harmony
2. South Park Deaths

## Основные файлы

- `About/About.xml` - метаданные мода.
- `SouthParkDeaths.csproj` - проект сборки под `net472`.
- `Source/SouthParkDeaths/SouthParkDeaths.cs` - вся Harmony/C# логика.
- `Assemblies/SouthParkDeaths.dll` - собранная DLL мода.
- `Defs/SoundDefs/SouthParkDeathSounds.xml` - звук Kenny death.
- `Defs/ThingDefs/Apparel_Kenny.xml` - Kenny parka и Kenny hood.
- `Defs/ThingDefs/Apparel_Mysterion.xml` - Mysterion suit и Mysterion hood.
- `Sounds/SouthPark/KennyDeath.wav` - пользовательский звук.
- `Textures/SouthPark/JesusResurrection.png` - popup resurrector serum.
- `Textures/SouthPark/KennyParka/*` - текстуры Kenny parka.
- `Textures/SouthPark/KennyHood/*` - текстуры Kenny hood.
- `Textures/SouthPark/MysterionSuit/*` - текстуры Mysterion suit.
- `Textures/SouthPark/MysterionHood/*` - текстуры Mysterion hood.
- `AssetBackups/*` - старые backup-копии PNG во время тюнинга ассетов.

## DefName и asset paths

SoundDef:

- `SPD_KennyDeath`
- clip path: `SouthPark/KennyDeath`
- файл: `Sounds/SouthPark/KennyDeath.wav`

Kenny apparel:

- `SPD_Apparel_KennyParka`
- `SPD_Apparel_KennyHood`
- worn graphic paths:
  - `SouthPark/KennyParka/KennyParka`
  - `SouthPark/KennyHood/KennyHood`

Mysterion apparel:

- `SPD_Apparel_MysterionSuit`
- `SPD_Apparel_MysterionHood`
- worn graphic paths:
  - `SouthPark/MysterionSuit/MysterionSuit`
  - `SouthPark/MysterionHood/MysterionHood`

Resurrection popup:

- texture path: `SouthPark/JesusResurrection`
- файл: `Textures/SouthPark/JesusResurrection.png`

Mysterion gameplay defs:

- trait: `Nimble`
- weapon: `Weapon_GrenadeFrag`

## C# логика

Namespace: `SouthParkDeaths`

Классы:

- `SouthParkDeathsMod` - вызывает `Harmony.PatchAll()` при загрузке.
- `SouthParkDeathsGameComponent` - тикает состояние, рисует popup, сохраняет счетчик самовоскрешений.
- `SouthParkDeathsMapComponent` - пустой map component для совместимости.
- `PawnKillPatch` - postfix на `Pawn.Kill`.
- `ResurrectorSerumPatch` - postfix на `CompTargetEffect_Resurrect.DoEffectOn`.
- `StrangerInBlackGeneratePawnPatch` - postfix на `IncidentWorker_WandererJoin.GeneratePawn`.
- `StrangerInBlackSpawnPatch` - postfix на `IncidentWorker_WandererJoin.SpawnJoiner`.
- `SouthParkDeathsState` - основная логика мода.
- `SouthParkDeathsDebugActions` - debug actions для Dev mode.

## Смерть колониста и Kenny outfit

`PawnKillPatch` реагирует на смерть колониста.

Для обычного колониста эффект Kenny запускается только если vanilla `StrangerInBlackJoin.Worker.CanFireNow(...)` возвращает `true`. Это условие близко к Man in Black:

- карта является player home;
- vanilla Storyteller считает, что Stranger in Black может прийти.

Если условие выполняется:

- старая одежда трупа снимается и дропается рядом;
- на труп надеваются `SPD_Apparel_KennyParka` и `SPD_Apparel_KennyHood`;
- проигрывается `SPD_KennyDeath` через `SoundStarter.PlayOneShotOnCamera`.

## Resurrector serum popup

После `CompTargetEffect_Resurrect.DoEffectOn` мод ставит таймер popup на 240 ticks.

В `GameComponentOnGUI` рисуется `Textures/SouthPark/JesusResurrection.png` по центру экрана. В конце таймера popup плавно исчезает.

## Мистерио

Мистерио создается из vanilla Stranger in Black.

Патчи на `IncidentWorker_WandererJoin.GeneratePawn` и `SpawnJoiner` проверяют:

`__instance.def.defName == "StrangerInBlackJoin"`

После этого `CustomizeMysterionJoiner` делает pawn Мистерио:

- имя: `Мистерио`
- фракция: `Faction.OfPlayer`
- `hostilityResponse`: `Attack`
- `selfTend`: `true`
- `FireAtWill`: `true`
- Melee level: `20`
- Melee passion: `Passion.Major`
- trait: `Nimble`
- apparel:
  - `SPD_Apparel_MysterionSuit`
  - `SPD_Apparel_MysterionHood`
- weapon:
  - `Weapon_GrenadeFrag`

Старое оружие Мистерио удаляется через `pawn.equipment.Remove(...)` и `DestroyMode.Vanish`, затем выдаются frag grenades.

В RimWorld экипированные гранаты работают как оружие без отдельной ammo-логики, поэтому отдельный счетчик боеприпасов не нужен.

## Самовоскрешения Мистерио

При смерти Мистерио `PawnKillPatch` сначала проверяет `SouthParkDeathsState.IsMysterionPawn(...)`.

Мистерио считается Мистерио, если:

- pawn уже есть в словаре счетчика самовоскрешений;
- или на нем есть `SPD_Apparel_MysterionSuit` / `SPD_Apparel_MysterionHood`;
- или его короткое имя равно `Мистерио`.

Лимит:

- `MysterionMaxResurrections = 4`
- `MysterionResurrectionDelayTicks = 120` - задержка самовоскрешения около 2 секунд.

Поведение:

- первые 4 смерти ставят pawn в очередь на воскрешение;
- через 120 ticks вызывается `RimWorld.ResurrectionUtility.TryResurrect(pawn)`;
- после воскрешения повторно применяется `CustomizeMysterionJoiner`;
- после 4 использованных самовоскрешений следующая смерть остается окончательной.

Счетчик сохраняется в save через:

- `SouthParkDeathsGameComponent.ExposeData()`
- `Scribe_Collections.Look(...)`

Ключ словаря: `pawn.thingIDNumber`.

## Debug actions

В RimWorld Dev mode доступны действия:

- `South Park Deaths / Play Kenny death sound`
- `South Park Deaths / Replace corpse apparel with Kenny outfit`
- `South Park Deaths / Replace corpse apparel with Mysterion outfit`
- `South Park Deaths / Show resurrection popup`

Для проверки Мистерио:

1. Включить `Development mode`.
2. Открыть debug actions.
3. Найти действие для запуска incident.
4. Запустить `StrangerInBlackJoin`.
5. Пришедший pawn должен стать `Мистерио`.

## Сборка

Команда:

```powershell
dotnet build SouthParkDeaths.csproj -c Release
```

Проект собирает:

`Assemblies/SouthParkDeaths.dll`

Target framework:

`net472`

Основные reference paths в `SouthParkDeaths.csproj`:

- `C:\Program Files (x86)\RimWorld\RimWorldWin64_Data\Managed\Assembly-CSharp.dll`
- `C:\Program Files (x86)\RimWorld\RimWorldWin64_Data\Managed\UnityEngine.dll`
- `C:\Program Files (x86)\RimWorld\RimWorldWin64_Data\Managed\UnityEngine.CoreModule.dll`
- `C:\Program Files (x86)\RimWorld\RimWorldWin64_Data\Managed\UnityEngine.IMGUIModule.dll`
- `C:\Program Files (x86)\RimWorld\Mods\HarmonyMod\Current\Assemblies\0Harmony.dll`

Последняя проверенная сборка проходила без ошибок и предупреждений.

## Синхронизация в Program Files

После изменения C# нужно:

1. Собрать DLL в AppData-копии.
2. Скопировать минимум эти файлы в `C:\Program Files (x86)\RimWorld\Mods\SouthParkDeaths`:
   - `Assemblies/SouthParkDeaths.dll`
   - `Assemblies/SouthParkDeaths.pdb`
   - `Source/SouthParkDeaths/SouthParkDeaths.cs`
   - `CONTEXT.md`
3. Перезапустить RimWorld.

Если `Copy-Item` получает `Access denied`, скорее всего мешают права Windows или запущенная игра.

Проверка совпадения копий:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath "path\to\file"
```
