# Choose your Meta! for Jellyfin

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11+-00A4DC?style=flat-square&logo=jellyfin&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

Плагин для Jellyfin, который позволяет независимо выбирать язык метаданных и
изображений. Текст локализуется на русский по отдельным полям, а для постеров и
логотипов можно назначить приоритет `RU → EN` или `EN → RU` отдельно российским
и зарубежным фильмам. Использует **TMDB** и **Wikidata**.

---

## Возможности

- **🇷🇺 Русские названия и описания** — фильмы, сериалы и эпизоды отображаются на русском языке
- **Русские данные о фильме** — слоган, жанры, студии, актёры, режиссёры и сценаристы
- **Fallback отдельно для каждого поля** — английский текст используется только при отсутствии русского
- **Разные правила изображений** — российским и зарубежным фильмам назначаются собственные языки постеров и логотипов
- **RU → EN или EN → RU** — второй язык используется, если на TMDB нет изображения предпочитаемого языка
- **Локальные изображения первыми** — обычное сканирование Jellyfin не заменяет найденные рядом с фильмом постеры и логотипы
- **Фоны без изменений** — плагин не вмешивается в загрузку фоновых изображений
- **Два источника данных** — TMDB (богатые метаданные, поддержка прокси), Wikidata (SPARQL) как резерв
- **Гибкая настройка** — включение/отключение замены названий и описаний независимо друг через друга
- **Прокси** — опциональный HTTP-прокси для доступа к TMDB (полезно в регионах, где TMDB заблокирован)
- **Умный каскад** — сначала TMDB (самые полные данные), затем Wikidata
- **Подробное логирование** — диагностические сообщения с префиксом `RussianMetadata:`
- **Извлечение IMDb ID** — автоматически находит IMDb ID из названий файлов и папок

## Требования

| Компонент | Версия |
|-----------|--------|
| Jellyfin  | 10.11.x |
| .NET Runtime | 9.0+ (входит в состав Jellyfin 10.11) |
| TMDB API key | [Бесплатно](https://www.themoviedb.org/settings/api) (необязательно, Wikidata работает без ключа) |

## Установка

### Вариант 1: Вручную

1. Скачайте `RussianMetadata.dll` из [Releases](https://github.com/Lootfullin/choose-your-meta/releases)
2. Скопируйте в директорию плагинов Jellyfin:
   ```
   /path/to/jellyfin/plugins/RussianMetadata/RussianMetadata.dll
   ```
3. Перезапустите Jellyfin
4. Перейдите в **Dashboard → Plugins → Choose your Meta!** и настройте TMDB API key, текст и изображения.
5. **Назначьте Choose your Meta! загрузчиком метаданных для библиотек** — без этого плагин не будет обрабатывать медиа:

   **Для фильмов:** Dashboard → Libraries → ваша библиотека фильмов → ✏️ → **Metadata Downloaders** → выберите `Choose your Meta!` → переместите наверх списка ✅

   **Для ТВ-передач:** Dashboard → Libraries → ваша библиотека ТВ → ✏️ → **Metadata Downloaders** → выберите `Choose your Meta!` → переместите наверх списка ✅

   > **Зачем это нужно?** Jellyfin вызывает провайдеров метаданных для каждой библиотеки только если они включены в её Metadata Downloaders. Без этого шага плагин не будет обрабатывать фильмы и сериалы.
   > Приоритет: переместите `Choose your Meta!` наверх, чтобы он обрабатывался первым.

6. **Для русских постеров и логотипов:** в настройках той же библиотеки откройте
   **Image Fetchers / Загрузчики изображений**, включите
   `Choose your Meta! — изображения` и переместите его выше TMDb.
   Обычный TMDb можно оставить включённым как дополнительный резерв.

7. **Обновите метаданные** после настройки:
   - 📺 TV библиотека: три точки → **Refresh Metadata** → ✅ **Replace all existing metadata** → **Refresh**
   - 🎬 Movies библиотека: три точки → **Refresh Metadata** → ✅ **Replace all existing metadata** и, если нужно сменить постеры/логотипы, ✅ **Replace existing images** → **Refresh**

### Вариант 2: Сборка из исходников

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
| **TMDB API Key** | Ваш TMDB API ключ (v3 auth). [Получить](https://www.themoviedb.org/settings/api) |
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
| **Proxy URL** | URL HTTP-прокси (например `http://proxy.example.com:3128`) |
| **Proxy Username** | Имя пользователя для прокси (опционально) |
| **Proxy Password** | Пароль для прокси (опционально) |

> **Примечание:** TMDB не обязателен. Без API-ключа плагин использует Wikidata, который содержит русские названия и описания для большинства известных фильмов и сериалов.

### Конфигурационный XML-файл

Настройки также можно задать напрямую через XML-файл. Jellyfin автоматически создаёт его после первого запуска плагина по пути:

```
<папка_конфига>/plugins/configurations/RussianMetadata.xml
```

**Пример файла:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <TmdbApiKey>ваш_ключ_TMDB</TmdbApiKey>
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
├── RussianMovieImageProvider.cs  # Русские постеры и логотипы TMDB для фильмов
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

### v1.3.0

- Фильмы теперь локализуются по отдельным полям, а не по принципу «успешен весь источник или нет»
- Добавлены русские слоганы, жанры, студии, актёры, режиссёры и сценаристы
- Русские подписи людей и компаний пакетно запрашиваются из Wikidata по TMDB ID
- Английские значения остаются fallback, когда русская подпись отсутствует
- Добавлен отдельный загрузчик постеров и логотипов TMDB с меткой `ru`
- Добавлены отдельные правила `RU → EN` / `EN → RU` для российских и зарубежных фильмов
- Российские фильмы определяются по стране производства `RU`, затем по исходному языку `ru`
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
