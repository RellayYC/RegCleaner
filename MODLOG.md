# MODLOG - наблюдения за Reg Organizer 9.94 (эталон поведения)

Только наблюдение: файлы, ключи, форматы. Код и ресурсы оригинала в репо не попадают.

## Бинарники (`D:\Programs\Reg Organizer\Reg Organizer\`)
- `RegOrganizer.exe` x64 native, UI на Sciter (`sciter.dll`, HTML/CSS-движок).
- `WinRTApps.dll` - Store-приложения, `StartupCheckingService.exe` - слежка за автозапуском,
  `Shell\ShellContextMenu_4.dll` (+ MSIX для Win11-меню), `CloseApplication.dll` - закрытие процессов перед удалением.
- `winapp2.ini` - community-правила очистки (берём upstream: github.com/MoscaDotTo/Winapp2).

## Где хранит состояние
- `HKCU\Software\ChemTable Software\Reg Organizer\{SettingsEx,ExploredKeysEx,Translation,...}`
- `%APPDATA%\ChemTable Software\Reg Organizer\`: `CleanupIgnore.xml`, `IgnoreRegCleaner.xml`, `Tweaks.xml`, `StartupItems.bin`
- `%LOCALAPPDATA%\ChemTable Software\Reg Organizer\`: `UndoChangesCenter\*.ucc` (по файлу на операцию + `data.uccdata` индекс),
  `DeletedApps\Apps.txt`, `AppsCache.bin`, `TweakStates.xml`, `CleanupParts.xml` (галочки пунктов очистки).

## Форматы (смысл, не содержимое)
- Ignore рег-чистильщика: `Item{Type(100=по тексту элемента,200=по ключу), KeyNameSubString, ValueNameSubString, ItemTextSubString}` - подстрочное совпадение.
- Ignore деинсталлятора: `Item{ItemType(1=ключ,3=папка), ItemText, ItemScope}` - защищает хвосты антивирусов (Dr.Web и т.п.) от зачистки.
  -> у нас: `Data\protected.txt`, подстроки путей, которые хвостовой поиск никогда не предлагает.
- Твики: ключ, имя значения, тип, список фиксированных состояний {значение, default}, флаги NeedLogOff/OS-маска.
- Отключение автозапуска: в Windows есть родной `Explorer\StartupApproved\{Run,Run32,StartupFolder}` (байт 0: 02=вкл, 03=выкл).
  Используем его, а не свой ключ - совместимо с Диспетчером задач.

## Registry cleanup
- Checks: Uninstall (uninstaller and InstallLocation both gone, msiexec skipped), App Paths, SharedDLLs, MUICache, Run, Installer\Folders, Applications\*\shell\open, firewall rules (GUID-named only; non-GUID are Windows built-ins), CLSID In/LocalServer32 (HKLM, WOW, HKCU), TypeLib.
- COM/TypeLib under %WINDIR% skipped: Windows ships dangling WOW64 registrations (SysWOW64\Speech_OneCore, Midi2.*). WOW CLSID counts as broken only if both x86 and x64 expansions are missing (ink\tipskin.dll).
- TypeLib paths carry a resource index suffix (`foo.dll\2`); WOW6432Node\Classes\TypeLib is the same key as the 64-bit one.
- Real system: ~250 items in ~1.1 s, mostly MUICache and Edge/EdgeUpdate leftovers.
