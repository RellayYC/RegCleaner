// After FolderPicker.exe: its answer (already ending in \RegKeeper\) becomes the install folder. No file = cancelled.
var fso = new ActiveXObject("Scripting.FileSystemObject");
var f = Session.Property("TempFolder") + "RegKeeper-pick.txt";
if (fso.FileExists(f)) {
    var t = fso.OpenTextFile(f, 1, false, -1), p = t.ReadAll();
    t.Close(); fso.DeleteFile(f);
    try { Session.TargetPath("INSTALLFOLDER") = p; } catch (e) {} // a folder MSI refuses keeps the old one
}
