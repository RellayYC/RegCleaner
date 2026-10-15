# Gates: RegKeeper - finish the build and the replica pipeline

OWNS: src/**, tests/**, scripts/**, replica/**, README.md, LICENSE, MODLOG.md, GATES.md, RegKeeper.sln

Scope: every module real and tested, the clone measured against Reg Organizer and its users' complaints, own design and brand, and a releasable build; publishing and business decisions are handed to the user.

- [x] G0: this ledger states outcomes that can fail
  CHECK: node C:/Users/Rellay/.claude/skills/unlazy/scripts/gate-lint.mjs GATES.md
  EXPECT: LINT OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=24d4f94473b10b30e0a3dc574e44b77515e3f14fbc924e08a2ea8aa63580175f; exit=0; EXPECT=matched; output-sha256=ac018aa78f9775c8e810775baf602b8e72395f191639057bb20c708c13f15365; output-bytes=508; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G1: the whole solution builds and every test passes
  CHECK: node scripts/gates/check.mjs tests "" 40
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=0295a0e1c514fc54b85067c076fc5e17b7950766dd878ea9d3cb940bc25a031e; exit=0; EXPECT=matched; output-sha256=fe99798753b9227265087795b693a3350bf5b9bad5ca0c1fe4c41950fcf0767c; output-bytes=22; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G2: the tweak catalog is complete and well-formed
  CHECK: node scripts/gates/check.mjs tweaks 30
  EXPECT: TWEAKS CATALOG OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=f778ca2b4704ecbe3ab248a9c35d537df022bea0001aa07fb0f0d2aee343403d; exit=0; EXPECT=matched; output-sha256=e661e937f6d2dbd3cf2afce9209cec3f89386d09a29ef276855128a84a272449; output-bytes=23; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G3: tweaks read their state, apply and undo on the real registry
  CHECK: node scripts/gates/check.mjs tests TweaksTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=c2c3956916958033f61d9e3c0fdeb3a68b9f9d5cc63409c32ce087a724786171; exit=0; EXPECT=matched; output-sha256=63fc946e9e4fa9a73befb25fa122129faac06323b5665f112d16c81bd4cb3053; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G4: Store apps are listed from the system, frameworks/system packages filtered, removal never passes package names through a script string
  CHECK: node scripts/gates/check.mjs tests StoreAppsTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=0d5aaceb55624ef89e68eabf1aefbe8b6ca0a0991583600d9d2f5bf5e4bd484c; exit=0; EXPECT=matched; output-sha256=63fc946e9e4fa9a73befb25fa122129faac06323b5665f112d16c81bd4cb3053; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G5: the watcher detects a new autorun entry and ignores known ones
  CHECK: node scripts/gates/check.mjs tests WatcherTests 1
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=7a6cf6603af3f3e4d0532fa01e6ad7684db0561aca0a6f73f8d1e0d43983eb49; exit=0; EXPECT=matched; output-sha256=0350987bddcde7b3c57525d1719566be86c05536edc4e2f7c91b3defdacdfcf1; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G6: settings register and remove the shell verb and watcher autostart in HKCU
  CHECK: node scripts/gates/check.mjs tests SettingsTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=60a0f738acb5c3d4f7e600bc4bc75dc53353d9231226b2d74de89e531b17a497; exit=0; EXPECT=matched; output-sha256=63fc946e9e4fa9a73befb25fa122129faac06323b5665f112d16c81bd4cb3053; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G7: compaction plans hives, refuses without admin rights and never schedules a replace without a backup
  CHECK: node scripts/gates/check.mjs tests CompactionTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=1654f5e28704b37d206da89ddf60eff27578b506bab1c60c769ea9ab6cda5c35; exit=0; EXPECT=matched; output-sha256=63fc946e9e4fa9a73befb25fa122129faac06323b5665f112d16c81bd4cb3053; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G8: system keys and folders are protected from deletion by every module
  CHECK: node scripts/gates/check.mjs tests ProtectionTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=38042399cb1f9a49f4ced2f730606510ba54829747903ff17c51ddd6147295a3; exit=0; EXPECT=matched; output-sha256=63fc946e9e4fa9a73befb25fa122129faac06323b5665f112d16c81bd4cb3053; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G9: every navigation entry is a real view, no stubs
  CHECK: node scripts/gates/check.mjs views
  EXPECT: ALL VIEWS REAL
  EVIDENCE: automatic-evidence=v1; definition-sha256=4e1f69d60b59c98b06e95567e689601416597f98e061ae737bb7fd10eaa1cb9e; exit=0; EXPECT=matched; output-sha256=e9c1525121b4df07d2c3c91ce2096e1fbe6bb5777f3d15f323e52b07cb6e2d41; output-bytes=20; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G10: every view opens in both themes without an exception
  CHECK: node scripts/gates/check.mjs tests SmokeTests 2
  EXPECT: TESTS GREEN
  EVIDENCE: automatic-evidence=v1; definition-sha256=dff91c820e691a0409e218302d5acb3c7aede5f339a9c8d9868c457e1fcf413c; exit=0; EXPECT=matched; output-sha256=7deced84738e8bf980a29cfb7504e9245095a9f4e1cfaeeb71055ea903265972; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G11: recon map and feature matrix cover every module
  CHECK: node scripts/gates/check.mjs recon
  EXPECT: RECON OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=f5976f4dd7cb400106e23ad29d89f09142ece869e5ae89f473f248ce6ea29ceb; exit=0; EXPECT=matched; output-sha256=ff126a94449edb6f3d83e56b7eec1ad44a3eff8fdf8be322c464272af6878deb; output-bytes=30; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G12: real, linked user reviews are collected and every complaint theme is either in the feature matrix or rejected with a reason
  CHECK: node scripts/gates/check.mjs reviews 15
  EXPECT: REVIEWS OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=9ae476c52cff373b43906f4e011d28165c834c05a08be6c4bf0b8377d5873ae8; exit=0; EXPECT=matched; output-sha256=dc61a0d170391d5efcd74b2b30288ecdd8af84443e5d9e8c38c72ee8c14db318; output-bytes=34; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G13: five sampled review quotes appear word for word on their linked pages
  EVIDENCE: 2026-10-15 re-fetched the linked pages and compared verbatim: otvet.mail.ru/question/174402556 (ivan_russkii_161), softportal ...-2.html (Онаним, Kira, www, Санчос, Валерий, Андрей), softportal ...-4.html (3Dflame, Вано, Дмитрий.), forums.malwarebytes.com/topic/262535 (alexghilovsky - author fixed from "автор темы", AdvancedSetup), snapfiles 102885 (dsbluecoffee443, Nelson-Carvalho); all match.

- [x] G14: parity with the original: all must features, at least 90% of should, every must from feedback done
  CHECK: node scripts/gates/check.mjs parity 0.9
  EXPECT: PARITY OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=e26bf00ca952d558aa4eb34885afe6a417c75d3fadf2c277be44698200bfcc74; exit=0; EXPECT=matched; output-sha256=8f9f23d65b95d46a1b23929db5017b2b6ba2c19dbba9c90640d8a2094d35f575; output-bytes=46; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G15: both themes pass WCAG contrast
  CHECK: node scripts/gates/check.mjs contrast
  EXPECT: CONTRAST OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=30168acd8efe236340e2bf9008c60482244443ac271e128be2c7d55401f3e8eb; exit=0; EXPECT=matched; output-sha256=6a9d583a6fb136625219c7c6562cd8301f684d54666b98dd9eca027a60b04fe4; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G16: views take colours only from the theme
  CHECK: node scripts/gates/check.mjs colors
  EXPECT: NO HARDCODED COLORS
  EVIDENCE: automatic-evidence=v1; definition-sha256=1feccd1bb8296d2fbe30052a204318898a9e7835277d12f661aebeceb4ab9ae5; exit=0; EXPECT=matched; output-sha256=0b06ce1032823fa8fac2d06a724b7066064a4515b3eb06187e15592fa59f833b; output-bytes=20; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G17: test plan covers every module and no S1/S2 bug is open
  CHECK: node scripts/gates/check.mjs qa
  EXPECT: QA OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=c85d7d01216bb13b171f0b82315a54d135c3b0c2d59c9cd58e94f6cf677e62bb; exit=0; EXPECT=matched; output-sha256=80e91a06e6e55ae30d59a2c8c13f15fb16045b9307b7f1383bd25e59f1d071c4; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G18: every module was clicked through in the running app per the test plan
  EVIDENCE: 2026-10-15 UI Automation passes over the Debug build (artifacts/g18/ui.ps1 + C:\tmp\g18d..g18q.ps1, screenshots in artifacts/g18), run with __COMPAT_LAYER=RunAsInvoker (no UAC, so HKLM writes were not exercised through the UI), test data only, every change reverted through Центр отмены. All 12 views open. Done and PASS: Деинсталлятор - search and empty search, program without uninstaller offers forced removal (answered No), «Убрать запись» + undo, «Ключ в редакторе»; Приложения Windows - «Показать системные» 58 -> 143, removal of a system package refused with a message; Автозагрузка - «Службы»/«Задачи» 30 -> 15/26, «Добавить» to HKCU Run, «файл не найден», «Вкл / выкл», delete, RunOnce delete + undo, «Перейти к записи», three records undone at once; Очистка диска - «Анализ» (153 rules, 361,5 МБ), «Подробно»; Чистка реестра - scan (254-256 findings), ignore -> stays hidden on rescan -> returns when removed from the list, «Ключ в редакторе»; Редактор - create/rename/copy/delete key + undo, copy into itself refused, all six value types, invalid DWORD re-asks, edit/rename/delete value, no-access key message, «Из буфера», favorite survives restart and toggles off; Поиск - invalid regex message, empty result, «Стоп» mid-HKLM keeps hits, delete checked + undo, open in editor; Снимки - full snapshot 19 s, two identical snapshots -> «изменений 0», delete, Esc mid-snapshot leaves no file; Твики - apply as one undo record, Explorer restart question (No), undo, «Сбросить отметки», apply with nothing changed only informs, «Ключ в редакторе»; Центр отмены - list, broken info.json skipped, multi-undo newest first, «Удалить запись», «Удалить старше 30 дней»; Настройки - theme repaints at once, shell verb toggles. Found and fixed: B7-B14 (bugs.md). Skipped on purpose (destructive to this real system, admin-only or OS file dialogs; covered by [A] tests where noted): real uninstall + leftovers window, «Принудительно» with process close, Store package removal error path, disk «Очистить» and its follow-ups (unchecked file kept [A Analyze_Clean_...], locked file, re-analysis), missing winapp2 hint, «Исправить отмеченное» on real findings [A BrokenOpenWith_FoundFixedAndUndone], full HKCU export and .reg import preview (file dialogs) [A RegFile_RoundTrip_IsExact], install trace [A TakeDiffRollback], compaction «Анализ» (admin) [A Plan_FindsMainHives], «Открыть .reg»/«Папка»/«Папка файла»/«Экспорт» (Explorer and save dialogs), shell verb opening the uninstaller from Explorer [A ShellVerb_RegistersAndRemoves].

- [x] G19: nothing of the original's name ships
  CHECK: node scripts/gates/check.mjs sweep
  EXPECT: SWEEP CLEAN
  EVIDENCE: automatic-evidence=v1; definition-sha256=2d2dbf917fe686087fd77d3e5e806d7f312c1f76a868ca0ae86d18ecf3a39209; exit=0; EXPECT=matched; output-sha256=b547debec249fe4c3162a8c8144bec6cab1e796ad20e1678749cf0daf779a08f; output-bytes=21; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G20: own multi-size icon is in the exe and the window
  CHECK: node scripts/gates/check.mjs icon
  EXPECT: ICON OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=e7c7ebfff021e45ebf31afaee3aaadacea8585fca059a14336b5ec540f0f661c; exit=0; EXPECT=matched; output-sha256=4218e5b7785524f5f1a9ba4b24d7aff3c501ea8e530a25931fe4aabcdb6324da; output-bytes=16; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [ ] G21: README covers features, install, safety and license
  CHECK: node scripts/gates/check.mjs readme
  EXPECT: README OK
  EVIDENCE: pending

- [x] G22: self-contained single-file release exes build
  CHECK: node scripts/gates/check.mjs publish
  EXPECT: PUBLISH OK
  EVIDENCE: automatic-evidence=v1; definition-sha256=0a3f9af5e990e8874d096ef33ba3551873c6c8f5b42c4e6f864c419280bd78c0; exit=0; EXPECT=matched; output-sha256=0ced6a1d64f8e44f95d0397e919d6603e93264f848537c4af44e92d476a6cf9a; output-bytes=11; shell=C:\WINDOWS\system32\cmd.exe; cwd=W:\Cleaner; path=0e1dbb07d3aa/35 entries

- [x] G23: distribution model, license and price are chosen by the user
  EVIDENCE: 2026-10-15 user: "Mit ... пока не публикуме" - MIT, free, open source; LICENSE added, README says so. Publishing deferred by the user (G24).

- [ ] G24: the release is published (GitHub Release / winget) after the user's go-ahead
  EVIDENCE: pending

- [ ] G25: the interface is translated to English: every Russian literal goes through L.T and has a translation
  CHECK: node scripts/gates/check.mjs i18n
  EXPECT: I18N OK
  EVIDENCE: pending
