using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace _8085
{
    // Windows Common Item Dialog, including its supported customization API.
    public sealed class NativeEpromDialog : IDisposable
    {
        internal const uint EjectId = 1001;
        private const int Cancelled = unchecked((int)0x800704C7);
        private readonly IFileDialog dialog;
        private readonly Events events;
        private readonly Timer selectionTimer = new Timer { Interval = 100 };
        private readonly string currentPath;
        private uint cookie;
        private int attempts;
        private bool ejected;
        internal bool CurrentFileSelected { get; private set; }
        internal Action<NativeEpromDialog> ReadyForTest;
        internal NativeEpromDialog(int type, string path)
        {
            currentPath = path;
            dialog = (IFileDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
            dialog.SetOptions(0x40 | 0x800 | 0x10000000); // filesystem, existing directory, no CWD change
            dialog.SetTitle(PkwEprom.Names[type] + ": Open or create an EP-ROM file");
            dialog.SetFileTypes(2,new[] { new Filter("Binary image (*.bin)","*.bin"),new Filter("Intel HEX (*.hex)","*.hex") });
            bool hex = string.Equals(Path.GetExtension(path),".hex",StringComparison.OrdinalIgnoreCase);
            dialog.SetFileTypeIndex(hex ? 2u : 1u); dialog.SetDefaultExtension(hex ? "hex" : "bin");
            if (path != null) {
                var iid = typeof(IShellItem).GUID;
                IShellItem folder;
                SHCreateItemFromParsingName(Path.GetDirectoryName(path),IntPtr.Zero,ref iid,out folder);
                try { dialog.SetFolder(folder); } finally { Marshal.ReleaseComObject(folder); }
                dialog.SetFileName(Path.GetFileName(path));
            }
            var customize = (IFileDialogCustomize)dialog;
            customize.AddPushButton(EjectId,"Eject ROM");
            customize.SetControlState(EjectId,path == null ? 2u : 3u); // visible, enabled only when inserted
            events = new Events(this); dialog.Advise(events,out cookie);
            selectionTimer.Tick += (s,e) => {
                attempts++;
                // Wait for the shell's initial filename focus/navigation to finish.
                // Notify tests on the next message-loop pass so the list is painted.
                if (attempts < 5) return;
                if (CurrentFileSelected) { selectionTimer.Stop(); ReadyForTest?.Invoke(this); return; }
                if (path != null && !CurrentFileSelected) CurrentFileSelected = SelectCurrentFile();
                if (path == null || attempts >= 30) {
                    selectionTimer.Stop(); ReadyForTest?.Invoke(this);
                }
            };
        }
        internal string Show(IWin32Window owner)
        {
            selectionTimer.Start();
            int hr = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
            selectionTimer.Stop();
            if (ejected) return "";
            if (hr == Cancelled) return null;
            Marshal.ThrowExceptionForHR(hr);
            IShellItem result; dialog.GetResult(out result);
            try { return FileSystemPath(result); } finally { Marshal.ReleaseComObject(result); }
        }
        internal void Eject() { if (currentPath != null) { ejected = true; dialog.Close(Cancelled); } }
        internal void Cancel() { dialog.Close(Cancelled); }
        internal IntPtr WindowHandle { get { IntPtr hwnd; ((IOleWindow)dialog).GetWindow(out hwnd); return hwnd; } }
        private static string FileSystemPath(IShellItem item)
        {
            IntPtr name; item.GetDisplayName(0x80058000,out name);
            try { return Marshal.PtrToStringUni(name); } finally { Marshal.FreeCoTaskMem(name); }
        }
        private bool SelectCurrentFile()
        {
            if (!File.Exists(currentPath)) return false;
            object browserObject = null, viewObject = null; IntPtr absolute = IntPtr.Zero;
            try {
                IShellItem folder; dialog.GetFolder(out folder);
                try { if (!string.Equals(FileSystemPath(folder),Path.GetDirectoryName(currentPath),StringComparison.OrdinalIgnoreCase)) return false; }
                finally { Marshal.ReleaseComObject(folder); }
                var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                var iid = typeof(IShellBrowser).GUID;
                ((IServiceProvider)dialog).QueryService(ref service,ref iid,out browserObject);
                ((IShellBrowser)browserObject).QueryActiveShellView(out viewObject);
                var view = (IFolderView)viewObject;
                uint attributes;
                SHParseDisplayName(currentPath,IntPtr.Zero,out absolute,0,out attributes);
                IntPtr child = ILFindLastID(absolute);
                int count; view.ItemCount(2,out count);
                for (int i=0;i<count;i++) {
                    IntPtr item; view.Item(i,out item);
                    bool match;
                    try { match = ILIsEqual(child,item); } finally { Marshal.FreeCoTaskMem(item); }
                    if (match) { view.SelectItem(i,1|4|8|16|64); return true; }
                }
            } catch (COMException) { /* The shell view may still be loading. */ }
            finally {
                if (absolute != IntPtr.Zero) Marshal.FreeCoTaskMem(absolute);
                if (viewObject != null) Marshal.ReleaseComObject(viewObject);
                if (browserObject != null) Marshal.ReleaseComObject(browserObject);
            }
            return false;
        }
        public void Dispose()
        {
            selectionTimer.Dispose();
            if (cookie != 0) dialog.Unadvise(cookie);
            Marshal.ReleaseComObject(dialog);
            GC.KeepAlive(events);
        }
        [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
        private sealed class Events : IFileDialogEvents, IFileDialogControlEvents
        {
            private readonly NativeEpromDialog owner;
            internal Events(NativeEpromDialog owner) { this.owner=owner; }
            public int OnFileOk(IFileDialog d) { return 0; }
            public int OnFolderChanging(IFileDialog d,IShellItem i) { return 0; }
            public int OnFolderChange(IFileDialog d) { return 0; }
            public int OnSelectionChange(IFileDialog d) { return 0; }
            public int OnShareViolation(IFileDialog d,IShellItem i,out uint response) { response=0; return 0; }
            public int OnTypeChange(IFileDialog d) { uint index; d.GetFileTypeIndex(out index); d.SetDefaultExtension(index==2 ? "hex" : "bin"); return 0; }
            public int OnOverwrite(IFileDialog d,IShellItem i,out uint response) { response=0; return 0; }
            public int OnItemSelected(IFileDialogCustomize d,uint id,uint item) { return 0; }
            public int OnButtonClicked(IFileDialogCustomize d,uint id) { if(id==EjectId) owner.Eject(); return 0; }
            public int OnCheckButtonToggled(IFileDialogCustomize d,uint id,bool value) { return 0; }
            public int OnControlActivating(IFileDialogCustomize d,uint id) { return 0; }
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        public struct Filter { [MarshalAs(UnmanagedType.LPWStr)] public string Name; [MarshalAs(UnmanagedType.LPWStr)] public string Pattern; internal Filter(string n,string p){Name=n;Pattern=p;} }
        [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileDialog {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count,[MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)] Filter[] types);
            void SetFileTypeIndex(uint index); void GetFileTypeIndex(out uint index);
            void Advise(IFileDialogEvents events,out uint cookie); void Unadvise(uint cookie);
            void SetOptions(uint options); void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder); void SetFolder(IShellItem folder); void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item); void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name); void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label); void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem result); void AddPlace(IShellItem item,int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension); void Close(int hr);
        }
        [ComImport,Guid("E6FDD21A-163F-4975-9C8C-A69F1BA37034"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileDialogCustomize {
            void EnableOpenDropDown(uint id); void AddMenu(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text);
            void AddPushButton(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text);
            void AddComboBox(uint id); void AddRadioButtonList(uint id); void AddCheckButton(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text,bool value);
            void AddEditBox(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text); void AddSeparator(uint id); void AddText(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text);
            void SetControlLabel(uint id,[MarshalAs(UnmanagedType.LPWStr)]string text); void GetControlState(uint id,out uint state); void SetControlState(uint id,uint state);
        }
        [ComVisible(true),Guid("973510DB-7D7F-452B-8975-74A85828D354"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileDialogEvents {
            [PreserveSig]int OnFileOk(IFileDialog d); [PreserveSig]int OnFolderChanging(IFileDialog d,IShellItem i);
            [PreserveSig]int OnFolderChange(IFileDialog d); [PreserveSig]int OnSelectionChange(IFileDialog d);
            [PreserveSig]int OnShareViolation(IFileDialog d,IShellItem i,out uint response);
            [PreserveSig]int OnTypeChange(IFileDialog d); [PreserveSig]int OnOverwrite(IFileDialog d,IShellItem i,out uint response);
        }
        [ComVisible(true),Guid("36116642-D713-4B97-9B83-7484A9D00433"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFileDialogControlEvents {
            [PreserveSig]int OnItemSelected(IFileDialogCustomize d,uint id,uint item); [PreserveSig]int OnButtonClicked(IFileDialogCustomize d,uint id);
            [PreserveSig]int OnCheckButtonToggled(IFileDialogCustomize d,uint id,bool value); [PreserveSig]int OnControlActivating(IFileDialogCustomize d,uint id);
        }
        [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItem { void BindToHandler(IntPtr p,ref Guid handler,ref Guid iid,out IntPtr result); void GetParent(out IShellItem parent); void GetDisplayName(uint kind,out IntPtr name); }
        [ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IServiceProvider { void QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object result); }
        [ComImport,Guid("00000114-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleWindow { void GetWindow(out IntPtr hwnd); }
        [ComImport,Guid("000214E2-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellBrowser {
            void GetWindow(out IntPtr hwnd); void ContextSensitiveHelp(bool enter); void InsertMenus(); void SetMenu(); void RemoveMenus(); void SetStatusText(); void EnableModeless(); void TranslateAccelerator(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)]out object view);
        }
        [ComImport,Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFolderView {
            void GetCurrentViewMode(); void SetCurrentViewMode(); void GetFolder(); void Item(int index,out IntPtr item); void ItemCount(uint flags,out int count); void Items(); void GetSelectionMarkedItem(); void GetFocusedItem(); void GetItemPosition(); void GetSpacing(); void GetDefaultSpacing(); void GetAutoArrange(); void SelectItem(int index,uint flags);
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr bind,ref Guid iid,out IShellItem item);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHParseDisplayName(string path,IntPtr bind,out IntPtr pidl,uint attributes,out uint resultAttributes);
        [DllImport("shell32.dll")] static extern IntPtr ILFindLastID(IntPtr pidl);
        [DllImport("shell32.dll")][return:MarshalAs(UnmanagedType.Bool)] static extern bool ILIsEqual(IntPtr a,IntPtr b);
    }
}
