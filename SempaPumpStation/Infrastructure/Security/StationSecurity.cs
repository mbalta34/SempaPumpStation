using System;
using System.Collections.Generic;
using System.IO;
using TerakkiFramework.Security;

namespace SempaPumpStation.Infrastructure.Security
{
    /// <summary>
    /// The station's access levels and what each may do, for the framework's login system
    /// (<see cref="UserSecurity"/>). Without a login the HMI is view-only, except for stopping a pump,
    /// which is never locked.
    /// </summary>
    public static class StationSecurity
    {
        public const int Operator = 1;
        public const int Engineer = 2;
        public const int Admin = 3;

        /// <summary>Zone operation: source / instrument selection, speed, tests, samples, fault reset.</summary>
        public const string Operate = "Station.Operate";
        /// <summary>Which library device is mounted at each instrument position.</summary>
        public const string MountInstruments = "Instruments.Mount";
        /// <summary>Filling in the report data and choosing recipes for the report.</summary>
        public const string ReportData = "Report.Data";
        public const string AlarmReset = "Alarms.Reset";
        /// <summary>Calibration data of the instrument library.</summary>
        public const string EditLibrary = "Instruments.Library";
        /// <summary>Creating, saving and deleting recipes.</summary>
        public const string ManageRecipes = "Recipes.Manage";
        public const string ExitApplication = "App.Exit";
        /// <summary>Forcing digital outputs on or off (bypasses the program's interlocks).</summary>
        public const string ForceOutputs = "Io.Force";

        public static readonly PermissionTable Table = new PermissionTable(
            new Dictionary<int, string>
            {
                { Operator, "Operatör" },
                { Engineer, "Mühendis" },
                { Admin, "Yönetici" },
            },
            new Dictionary<string, int>
            {
                { Operate, Operator },
                { MountInstruments, Operator },
                { ReportData, Operator },
                { AlarmReset, Operator },
                { EditLibrary, Engineer },
                { ManageRecipes, Engineer },
                { ExitApplication, Engineer },
                { ForceOutputs, Engineer },
                { SecurityPermissions.ManageUsers, Admin },
            });

        /// <summary>Where users.xml and user_log.csv live; the smoke test points it at a temp folder.</summary>
        public static string DataFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SempaPumpStation");

        public static SecurityTexts Turkish() => new SecurityTexts
        {
            NotLoggedIn = "Giriş yapılmadı",
            BadCredentials = "Kullanıcı adı veya şifre hatalı.",
            AccountDisabled = "Bu hesap pasif. Yöneticiye başvurun.",
            AccountLockedFormat = "Hesap çok fazla hatalı denemeden dolayı kilitli. {0} dk sonra tekrar deneyin veya yöneticiye başvurun.",
            UserFileUnreadableFormat = "Kullanıcı dosyası okunamadı ({0}): {1}",
            EnterUsername = "Kullanıcı adını girin.",
            LoginFailedFormat = "Giriş yapılamadı: {0}",

            LoginFirst = "Önce giriş yapın.",
            CurrentPasswordWrong = "Mevcut şifre hatalı.",
            PasswordTooShortFormat = "Şifre en az {0} karakter olmalı.",
            PasswordUnchanged = "Yeni şifre mevcut şifreyle aynı olamaz.",
            PasswordsDoNotMatch = "Yeni şifre ile tekrarı aynı değil.",
            PasswordChangeFailedFormat = "Şifre değiştirilemedi: {0}",
            ChangePasswordHeading = "ŞİFRE DEĞİŞTİR",
            ForcedChangePasswordHeading = "ŞİFRENİZİ DEĞİŞTİRİN",
            ForcedChangePasswordMessage = "Devam etmek için kendi şifrenizi belirleyin (yeni hesap, sıfırlanmış veya süresi dolmuş şifre). ",
            PasswordRuleFormat = "Şifre en az {0} karakter olmalı.",
            Cancel = "Vazgeç",
            LogOut = "Çıkış Yap",

            AdminRequired = "Bu işlem için yönetici girişi gerekli.",
            InvalidUsername = "Kullanıcı adı 2–32 karakter olmalı; harf, rakam, nokta, tire ve alt çizgi kullanılabilir.",
            UsernameTaken = "Bu kullanıcı adı zaten var.",
            LastAdminDemote = "Son aktif yöneticinin seviyesi düşürülemez.",
            LastAdminDisable = "Son aktif yönetici pasif yapılamaz.",
            LastAdminDelete = "Son aktif yönetici silinemez.",
            DisableSelf = "Kendi hesabınızı pasif yapamazsınız.",
            DeleteSelf = "Kendi hesabınızı silemezsiniz.",
            Saved = "Kaydedildi.",
            UserAddedFormat = "Kullanıcı eklendi: {0}. İlk girişte kendi şifresini belirleyecek.",
            PasswordResetFormat = "{0} için geçici şifre ayarlandı; ilk girişte değiştirilecek.",
            UserDeletedFormat = "Kullanıcı silindi: {0}",
            ConfirmDeleteFormat = "{0} kullanıcısı silinsin mi?",
            ConfirmDeleteTitle = "Kullanıcı Sil",
            Yes = "Evet",
            No = "Hayır",
            Enable = "Aktif Yap",
            Disable = "Pasif Yap",

            LoginHeading = "KULLANICI GİRİŞİ",
            LoginHint = "Bölge kumandası ve ayarlar için giriş yapın.",
            UsernameLabel = "Kullanıcı adı",
            PasswordLabel = "Şifre",
            LoginButton = "Giriş",
            CurrentPasswordLabel = "Mevcut şifre",
            NewPasswordLabel = "Yeni şifre",
            ConfirmPasswordLabel = "Yeni şifre (tekrar)",
            SaveButton = "Kaydet",
            UsersHeading = "KULLANICILAR",
            DisplayNameLabel = "Ad Soyad",
            LevelLabel = "Seviye",
            StatusLabel = "Durum",
            LastLoginLabel = "Son giriş",
            PasswordChangedLabel = "Şifre",
            SelectedUserHeading = "SEÇİLİ KULLANICI",
            TemporaryPasswordLabel = "Geçici şifre (kullanıcı ilk girişte değiştirir)",
            ResetPasswordButton = "Şifre Sıfırla",
            UnlockButton = "Kilidi Aç",
            DeleteButton = "Sil",
            NewUserHeading = "YENİ KULLANICI",
            AddUserButton = "Kullanıcı Ekle",

            StatusActive = "Aktif",
            StatusDisabled = "Pasif",
            StatusLocked = "Kilitli",
            StatusMustChangePassword = "Şifre değişmeli",

            LogLoginFormat = "Giriş ({0})",
            LogLogout = "Çıkış",
            LogLogoutUserSwitch = "Çıkış: kullanıcı değişimi",
            LogLogoutPasswordNotChanged = "Çıkış: şifre değiştirilmedi",
            LogAutoLogoutFormat = "Otomatik çıkış: {0} dk işlem yapılmadı",
            LogUnknownUser = "Hatalı giriş: bilinmeyen kullanıcı",
            LogWrongPassword = "Hatalı giriş: yanlış şifre",
            LogDisabledAccount = "Hatalı giriş: hesap pasif",
            LogLockedFormat = "Hesap kilitlendi: {0} hatalı şifre",
            LogPasswordChanged = "Şifre değiştirildi",
            LogUserAddedFormat = "Kullanıcı eklendi: {0} ({1})",
            LogPasswordResetFormat = "Şifre sıfırlandı: {0}",
            LogLevelChangedFormat = "Seviye değişti: {0} {1} → {2}",
            LogEnabledFormat = "Hesap aktif yapıldı: {0}",
            LogDisabledFormat = "Hesap pasif yapıldı: {0}",
            LogUnlockedFormat = "Kilit açıldı: {0}",
            LogUserDeletedFormat = "Kullanıcı silindi: {0}",
        };
    }
}
