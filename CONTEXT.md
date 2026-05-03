# SouthparkMod - краткий контекст

Актуально на 2026-05-03.

Мод для RimWorld 1.6.4566 rev571.

Основная рабочая папка:

`C:\Users\svmrabota\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Mods\SouthParkDeaths`

Runtime-копия для игры:

`C:\Program Files (x86)\RimWorld\Mods\SouthParkDeaths`

AppData-копия считается основным источником. После правок C# нужно собрать DLL. Runtime-копию синхронизировать только по явной команде `Давай детка`.

## Что делает мод

1. Когда смерть колониста совпадает с vanilla-условием Man in Black, мод проигрывает звук Kenny death и переодевает труп в Kenny outfit.
2. При использовании resurrector mech serum мод показывает PNG-картинку Иисуса по центру экрана.
3. Vanilla Stranger in Black превращается в Мистерио.
4. Мистерио получает имя, костюм, гранаты, Melee 20, черту Nimble, x2 HP и восстановление через 2 секунды после падения/смерти.
5. Каждый год 10 Decembary в 12:00 Мистер Какашка приходит с края карты как настоящая пешка, но не поселенец, оставляет 1-3 случайных не-имбовых подарка, гуляет около поселенцев и потом уходит. Во время события сутки играет звук `MrHankey.wav` раз в 4 игровых часа.

## Зависимости

`About/About.xml`:

- `packageId`: `local.southpark.deaths`
- зависимость: Harmony
- Harmony packageId: `brrainz.harmony`
- поддерживаемая версия RimWorld: `1.6`

Порядок модов в игре:

1. Harmony
2. SouthparkMod

## Основные файлы

- `About/About.xml` - метаданные мода.
- `SouthParkDeaths.csproj` - проект сборки под `net472`.
- `Source/SouthParkDeaths/SouthParkDeaths.cs` - вся Harmony/C# логика.
- `Assemblies/SouthParkDeaths.dll` - собранная DLL мода.
- `Defs/SoundDefs/SouthParkDeathSounds.xml` - звуки Kenny death и Mr. Hankey event.
- `Defs/IncidentDefs/SouthParkIncidents.xml` - custom incident подарка Мистера Какашки.
- `Defs/ThingDefs/MrHankey.xml` - race ThingDef и PawnKindDef Мистера Какашки.
- `Defs/ThingDefs/Apparel_Kenny.xml` - Kenny parka и Kenny hood.
- `Defs/ThingDefs/Apparel_Mysterion.xml` - Mysterion suit и Mysterion hood.
- `Sounds/SouthPark/KennyDeath.wav` - пользовательский звук.
- `Textures/SouthPark/JesusResurrection.png` - popup resurrector serum.
- `Textures/SouthPark/KennyParka/*` - текстуры Kenny parka.
- `Textures/SouthPark/KennyHood/*` - текстуры Kenny hood.
- `Textures/SouthPark/MysterionSuit/*` - текстуры Mysterion suit.
- `Textures/SouthPark/MysterionHood/*` - текстуры Mysterion hood.
- `Textures/MrHankey*.png` - directional sprites Мистера Какашки.

## DefName и asset paths

SoundDef:

- `SPD_KennyDeath`
- clip path: `SouthPark/KennyDeath`
- файл: `Sounds/SouthPark/KennyDeath.wav`

Mr. Hankey SoundDef:

- `SPD_MrHankeyEvent`
- clip path: `SouthPark/MrHankey`
- файл: `Sounds/SouthPark/MrHankey.wav`
- запускается на сутки события каждые `10000` ticks, то есть раз в 4 игровых часа.

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

Mr. Hankey incident:

- incident def: `SPD_MrHankeyGift`
- visitor race thing def: `SPD_MrHankeyVisitor`
- visitor pawn kind def: `SPD_MrHankeyVisitorKind`
- worker class: `SouthParkDeaths.IncidentWorker_MrHankeyGift`
- target tag: `Map_PlayerHome`
- category: `Special`
- baseChance: `0`
- graphic path: `MrHankey`
- drawSize: `(0.975, 1.65)`, это в 1.5 раза больше старого `(0.65, 1.10)`
- thingClass: `SouthParkDeaths.Pawn_MrHankeyVisitor`
- combatPower: `0`
- required direction textures:
  - `Textures/MrHankey.png`
  - `Textures/MrHankey_north.png`
  - `Textures/MrHankey_east.png`
  - `Textures/MrHankey_south.png`
  - `Textures/MrHankey_west.png`
- direction textures normalized to a `420x840` transparent canvas, with visible bounds centered and bottom padding so the lower part is not clipped.
- current visible bounds: front/back about `368-370x790`, side about `236x790`.
- due to observed pawn renderer behavior, `MrHankey_east.png` currently contains the left-facing side sprite and `MrHankey_west.png` contains the right-facing side sprite.

## C# логика

Namespace: `SouthParkDeaths`

Классы:

- `SouthParkDeathsMod` - вызывает `Harmony.PatchAll()` при загрузке.
- `SouthParkDeathsGameComponent` - тикает состояние, рисует popup, сохраняет счетчики.
- `SouthParkDeathsMapComponent` - пустой map component для совместимости.
- `PawnKillPatch` - postfix на `Pawn.Kill`.
- `ResurrectorSerumPatch` - postfix на `CompTargetEffect_Resurrect.DoEffectOn`.
- `StrangerInBlackGeneratePawnPatch` - postfix на `IncidentWorker_WandererJoin.GeneratePawn`.
- `StrangerInBlackSpawnPatch` - postfix на `IncidentWorker_WandererJoin.SpawnJoiner`.
- `SouthParkDeathsState` - основная логика мода.
- `IncidentWorker_MrHankeyGift` - custom incident подарка Мистера Какашки.
- `Pawn_MrHankeyVisitor` - текущий Мистер Какашка как настоящая пешка.
- `Graphic_MrHankey` - custom `Graphic_Multi`, который выбирает левый/правый side material по фактическому горизонтальному направлению движения Мистера Какашки.
- `Thing_MrHankeyVisitor` - старый thing-visitor, оставлен в коде для совместимости, новый incident его не использует.
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

Мистерио создаётся из vanilla Stranger in Black.

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
- body part max HP multiplier: `MysterionHealthMultiplier = 2`
- apparel:
  - `SPD_Apparel_MysterionSuit`
  - `SPD_Apparel_MysterionHood`
- weapon:
  - `Weapon_GrenadeFrag`

Старое оружие Мистерио удаляется через `pawn.equipment.Remove(...)` и `DestroyMode.Vanish`, затем выдаются frag grenades.

В RimWorld экипированные гранаты работают как оружие без отдельной ammo-логики, поэтому отдельный счетчик боеприпасов не нужен.

## Восстановление Мистерио

При смерти Мистерио `PawnKillPatch` сначала проверяет `SouthParkDeathsState.IsMysterionPawn(...)`.
Если Мистерио downed, `TickMysterionDownedRecoveryScheduler()` ставит его в ту же очередь восстановления.

Мистерио считается Мистерио, если:

- pawn уже есть в словаре счетчика самовоскрешений;
- или на нем есть `SPD_Apparel_MysterionSuit` / `SPD_Apparel_MysterionHood`;
- или его короткое имя равно `Мистерио`.

Лимит:

- `MysterionMaxResurrections = 4`
- `MysterionResurrectionDelayTicks = 120` - задержка восстановления около 2 секунд.
- `MysterionHealthMultiplier = 2` - x2 max HP для всех body parts через Harmony postfix на `BodyPartDef.GetMaxHealth(...)`.

Поведение:

- первые 4 смерти ставят pawn в очередь на воскрешение;
- downed-состояние ставит pawn в очередь на восстановление без траты лимита смертей;
- через 120 ticks для dead pawn вызывается `RimWorld.ResurrectionUtility.TryResurrect(pawn)`, для downed pawn сразу идёт очистка негативных эффектов;
- после воскрешения или восстановления вызывается `HealMysterionCompletely(...)`: удаляются травмы, болезни, missing parts и другие bad/curable hediffs;
- после очистки повторно применяется `CustomizeMysterionJoiner`;
- после 4 использованных самовоскрешений следующая смерть остаётся окончательной.

Счетчик сохраняется в save через:

- `SouthParkDeathsGameComponent.ExposeData()`
- `Scribe_Collections.Look(...)`

Ключ словаря: `pawn.thingIDNumber`.

## Мистер Какашка

Мистер Какашка реализован как custom incident `SPD_MrHankeyGift` и настоящая pawn-раса `SPD_MrHankeyVisitor` с pawn kind `SPD_MrHankeyVisitorKind`.

Автоматический scheduler хранится в `SouthParkDeathsState`:

- `MrHankeyGiftScheduleVersion = 2` - мигрирует старые сейвы со старого двухдневного расписания;
- `MrHankeyChristmasDayOfQuadrum = 10` и `MrHankeyChristmasHourOfDay = 12` - выбранное игровое рождество: 10 Decembary, 12:00;
- `MrHankeyGiftRetryTicks = 2500` - повторная проверка, если сейчас нет подходящей home map;
- save field: `southParkDeathsNextMrHankeyGiftTick`.

Условия автоматического запуска:

- есть player home map;
- на карте есть хотя бы один spawned free colonist.

Поведение при запуске:

- visitor генерируется через `PawnGenerator.GeneratePawn(...)` как `Pawn_MrHankeyVisitor`;
- он не получает `Faction.OfPlayer`, поэтому не становится поселенцем и не управляется игроком как colonist;
- как animal-like pawn он не имеет человеческих навыков Shooting/Melee; если skills вдруг существуют, код принудительно держит Shooting и Melee на 0;
- race не задаёт `tools`, а `combatPower = 0`, поэтому у него нет нормального ближнего/дальнего боя;
- `PreApplyDamage` всегда ставит `absorbed = true`, а `Kill(...)` переопределён в no-op, поэтому он фактически неубиваем во время визита;
- visitor появляется на случайной свободной клетке у края карты;
- запускается звуковое событие `SPD_MrHankeyEvent` на `MrHankeySoundDurationTicks = 60000` ticks, звук повторяется каждые `MrHankeySoundIntervalTicks = 10000` ticks;
- идёт к клетке в `Home area` рядом с поселенцами, если такая клетка доступна; fallback - клетка рядом с поселенцами, затем vanilla trade/drop spot;
- движение, поворот и `DrawPos` выполняются ванильной pawn-системой;
- важно: нельзя каждый тик вызывать `jobs.StopAll(...)`/`pather.StopDead()` для этого pawn, иначе path follower и renderer начинают дёргаться и мерцать;
- при движении pawn renderer берёт нужный direction sprite;
- для side sprite используется `Graphic_MrHankey`: если следующий шаг левее текущей клетки, берётся левый профиль, если правее - правый профиль;
- рядом с колонией он оставляет случайное число подарков: от 1 до 3;
- после подарков гуляет около поселенцев `WanderAfterGiftTicks = 30000` ticks, примерно 12 игровых часов; wander target сначала выбирается в `Home area`;
- после прогулки идёт обратно к edge cell и исчезает.

Отдельная иконка подарка не нужна: подарок является обычным vanilla-предметом или vanilla-животным, поэтому RimWorld использует стандартную иконку/графику самого подарка. Отдельная иконка понадобится только если вводить новый абстрактный предмет вроде `MrHankeyGiftBox`.

Пул обычных подарков:

- `MedicineIndustrial`, 3 шт.
- `MealSimple`, 8 шт.
- `Pemmican`, 50 шт.
- `PackagedSurvivalMeal`, 6 шт.
- `Chocolate`, 20 шт.
- `Gun_Revolver`, 1 шт.
- `Gun_Autopistol`, 1 шт.
- `ComponentIndustrial`, 3 шт.
- `Steel`, 75 шт.
- `Cloth`, 75 шт.

Животный подарок выпадает примерно в 20% случаев для каждого подарка:

- `Chicken`
- `Cat`
- `YorkshireTerrier`
- `Pig`

Если генерация животного не удалась, подарок заменяется на 3 `MedicineIndustrial`.

Инцидент можно вызвать вручную через vanilla debug actions для incidents, выбрав `SPD_MrHankeyGift` / `Mr. Hankey gift`.

## Debug actions

В RimWorld Dev mode доступны действия:

- `SouthparkMod / Play Kenny death sound`
- `SouthparkMod / Replace corpse apparel with Kenny outfit`
- `SouthparkMod / Replace corpse apparel with Mysterion outfit`
- `SouthparkMod / Show resurrection popup`
- `SouthparkMod / Trigger Mr. Hankey gift`

Для проверки Мистерио:

1. Включить `Development mode`.
2. Открыть debug actions.
3. Найти действие для запуска incident.
4. Запустить `StrangerInBlackJoin`.
5. Пришедший pawn должен стать `Мистерио`.

Для проверки Мистера Какашки:

1. Включить `Development mode`.
2. Открыть debug actions.
3. Запустить `SouthparkMod / Trigger Mr. Hankey gift` или vanilla incident `SPD_MrHankeyGift`.
4. Visitor должен появиться с края карты, плавно дойти к колонии, положить 1-3 подарка, погулять рядом и уйти.

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

Последняя проверенная сборка проходит без ошибок и предупреждений.

## Синхронизация в Program Files

После изменения C# нужно:

1. Собрать DLL в AppData-копии.
2. Скопировать runtime-файлы в `C:\Program Files (x86)\RimWorld\Mods\SouthParkDeaths`.
3. Сверить SHA256 важных файлов между AppData и Program Files.
4. Перезапустить RimWorld.

Минимальный список для синхронизации после текущих правок:

- `Assemblies/SouthParkDeaths.dll`
- `About/About.xml`
- `README.txt`
- `Source/SouthParkDeaths/SouthParkDeaths.cs`
- `Defs/IncidentDefs/SouthParkIncidents.xml`
- `Defs/SoundDefs/SouthParkDeathSounds.xml`
- `Defs/ThingDefs/MrHankey.xml`
- `Defs/ThingDefs/Apparel_Kenny.xml`
- `Defs/ThingDefs/Apparel_Mysterion.xml`
- `CONTEXT.md`
- `Sounds/SouthPark/MrHankey.wav`
- `Textures/MrHankey.png`
- `Textures/MrHankey_north.png`
- `Textures/MrHankey_east.png`
- `Textures/MrHankey_south.png`
- `Textures/MrHankey_west.png`

Если `Copy-Item` получает `Access denied`, скорее всего мешают права Windows или запущенная игра.

Проверка совпадения копий:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath "path\to\file"
```
