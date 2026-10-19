# Daycord

Daycord — локальное desktop-приложение для персонального планирования задач, учёта рабочего времени и формирования ежемесячных Excel-отчётов.

Приложение разрабатывается для macOS Apple Silicon и Windows x64.

Daycord работает по принципу offline-first: без сервера, обязательной регистрации и облачной синхронизации.

## Технологический стек

| Компонент | Технология |
|---|---|
| Язык | C# |
| Платформа | .NET 10 |
| UI | Avalonia UI 12.1.4 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| DI | Microsoft.Extensions.DependencyInjection |
| База данных | SQLite (планируется) |
| Доступ к данным | Dapper + Microsoft.Data.Sqlite (планируется) |
| Миграции | Версионированные SQL-скрипты (планируются) |
| Excel | ClosedXML (планируется) |
| IDE | JetBrains Rider |

Entity Framework Core не используется.

## Архитектура

Solution состоит из трёх проектов.

### Daycord.Core

Содержит доменные сущности и в дальнейшем прикладную бизнес-логику.

Текущие сущности:

- TaskItem
- PlanningPeriod
- WorkEntry
- TaskStatus

Проект не зависит от UI или инфраструктуры хранения.

### Daycord.Infrastructure

Предназначен для будущей реализации:

- SQLite и Dapper.
- SQL-миграций.
- Локальных настроек.
- Excel-экспорта.
- Резервного копирования и восстановления.

На этапе DC-002 инфраструктура хранения ещё не реализована.

### Daycord.Desktop

Нативное desktop-приложение на Avalonia UI.

Содержит:

- Единственную точку входа.
- Главное окно.
- MVVM.
- Dependency Injection.
- Палитру Graphite & Amber.

## Зависимости проектов

Daycord.Infrastructure -> Daycord.Core

Daycord.Desktop -> Daycord.Core

Daycord.Desktop -> Daycord.Infrastructure

Обратные зависимости запрещены.

## Требования

- macOS ARM64 или Windows x64.
- .NET SDK 10.
- JetBrains Rider либо .NET CLI.
- Доступ к NuGet для первоначального восстановления пакетов.

Текущий global.json использует SDK 10.0.201 с latestPatch.

Это временное отклонение от согласованной ветки SDK 10.0.400. Целевой Framework остаётся net10.0.

## Сборка и запуск

Открыть терминал в корневой директории Daycord.

Проверка SDK:

```bash
dotnet --info
```

Восстановление зависимостей:

```bash
dotnet restore Daycord.slnx
```

Сборка:

```bash
dotnet build Daycord.slnx
```

Запуск приложения:

```bash
dotnet run --project src/Daycord.Desktop/Daycord.Desktop.csproj
```

## Поддерживаемые платформы

- macOS Apple Silicon: osx-arm64.
- Windows x64: win-x64.

Первичная сборка и запуск выполнены на macOS ARM64.

Публикация и полноценная проверка Windows x64 пока не проводились.

## Темы оформления

Daycord использует Graphite & Amber.

Поддерживаются следующие режимы:

- Light.
- Dark.
- System.

Начальный режим — System.

Цветовые ресурсы определены в:

src/Daycord.Desktop/Themes/Palette.axaml

Сохранение пользовательского выбора темы пока не реализовано.

## Текущий этап разработки

DC-002 — создание Solution, базового приложения и доменных сущностей.

Реализовано:

- Solution формата .slnx.
- Три проекта .NET 10.
- Основные доменные сущности.
- Главное окно Avalonia.
- MVVM через CommunityToolkit.
- Dependency Injection.
- Палитра Graphite & Amber.
- Сборка и базовый запуск на macOS ARM64.

Пока не реализованы:

- SQLite и SQL-миграции.
- CRUD задач.
- Календарь.
- Dashboard.
- Терминальный интерфейс.
- Учёт времени через пользовательский интерфейс.
- Excel-отчёты.
- Резервное копирование.
- Экран настроек.

Автоматические unit-тесты и тестовые проекты исключены из MVP решением владельца.

## Документация

Архитектурные решения:

docs/architecture/DC-001.md

Изменения архитектуры выполняются только после согласования с владельцем Daycord.