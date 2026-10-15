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

- [ ] G13: five sampled review quotes appear word for word on their linked pages
  EVIDENCE: pending

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

- [ ] G18: every module was clicked through in the running app per the test plan
  EVIDENCE: pending

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
