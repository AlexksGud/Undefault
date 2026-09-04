# Предложение: четыре редакционные правки

**Статус:** revised after review — источник правды для реализации: [editorial-fixes-review-2026-09-04.md](editorial-fixes-review-2026-09-04.md)  
**Дата:** 2026-09-04  
**Контекст:** tech-lead audit leftover / overengineering после product pivot (Tauon + Flow) и SMTC onboarding

Ниже — исходные четыре пункта с пометками, что review принял, урезал или заблокировал. Не реализовывать «Сделать» из исходного текста вслепую: следовать revised scope.

## Живой продукт-путь (не трогать)

```text
POST /gsi → detect → music.control_profile → IMusicPlaybackControl → IMusicPlayer
```

Default Flow: `round_start → resume`, `death → pause`.  
Providers: Tauon (default), SMTC, Mock (`--quick`).

## Вне scope

- Dual TFM GsiHost
- Полный delete `MusicIntent` / mixer алгебры
- Полный delete `TimelineCaptureService`
- Conditional DI / `Core/Music/Deferred/` для shadow
- Focus-пресет как продукт
- Новые фичи
- Mixer → live player

---

## 1. Удалить мёртвый track-URI profile stack + orphan Spotify/SmartTrackStart config

**Вердикт review:** proceed (после Linear filing).

**Аргумент:** На MVP-пути это не участвует. Flow читает только `control-profiles.json` через `IControlProfileService`. Параллельно живёт второй «profile»-мир и orphan-секции в appsettings. Docs и `.cursor/rules` всё ещё описывают STS / Spotify OAuth как текущий продукт.

**Сделать (revised):**

- Удалить `IProfileService`, `JsonProfileService`, track-типы в `AppConfig.cs`, DI + `GET/PUT /profiles`, связанные тесты.
- Убрать `SpotifySystemConfig` из `SystemConfig` и R/W в `AppSettingsConfigurationService` (это **wire-shape change** для `GET/PUT /config`: тело больше не `{ spotify, gsi }`). Расширить `ConfigEndpoint_DoesNotExposeUseMockSpotify`: `spotify` отсутствует.
- `SaveAsync` уже снимает `UseMockSpotify`; так же снимать узлы `Spotify` и `SmartTrackStart` с диска при сохранении.
- Вырезать секции `Spotify` и `SmartTrackStart` из git `appsettings.json` и тестовых фикстур.
- Переименовать bind `SpotifyVolumeDuck` → `VolumeDuck` в том же PR. Без dual binding: code defaults (0 / 50) совпадают с shipped values; кастомный on-disk ключ отвалится на те же defaults — сказать это в PR.
- Docs sweep с явным списком файлов, не одной фразой «привести в соответствие». Минимум: `docs/backend-architecture.md` (секции STS, `/profiles`, `spotify.control_profile` как default ActionMap, `ISpotifyPlaybackControl`); `docs/tauon-integration.md:77` (PIVOT-11 leftover — задача Done).
- `.cursor/rules`: `core-architecture.mdc:14`, `code-reviewer.mdc:13–14,36`, **`gsihost-architecture.mdc:12`**.

**Не трогать:** `music.control_profile`, `JsonControlProfileService`, Tauon/SMTC/Mock, `VolumeDuckOptions` (тип).

---

## 2. Shadow-orchestration default — optional, не отдельный issue

**Вердикт review:** superfluous as scoped. Убрать conditional DI и `Deferred/`. Default flip — опциональная двухстрочная правка, **не** гейтится UND-83.

**Не делать:** регистрацию facade только при `ShadowMode`; перенос типов в `Deferred/`; wiring mixer → player; отдельный Linear issue.

**Если всё же flip:** `MusicOrchestrationOptions` + `appsettings.json` `ShadowMode: false` + фикстуры тестов, которые ждут default `true`.

---

## 3. Убрать коллизию `--mvp` / product MVP

**Вердикт review:** proceed. Hard error на `--mvp`, не silent remove. Перед этим `--intent-capture` должен стать one-flag observe launch.

**Аргумент:** `--mvp` ставит `intent_capture`, включает Timeline/PlaybackObserver и **не исполняет** music actions. Имя флага = продуктный термин, эффект противоположный. Silent remove превратит `dotnet run -- --mvp` в live automation (хост не падает на неизвестном switch).

**Сделать (revised):**

- Сначала: `--intent-capture` (или новый `--observe`) включает `Runtime:Mode=intent_capture` **и** `Timeline:Enabled` + `PlaybackObserver:Enabled` (сегодня это делает только `--mvp`).
- Затем: `--mvp` → non-zero exit + сообщение (default launch / observe flag). Никакого player call.
- Обновить `ConsoleLaunchSettings.IsMvpLaunch`, checklist в `Program.cs`, `PlaybackObserverOptions`, `ConsoleLaunchBootstrapTests`, `docs/quick-launch.md`, `docs/release-checklist.md`, `docs/tauon-integration.md:70`, `docs/README.md:26`, `docs/backend-architecture.md:75`, `product-boundaries.mdc`, `hotkeys-timeline.mdc`.
- `docs/archive/` можно оставить исторический флаг.

**Не делать:** полный delete `TimelineCaptureService`.

---

## 4. Alias `spotify.control_profile` vs пустой `DomainEvents`

**Вердикт review:** это два разных изменения.

### 4a. `DomainEvents` / `TitleDomainEvent`

Не заблокировано PIVOT-6. Unused: `Cs2GameAdapter` всегда отдаёт empty list, detector не читает. Можно отдельным commit после пункта 1 (тесты-конструкторы `AdapterObservation` + docs `ingestion-spec-cs2-dota.md`, `rules-engine-migration.md`, `multi-adapter-routing.md`).

### 4b. Alias `spotify.control_profile`

**Заблокировано** до решения PO: roadmap PIVOT-6 явно «keep alias» и помечен Done. Git default был `spotify.control_profile` с `361afaa` до `6c21da2`; on-disk owner files могут всё ещё мапить на alias.

Перед снятием регистрации: startup/ctor warning на каждый `ActionMap` ключ без зарегистрированного `IEventAction` + тест. Сейчас `RulesEngine.ExecuteActionsAsync` **молча** `continue` — не log.

Когда PO одобрит: снять вторую DI-регистрацию и `LegacySpotifyKey`; обновить `docs/roadmap.md` (PIVOT-6 + Current code) и `docs/music-provider-architecture.md:113`. Не бандлить с пунктами 1–3.

---

## Порядок (revised)

`1 → 3 → 4a DomainEvents → (2 optional) → 4b alias (после PO + ActionMap warning)`

Пункты 1 и 4b оба трогают `BuildAppSettingsJson` в `GsiHostIntegrationTests` — ждать rebase.

## Acceptance (amended)

- `dotnet test UndefaultIt.sln` зелёный на `windows-latest`
- default launch без флагов по-прежнему Flow pause/resume через Mock/Tauon
- `dotnet run -- --mvp` → non-zero + сообщение; player не вызывается
- `--intent-capture` (или `--observe`) = one-flag observe
- host с `ActionMap` → `spotify.control_profile` пишет warning на старте (**только 4b**)
- нет новых фич; нет Spotify provider; нет mixer → player
