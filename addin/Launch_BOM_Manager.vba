' =============================================================
' SolidWorks VBA Macro: Launch BOM Manager V0.0
' Description: BOM Manager V0.0 팝업 실행 매크로
' =============================================================

Dim swApp As Object

Sub main()
    Dim exePath As String
    Dim wsh As Object
    
    exePath = "c:\Temp\BOM_Manager\BOM_Manager.exe"
    
    Set wsh = CreateObject("WScript.Shell")
    
    ' BOM_Manager.exe 실행
    wsh.Run """" & exePath & """", 1, False
    
    Set wsh = Nothing
End Sub
