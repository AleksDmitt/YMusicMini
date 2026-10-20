# Как опубликовать YMusic Mini на GitHub

Перед публикацией прочитайте **[INSTALLER_VISUAL_STUDIO.md](INSTALLER_VISUAL_STUDIO.md)**: там описаны сборка приложения в Visual Studio, EXE-установщик Inno Setup, тест удаления и создание GitHub Release.

## До публикации

1. Проверьте `docs/QA_RELEASE.md` на Windows и соберите `Release`.
2. Убедитесь, что есть права на использование и распространение всех PNG и иконок из `Assets/`.
3. Решите вопрос с лицензией исходного кода (`LICENSE`) и лицензиями сторонних ресурсов. При отсутствии `LICENSE` другим обычно не выдаётся явное разрешение на копирование/модификацию кода.
4. Не публикуйте `%AppData%\YMusicMini\settings.json`, `%LocalAppData%\YMusicMini\repeat.log`, `bin`, `obj`, токены или персональные файлы. Проверяйте `git status` перед коммитом.

## Самый простой путь — Visual Studio

1. Откройте `YMusicMini.csproj`.
2. Меню **Git → Создать репозиторий Git**.
3. Выберите GitHub (при необходимости авторизуйтесь), название `YMusicMini`, описание и Public/Private.
4. Проверьте список файлов в Git Changes: там должны быть исходники, README, docs и Assets, но **не** `bin/`, `obj/`, `settings.json` и `repeat.log`.
5. Сделайте коммит, нажмите **Push/Опубликовать**.
6. Откройте вкладку Actions GitHub и проверьте первый Windows-build.

## Через терминал

Сначала создайте пустой репозиторий на GitHub, затем в папке проекта:

```powershell
git init
git add .
git status
git commit -m "Prepare YMusic Mini release candidate"
git branch -M main
git remote add origin https://github.com/ВАШ_ЛОГИН/YMusicMini.git
git push -u origin main
```

Замените `ВАШ_ЛОГИН` своим именем пользователя; URL приведён как **пример**, не готовая ссылка на существующий репозиторий.

## GitHub Releases после тестирования

Создайте тег `v0.6.12-rc2`; после подтверждённой сборки и тестов опубликуйте архив собранной программы в GitHub Releases. Не выдавайте RC за стабильный релиз. В описании укажите, что программа независимая, не связана с Яндексом и использует Windows Media Control. Команда `dotnet publish` показана в корневом README.

## CI

В репозитории есть `.github/workflows/build.yml`: Windows-runner пробует восстановить NuGet и собрать проект в Release при push/PR. Успешная CI-сборка **не** подтверждает работу с Яндекс Музыкой без ручной проверки.
