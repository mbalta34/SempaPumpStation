param([string]$Source, [string]$Target)
# Builds the HMI's report template from Sempa_Rapor_Sablon_2017.xlsm:
#  - four columns after O (pump efficiency), the torque meter: torque (Nm), speed (d/dk), the shaft power from them
#    (kW = T [Nm] x n [rpm] / 9549, i.e. P [W] = T x n / 9.549) and the pump efficiency on that shaft power (%)
#  - header blocks that end at column O are widened to the new last column S
#  - Hm (column I) is Hb - He, the suction pressure entered as measured
#  - the example test data is cleared; formulas, charts and the hidden calculation columns stay
$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false
$xl.AutomationSecurity = 3   # never run the template's macros
try {
    $wb = $xl.Workbooks.Open($Source, 0, $true)
    $ws = $wb.Worksheets.Item("Sablon")

    # ---- four new columns P:S, formatted like O
    $ws.Range("P:S").EntireColumn.Insert(-4161, 0) | Out-Null    # xlShiftToRight, xlFormatFromLeftOrAbove
    foreach ($c in "P", "Q", "R", "S") { $col = $ws.Columns.Item($c); $col.Hidden = $false; $col.ColumnWidth = 7.3 }

    # ---- header rows (1-28) and footer (52-53): blocks ending at O now end at S
    $areas = @{}
    foreach ($r in (1..28 + 52..53)) {
        $cell = $ws.Cells.Item($r, 15)
        $area = $cell.MergeArea
        $areas[$area.Address(0,0)] = @($area.Row, $area.Column, ($area.Row + $area.Rows.Count - 1))
    }
    foreach ($a in $areas.Values) {
        $rng = $ws.Range($ws.Cells.Item($a[0], $a[1]), $ws.Cells.Item($a[2], 15))
        $value = $rng.Cells.Item(1, 1).Formula
        $rng.UnMerge()
        $wide = $ws.Range($ws.Cells.Item($a[0], $a[1]), $ws.Cells.Item($a[2], 19))
        $wide.Merge()
        $wide.Cells.Item(1, 1).Formula = $value
    }

    # ---- table header of the new columns, styled like the power columns
    $ws.Range("M30:N51").Copy() | Out-Null
    $ws.Range("P30:Q51").PasteSpecial(-4122) | Out-Null          # xlPasteFormats
    $ws.Range("R30:S51").PasteSpecial(-4122) | Out-Null
    $h = $ws.Range("P29:S29")
    $h.Merge()
    $src = $ws.Range("J29")
    $h.Font.Name = $src.Font.Name; $h.Font.Size = $src.Font.Size; $h.Font.Bold = $src.Font.Bold
    $h.Interior.Color = $src.Interior.Color
    $h.HorizontalAlignment = -4108; $h.VerticalAlignment = -4108; $h.WrapText = $true
    foreach ($b in 7, 8, 9, 10) { $h.Borders.Item($b).LineStyle = 1; $h.Borders.Item($b).Weight = 2 }
    $ws.Range("P29").Value2 = "TORK ÖLÇER"
    $ws.Range("P30").Value2 = "Tork"
    $ws.Range("Q30").Value2 = "Devir"
    $ws.Range("P31").Value2 = "Nm"
    $ws.Range("Q31").Value2 = "d/dk"
    $ws.Range("R30").Value2 = "Nmil"
    $ws.Range("S30").Value2 = "Verim"
    $ws.Range("R31").Value2 = "KW"
    $ws.Range("S31").Value2 = "%"
    $ws.Range("P32:P51").NumberFormat = $ws.Range("N32").NumberFormat   # 0.0 (a literal "0.0" is read with the Turkish separators)
    $ws.Range("Q32:Q51").NumberFormat = "0"
    $ws.Range("R32:R51").NumberFormat = $ws.Range("N32").NumberFormat
    $ws.Range("S32:S51").NumberFormat = $ws.Range("O32").NumberFormat

    # ---- shaft power from the torque meter, P [W] = T [Nm] x n [rpm] / 9.549, in kW like Ns / Np; and the pump
    #      efficiency on it, hydraulic power (as column O: Q [l/s] x Hm / 102) over the shaft power
    $ws.Range("R32:R51").Formula = '=IFERROR(P32*Q32/9549,0)'
    $ws.Range("S32:S51").Formula = '=IFERROR(((C32*I32/102)/R32)*100,0)'

    # ---- Hm = Hb - He: F holds the suction transmitter's value as measured (bar, negative under vacuum), so it is
    #      subtracted (the 2017 template added an inverted F): G x 10.2 - F x 10.2 + velocity head (H) + height (AG)
    $ws.Range("I32:I51").Formula = '=IFERROR((G32*AN32)-(F32*AN32)+H32+AG32,0)'

    # ---- clear the example test (the HMI fills these)
    foreach ($a in "D32:D51", "F32:G51", "J32:L51", "P32:Q51", "A53",
                   "D3", "J3", "D5", "F5", "D6", "D8:G8", "D9", "D10", "D11", "D12", "D13", "D14", "D15", "F11",
                   "M5", "M6", "M7", "M8", "M13", "M14", "M15", "M16",
                   "D18", "D19", "D20", "D21", "D22", "L18", "L19", "L20", "L21", "L22", "N1", "AU1") {
        foreach ($cell in $ws.Range($a).Cells) { $cell.MergeArea.ClearContents() | Out-Null }
    }

    # ---- page: the whole report one page wide
    $ps = $ws.PageSetup
    $ps.PrintArea = '$A$1:$AQ$127'
    $ps.Zoom = $false
    $ps.FitToPagesWide = 1
    $ps.FitToPagesTall = $false

    $ws.Activate(); $ws.Range("A1").Select() | Out-Null
    $wb.SaveAs($Target, 51)   # xlOpenXMLWorkbook (.xlsx, no macros)
    $wb.Close($false)
    "saved $Target"
} finally { $xl.Quit(); [System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null }




