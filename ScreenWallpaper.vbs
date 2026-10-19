' Starts ScreenWallpaper without a console window.
' Needed when Windows Smart App Control blocks the unsigned ScreenWallpaper.exe.
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
args = ""
For Each a In WScript.Arguments
    args = args & " """ & a & """"
Next
CreateObject("WScript.Shell").Run "dotnet """ & dir & "\ScreenWallpaper.dll""" & args, 0, False
