# Russian Metadata for Jellyfin

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11+-00A4DC?style=flat-square&logo=jellyfin&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)

Плагин для Jellyfin, который автоматически заменяет английские названия и описания фильмов и сериалов на русские. Использует **TMDB** (основной источник) и **Wikidata** (резервный).

---

## Возможности

- **🇷🇺 Русские названия и описания** — фильмы и сериалы отображаются на русском языке
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

1. Скачайте `RussianMetadata.dll` из [Releases](https://github.com/Opiumforme/jellifin-russian-metadata/releases)
2. Скопируйте в директорию плагинов Jellyfin:
   ```
   /path/to/jellyfin/plugins/RussianMetadata/RussianMetadata.dll
   ```
3. Перезапустите Jellyfin
4. Перейдите в **Dashboard → Plugins → Russian Metadata** и настройте
5. Назначьте **Russian Metadata** загрузчиком метаданных для библиотек:
   - **Фильмы**: Dashboard → Libraries → ваша библиотека фильмов → Metadata downloaders → отметьте **Russian Metadata**
   - **ТВ-передачи**: Dashboard → Libraries → ваша библиотека ТВ → Metadata downloaders → отметьте **Russian Metadata**

### Вариант 2: Сборка из исходников

```bash
git clone https://github.com/Opiumforme/jellifin-russian-metadata.git
cd jellifin-russian-metadata/RussianMetadata
dotnet build -c Release
```

Скопируйте `bin/Release/net9.0/RussianMetadata.dll` в папку плагинов.

## Настройка

**Dashboard → Plugins → Russian Metadata → Settings:**

| Параметр | Описание |
|----------|----------|
| **TMDB API Key** | Ваш TMDB API ключ (v3 auth). [Получить](https://www.themoviedb.org/settings/api) |
| **Enable Russian Titles** | Заменять английские названия на русские |
| **Enable Russian Overviews** | Заменять английские описания на русские |
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
  <ProxyUrl>http://proxy.example.com:3128</ProxyUrl>
  <ProxyUsername>логин_прокси</ProxyUsername>
  <ProxyPassword>пароль_прокси</ProxyPassword>
</PluginConfiguration>
```

> Если файла нет — создайте его вручную или откройте **Dashboard → Plugins → Russian Metadata → Settings**, сохраните любые настройки — Jellyfin сам сгенерирует файл. После редактирования XML перезапустите Jellyfin, чтобы изменения применились.

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

git clone https://github.com/Opiumforme/jellifin-russian-metadata.git
cd jellifin-russian-metadata/RussianMetadata

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
├── RussianSeriesProvider.cs      # Провайдер метаданных для сериалов
├── RussianMetadata.csproj        # Файл проекта .NET
├── README.md
└── LICENSE
```

### Ключевые интерфейсы

| Интерфейс | Назначение |
|-----------|------------|
| `IRemoteMetadataProvider<Movie, MovieInfo>` | Удалённый провайдер для фильмов |
| `IRemoteMetadataProvider<Series, SeriesInfo>` | Удалённый провайдер для сериалов |
| `ICustomMetadataProvider<Movie>` | Применяет русские данные после всех провайдеров |
| `ICustomMetadataProvider<Series>` | То же для сериалов |

## Решение проблем

1. **Проверьте логи Jellyfin** — ищите записи с префиксом `RussianMetadata:`
2. **TMDB не отвечает?** — настройте прокси, если TMDB заблокирован в вашем регионе
3. **Русские данные не применяются?** — убедитесь, что у элемента есть IMDb ID (`ProviderIds.Imdb`)
4. **Сборка не удаётся?** — проверьте, что установлен .NET 9.0 SDK

## Лицензия

[MIT](LICENSE) © Tarasov Radomir

---

<p align="center">
  <sub>Сделано для сообщества Jellyfin · Не аффилирован с Jellyfin или TMDB</sub>
</p>
