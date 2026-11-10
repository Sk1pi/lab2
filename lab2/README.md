# Лабораторна робота №2. NoSQL

**Варіант 13:** Система управління спортивними подіями
**Бази:** LiteDB (документна), DBreeze (key-value), MS SQL Server
**Мова:** C# (.NET 8)

Обидві NoSQL-бази вбудовані: вони працюють усередині застосунку і зберігають дані у файлах, окремий сервер не потрібен.

Повний опис, аргументація та методика тестування — у [REPORT.md](REPORT.md).

## Структура

```
sql/                      модель у SQL Server (7 таблиць, індекси, запити)
src/SportLive.Benchmark/  генератор даних, сховища LiteDB і SQL Server, тести, демо запитів
results/                  з'являється після запуску
```

## Що потрібно

- .NET 8 SDK
- SQL Server на `localhost,1433` (Windows-автентифікація)

LiteDB і DBreeze підтягуються як NuGet-пакети під час першого запуску.

## Запуск

```
run_benchmark.bat
```

Скрипт проганяє тести на 1 000, 10 000 і 100 000 матчів, потім виконує базові запити LiteDB і DBreeze.

Швидкий прогін:

```
run_benchmark.bat --scales 1000,10000
```

Лише базові запити, без тестів швидкодії:

```
run_benchmark.bat --demo-only
```

Якщо до SQL Server потрібен логін і пароль:

```
run_benchmark.bat --sql "Server=localhost,1433;Database=master;User Id=sa;Password=...;TrustServerCertificate=True;"
```

## Результати

- `results/results.md` — таблиці з медіаною і p95 для кожної операції
- `results/report.html` — графіки
- `results/benchmark_log.txt` — повний вивід запуску
- `results/litedb_queries_output.txt` — базові запити LiteDB
- `results/keyvalue_output.txt` — базові запити DBreeze
