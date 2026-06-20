# Процедурный генератор баннеров

Клиентский генератор стилизованных баннеров (стиль превью Tarkov-стримов) — Blazor WebAssembly +
[SkiaSharp](https://github.com/mono/SkiaSharp). Весь рендер идёт **в браузере**, бэкенда нет, поэтому
хостится статикой на GitHub Pages.

Резервная версия рендер-ядра из бота [TwitchTrackerBot](https://github.com/TheVSAKeeper) — без правки
стиля промптом (LLM), только форма ввода: заголовок, подзаголовок, третья строка и seed. Рендер
детерминирован по seed: один и тот же seed даёт одинаковую картинку.

## Локально

```bash
dotnet run -c Release
```

Откроется dev-сервер; страница рисует баннер сразу после загрузки шрифта.

## Деплой

`.github/workflows/deploy.yml` на push в `main`: `dotnet publish` → правка `<base href>` под подпуть
проекта → публикация артефакта на GitHub Pages. В настройках репозитория **Settings → Pages → Source**
должно стоять **GitHub Actions**.

## Структура

- `Rendering/` — рендер-ядро (чистый SkiaSharp, детерминировано по seed, без файлового IO):
  `BannerRenderer`, `Grunge` (процедурная фактура), `BannerStyle` (декларативный стиль), `StyleColor`,
  `BannerSpec`.
- `Pages/Home.razor` — форма + вызов рендера, PNG отдаётся как `data:`-URL.
- `wwwroot/Bender-Black.otf` — шрифт заголовка.
