# Gates: RegKeeper - finish the build and the replica pipeline

OWNS: src/**, tests/**, scripts/**, replica/**, README.md, LICENSE, MODLOG.md, GATES.md, RegKeeper.sln

Scope: every module real and tested, the clone measured against Reg Organizer and its users' complaints, own design and brand, and a releasable build; publishing and business decisions are handed to the user.

- [ ] G0: this ledger states outcomes that can fail
  CHECK: node C:/Users/Rellay/.claude/skills/unlazy/scripts/gate-lint.mjs GATES.md
  EXPECT: LINT OK
  EVIDENCE: pending

- [ ] G1: the whole solution builds and every test passes
  CHECK: node scripts/gates/check.mjs tests "" 40
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G2: the tweak catalog is complete and well-formed
  CHECK: node scripts/gates/check.mjs tweaks 30
  EXPECT: TWEAKS CATALOG OK
  EVIDENCE: pending

- [ ] G3: tweaks read their state, apply and undo on the real registry
  CHECK: node scripts/gates/check.mjs tests TweaksTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G4: Store apps are listed from the system, frameworks/system packages filtered, removal never passes package names through a script string
  CHECK: node scripts/gates/check.mjs tests StoreAppsTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G5: the watcher detects a new autorun entry and ignores known ones
  CHECK: node scripts/gates/check.mjs tests WatcherTests 1
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G6: settings register and remove the shell verb and watcher autostart in HKCU
  CHECK: node scripts/gates/check.mjs tests SettingsTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G7: compaction plans hives, refuses without admin rights and never schedules a replace without a backup
  CHECK: node scripts/gates/check.mjs tests CompactionTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G8: system keys and folders are protected from deletion by every module
  CHECK: node scripts/gates/check.mjs tests ProtectionTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G9: every navigation entry is a real view, no stubs
  CHECK: node scripts/gates/check.mjs views
  EXPECT: ALL VIEWS REAL
  EVIDENCE: pending

- [ ] G10: every view opens in both themes without an exception
  CHECK: node scripts/gates/check.mjs tests SmokeTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: pending

- [ ] G11: recon map and feature matrix cover every module
  CHECK: node scripts/gates/check.mjs recon
  EXPECT: RECON OK
  EVIDENCE: pending

- [ ] G12: real, linked user reviews are collected and every complaint theme is either in the feature matrix or rejected with a reason
  CHECK: node scripts/gates/check.mjs reviews 15
  EXPECT: REVIEWS OK
  EVIDENCE: pending

- [x] G13: five sampled review quotes appear word for word on their linked pages
  EVIDENCE: 2026-10-15 re-fetched the linked pages and compared verbatim: otvet.mail.ru/question/174402556 (ivan_russkii_161), softportal ...-2.html (Онаним, Kira, www, Санчос, Валерий, Андрей), softportal ...-4.html (3Dflame, Вано, Дмитрий.), forums.malwarebytes.com/topic/262535 (alexghilovsky - author fixed from "автор темы", AdvancedSetup), snapfiles 102885 (dsbluecoffee443, Nelson-Carvalho); all match.

- [ ] G14: parity with the original: all must features, at least 90% of should, every must from feedback done
  CHECK: node scripts/gates/check.mjs parity 0.9
  EXPECT: PARITY OK
  EVIDENCE: pending

- [ ] G15: both themes pass WCAG contrast
  CHECK: node scripts/gates/check.mjs contrast
  EXPECT: CONTRAST OK
  EVIDENCE: pending

- [ ] G16: views take colours only from the theme
  CHECK: node scripts/gates/check.mjs colors
  EXPECT: NO HARDCODED COLORS
  EVIDENCE: pending

- [ ] G17: test plan covers every module and no S1/S2 bug is open
  CHECK: node scripts/gates/check.mjs qa
  EXPECT: QA OK
  EVIDENCE: pending

- [x] G18: every module was clicked through in the running app per the test plan
  EVIDENCE: 2026-10-15 UI Automation passes over the Debug build (artifacts/g18/ui.ps1 + C:\tmp\g18d..g18q.ps1, screenshots in artifacts/g18), run with __COMPAT_LAYER=RunAsInvoker (no UAC, so HKLM writes were not exercised through the UI), test data only, every change reverted through Центр отмены. All 12 views open. Done and PASS: Деинсталлятор - search and empty search, program without uninstaller offers forced removal (answered No), «Убрать запись» + undo, «Ключ в редакторе»; Приложения Windows - «Показать системные» 58 -> 143, removal of a system package refused with a message; Автозагрузка - «Службы»/«Задачи» 30 -> 15/26, «Добавить» to HKCU Run, «файл не найден», «Вкл / выкл», delete, RunOnce delete + undo, «Перейти к записи», three records undone at once; Очистка диска - «Анализ» (153 rules, 361,5 МБ), «Подробно»; Чистка реестра - scan (254-256 findings), ignore -> stays hidden on rescan -> returns when removed from the list, «Ключ в редакторе»; Редактор - create/rename/copy/delete key + undo, copy into itself refused, all six value types, invalid DWORD re-asks, edit/rename/delete value, no-access key message, «Из буфера», favorite survives restart and toggles off; Поиск - invalid regex message, empty result, «Стоп» mid-HKLM keeps hits, delete checked + undo, open in editor; Снимки - full snapshot 19 s, two identical snapshots -> «изменений 0», delete, Esc mid-snapshot leaves no file; Твики - apply as one undo record, Explorer restart question (No), undo, «Сбросить отметки», apply with nothing changed only informs, «Ключ в редакторе»; Центр отмены - list, broken info.json skipped, multi-undo newest first, «Удалить запись», «Удалить старше 30 дней»; Настройки - theme repaints at once, shell verb toggles. Found and fixed: B7-B14 (bugs.md). Skipped on purpose (destructive to this real system, admin-only or OS file dialogs; covered by [A] tests where noted): real uninstall + leftovers window, «Принудительно» with process close, Store package removal error path, disk «Очистить» and its follow-ups (unchecked file kept [A Analyze_Clean_...], locked file, re-analysis), missing winapp2 hint, «Исправить отмеченное» on real findings [A BrokenOpenWith_FoundFixedAndUndone], full HKCU export and .reg import preview (file dialogs) [A RegFile_RoundTrip_IsExact], install trace [A TakeDiffRollback], compaction «Анализ» (admin) [A Plan_FindsMainHives], «Открыть .reg»/«Папка»/«Папка файла»/«Экспорт» (Explorer and save dialogs), shell verb opening the uninstaller from Explorer [A ShellVerb_RegistersAndRemoves].

- [ ] G19: nothing of the original's name ships
  CHECK: node scripts/gates/check.mjs sweep
  EXPECT: SWEEP CLEAN
  EVIDENCE: pending

- [ ] G20: own multi-size icon is in the exe and the window
  CHECK: node scripts/gates/check.mjs icon
  EXPECT: ICON OK
  EVIDENCE: pending

- [ ] G21: README covers features, install, safety and license
  CHECK: node scripts/gates/check.mjs readme
  EXPECT: README OK
  EVIDENCE: pending

- [ ] G22: self-contained single-file release exes build
  CHECK: node scripts/gates/check.mjs publish
  EXPECT: PUBLISH OK
  EVIDENCE: pending

- [ ] G23: distribution model, license and price are chosen by the user
  EVIDENCE: pending

- [ ] G24: the release is published (GitHub Release / winget) after the user's go-ahead
  EVIDENCE: pending
