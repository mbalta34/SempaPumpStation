using System.Collections.Generic;
using SempaPumpStation.Infrastructure;

namespace SempaPumpStation.Screens
{
    public sealed class FirmaRecipeViewModel : RecipeEditorViewModel
    {
        public FirmaRecipeViewModel(HmiContext ctx) : base(ctx) { }
        public override string Key => "FirmaRecipe";
        public override string Title => "Firma Reçetesi";
        public override string Category => "Firma";
        protected override IReadOnlyList<RecipeFieldDef> Defs => new[]
        {
            new RecipeFieldDef("Firma Adı", "FIRMA_ADI"),
            new RecipeFieldDef("Yetkili Kişi", "YETKILI_KISI"),
            new RecipeFieldDef("Adres", "ADRES"),
            new RecipeFieldDef("Telefon", "TEL_NO"),
            new RecipeFieldDef("Cep No", "CEP_NO"),
            new RecipeFieldDef("Faks No", "FAX_NO"),
            new RecipeFieldDef("Vergi Dairesi", "VERGI_DAIRESI"),
            new RecipeFieldDef("Vergi No", "VERGI_NO"),
        };
        protected override IReadOnlyDictionary<string, string> ReportMap => new Dictionary<string, string>
        {
            { "FIRMA_ADI", "WRT_FIRMA_ADI" }, { "ADRES", "WRT_ADRES" },
        };
    }

    public sealed class PompaRecipeViewModel : RecipeEditorViewModel
    {
        public PompaRecipeViewModel(HmiContext ctx) : base(ctx) { }
        public override string Key => "PompaRecipe";
        public override string Title => "Pompa Reçetesi";
        public override string Category => "Pompa";
        protected override IReadOnlyList<RecipeFieldDef> Defs => new[]
        {
            new RecipeFieldDef("Pompa Tipi", "POMPA_TIPI"),
            new RecipeFieldDef("Seri No", "PMP_SERI_NO"),
            new RecipeFieldDef("Emme Çapı", "PMP_EMME_CAP"),
            new RecipeFieldDef("Basma Çapı", "PMP_BAS_CAP"),
            new RecipeFieldDef("Mil Malzemesi", "PMP_MIL_MALZ"),
            new RecipeFieldDef("Montaj Şekli", "PMP_MONTAJ_TIP"),
            new RecipeFieldDef("Şase Durumu", "PMP_SASE_DRM"),
            new RecipeFieldDef("Çark Malzemesi", "CARK_MALZ"),
            new RecipeFieldDef("Çark Çapı", "CARK_CAPI"),
            new RecipeFieldDef("Salmastra Tipi", "SALMAST_TIPI"),
            new RecipeFieldDef("Gövde/Salyangoz Malz.", "SALYANGOZ_MALZ"),
        };
        protected override IReadOnlyDictionary<string, string> ReportMap => new Dictionary<string, string>
        {
            { "POMPA_TIPI", "WRT_POMPA_TIPI" }, { "PMP_SERI_NO", "WRT_PMP_SERI_NO" },
            { "PMP_EMME_CAP", "WRT_PMP_EMME_CAP" }, { "PMP_BAS_CAP", "WRT_PMP_BAS_CAP" },
            { "PMP_MIL_MALZ", "WRT_PMP_MIL_MALZ" }, { "PMP_MONTAJ_TIP", "WRT_PMP_MONTAJ_TIP" },
            { "CARK_MALZ", "WRT_CARK_MALZ" }, { "CARK_CAPI", "WRT_CARK_CAPI" },
            { "SALMAST_TIPI", "WRT_SALMAST_TIPI" }, { "SALYANGOZ_MALZ", "WRT_SALYANGOZ_MALZ" },
        };
    }

    public sealed class MotorRecipeViewModel : RecipeEditorViewModel
    {
        public MotorRecipeViewModel(HmiContext ctx) : base(ctx) { }
        public override string Key => "MotorRecipe";
        public override string Title => "Motor Reçetesi";
        public override string Category => "Motor";
        protected override IReadOnlyList<RecipeFieldDef> Defs => new[]
        {
            new RecipeFieldDef("Motor Tipi", "MOTOR_TIPI"),
            new RecipeFieldDef("Markası", "MOTOR_MARKA"),
            new RecipeFieldDef("Seri No", "MOTOR_SERI_NO"),
            new RecipeFieldDef("Ölçüsü (Frame)", "MOTOR_FRAME"),
            new RecipeFieldDef("HP", "MOTOR_HP"),
            new RecipeFieldDef("Verim Sınıfı", "MOTOR_VERIM_SIN"),
            new RecipeFieldDef("Gücü (kW)", "MOTOR_GUCU", "0.0"),
            new RecipeFieldDef("Devri (d/dk)", "MOTOR_DEVRI", "0"),
            new RecipeFieldDef("Akımı (A)", "MOTOR_AKIMI", "0.0"),
            new RecipeFieldDef("Cos φ", "MOTOR_COSPHI", "0.00"),
            new RecipeFieldDef("Verim 1/4 Yük (%)", "MOTOR_VERIM_1_4", "0.0"),
            new RecipeFieldDef("Verim 2/4 Yük (%)", "MOTOR_VERIM_2_4", "0.0"),
            new RecipeFieldDef("Verim 3/4 Yük (%)", "MOTOR_VERIM_3_4", "0.0"),
            new RecipeFieldDef("Verim 4/4 Yük (%)", "MOTOR_VERIM_4_4", "0.0"),
        };
        protected override IReadOnlyDictionary<string, string> ReportMap => new Dictionary<string, string>
        {
            { "MOTOR_TIPI", "WRT_MOTOR_TIPI" }, { "MOTOR_MARKA", "WRT_MOTOR_MARKA" },
            { "MOTOR_SERI_NO", "WRT_MOTOR_SERI_NO" }, { "MOTOR_FRAME", "WRT_MOTOR_FRAME" },
            { "MOTOR_HP", "WRT_MOTOR_HP" }, { "MOTOR_VERIM_SIN", "WRT_MOTOR_VERIM_SIN" },
            { "MOTOR_GUCU", "WRT_MOTOR_GUCU" }, { "MOTOR_DEVRI", "WRT_MOTOR_DEVRI" },
            { "MOTOR_AKIMI", "WRT_MOTOR_AKIMI" }, { "MOTOR_COSPHI", "WRT_MOTOR_COSPHI" },
            { "MOTOR_VERIM_1_4", "WRT_MOTOR_VERIM_1_4" }, { "MOTOR_VERIM_2_4", "WRT_MOTOR_VERIM_2_4" },
            { "MOTOR_VERIM_3_4", "WRT_MOTOR_VERIM_3_4" }, { "MOTOR_VERIM_4_4", "WRT_MOTOR_VERIM_4_4" },
        };
    }

    public sealed class HatRecipeViewModel : RecipeEditorViewModel
    {
        public HatRecipeViewModel(HmiContext ctx) : base(ctx) { }
        public override string Key => "HatRecipe";
        public override string Title => "Hat Geometrisi Reçetesi";
        public override string Category => "Hat";
        // The instruments themselves are chosen per position from the PLC library (Enstrümanlar screen)
        protected override IReadOnlyList<RecipeFieldDef> Defs => new[]
        {
            new RecipeFieldDef("Manometre Yükseklik Farkı (m)", "GEO_YUKSEKLIK", "0.00"),
            new RecipeFieldDef("Hat İç Çapı (mm)", "HAT_BAS_IC_CAPI", "0.0"),
            new RecipeFieldDef("Hat Uzunluğu (m)", "HAT_UZUNLUGU", "0.0"),
            new RecipeFieldDef("Basma Dirsek Adedi", "BASMA_DIRSEK_ADET", "0"),
            new RecipeFieldDef("Emme Redüksiyon Adedi", "EMME_RED_ADET", "0"),
            new RecipeFieldDef("Boru Pürüzsüzlük Kats.", "FDUZ_DEGERI", "0.000"),
            new RecipeFieldDef("Dirsek Katsayısı", "FDIRS_DEGERI", "0.00"),
            new RecipeFieldDef("Redüksiyon Katsayısı", "FRED_DEGERI", "0.00"),
        };
        protected override IReadOnlyDictionary<string, string> ReportMap => new Dictionary<string, string>();
    }

    public sealed class TestiYapanRecipeViewModel : RecipeEditorViewModel
    {
        public TestiYapanRecipeViewModel(HmiContext ctx) : base(ctx) { }
        public override string Key => "TestiYapan";
        public override string Title => "Testi Yapan";
        public override string Category => "TestiYapan";
        protected override IReadOnlyList<RecipeFieldDef> Defs => new[]
        {
            new RecipeFieldDef("Testi Yapan Personel", "TESTI_YAPAN"),
        };
        protected override IReadOnlyDictionary<string, string> ReportMap => new Dictionary<string, string>
        {
            { "TESTI_YAPAN", "WRT_TESTI_YAPAN" },
        };
    }
}
