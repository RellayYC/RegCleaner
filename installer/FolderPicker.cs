// The "Browse" button of the installer: the Explorer folder dialog instead of the MSI one.
// Built by build.ps1 with the csc of .NET Framework 4 that every Windows 10/11 has, stored in the MSI Binary table.
// FolderPicker.exe "<result file>" "<current folder>.": on OK writes "<picked>\RegKeeper\" (UTF-16) to the result file.
// The folder goes last with a "." after it: a trailing \ before the closing quote would eat the quote.
using System;
using System.IO;
using System.Runtime.InteropServices;

static class FolderPicker
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--target") { Console.WriteLine(Target(args[1])); return 0; } // self-check hook for build.ps1
        if (args.Length < 2) return 2;
        File.Delete(args[0]); // a leftover from a crashed run must not pose as this choice
        var dlg = (IFileOpenDialog)new FileOpenDialog();
        dlg.SetOptions(0x20 | 0x40 | 0x800); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST
        dlg.SetTitle("Папка для RegKeeper");
        dlg.SetOkButtonLabel("Выбрать");
        IShellItem start;
        // start next to the current choice: its parent, or the nearest folder that exists
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(args[1].Trim('"'))); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(dir) && SHCreateItemFromParsingName(dir, IntPtr.Zero, typeof(IShellItem).GUID, out start) == 0)
            { dlg.SetFolder(start); break; }
        // no owner: a modal dialog disables its owner with a sent message, and the MSI window's thread is
        // blocked waiting for this process, so owning it would hang both
        if (dlg.Show(IntPtr.Zero) != 0) return 1; // cancelled: the folder stays as it was
        IShellItem item; string path; dlg.GetResult(out item);
        item.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
        File.WriteAllText(args[0], Target(path), System.Text.Encoding.Unicode);
        return 0;
    }

    /// <summary>The app always gets a folder of its own: D:\Programs becomes D:\Programs\RegKeeper\.</summary>
    static string Target(string picked)
    {
        var p = picked.TrimEnd('\\');
        return (p.EndsWith("\\RegKeeper", StringComparison.OrdinalIgnoreCase) ? p : p + "\\RegKeeper") + "\\";
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialog { }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
}
