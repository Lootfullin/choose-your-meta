# Choose your Meta! for Jellyfin

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.11-00A4DC?style=flat-square&logo=jellyfin&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

Плагин для Jellyfin, который позволяет независимо выбирать язык метаданных и
изображений. Текст локализуется на русский по отдельным полям, а для постеров и
логотипов можно назначить приоритет `RU → EN` или `EN → RU` отдельно российским
и зарубежным фильмам. Использует **TMDB**, **Fanart.tv** и **Wikidata**.

---

## Возможности

- **🇷🇺 Русские названия и описания** — фильмы, сериалы и эпизоды отображаются на русском языке
- **Русские данные о фильме** — слоган, жанры, студии, актёры, режиссёры и сценаристы
- **Fallback отдельно для каждого поля** — английский текст используется только при отсутствии русского
- **Разные правила изображений** — российским и зарубежным фильмам назначаются собственные языки постеров и логотипов
- **RU → EN или EN → RU** — второй язык используется, если у источника нет изображения предпочитаемого языка
- **Локальные изображения первыми** — обычное сканирование Jellyfin не заменяет найденные рядом с фильмом постеры и логотипы
- **Коллекции** — русское название/описание и независимый язык постера и логотипа
- **Без отдельной регистрации TMDB** — используется API-ключ встроенного TheMovieDB из установленной версии Jellyfin
- **Настройка библиотек одной кнопкой** — плагин сам включает и поднимает свои загрузчики
- **Фоны без изменений** — плагин не вмешивается в загрузку фоновых изображений
- **Три источника данных** — TMDB для метаданных и постеров, Fanart.tv для логотипов коллекций, Wikidata как резерв
- **Гибкая настройка** — включение/отключение замены названий и описаний независимо друг через друга
- **Прокси** — опциональный HTTP-прокси для доступа к TMDB и Fanart.tv
- **Умный каскад** — сначала TMDB (самые полные данные), затем Wikidata
- **Подробное логирование** — диагностические сообщения с префиксом `RussianMetadata:`
- **Извлечение IMDb ID** — автоматически находит IMDb ID из названий файлов и папок

## Требования

| Компонент | Версия |
|-----------|--------|
| Jellyfin  | 10.11.11 (другие версии требуют проверки совместимости) |
| .NET Runtime | 9.0+ (входит в состав Jellyfin 10.11) |
| TheMovieDB | Встроенный плагин Jellyfin 10.11.11 |
| Fanart | Официальный плагин Jellyfin; нужен только для логотипов коллекций |

## Установка

### Вариант 1: Из каталога Jellyfin — рекомендуется

1. Откройте **Dashboard → Plugins → Repositories**.
2. Добавьте репозиторий:

   ```text
   https://raw.githubusercontent.com/Lootfullin/jellyfin-smart-resolver/main/manifest.json
   ```

3. Откройте **Catalog → General → Choose your Meta!** и нажмите **Install**.
4. Перезапустите Jellyfin.

В этом же репозитории находится отдельный плагин **Jellyfin Smart Resolver**.
Их можно устанавливать и обновлять независимо.

### Вариант 2: Вручную

1. Скачайте ZIP последней версии из
   [Releases](https://github.com/Lootfullin/choose-your-meta/releases).
2. Распакуйте `RussianMetadata.dll` и `meta.json` в отдельную папку:

   ```text
   /path/to/jellyfin/plugins/Choose your Meta/
   ```

3. Перезапустите Jellyfin.

### После установки

1. Для логотипов коллекций установите официальный **Fanart** из каталога
   Jellyfin и перезапустите сервер. Его API key настраивать не нужно.
2. Перейдите в **Dashboard → Plugins → Choose your Meta!** и настройте текст и
   изображения. Поле собственного TMDB API key оставьте пустым.
3. Нажмите **Настроить библиотеки автоматически**. Плагин включит себя для
   фильмов, сериалов, эпизодов и коллекций и поставит выше TheMovieDB, сохранив
   остальные загрузчики.
4. **Обновите метаданные** после настройки:
   - 📺 TV библиотека: три точки → **Refresh Metadata** → ✅ **Replace all existing metadata** → **Refresh**
   - 🎬 Movies библиотека: три точки → **Refresh Metadata** → ✅ **Replace all existing metadata** и, если нужно сменить постеры/логотипы, ✅ **Replace existing images** → **Refresh**

### Вариант 3: Сборка из исходников

```bash
git clone https://github.com/Lootfullin/choose-your-meta.git
cd choose-your-meta
dotnet build -c Release
```

Скопируйте `bin/Release/net9.0/RussianMetadata.dll` в папку плагинов.

## Настройка

**Dashboard → Plugins → Choose your Meta! → Settings:**

| Параметр | Описание |
|----------|----------|
| **TMDB API Key** | Необязательный аварийный override; обычно оставляется пустым |
| **Enable Russian Titles** | Заменять английские названия на русские |
| **Enable Russian Overviews** | Заменять английские описания на русские |
| **Enable Russian Taglines** | Использовать русский слоган, если он существует |
| **Enable Russian Genres** | Загружать русские названия жанров |
| **Enable Russian Studios** | Использовать русское название студии, если оно существует |
| **Enable Russian People** | Использовать русские имена актёров и съёмочной группы, если они существуют |
| **Зарубежные фильмы → Постеры** | `EN → RU`, `RU → EN` или отключено |
| **Зарубежные фильмы → Логотипы** | `EN → RU`, `RU → EN` или отключено |
| **Российские фильмы → Постеры** | `RU → EN`, `EN → RU` или отключено |
| **Российские фильмы → Логотипы** | `RU → EN`, `EN → RU` или отключено |
| **Коллекции → Постеры** | `EN → RU`, `RU → EN` или отключено |
| **Коллекции → Логотипы** | `EN → RU`, `RU → EN` или отключено; источник Fanart.tv |
| **Proxy URL** | URL HTTP-прокси (например `http://proxy.example.com:3128`) |
| **Proxy Username** | Имя пользователя для прокси (опционально) |
| **Proxy Password** | Пароль для прокси (опционально) |

> **Примечание:** отдельный API-ключ не требуется. Choose your Meta! извлекает ключ из встроенного TheMovieDB текущей версии Jellyfin. Собственный ключ остаётся резервом на случай изменения внутренней интеграции.

TMDB предоставляет коллекциям постеры и фоны. Логотипы коллекций Choose your
Meta! получает с Fanart.tv через официальный плагин Fanart для Jellyfin.
Отдельно регистрировать API-ключ Fanart.tv не требуется.

### Конфигурационный XML-файл

Настройки также можно задать напрямую через XML-файл. Jellyfin автоматически создаёт его после первого запуска плагина по пути:

```
<папка_конфига>/plugins/configurations/RussianMetadata.xml
```

**Пример файла:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <TmdbApiKey />
  <EnableRussianTitles>true</EnableRussianTitles>
  <EnableRussianOverviews>true</EnableRussianOverviews>
  <EnableRussianTaglines>true</EnableRussianTaglines>
  <EnableRussianGenres>true</EnableRussianGenres>
  <EnableRussianStudios>true</EnableRussianStudios>
  <EnableRussianPeople>true</EnableRussianPeople>
  <ForeignMoviePosterPreference>EnglishFirst</ForeignMoviePosterPreference>
  <ForeignMovieLogoPreference>EnglishFirst</ForeignMovieLogoPreference>
  <RussianMoviePosterPreference>RussianFirst</RussianMoviePosterPreference>
  <RussianMovieLogoPreference>RussianFirst</RussianMovieLogoPreference>
  <CollectionPosterPreference>EnglishFirst</CollectionPosterPreference>
  <CollectionLogoPreference>EnglishFirst</CollectionLogoPreference>
  <ProxyUrl>http://proxy.example.com:3128</ProxyUrl>
  <ProxyUsername>логин_прокси</ProxyUsername>
  <ProxyPassword>пароль_прокси</ProxyPassword>
</PluginConfiguration>
```

> Если файла нет — создайте его вручную или откройте **Dashboard → Plugins → Choose your Meta! → Settings**, сохраните любые настройки — Jellyfin сам сгенерирует файл. После редактирования XML перезапустите Jellyfin, чтобы изменения применились.

## Как это работает

```
┌─────────────────────────────────────────────────┐
│  Обновление метаданных Jellyfin                   │
│                                                   │
│  1. Извлечение IMDb ID (ttXXXXXXXX) из пути к     │
│     файлу или существующих ProviderIds            │
│                                                   │
│  2. Запрос к TMDB (через прокси, если настроен)   │
│     ├── Поиск фильма/сериала по IMDb ID           │
│     └── Получение русских данных (название, опис.) │
│                                                   │
│  3. Резерв: запрос к Wikidata (SPARQL)            │
│     ├── Поиск по IMDb ID → русская метка/описание │
│     └── Поиск по названию, если нет IMDb ID       │
│                                                   │
│  4. Применение русских метаданных к элементу      │
└─────────────────────────────────────────────────┘
```

### Приоритет источников

1. **TMDB** — богатые метаданные, русские описания (500+ символов), поддержка прокси
2. **Wikidata по IMDb ID** — надёжный источник, более короткие описания
3. **Wikidata по названию** — резерв, когда IMDb ID недоступен

## Сборка из исходников

```bash
# Требуется: .NET 9.0 SDK
# Скачать: https://dotnet.microsoft.com/download

git clone https://github.com/Lootfullin/choose-your-meta.git
cd choose-your-meta

# Сборка
dotnet build

# Сборка (release)
dotnet build -c Release

# Результат: bin/Debug/net9.0/RussianMetadata.dll
```

## Структура проекта

```
RussianMetadata/
├── Configuration/
│   ├── PluginConfiguration.cs    # Модель настроек плагина
│   └── configPage.html           # Веб-интерфейс в Dashboard
├── Plugin.cs                     # Точка входа плагина
├── RussianMovieProvider.cs       # Провайдер метаданных для фильмов
├── RussianMovieImageProvider.cs  # Политики RU/EN для изображений фильмов
├── ChooseYourMetaBoxSetProvider.cs       # Метаданные коллекций
├── ChooseYourMetaBoxSetImageProvider.cs  # Постеры и логотипы коллекций
├── FanartApiKeyResolver.cs               # Интеграция с официальным Fanart
├── LibraryConfigurationService.cs        # Автонастройка библиотек
├── ChooseYourMetaController.cs           # Административный API
├── RussianSeriesProvider.cs      # Провайдер метаданных для сериалов
├── RussianEpisodeProvider.cs     # Провайдер метаданных для эпизодов
├── RussianMetadata.csproj        # Файл проекта .NET
├── README.md
└── LICENSE
```

### Ключевые интерфейсы

| Интерфейс | Назначение |
|-----------|------------|
| `IRemoteMetadataProvider<Movie, MovieInfo>` | Удалённый провайдер для фильмов |
| `IRemoteMetadataProvider<Series, SeriesInfo>` | Удалённый провайдер для сериалов |
| `IRemoteMetadataProvider<Episode, EpisodeInfo>` | Удалённый провайдер для эпизодов |
| `IRemoteMetadataProvider<Movie, MovieInfo>` | Возвращает полный русский результат первым; следующие провайдеры заполняют пропуски |
| `ICustomMetadataProvider<Series>` | То же для сериалов |
| `ICustomMetadataProvider<Episode>` | То же для эпизодов |

## Решение проблем

1. **Проверьте логи Jellyfin** — ищите записи с префиксом `RussianMetadata:`, `RussianMetadata (Series):`, `RussianMetadata (Episode):`
2. **TMDB не отвечает?** — настройте прокси, если TMDB заблокирован в вашем регионе
3. **Русские данные не применяются?** — убедитесь, что плагин включён в **Metadata Downloaders** для нужной библиотеки (Dashboard → Libraries → ✏️ → Metadata Downloaders)
4. **Сериал пропадает из UI после сканирования?** — обновите до v1.2.0. Исправлено: извлечение имени сериала из пути папки вместо корня библиотеки.
5. **Пустые описания эпизодов / сломанные сезоны?** — обновите плагин до последней версии (v1.2+). При обновлении сделайте полное обновление метаданных для TV-библиотеки: Dashboard → Libraries → три точки → **Refresh Metadata** → **Replace all existing metadata**
6. **Сборка не удаётся?** — проверьте, что установлен .NET 9.0 SDK

## Changelog

### v1.3.2

- Исправлено английское название фильма при уже локализованных описании, слогане и жанрах
- Русское название теперь применяется после стандартного слияния удалённых провайдеров Jellyfin
- Изменение затрагивает только название и не заменяет изображения или остальные метаданные

### v1.3.1

- Исправлена локализация фильмов, для которых Jellyfin не передал IMDb ID
- Добавлено прямое использование уже известного TMDB ID
- Добавлен резервный поиск TMDB по названию и году
- Из служебных имён вида `(1967) You Only Live Twice` корректно извлекаются название и год
- Числовые названия фильмов (`1917`, `2012`) не принимаются за год
- При неудаче источника плагин больше не возвращает пустой фильм и не стирает название

### v1.3.0

- Фильмы теперь локализуются по отдельным полям, а не по принципу «успешен весь источник или нет»
- Добавлены русские слоганы, жанры, студии, актёры, режиссёры и сценаристы
- Русские подписи людей и компаний пакетно запрашиваются из Wikidata по TMDB ID
- Английские значения остаются fallback, когда русская подпись отсутствует
- Добавлен отдельный загрузчик постеров и логотипов TMDB с меткой `ru`
- Добавлены отдельные правила `RU → EN` / `EN → RU` для российских и зарубежных фильмов
- Российские фильмы определяются по стране производства `RU`, затем по исходному языку `ru`
- Убрана обязательная регистрация собственного TMDB API key: используется ключ встроенного TheMovieDB Jellyfin
- Добавлена настройка библиотек одной кнопкой с сохранением остальных провайдеров
- Добавлены русские метаданные и языковой приоритет постеров для коллекций
- Добавлены логотипы коллекций с Fanart.tv и отдельный приоритет `RU → EN` / `EN → RU`
- Плагин переименован в Choose your Meta!
- Фоновые изображения не изменяются
- Добавлены автоматические регрессионные тесты
- Обновлена совместимость до Jellyfin 10.11.11

### v1.2.0 (2026-07-23)

- **Исправлено:** Сериал не удаляется из UI после сканирования — имя извлекается из последнего сегмента пути папки, а не из корня библиотеки
- **Исправлено:** Поддержка форматов `S01.E01`, `S01 E01`, `S01-E01`, `1x01` в именах эпизодов
- **Исправлено:** SPARQL-экранирование специальных символов в названиях (безопасные запросы к Wikidata)
- **Исправлено:** Убран фильтр языка `FILTER(LANG = "en")` — русские сериалы теперь находятся по русскому названию
- **Исправлено:** IMDb ID извлекается из пути к папке в MovieProvider (аналогично SeriesProvider)
- **Исправлено:** IMDb ID передаётся через `GetMetadata` → Jellyfin сохраняет его до вызова `FetchAsync`
- **Исправлено:** API-ключ TMDB экранируется в URL через `Uri.EscapeDataString`
- **Исправлено:** Wikidata-запросы используют `IHttpClientFactory` вместо создания новых `HttpClient` (меньше socket exhaustion)
- **Исправлено:** `OperationCanceledException` не логируется как Error — корректная обработка отмены задач
- **Доработано:** Тайм-ауты Wikidata увеличены с 10 до 30 секунд (все провайдеры)

### v1.1.0

- Добавлена поддержка прокси для TMDB
- Добавлен `ICustomMetadataProvider` для сериалов, фильмов и эпизодов
- Добавлена конфигурация через Dashboard (TMDB API key, прокси, включение названий/описаний)
- Добавлен поиск по имени в Wikidata (резерв, когда IMDb ID недоступен)
- Оптимизация: SPARQL-запросы через прямые вызовы Wikidata API

### v1.0.0

- Первый релиз: извлечение IMDb ID, TMDB + Wikidata, русские названия и описания

## Лицензия

[MIT](LICENSE) © Tarasov Radomir

---

<p align="center">
  <sub>Сделано для сообщества Jellyfin · Не аффилирован с Jellyfin или TMDB</sub>
</p>
