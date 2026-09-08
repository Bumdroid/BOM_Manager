using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using BOMManager.Core;
using BOMManager.Modules.SpringDesigner.Models;
using BOMManager.Modules.SpringDesigner.Services;
using BOMManager.Modules.SpringDesigner.Views;

namespace BOMManager.Modules.SpringDesigner.ViewModels
{

    public class SpringDesignerViewModel : INotifyPropertyChanged
    {
        private SpringDesignParameters _parameters = new SpringDesignParameters();

        private string _vaultServer = "192.168.150.105";
        private string _vaultName = "ISC_Vault";
        private string _vaultUser = "이두규";
        private string _vaultPassword = "";

        public ImageSource SpringIcon { get; } = SpringCadService.CreateSpringIconBitmap(24);

        public string VaultServer
        {
            get => _vaultServer;
            set { _vaultServer = value; OnPropertyChanged(); }
        }

        public string VaultName
        {
            get => _vaultName;
            set { _vaultName = value; OnPropertyChanged(); }
        }

        public string VaultUser
        {
            get => _vaultUser;
            set { _vaultUser = value; OnPropertyChanged(); }
        }

        public string VaultPassword
        {
            get => _vaultPassword;
            set { _vaultPassword = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> MaterialOptions { get; } = new ObservableCollection<string>();

        public SpringDesignerViewModel()
        {
            MaterialOptions.Clear();
            MaterialOptions.Add("Music wire");
            MaterialOptions.Add("SUS");

            SelectedMaterialString = "Music wire";

            // BOM_Manager Vault 세션 자동 연동
            try
            {
                var vCfg = VaultConfigManager.Load();
                _vaultServer = !string.IsNullOrEmpty(VaultService.CurrentServer) ? VaultService.CurrentServer : vCfg.Server;
                _vaultName = !string.IsNullOrEmpty(VaultService.CurrentVault) ? VaultService.CurrentVault : vCfg.VaultName;
                _vaultUser = !string.IsNullOrEmpty(VaultService.CurrentUsername) ? VaultService.CurrentUsername : (!string.IsNullOrEmpty(vCfg.LastUsername) ? vCfg.LastUsername : "이두규");
                _vaultPassword = vCfg.Password ?? "";
            }
            catch { }

            LoadVaultConfig();
            LoadElastomerSpecConfig();
            LoadPkgGapSpecConfig();
            LoadTemplateOptionConfig();
            LoadSpringSpecConfig();
        }

        private static string GetConfigDirectoryPath()
        {
            return SpringConfigService.GetConfigDirectoryPath();
        }

        private static string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectoryPath(), "vault_config.json");
        }

        public void LoadVaultConfig()
        {
            try
            {
                string appDataDir = GetConfigDirectoryPath();
                if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);

                string configPath = GetConfigFilePath();
                if (!File.Exists(configPath))
                {
                    string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    string bundleConfig = Path.Combine(asmDir, "vault_config.json");
                    if (File.Exists(bundleConfig))
                    {
                        try { File.Copy(bundleConfig, configPath, true); } catch { }
                        configPath = bundleConfig;
                    }
                }

                if (File.Exists(configPath))
                {
                    string json = ReadTextAutoEncoding(configPath);
                    string vs = SpringConfigService.GetJsonString(json, "VaultServer");
                    string vn = SpringConfigService.GetJsonString(json, "VaultName");
                    string vu = SpringConfigService.GetJsonString(json, "VaultUser");
                    string vp = SpringConfigService.GetJsonString(json, "VaultPassword");

                    if (!string.IsNullOrWhiteSpace(vs)) _vaultServer = vs;
                    if (!string.IsNullOrWhiteSpace(vn)) _vaultName = vn;
                    if (!string.IsNullOrWhiteSpace(vu) && !vu.Equals("User", StringComparison.OrdinalIgnoreCase)) _vaultUser = vu;
                    if (!string.IsNullOrWhiteSpace(vp)) _vaultPassword = vp;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(_vaultServer)) _vaultServer = VaultConfigManager.DefaultServer;
            if (string.IsNullOrWhiteSpace(_vaultName)) _vaultName = VaultConfigManager.DefaultVaultName;
            if (string.IsNullOrWhiteSpace(_vaultUser) || _vaultUser.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                _vaultUser = !string.IsNullOrEmpty(VaultService.CurrentUsername) 
                    ? VaultService.CurrentUsername 
                    : (!string.IsNullOrWhiteSpace(VaultConfigManager.Load().LastUsername) ? VaultConfigManager.Load().LastUsername : "이두규");
            }
        }

        public static string ReadTextAutoEncoding(string filePath)
        {
            return SpringConfigService.ReadTextAutoEncoding(filePath);
        }

        public bool SaveVaultConfig(out string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(VaultServer)) VaultServer = VaultConfigManager.DefaultServer;
                if (string.IsNullOrWhiteSpace(VaultName)) VaultName = VaultConfigManager.DefaultVaultName;
                if (VaultPassword == null) VaultPassword = "";

                bool loginOk = VaultService.TestLogIn(VaultServer, VaultName, VaultUser ?? "", VaultPassword ?? "", out string _);
                if (!loginOk)
                {
                    message = "접속실패. 똑바로하세요";
                    return false;
                }

                string dirPath = GetConfigDirectoryPath();
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                string configPath = GetConfigFilePath();
                string targetFolder = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
                string fileName = "Spring_공용화리스트_260820__Text.csv";

                if (File.Exists(configPath))
                {
                    try
                    {
                        string existingJson = ReadTextAutoEncoding(configPath);
                        string tf = SpringConfigService.GetJsonString(existingJson, "TargetFolder");
                        string fn = SpringConfigService.GetJsonString(existingJson, "FileName");
                        if (!string.IsNullOrEmpty(tf)) targetFolder = tf;
                        if (!string.IsNullOrEmpty(fn)) fileName = fn;
                    }
                    catch { }
                }

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"VaultServer\": \"{SpringConfigService.EscapeJson(VaultServer ?? "")}\",");
                sb.AppendLine($"  \"VaultName\": \"{SpringConfigService.EscapeJson(VaultName ?? "")}\",");
                sb.AppendLine($"  \"VaultUser\": \"{SpringConfigService.EscapeJson(VaultUser ?? "")}\",");
                sb.AppendLine($"  \"VaultPassword\": \"{SpringConfigService.EscapeJson(VaultPassword ?? "")}\",");
                sb.AppendLine($"  \"TargetFolder\": \"{SpringConfigService.EscapeJson(targetFolder ?? "")}\",");
                sb.AppendLine($"  \"FileName\": \"{SpringConfigService.EscapeJson(fileName ?? "")}\"");
                sb.AppendLine("}");

                File.WriteAllText(configPath, sb.ToString(), new UTF8Encoding(true));

                // BOM_Manager VaultConfig 동기화
                try
                {
                    VaultConfigManager.SaveLastUsername(VaultUser ?? "", true);
                    VaultService.SetLoggedInUser(VaultUser ?? "", VaultServer ?? VaultConfigManager.DefaultServer, VaultName ?? VaultConfigManager.DefaultVaultName);
                }
                catch { }

                message = $"Vault 접속 성공!\n설정이 저장되었습니다.\n(사용자: {VaultUser})";
                return true;
            }
            catch (Exception)
            {
                message = "접속실패. 똑바로하세요";
                return false;
            }
        }

        public void LoadElastomerSpecConfig()
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                string filePath = Path.Combine(dirPath, "elastomer_spec.json");
                if (File.Exists(filePath))
                {
                    string json = ReadTextAutoEncoding(filePath);
                    double efMin = SpringConfigService.GetJsonDouble(json, "EF_min", -1);
                    double efNor = SpringConfigService.GetJsonDouble(json, "EF_nor", -1);
                    double efMax = SpringConfigService.GetJsonDouble(json, "EF_max", -1);
                    double etMin = SpringConfigService.GetJsonDouble(json, "ET_min", -1);
                    double etNor = SpringConfigService.GetJsonDouble(json, "ET_nor", -1);
                    double etMax = SpringConfigService.GetJsonDouble(json, "ET_max", -1);
                    double ptMin = SpringConfigService.GetJsonDouble(json, "PT_min", -1);
                    double ptNor = SpringConfigService.GetJsonDouble(json, "PT_nor", -1);
                    double ptMax = SpringConfigService.GetJsonDouble(json, "PT_max", -1);

                    if (efMin >= 0) _parameters.EF_min = efMin;
                    if (efNor >= 0) _parameters.EF_nor = efNor;
                    if (efMax >= 0) _parameters.EF_max = efMax;
                    if (etMin >= 0) _parameters.ET_min = etMin;
                    if (etNor >= 0) _parameters.ET_nor = etNor;
                    if (etMax >= 0) _parameters.ET_max = etMax;
                    if (ptMin >= 0) _parameters.PT_min = ptMin;
                    if (ptNor >= 0) _parameters.PT_nor = ptNor;
                    if (ptMax >= 0) _parameters.PT_max = ptMax;
                }
            }
            catch { }
        }

        public bool SaveElastomerSpecConfig(out string message)
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                string filePath = Path.Combine(dirPath, "elastomer_spec.json");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_min\": {0},", _parameters.EF_min));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_nor\": {0},", _parameters.EF_nor));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"EF_max\": {0},", _parameters.EF_max));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_min\": {0},", _parameters.ET_min));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_nor\": {0},", _parameters.ET_nor));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"ET_max\": {0},", _parameters.ET_max));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_min\": {0},", _parameters.PT_min));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_nor\": {0},", _parameters.PT_nor));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"PT_max\": {0}", _parameters.PT_max));
                sb.AppendLine("}");

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "Elastomer 및 PMD 스펙 설정이 성공적으로 저장되었습니다.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Elastomer 스펙 저장 실패: {ex.Message}";
                return false;
            }
        }

        public void LoadPkgGapSpecConfig()
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                string filePath = Path.Combine(dirPath, "pkg_gap_spec.json");
                if (File.Exists(filePath))
                {
                    string json = ReadTextAutoEncoding(filePath);
                    string pkg = SpringConfigService.GetJsonString(json, "PKG");
                    double lid = SpringConfigService.GetJsonDouble(json, "LID_gap", -1);
                    int spr = SpringConfigService.GetJsonInt(json, "SPR_num", -1);

                    if (!string.IsNullOrEmpty(pkg)) _parameters.PKG = pkg;
                    if (lid >= 0) _parameters.LID_gap = lid;
                    if (spr >= 0) _parameters.SPR_num = spr;
                }
            }
            catch { }
        }

        public bool SavePkgGapSpecConfig(out string message)
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                string filePath = Path.Combine(dirPath, "pkg_gap_spec.json");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"PKG\": \"{SpringConfigService.EscapeJson(_parameters.PKG)}\",");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"SPR_num\": {0},", _parameters.SPR_num));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"LID_gap\": {0}", _parameters.LID_gap));
                sb.AppendLine("}");

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "PKG & Gap 설계 데이터가 성공적으로 저장되었습니다.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"PKG & Gap 데이터 저장 실패: {ex.Message}";
                return false;
            }
        }

        public void LoadTemplateOptionConfig()
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                string filePath = Path.Combine(dirPath, "template_option.json");
                if (File.Exists(filePath))
                {
                    string json = ReadTextAutoEncoding(filePath);
                    string tmplStr = SpringConfigService.GetJsonString(json, "SelectedTemplate");
                    if (Enum.TryParse<TemplateOption>(tmplStr, out var opt))
                    {
                        _parameters.SelectedTemplate = opt;
                    }
                }
            }
            catch { }
        }

        public bool SaveTemplateOptionConfig(out string message)
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                string filePath = Path.Combine(dirPath, "template_option.json");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"SelectedTemplate\": \"{_parameters.SelectedTemplate}\"");
                sb.AppendLine("}");

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "사내 표준 도면 양식 설정이 성공적으로 저장되었습니다.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"도면 양식 설정 저장 실패: {ex.Message}";
                return false;
            }
        }

        public void LoadSpringSpecConfig()
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                string filePath = Path.Combine(dirPath, "spring_spec.json");
                if (File.Exists(filePath))
                {
                    string json = ReadTextAutoEncoding(filePath);
                    double wd = SpringConfigService.GetJsonDouble(json, "WireDiameter", -1);
                    double od = SpringConfigService.GetJsonDouble(json, "OuterDiameter", -1);
                    double fl = SpringConfigService.GetJsonDouble(json, "FreeLength", -1);
                    double p2h = SpringConfigService.GetJsonDouble(json, "P2h", -1);
                    double tc = SpringConfigService.GetJsonDouble(json, "TotalCoils", -1);
                    string matStr = SpringConfigService.GetJsonString(json, "Material");
                    bool grind = SpringConfigService.GetJsonBool(json, "IsGrindingEnds", true);

                    if (wd > 0) _parameters.WireDiameter = wd;
                    if (od > 0) _parameters.OuterDiameter = od;
                    if (fl > 0) _parameters.FreeLength = fl;
                    if (p2h > 0) _parameters.P2h = p2h;
                    if (tc > 0) _parameters.TotalCoils = tc;
                    if (Enum.TryParse<SpringMaterial>(matStr, out var sm)) _parameters.Material = sm;
                    _parameters.IsGrindingEnds = grind;

                    SelectedMaterialString = _parameters.Material == SpringMaterial.SUS ? "SUS" : "Music wire";
                }
            }
            catch { }
        }

        public bool SaveSpringSpecConfig(out string message)
        {
            try
            {
                string dirPath = GetConfigDirectoryPath();
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                string filePath = Path.Combine(dirPath, "spring_spec.json");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"WireDiameter\": {0},", _parameters.WireDiameter));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"OuterDiameter\": {0},", _parameters.OuterDiameter));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"FreeLength\": {0},", _parameters.FreeLength));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"P2h\": {0},", _parameters.P2h));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"TotalCoils\": {0},", _parameters.TotalCoils));
                sb.AppendLine($"  \"Material\": \"{_parameters.Material}\",");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"IsGrindingEnds\": {0}", _parameters.IsGrindingEnds ? "true" : "false"));
                sb.AppendLine("}");

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "스프링 설계 파라미터가 성공적으로 저장되었습니다.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"스프링 파라미터 저장 실패: {ex.Message}";
                return false;
            }
        }

        public SpringDesignParameters Parameters => _parameters;

        // 템플릿 선택 바인딩 (기본값: 구형 템플릿)
        public bool IsRev00Selected
        {
            get => _parameters.SelectedTemplate == TemplateOption.StandardRev00;
            set
            {
                if (value)
                {
                    _parameters.SelectedTemplate = TemplateOption.StandardRev00;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNewR01Selected));
                }
            }
        }

        public bool IsNewR01Selected
        {
            get => _parameters.SelectedTemplate == TemplateOption.NewManufacturingR01;
            set
            {
                if (value)
                {
                    _parameters.SelectedTemplate = TemplateOption.NewManufacturingR01;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRev00Selected));
                }
            }
        }

        // 기준 좌표
        public double BaseX
        {
            get => _parameters.BaseX;
            set { _parameters.BaseX = value; OnPropertyChanged(); }
        }

        public double BaseY
        {
            get => _parameters.BaseY;
            set { _parameters.BaseY = value; OnPropertyChanged(); }
        }

        private bool _isOptimizing = false;

        private static double RoundWireDiameter(double val)
        {
            if (val <= 0) return 0.0;
            // 0.6 미만: 0.05 단위 반올림 (0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55)
            if (val < 0.575)
            {
                return Math.Round(val * 20.0, MidpointRounding.AwayFromZero) / 20.0;
            }
            // 0.6 이상: 0.1 단위 반올림 (0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2)
            else
            {
                return Math.Round(val * 10.0, MidpointRounding.AwayFromZero) / 10.0;
            }
        }

        private static double GetMaxValidWireDiameter(double outerDiameter)
        {
            if (outerDiameter <= 0.0) return 0.0;
            // 내경 Di = Do - 2*d > 0 이 되려면 d < Do / 2
            double maxAllowed = (outerDiameter / 2.0) - 0.0001;
            if (maxAllowed <= 0.0) return 0.0;

            if (maxAllowed < 0.60)
            {
                return Math.Max(0.0, Math.Round(Math.Floor(maxAllowed * 20.0) / 20.0, 2));
            }
            else
            {
                return Math.Round(Math.Floor(maxAllowed * 10.0) / 10.0, 2);
            }
        }

        // 1. 선경 (Wire Diameter, d) - 0.6 미만: 0.05 단위, 0.6 이상: 0.1 단위
        public double WireDiameter
        {
            get => RoundWireDiameter(_parameters.WireDiameter);
            set
            {
                double rounded = RoundWireDiameter(value);

                // 내경 풀프루프: 외경이 입력된 상태에서 내경(Di = Do - 2*d)이 0 이하가 되면 선경값을 조정하여 내경이 0을 초과(Di > 0)하도록 보정
                if (OuterDiameter > 0 && rounded * 2.0 >= OuterDiameter)
                {
                    rounded = GetMaxValidWireDiameter(OuterDiameter);
                }

                _parameters.WireDiameter = rounded;
                _parameters.IsGrindingEnds = (rounded >= 0.50);

                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged();
                OnPropertyChanged(nameof(WireDiameterText));
                OnPropertyChanged(nameof(InnerDiameter));
                OnPropertyChanged(nameof(InnerDiameterText));
                OnPropertyChanged(nameof(IsGrindingEnds));
                OnPropertyChanged(nameof(GrindingEndsDisplayText));
                OnPropertyChanged(nameof(GrindingEndsTextColor));
                NotifyCalculatedValues();
            }
        }

        // 2. 내경 (Inner Diameter, Di) - 실시간 계산 프로퍼티: Di = Do - 2*d
        public double InnerDiameter => _parameters.InnerDiameter;

        // 3. 외경 (Outer Diameter, Do) - 소수점 둘째자리 반올림
        public double OuterDiameter
        {
            get => Math.Round(_parameters.OuterDiameter, 2);
            set
            {
                double val = Math.Round(value, 2);

                // 내경 풀프루프: 선경이 입력된 상태에서 내경(Di = Do - 2*d)이 0 이하가 되면 외경값을 최소 (2*d + 0.01) 이상으로 자동 보정하여 내경이 0을 초과(Di > 0)하도록 보정
                if (WireDiameter > 0 && val <= WireDiameter * 2.0)
                {
                    val = Math.Round((WireDiameter * 2.0) + 0.01, 2);
                }

                _parameters.OuterDiameter = val;
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged();
                OnPropertyChanged(nameof(OuterDiameterText));
                OnPropertyChanged(nameof(InnerDiameter));
                OnPropertyChanged(nameof(InnerDiameterText));
                NotifyCalculatedValues();
            }
        }

        // 4. 자유장 (Free Length, H)
        public double FreeLength
        {
            get => _parameters.FreeLength;
            set 
            { 
                _parameters.FreeLength = value; 
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        // 5. Min Device (P2h, mm) (기본값 4.0)
        public double P2h
        {
            get => _parameters.P2h;
            set
            {
                _parameters.P2h = value;
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        // 6. 총권수 (Total Coils, Nf) - 최소값 3.0 (3 미만 입력 시 3.0으로 자동 보정)
        public double TotalCoils
        {
            get => _parameters.TotalCoils;
            set 
            { 
                double clamped = value <= 0 ? 0.0 : (value < 3.0 ? 3.0 : value);
                _parameters.TotalCoils = clamped; 
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(TotalCoilsText));
                OnPropertyChanged(nameof(ActiveCoils));
                OnPropertyChanged(nameof(ActiveCoilsText));
                NotifyCalculatedValues();
            }
        }

        // 7. 유효권수 (Active Coils, Na) - 총권수 - 2.0 자동 계산 (읽기 전용)
        public double ActiveCoils => _parameters.ActiveCoils;

        // 8. 재료 선택 바인딩 (기본 Music wire)
        private string _selectedMaterialString = "Music wire";
        public string SelectedMaterialString
        {
            get => _selectedMaterialString;
            set
            {
                string cleanVal = value.Replace("(공용화)", "").Trim();
                _selectedMaterialString = cleanVal;
                _parameters.Material = cleanVal switch
                {
                    "SUS" => SpringMaterial.SUS,
                    "Music wire" => SpringMaterial.MusicWire,
                    "황동" => SpringMaterial.Brass,
                    "양백" => SpringMaterial.NickelSilver,
                    "인청동" => SpringMaterial.PhosphorBronze,
                    _ => SpringMaterial.MusicWire
                };
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        // 9. 양끝 연마가공 옵션 (선경 0.5mm 이상 시 자동 적용 / 읽기 전용 텍스트 연동)
        public bool IsGrindingEnds
        {
            get => _parameters.IsGrindingEnds;
            set
            {
                _parameters.IsGrindingEnds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GrindingEndsDisplayText));
                OnPropertyChanged(nameof(GrindingEndsTextColor));
                NotifyCalculatedValues();
            }
        }

        public string GrindingEndsDisplayText => WireDiameter >= 0.50 ? "적용" : "미적용(선경 0.5mm 미만)";
        public string GrindingEndsTextColor => WireDiameter >= 0.50 ? "#000000" : "#777777";

        // --- Socket 탭 입력 및 실시간 연동 ---
        public double EF_min
        {
            get => _parameters.EF_min;
            set { _parameters.EF_min = value; OnPropertyChanged(); }
        }

        public double EF_nor
        {
            get => _parameters.EF_nor;
            set { _parameters.EF_nor = value; OnPropertyChanged(); }
        }

        public double EF_max
        {
            get => _parameters.EF_max;
            set { _parameters.EF_max = value; OnPropertyChanged(); }
        }

        public double ET_min
        {
            get => _parameters.ET_min;
            set 
            { 
                _parameters.ET_min = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double ET_nor
        {
            get => _parameters.ET_nor;
            set 
            { 
                _parameters.ET_nor = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double ET_max
        {
            get => _parameters.ET_max;
            set 
            { 
                _parameters.ET_max = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double PT_min
        {
            get => _parameters.PT_min;
            set 
            { 
                _parameters.PT_min = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double PT_nor
        {
            get => _parameters.PT_nor;
            set 
            { 
                _parameters.PT_nor = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double PT_max
        {
            get => _parameters.PT_max;
            set 
            { 
                _parameters.PT_max = value; 
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        // --- Socket 실시간 계산 결과 ---
        public double EP_min => _parameters.EP_min;
        public double EP_nor => _parameters.EP_nor;
        public double EP_max => _parameters.EP_max;

        public double SF_min => _parameters.SF_min;
        public double SF_nor => _parameters.SF_nor;
        public double SF_max => _parameters.SF_max;

        public double TF_min => _parameters.TF_min;
        public double TF_nor => _parameters.TF_nor;
        public double TF_max => _parameters.TF_max;

        // --- Design Result 6행 4열 표 실시간 계산 및 통과여부 프로퍼티 ---
        public double PinForce_min => _parameters.PKGValue > 0 ? _parameters.TF_min / _parameters.PKGValue : 0.0;
        public double PinForce_nor => _parameters.PKGValue > 0 ? _parameters.TF_nor / _parameters.PKGValue : 0.0;
        public double PinForce_max => _parameters.PKGValue > 0 ? _parameters.TF_max / _parameters.PKGValue : 0.0;

        public bool IsForceMinPass => (_parameters.EF_max - 3.0) < PinForce_min && PinForce_min < (_parameters.EF_max + 2.0);
        public bool IsForceNorPass => PinForce_nor < (PinForce_min + 5.0);
        public bool IsForceMaxPass => PinForce_max < (PinForce_min + 10.0);

        public bool IsSAloPass => SAloPercent >= 20.0 && SAloPercent <= 30.0;
        public bool IsFullCompPass => P2h > (_parameters.FullComp + LID_gap);

        public string ForceMinStatusText => IsForceMinPass ? "OK" : "NG";
        public string ForceNorStatusText => IsForceNorPass ? "OK" : "NG";
        public string ForceMaxStatusText => IsForceMaxPass ? "OK" : "NG";

        public string ForceMinSubText => $"({(_parameters.EF_max - 3.0):0.#}~{(_parameters.EF_max + 2.0):0.#})";
        public string ForceNorSubText => $"(~{(PinForce_min + 5.0):F1})";
        public string ForceMaxSubText => $"(~{(PinForce_min + 10.0):F1})";

        public string ForceMinEvaluationLine => $"{ForceMinStatusText} {ForceMinSubText}";
        public string ForceNorEvaluationLine => $"{ForceNorStatusText} {ForceNorSubText}";
        public string ForceMaxEvaluationLine => $"{ForceMaxStatusText} {ForceMaxSubText}";

        public string ForceMinPassText => $"{ForceMinStatusText} {ForceMinSubText}";
        public string ForceNorPassText => $"{ForceNorStatusText} {ForceNorSubText}";
        public string ForceMaxPassText => $"{ForceMaxStatusText} {ForceMaxSubText}";

        public string SAloPassText => IsSAloPass ? $"OK ({SAloPercent:F1}%)" : $"NG ({SAloPercent:F1}%)";
        public string FullCompPassText => IsFullCompPass ? $"OK ({_parameters.FullComp:F2}mm)" : $"NG ({_parameters.FullComp:F2}mm)";

        public string ForceMinPassColor => IsForceMinPass ? "#0044CC" : "#D32F2F";
        public string ForceNorPassColor => IsForceNorPass ? "#0044CC" : "#D32F2F";
        public string ForceMaxPassColor => IsForceMaxPass ? "#0044CC" : "#D32F2F";

        public string SAloPassColor => IsSAloPass ? "#0044CC" : "#D32F2F";
        public string FullCompPassColor => IsFullCompPass ? "#0044CC" : "#D32F2F";

        private bool _hasExecutedAction = false;
        public bool HasExecutedAction
        {
            get => _hasExecutedAction;
            set { _hasExecutedAction = value; OnPropertyChanged(); }
        }

        public bool HasNgEvaluation => !IsForceMinPass || !IsForceNorPass || !IsForceMaxPass || !IsSAloPass || !IsFullCompPass;

        public string PKG
        {
            get => _parameters.PKG;
            set { _parameters.PKG = value; OnPropertyChanged(); }
        }

        public int SPR_num
        {
            get => _parameters.SPR_num;
            set 
            { 
                _parameters.SPR_num = value; 
                if (!_isOptimizing) ClearAllStandardization();
                OnPropertyChanged(); 
                NotifyCalculatedValues();
            }
        }

        public double LID_gap
        {
            get => _parameters.LID_gap;
            set { _parameters.LID_gap = value; OnPropertyChanged(); }
        }

        // --- 공학 실시간 계산 결과 바인딩 프로퍼티 ---
        public double SpIdx => _parameters.SpIdx;
        public double SModi => _parameters.SModi;
        public double STor => _parameters.STor;
        public double SAlo => _parameters.SAlo;
        public double SAloPercent => _parameters.SAlo * 100.0;

        public double MatK => _parameters.MatK;
        public double Sten => _parameters.Sten;

        public void ClearAllStandardization()
        {
            if (!_isOptimizing)
            {
                bool hasHighlight = _wireDiameterBg != "#FFFFFF" ||
                                    _outerDiameterBg != "#FFFFFF" ||
                                    _freeLengthBg != "#FFFFFF" ||
                                    _totalCoilsBg != "#FFFFFF" ||
                                    _activeCoilsBg != "#FFFFFF" ||
                                    _p2hBg != "#FFFFFF" ||
                                    _materialBg != "#FFFFFF" ||
                                    _sprNumBg != "#FFFFFF";

                if (IsStandardized || hasHighlight)
                {
                    const string white = "#FFFFFF";
                    _wireDiameterBg = white;
                    _outerDiameterBg = white;
                    _freeLengthBg = white;
                    _totalCoilsBg = white;
                    _activeCoilsBg = white;
                    _p2hBg = white;
                    _materialBg = white;
                    _sprNumBg = white;

                    OnPropertyChanged(nameof(WireDiameterBg));
                    OnPropertyChanged(nameof(OuterDiameterBg));
                    OnPropertyChanged(nameof(FreeLengthBg));
                    OnPropertyChanged(nameof(TotalCoilsBg));
                    OnPropertyChanged(nameof(ActiveCoilsBg));
                    OnPropertyChanged(nameof(P2hBg));
                    OnPropertyChanged(nameof(MaterialBg));
                    OnPropertyChanged(nameof(SprNumBg));

                    IsStandardized = false;
                    MatchedPartNumber = "";
                    MatchedDrawingNo = "";
                    UpdateMaterialOptions();
                    NotifyAllTextProperties();
                }
            }
        }

        // --- 최적화용 Fix 체크박스 바인딩 ---
        public bool FixSprNum
        {
            get => _parameters.FixSprNum;
            set
            {
                _parameters.FixSprNum = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixWireDiameter
        {
            get => _parameters.FixWireDiameter;
            set
            {
                _parameters.FixWireDiameter = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixOuterDiameter
        {
            get => _parameters.FixOuterDiameter;
            set
            {
                _parameters.FixOuterDiameter = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixFreeLength
        {
            get => _parameters.FixFreeLength;
            set
            {
                _parameters.FixFreeLength = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixTotalCoils
        {
            get => _parameters.FixTotalCoils;
            set
            {
                _parameters.FixTotalCoils = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixActiveCoils
        {
            get => _parameters.FixActiveCoils;
            set
            {
                _parameters.FixActiveCoils = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixP2h
        {
            get => _parameters.FixP2h;
            set
            {
                _parameters.FixP2h = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        public bool FixMaterial
        {
            get => _parameters.FixMaterial;
            set
            {
                _parameters.FixMaterial = value;
                OnPropertyChanged();
                ClearAllStandardization();
            }
        }

        // --- 최적화로 수치가 변경되었을 때 노란색 하이라이트 배경색 바인딩 ---
        private string _wireDiameterBg = "#FFFFFF";
        private string _outerDiameterBg = "#FFFFFF";
        private string _freeLengthBg = "#FFFFFF";
        private string _totalCoilsBg = "#FFFFFF";
        private string _activeCoilsBg = "#FFFFFF";
        private string _p2hBg = "#FFFFFF";
        private string _materialBg = "#FFFFFF";
        private string _sprNumBg = "#FFFFFF";

        public string WireDiameterBg { get => _wireDiameterBg; set { _wireDiameterBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string OuterDiameterBg { get => _outerDiameterBg; set { _outerDiameterBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string FreeLengthBg { get => _freeLengthBg; set { _freeLengthBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string TotalCoilsBg { get => _totalCoilsBg; set { _totalCoilsBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string ActiveCoilsBg { get => _activeCoilsBg; set { _activeCoilsBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string P2hBg { get => _p2hBg; set { _p2hBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string MaterialBg { get => _materialBg; set { _materialBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }
        public string SprNumBg { get => _sprNumBg; set { _sprNumBg = value; OnPropertyChanged(); NotifyAllTextProperties(); } }

        public bool IsStandardized
        {
            get => _parameters.IsStandardized;
            set { _parameters.IsStandardized = value; OnPropertyChanged(); NotifyAllTextProperties(); }
        }

        public string MatchedDrawingNo
        {
            get => _parameters.MatchedDrawingNo;
            set { _parameters.MatchedDrawingNo = value; OnPropertyChanged(); }
        }

        public string WireDiameterText
        {
            get => IsStandardized && WireDiameterBg == "#FFFFE0" ? $"{WireDiameter} (공용화)" : WireDiameter.ToString();
            set
            {
                string cleanStr = (value ?? "").Replace("(공용화)", "").Trim();
                if (double.TryParse(cleanStr, out double val)) WireDiameter = val;
                else OnPropertyChanged();
            }
        }

        public string OuterDiameterText
        {
            get => IsStandardized && OuterDiameterBg == "#FFFFE0" ? $"{OuterDiameter:F2} (공용화)" : OuterDiameter.ToString("F2");
            set
            {
                string cleanStr = (value ?? "").Replace("(공용화)", "").Trim();
                if (double.TryParse(cleanStr, out double val)) OuterDiameter = val;
                else OnPropertyChanged();
            }
        }

        public string InnerDiameterText
        {
            get
            {
                string valStr = InnerDiameter.ToString("F2");
                return IsStandardized && (OuterDiameterBg == "#FFFFE0" || WireDiameterBg == "#FFFFE0") ? $"{valStr} (공용화)" : valStr;
            }
        }

        public string FreeLengthText
        {
            get
            {
                if (FreeLength <= 0) return "";
                return IsStandardized && FreeLengthBg == "#FFFFE0" ? $"{FreeLength} (공용화)" : FreeLength.ToString();
            }
            set
            {
                string cleanStr = (value ?? "").Replace("(공용화)", "").Trim();
                if (double.TryParse(cleanStr, out double val))
                {
                    FreeLength = val;
                    // 풀프루프: 자유장(Hs)은 항상 취부, 장착장(P2h)보다 커야 함 (Hs > P2h)
                    // 사용자가 자유장을 변경하여 FreeLength <= P2h 가 되는 경우 취부, 장착장 값을 삭제
                    if (P2h > 0 && FreeLength <= P2h)
                    {
                        P2h = 0.0;
                        OnPropertyChanged(nameof(P2hText));
                    }
                }
                else
                {
                    FreeLength = 0.0;
                    OnPropertyChanged(nameof(FreeLengthText));
                }
            }
        }

        public string P2hText
        {
            get
            {
                if (P2h <= 0) return "";
                return IsStandardized && P2hBg == "#FFFFE0" ? $"{P2h} (공용화)" : P2h.ToString();
            }
            set
            {
                string cleanStr = (value ?? "").Replace("(공용화)", "").Trim();
                if (double.TryParse(cleanStr, out double val))
                {
                    P2h = val;
                    // 풀프루프: 취부, 장착장(P2h)은 항상 자유장(Hs)보다 작아야 함 (P2h < Hs)
                    // 사용자가 취부, 장착장을 변경하여 P2h >= FreeLength 가 되는 경우 자유장 값을 삭제
                    if (FreeLength > 0 && P2h >= FreeLength)
                    {
                        FreeLength = 0.0;
                        OnPropertyChanged(nameof(FreeLengthText));
                    }
                }
                else
                {
                    P2h = 0.0;
                    OnPropertyChanged(nameof(P2hText));
                }
            }
        }

        public string TotalCoilsText
        {
            get
            {
                if (TotalCoils <= 0) return "";
                return IsStandardized && TotalCoilsBg == "#FFFFE0" ? $"{TotalCoils} (공용화)" : TotalCoils.ToString();
            }
            set
            {
                string cleanStr = (value ?? "").Replace("(공용화)", "").Trim();
                if (double.TryParse(cleanStr, out double val))
                {
                    // 총권수 최소값은 3 (3 미만 입력 시 3으로 자동 보정)
                    if (val < 3.0)
                    {
                        val = 3.0;
                    }
                    TotalCoils = val;
                }
                else
                {
                    TotalCoils = 0.0;
                }
                OnPropertyChanged(nameof(TotalCoilsText));
            }
        }

        public string ActiveCoilsText
        {
            get
            {
                string valStr = ActiveCoils.ToString("0.###");
                return IsStandardized && (TotalCoilsBg == "#FFFFE0" || ActiveCoilsBg == "#FFFFE0") ? $"{valStr} (공용화)" : valStr;
            }
        }

        private string _matchedPartNumber = "";
        public string MatchedPartNumber
        {
            get => _matchedPartNumber;
            set 
            { 
                _matchedPartNumber = value; 
                _parameters.MatchedPartNumber = value;
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(SPR_numText));
                OnPropertyChanged(nameof(MatchedPartNumberDisplay));
                OnPropertyChanged(nameof(MatchedPartNumberVisibility));
            }
        }

        public string MatchedPartNumberDisplay => string.IsNullOrEmpty(MatchedPartNumber) ? "" : $"📌 추천 품목코드 : {MatchedPartNumber}";
        public System.Windows.Visibility MatchedPartNumberVisibility => string.IsNullOrEmpty(MatchedPartNumber) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

        public string SPR_numText
        {
            get => SPR_num.ToString();
            set
            {
                string cleanStr = value ?? "";
                int idx = cleanStr.IndexOf("(");
                if (idx >= 0) cleanStr = cleanStr.Substring(0, idx);
                cleanStr = cleanStr.Trim();
                if (int.TryParse(cleanStr, out int val)) SPR_num = val;
                else OnPropertyChanged();
            }
        }

        public void UpdateMaterialOptions()
        {
            if (MaterialOptions == null) return;
            string currentMat = (_selectedMaterialString ?? "Music wire").Replace("(공용화)", "").Trim();
            if (string.IsNullOrEmpty(currentMat)) currentMat = "Music wire";
            _selectedMaterialString = currentMat;
            OnPropertyChanged(nameof(SelectedMaterialString));
        }

        public void NotifyAllTextProperties()
        {
            OnPropertyChanged(nameof(WireDiameterText));
            OnPropertyChanged(nameof(OuterDiameterText));
            OnPropertyChanged(nameof(InnerDiameterText));
            OnPropertyChanged(nameof(FreeLengthText));
            OnPropertyChanged(nameof(P2hText));
            OnPropertyChanged(nameof(TotalCoilsText));
            OnPropertyChanged(nameof(ActiveCoilsText));
            OnPropertyChanged(nameof(SPR_numText));
            OnPropertyChanged(nameof(SelectedMaterialString));
            OnPropertyChanged(nameof(GrindingEndsDisplayText));
            OnPropertyChanged(nameof(GrindingEndsTextColor));
            OnPropertyChanged(nameof(IsGrindingEnds));
        }

        private SpringDesignParameters? _backupParametersBeforeOptimization;

        private bool ValidateFixedParameters(out string errorMessage)
        {
            var missingList = new List<string>();

            if (FixSprNum && SPR_num <= 0) missingList.Add("스프링 개수");
            if (FixWireDiameter && WireDiameter <= 0) missingList.Add("선경 (d)");
            if (FixOuterDiameter && OuterDiameter <= 0) missingList.Add("외경 (Do)");
            if (FixFreeLength && FreeLength <= 0) missingList.Add("자유장 (Hs)");
            if (FixP2h && P2h <= 0) missingList.Add("취부, 장착장 (P2h)");
            if (FixTotalCoils && TotalCoils <= 0) missingList.Add("총권수 (Nf)");

            if (missingList.Count > 0)
            {
                string items = string.Join(", ", missingList);
                errorMessage = $"Fix 값 중 [{items}] 항목이 미입력 상태입니다.\n\n값을 입력하거나 Fix 체크를 해제한 후 다시 실행해 주세요.";
                return false;
            }

            if (FixFreeLength && FixP2h && FreeLength > 0 && P2h > 0 && FreeLength <= P2h)
            {
                errorMessage = "자유장 (Hs)은 취부, 장착장 (P2h)보다 커야 합니다.\n\n수치를 확인한 후 다시 실행해 주세요.";
                return false;
            }

            errorMessage = "";
            return true;
        }

        // --- 최적화 실행 메서드 ---
        public void ExecuteOptimization()
        {
            if (!ValidateFixedParameters(out string errorMsg))
            {
                System.Windows.MessageBox.Show(errorMsg, "Fix 입력값 확인", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            IsStandardized = false;
            MatchedPartNumber = "";
            UpdateMaterialOptions();
            // 1. 최적화 실행 전 현재 상태 백업 (Undo 지원용)
            _backupParametersBeforeOptimization = CloneParameters(Parameters);

            // 2. 현재 사용자 입력값 가져오기
            double currentD = WireDiameter;
            double currentDo = OuterDiameter;
            double currentHs = FreeLength;
            double currentNf = TotalCoils;
            double currentP2h = P2h;
            SpringMaterial currentMat = Parameters.Material;
            int currentSprNum = SPR_num;

            // 3. 탐색 범위 세팅 (Fix 체크된 항목은 사용자가 현재 입력한 고정값만 탐색)
            double[] dRange = FixWireDiameter ? new[] { currentD } : GenerateWireDiameterRange();
            double[] doRange = FixOuterDiameter ? new[] { currentDo } : GenerateRange(2.0, 8.0, 0.1);
            double[] hsRange = FixFreeLength ? new[] { currentHs } : GenerateRange(4.0, 15.0, 0.5);
            double[] nfRange = FixTotalCoils ? new[] { currentNf } : GenerateRange(3.0, 12.0, 0.5);
            double[] p2hRange = FixP2h ? new[] { currentP2h } : GenerateRange(2.0, 8.0, 0.5);
            SpringMaterial[] matRange = FixMaterial ? new[] { currentMat } : new[] { SpringMaterial.MusicWire, SpringMaterial.SUS };
            int[] sprNumRange = FixSprNum ? new[] { currentSprNum } : GenerateEvenIntRange(2, 100);

            double bestSprNum = double.MaxValue;
            SpringDesignParameters? bestSolution = null;

            // 백업 및 알고리즘 시뮬레이션
            SpringDesignParameters testParam = new SpringDesignParameters
            {
                EF_min = Parameters.EF_min,
                EF_nor = Parameters.EF_nor,
                EF_max = Parameters.EF_max,
                ET_min = Parameters.ET_min,
                ET_nor = Parameters.ET_nor,
                ET_max = Parameters.ET_max,
                PT_min = Parameters.PT_min,
                PT_nor = Parameters.PT_nor,
                PT_max = Parameters.PT_max,
                PKG = Parameters.PKG,
                LID_gap = Parameters.LID_gap,
                IsGrindingEnds = Parameters.IsGrindingEnds
            };

            foreach (int sprNum in sprNumRange)
            {
                testParam.SPR_num = sprNum;

                foreach (SpringMaterial mat in matRange)
                {
                    testParam.Material = mat;

                    foreach (double d in dRange)
                    {
                        testParam.WireDiameter = d;
                        testParam.IsGrindingEnds = (d >= 0.50);

                        foreach (double doVal in doRange)
                        {
                            testParam.OuterDiameter = doVal;
                            // InnerDiameter는 OuterDiameter - (2 * WireDiameter) 연산 프로퍼티로 자동 계산됨

                            foreach (double hs in hsRange)
                            {
                                testParam.FreeLength = hs;

                                foreach (double nf in nfRange)
                                {
                                    testParam.TotalCoils = nf;
                                    // testParam.ActiveCoils는 TotalCoils - 2.0으로 자동 계산됨

                                    foreach (double p2h in p2hRange)
                                    {
                                        testParam.P2h = p2h;

                                        // 제약 조건 검증
                                        if (IsValidSolution(testParam))
                                        {
                                            if (sprNum < bestSprNum)
                                            {
                                                bestSprNum = sprNum;
                                                bestSolution = CloneParameters(testParam);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // 최소 spr_num을 찾은 경우 즉시 조기 종료
                if (bestSolution != null) break;
            }

            // 4. 결과 적용 및 배경색 노란색 하이라이트 세팅
            if (bestSolution != null)
            {
                _isOptimizing = true;
                try
                {
                    WireDiameter = bestSolution.WireDiameter;
                    OuterDiameter = Math.Round(bestSolution.OuterDiameter, 2);
                    FreeLength = bestSolution.FreeLength;
                    TotalCoils = bestSolution.TotalCoils;
                    P2h = bestSolution.P2h;
                    Parameters.Material = bestSolution.Material;
                    SPR_num = bestSolution.SPR_num;
                    _parameters.IsGrindingEnds = (bestSolution.WireDiameter >= 0.50);
                    OnPropertyChanged(nameof(IsGrindingEnds));
                    OnPropertyChanged(nameof(GrindingEndsDisplayText));
                    OnPropertyChanged(nameof(GrindingEndsTextColor));

                    IsStandardized = false;
                    UpdateMaterialOptions();
                    SelectedMaterialString = bestSolution.MaterialDisplayName;
                    OnPropertyChanged(nameof(SelectedMaterialString));
                }
                finally
                {
                    _isOptimizing = false;
                }

                // Fix 체크박스가 해제된(최적화에 의해 새로 결정된) 항목 및 변경된 항목 배경색 노란색 (#FFF2A8) 처리
                const string yellow = "#FFF2A8";
                const string white = "#FFFFFF";

                WireDiameterBg = (!FixWireDiameter || Math.Abs(WireDiameter - currentD) > 0.0001) ? yellow : white;
                OuterDiameterBg = (!FixOuterDiameter || Math.Abs(OuterDiameter - currentDo) > 0.0001) ? yellow : white;
                FreeLengthBg = (!FixFreeLength || Math.Abs(FreeLength - currentHs) > 0.0001) ? yellow : white;
                TotalCoilsBg = (!FixTotalCoils || Math.Abs(TotalCoils - currentNf) > 0.0001) ? yellow : white;
                P2hBg = (!FixP2h || Math.Abs(P2h - currentP2h) > 0.0001) ? yellow : white;
                MaterialBg = (!FixMaterial || Parameters.Material != currentMat) ? yellow : white;
                SprNumBg = (!FixSprNum || SPR_num != currentSprNum) ? yellow : white;

                // 최적화된 새로운 수치로 UI 실시간 공학 평가 (Force min, SAlo, OK/NG 등) 강제 갱신
                NotifyCalculatedValues();
                HasExecutedAction = true;

                System.Windows.MessageBox.Show($"최적화 성공!\n최소 스프링 수량(SPR_num): {SPR_num}개\n변경된 파라미터는 노란색으로 표시됩니다.", "최적화 완료", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show("지정한 Fix 파라미터 및 제약조건 범위 내에서 조건을 충족하는 최적해를 찾지 못했습니다.\nFix 조건을 조정해 보세요.", "최적화 실패", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        // --- Undo 실행 메서드 (최적화 실행 전 상태로 복원) ---
        public void ExecuteUndo()
        {
            if (_backupParametersBeforeOptimization == null)
            {
                System.Windows.MessageBox.Show("복원할 최적화 이전 상태가 없습니다.", "Undo 불가", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            _isOptimizing = true;
            try
            {
                WireDiameter = _backupParametersBeforeOptimization.WireDiameter;
                OuterDiameter = Math.Round(_backupParametersBeforeOptimization.OuterDiameter, 2);
                FreeLength = _backupParametersBeforeOptimization.FreeLength;
                TotalCoils = _backupParametersBeforeOptimization.TotalCoils;
                P2h = _backupParametersBeforeOptimization.P2h;
                Parameters.Material = _backupParametersBeforeOptimization.Material;
                SelectedMaterialString = _backupParametersBeforeOptimization.MaterialDisplayName;
                SPR_num = _backupParametersBeforeOptimization.SPR_num;
                _parameters.IsGrindingEnds = _backupParametersBeforeOptimization.IsGrindingEnds;
                OnPropertyChanged(nameof(IsGrindingEnds));
            }
            finally
            {
                _isOptimizing = false;
            }

            // 모든 입력 배경색 기본 흰색(#FFFFFF)으로 복원
            const string white = "#FFFFFF";
            WireDiameterBg = white;
            OuterDiameterBg = white;
            FreeLengthBg = white;
            TotalCoilsBg = white;
            ActiveCoilsBg = white;
            P2hBg = white;
            MaterialBg = white;
            SprNumBg = white;

            IsStandardized = false;
            MatchedPartNumber = "";
            UpdateMaterialOptions();
            NotifyCalculatedValues();

            NotifyCalculatedValues();
            System.Windows.MessageBox.Show("최적화 실행 전 상태로 원복되었습니다.", "Undo 완료", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        public void ExecuteStandardizationSearch()
        {
            if (!ValidateFixedParameters(out string errorMsg))
            {
                System.Windows.MessageBox.Show(errorMsg, "Fix 입력값 확인", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            string vaultFolderPath = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
            string appDataDir = GetConfigDirectoryPath();

            // 1. Vault 실시간 WebServices 접속하여 최신 공용화 CSV 파일 다운로드
            bool isVaultSuccess = false;
            string csvPath = "";
            string vaultErrMsg = "";

            if (!string.IsNullOrWhiteSpace(VaultServer) && !VaultServer.Contains("???"))
            {
                isVaultSuccess = VaultService.DownloadLatestCsvFromVault(
                    VaultServer,
                    VaultName,
                    VaultUser,
                    VaultPassword,
                    vaultFolderPath,
                    appDataDir,
                    out csvPath,
                    out vaultErrMsg);
            }

            if (!isVaultSuccess)
            {
                // 오프라인 / 네트워크 비연결 시 로컬 캐시 폴더에서 검색
                csvPath = GetLatestSpringCsvFilePath(appDataDir);

                if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                {
                    System.Windows.MessageBox.Show(
                        "Vault 서버 연결 실패. 설정에 로그인 정보를 확인하세요. 자동업데이트 및 공용화DB 최신화를 위해 Vault 연결을 권장합니다.",
                        "Vault 연결 안내",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }

            if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
            {
                System.Windows.MessageBox.Show(
                    "Vault 및 로컬 공용화 DB 폴더에서 공용화 가능 리스트 파일(.csv)을 찾지 못했습니다.",
                    "공용화 DB 읽기 오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            string csvFileNameOnly = Path.GetFileName(csvPath);
            List<SpringCsvRecord> records = ParseSpringCsv(csvPath, out string headerReport);

            if (records.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "Vault에서 공용화 가능 리스트 정보를 읽어오지 못했습니다. 개발자에 연락해 비난하세요.",
                    "공용화 DB 읽기 오류",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            _backupParametersBeforeOptimization = CloneParameters(_parameters);
            _isOptimizing = true;

            List<StandardizationCandidateModel> candidateModels = new List<StandardizationCandidateModel>();

            foreach (var rec in records)
            {
                // Fix 조건 필터링: 사용자가 UI에서 체크(Fix)한 항목은 CSV 사양과 일치하는 후보만 통과
                if (FixWireDiameter && Math.Abs(rec.WireDiameter - WireDiameter) > 0.001) continue;
                if (FixOuterDiameter && Math.Abs(rec.OuterDiameter - OuterDiameter) > 0.01) continue;
                if (FixFreeLength && Math.Abs(rec.FreeLength - FreeLength) > 0.01) continue;
                if (FixTotalCoils && Math.Abs(rec.TotalCoils - TotalCoils) > 0.01) continue;
                if (FixActiveCoils && Math.Abs(rec.ActiveCoils - ActiveCoils) > 0.01) continue;
                if (FixP2h && Math.Abs(rec.P2h - P2h) > 0.01) continue;
                if (FixMaterial && rec.Material != _parameters.Material) continue;

                // Fix 안 된 변수들은 CSV의 사양값을 적용하고, 수량(SPR_num)을 미세 조정하여 히트 여부 판정
                int minSpr = FixSprNum ? SPR_num : 2;
                int maxSpr = FixSprNum ? SPR_num : 300;

                for (int spr = minSpr; spr <= maxSpr; spr += (FixSprNum ? 1 : 2))
                {
                    SpringDesignParameters testParam = CloneParameters(_parameters);
                    testParam.WireDiameter = rec.WireDiameter;
                    testParam.OuterDiameter = rec.OuterDiameter;
                    testParam.FreeLength = rec.FreeLength;
                    testParam.P2h = rec.P2h;
                    testParam.TotalCoils = rec.TotalCoils;
                    testParam.Material = rec.Material;
                    testParam.SPR_num = spr;
                    testParam.IsGrindingEnds = (rec.WireDiameter >= 0.50);

                    if (IsValidSolution(testParam))
                    {
                        candidateModels.Add(new StandardizationCandidateModel
                        {
                            PartNumber = rec.PartNumber,
                            DrawingNo = string.IsNullOrWhiteSpace(rec.DrawingNo) ? rec.PartNumber : rec.DrawingNo,
                            WireDiameter = rec.WireDiameter,
                            OuterDiameter = rec.OuterDiameter,
                            FreeLength = rec.FreeLength,
                            P2h = rec.P2h,
                            TotalCoils = rec.TotalCoils,
                            ActiveCoils = rec.ActiveCoils,
                            Material = rec.Material,
                            MaterialName = rec.Material switch
                            {
                                SpringMaterial.SUS => "SUS",
                                SpringMaterial.MusicWire => "Music wire",
                                SpringMaterial.Brass => "황동",
                                SpringMaterial.NickelSilver => "양백",
                                SpringMaterial.PhosphorBronze => "인청동",
                                _ => "Music wire"
                            },
                            RecommendedSprNum = spr,
                            OriginalRecord = rec
                        });
                        break;
                    }
                }
            }

            _isOptimizing = false;

            // 중복 사양 그룹화 및 정렬 (1순위: SPR_num 최솟값, 2순위: 선경 d 최솟값)
            var sortedCandidates = candidateModels
                .GroupBy(c => new { c.PartNumber, c.WireDiameter, c.OuterDiameter, c.FreeLength, c.TotalCoils, c.P2h })
                .Select(g => g.OrderBy(x => x.RecommendedSprNum).First())
                .OrderBy(c => c.RecommendedSprNum)
                .ThenBy(c => c.WireDiameter)
                .ToList();

            string white = "#FFFFFF";

            if (sortedCandidates.Count > 0)
            {
                StandardizationCandidateModel chosenCandidate;

                if (sortedCandidates.Count > 1)
                {
                    // 2개 이상의 사양이 발견된 경우: 상위 후보 최대 4개에 순위 라벨 지정
                    var topCandidates = sortedCandidates.Take(4).ToList();
                    for (int i = 0; i < topCandidates.Count; i++)
                    {
                        topCandidates[i].IsRecommended = (i == 0);
                        topCandidates[i].RankLabel = i == 0 ? "🏆 [추천 1순위] 공용화 사양" : $"후보 {i + 1} 공용화 사양";
                    }

                    StandardizationCandidateModel? selectedInDialog = null;

                    // WPF 비교 선택 팝업창 띄우기
                    if (System.Windows.Application.Current != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var compWindow = new SpringStandardizationWindow(topCandidates);
                            if (compWindow.ShowDialog() == true)
                            {
                                selectedInDialog = compWindow.SelectedCandidate;
                            }
                        });
                    }
                    else
                    {
                        var compWindow = new SpringStandardizationWindow(topCandidates);
                        if (compWindow.ShowDialog() == true)
                        {
                            selectedInDialog = compWindow.SelectedCandidate;
                        }
                    }

                    if (selectedInDialog == null)
                    {
                        // 사용자가 팝업창을 그냥 닫거나 취소를 누른 경우
                        return;
                    }

                    chosenCandidate = selectedInDialog;
                }
                else
                {
                    chosenCandidate = sortedCandidates[0];
                }

                // 선택된 사양을 메인창 파라미터로 반영
                ApplyCandidateToMainForm(chosenCandidate);
                HasExecutedAction = true;
            }
            else
            {
                WireDiameterBg = white;
                OuterDiameterBg = white;
                FreeLengthBg = white;
                TotalCoilsBg = white;
                ActiveCoilsBg = white;
                P2hBg = white;
                MaterialBg = white;
                SprNumBg = white;

                string failMsg = "현재 입력된 조건에서 모든 조건을 통과하는 공용화 스프링 사양을 찾지 못했습니다.\n\n다른 버튼으로 신규 최적 사양을 계산해 주세요.";

                System.Windows.MessageBox.Show(
                    failMsg,
                    "공용화 스프링 검색 결과",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private void ApplyCandidateToMainForm(StandardizationCandidateModel cand)
        {
            string yellow = "#FFFFE0";
            string white = "#FFFFFF";

            _isOptimizing = true;
            try
            {
                WireDiameter = cand.WireDiameter;
                OuterDiameter = cand.OuterDiameter;
                FreeLength = cand.FreeLength;
                P2h = cand.P2h;
                TotalCoils = cand.TotalCoils;
                SelectedMaterialString = cand.MaterialName;
                SPR_num = cand.RecommendedSprNum;
                _parameters.IsGrindingEnds = (cand.WireDiameter >= 0.50);
                OnPropertyChanged(nameof(IsGrindingEnds));
                OnPropertyChanged(nameof(GrindingEndsDisplayText));
                OnPropertyChanged(nameof(GrindingEndsTextColor));
            }
            finally
            {
                _isOptimizing = false;
            }

            WireDiameterBg = yellow;
            OuterDiameterBg = yellow;
            FreeLengthBg = yellow;
            TotalCoilsBg = yellow;
            ActiveCoilsBg = yellow;
            P2hBg = yellow;
            MaterialBg = yellow;
            SprNumBg = white;

            IsStandardized = true;
            MatchedPartNumber = cand.PartNumber;
            MatchedDrawingNo = string.IsNullOrWhiteSpace(cand.DrawingNo) ? cand.PartNumber : cand.DrawingNo;
            UpdateMaterialOptions();
            NotifyCalculatedValues();

            string successMsg = $"🎉 공용화 스프링 사양 반영 완료!\n\n" +
                               $"📌 선택된 품목코드명: {cand.PartNumber}\n" +
                               $"· 도면 번호 (Drawing No): {cand.DrawingNo}\n" +
                               $"· 필요 스프링 수량: {cand.RecommendedSprNum} 개\n\n" +
                               $"· 선경 (d): {cand.WireDiameter} mm\n" +
                               $"· 외경 (Do): {cand.OuterDiameter} mm\n" +
                               $"· 자유장 (hs): {cand.FreeLength} mm\n" +
                               $"· 총권수 (Nf): {cand.TotalCoils} / 유효권수 (Na): {cand.ActiveCoils}\n" +
                               $"· P2h (취부, 장착장): {cand.P2h} mm\n" +
                               $"· 재료: {cand.MaterialName}\n\n" +
                               $"메인 폼 사양이 노란색으로 업데이트되었습니다.\n[⚡ 도면 해주세요]를 누르시면 원본 DWG 블록이 자동 생성/복사됩니다.";

            System.Windows.MessageBox.Show(
                successMsg,
                "공용화 사양 메인창 반영 완료",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private string GetLatestSpringCsvFilePath(string appDataDir)
        {
            // 1. AppData 폴더 탐색
            try
            {
                if (Directory.Exists(appDataDir))
                {
                    var files = Directory.GetFiles(appDataDir, "*.csv", SearchOption.TopDirectoryOnly)
                                         .Where(f => !Path.GetFileName(f).StartsWith("Feedback", StringComparison.OrdinalIgnoreCase))
                                         .OrderByDescending(f => File.GetLastWriteTime(f))
                                         .ToList();
                    if (files.Count > 0) return files[0];
                }
            }
            catch { }

            // 2. 번들 디렉터리 폴백
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (Directory.Exists(baseDir))
                {
                    var files = Directory.GetFiles(baseDir, "*.csv", SearchOption.TopDirectoryOnly)
                                         .Where(f => !Path.GetFileName(f).StartsWith("Feedback", StringComparison.OrdinalIgnoreCase))
                                         .OrderByDescending(f => File.GetLastWriteTime(f))
                                         .ToList();
                    if (files.Count > 0) return files[0];
                }
            }
            catch { }

            // 3. 개발 디렉터리 폴백
            try
            {
                string devDir = @"c:\Temp\Common_Draw";
                if (Directory.Exists(devDir))
                {
                    var files = Directory.GetFiles(devDir, "*.csv", SearchOption.TopDirectoryOnly)
                                         .Where(f => !Path.GetFileName(f).StartsWith("Feedback", StringComparison.OrdinalIgnoreCase))
                                         .OrderByDescending(f => File.GetLastWriteTime(f))
                                         .ToList();
                    if (files.Count > 0) return files[0];
                }
            }
            catch { }

            return string.Empty;
        }

        /// <summary>
        /// 공용화 플래그가 ON 상태일 때 '도면 해주세요' 클릭 시 호출되는 메서드입니다.
        /// Vault/로컬 공용화 DB 폴더에서 최신 DWG 파일을 찾아 매칭된 Drawing No(도면번호)의 위치를 팝업 리포트로 반환합니다.
        /// </summary>
        public bool SearchAndShowStandardizationDwgLocation(out string reportMessage)
        {
            reportMessage = string.Empty;

            string targetDrawingNo = !string.IsNullOrWhiteSpace(MatchedDrawingNo) ? MatchedDrawingNo : MatchedPartNumber;

            if (string.IsNullOrWhiteSpace(targetDrawingNo))
            {
                reportMessage = "공용화 매칭된 Drawing No / 품목코드 정보가 없습니다.\n\n[⚡ 공용화 해줘] 버튼을 먼저 실행해 주세요.";
                return false;
            }

            string vaultFolderPath = "$/05_KR 설계팀 자료/04_DT 운영관리팀/99_자동화/10_공용화DB";
            string appDataDir = GetConfigDirectoryPath();

            bool isVaultSuccess = false;
            string dwgPath = "";
            string vaultErrMsg = "";

            if (!string.IsNullOrWhiteSpace(VaultServer) && !VaultServer.Contains("???"))
            {
                isVaultSuccess = VaultService.DownloadLatestDwgFromVault(
                    VaultServer,
                    VaultName,
                    VaultUser,
                    VaultPassword,
                    vaultFolderPath,
                    appDataDir,
                    out dwgPath,
                    out vaultErrMsg);
            }

            if (!isVaultSuccess)
            {
                dwgPath = GetLatestDwgFilePath(appDataDir);
            }

            if (string.IsNullOrEmpty(dwgPath) || !File.Exists(dwgPath))
            {
                reportMessage = "Vault 및 로컬 공용화 DB 폴더에서 최신 DWG 도면 파일(.dwg)을 찾을 수 없습니다.";
                return false;
            }

            reportMessage = string.Format("공용화 도면 파일 확인 완료: {0}\n도면 번호: {1}\n품목 코드: {2}", Path.GetFileName(dwgPath), targetDrawingNo, MatchedPartNumber);
            return true;
        }

        private string GetLatestDwgFilePath(string dirPath)
        {
            Func<FileInfo, bool> isStandardizationDwg = f =>
            {
                string name = f.Name.ToLower();
                if (name.Contains("template") || name.Contains("schematic") || name.Equals("temp.dwg")) return false;
                return true;
            };

            try
            {
                if (Directory.Exists(dirPath))
                {
                    var files = Directory.GetFiles(dirPath, "*.dwg", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(isStandardizationDwg)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (files.Count > 0) return files[0].FullName;
                }

                string cwd = Directory.GetCurrentDirectory();
                if (Directory.Exists(cwd))
                {
                    var files = Directory.GetFiles(cwd, "*.dwg", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(isStandardizationDwg)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (files.Count > 0) return files[0].FullName;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (Directory.Exists(baseDir))
                {
                    var files = Directory.GetFiles(baseDir, "*.dwg", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(isStandardizationDwg)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (files.Count > 0) return files[0].FullName;
                }

                string searchDir = @"c:\Temp\Common_Search";
                if (Directory.Exists(searchDir))
                {
                    var files = Directory.GetFiles(searchDir, "*.dwg", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(isStandardizationDwg)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (files.Count > 0) return files[0].FullName;
                }

                string devDir = @"c:\Temp\Common_Draw";
                if (Directory.Exists(devDir))
                {
                    var files = Directory.GetFiles(devDir, "*.dwg", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .Where(isStandardizationDwg)
                        .OrderByDescending(f => f.LastWriteTime)
                        .ToList();

                    if (files.Count > 0) return files[0].FullName;
                }
            }
            catch { }

            return string.Empty;
        }

        private void EnsureDefaultSpringCsvCreated(string csvPath)
        {
            try
            {
                string header = "품목코드,선경(d),외경(Do),자유장(hs),MinDevice(P2h),총권수(Nf),유효권수(Na),재료\n";
                string content = header +
                    "E20-00000B9900043-00,0.55,4.9,9.0,4.0,6.5,4.5,MusicWire\n" +
                    "R94-01068B0400000-00,0.55,4.9,9.0,4.0,6.5,4.5,MusicWire\n" +
                    "R94-00928B0350003-00,0.45,4.2,8.5,3.8,6.0,4.0,MusicWire\n" +
                    "R94-01074B0800002-00,0.60,5.2,10.0,4.5,7.0,5.0,SUS\n" +
                    "R94-01088B0800000-00,0.50,4.5,8.0,3.5,6.5,4.5,MusicWire\n" +
                    "R94-01137B9900000-00,0.40,3.8,7.5,3.2,5.5,3.5,MusicWire\n" +
                    "R94-01155B0650000-00,0.65,5.5,11.0,5.0,7.5,5.5,SUS\n" +
                    "R94-01203B0350000-00,0.50,4.8,9.5,4.2,6.5,4.5,MusicWire\n" +
                    "R94-01266B1000000-00,0.70,6.0,12.0,5.5,8.0,6.0,MusicWire\n" +
                    "R94-01270B0640002-00,0.55,5.0,9.0,4.0,6.5,4.5,MusicWire\n" +
                    "R94-01292B1000002-00,0.60,5.4,10.5,4.8,7.0,5.0,SUS\n";

                File.WriteAllText(csvPath, content, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result.ToArray();
        }

        private List<SpringCsvRecord> ParseSpringCsv(string filePath, out string detectionReport)
        {
            var list = new List<SpringCsvRecord>();
            detectionReport = "";
            try
            {
                string[] lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
                if (lines.Length <= 1)
                {
                    detectionReport = "❌ CSV 파일에 파싱할 데이터 행이 없습니다.";
                    return list;
                }

                // 1번: 상위 10개 행 중 실제 헤더 행("품목코드" 또는 "품목"과 "외경" 동시 포함) 정확한 탐색
                int headerRowIndex = 0;
                for (int i = 0; i < Math.Min(10, lines.Length); i++)
                {
                    string testLine = lines[i].Trim('\uFEFF', '\uFFFE', ' ', '\t', '"').ToLower();
                    if (testLine.Contains("품목코드") || (testLine.Contains("품목") && (testLine.Contains("외경") || testLine.Contains("내경") || testLine.Contains("자유장"))))
                    {
                        headerRowIndex = i;
                        break;
                    }
                }

                string headerLine = lines[headerRowIndex].Trim('\uFEFF', '\uFFFE', ' ', '\t', '"');
                string[] headers = SplitCsvLine(headerLine);

                int idxPartNo = -1, idxDrawingNo = -1, idxD = -1, idxDo = -1, idxDi = -1, idxHs = -1, idxP2h = -1, idxNf = -1, idxNa = -1, idxMat = -1;

                // 헤더 키워드 검색 (Drawing No. 및 품목코드 매칭)
                for (int i = 0; i < headers.Length; i++)
                {
                    string h = headers[i].Trim(' ', '"', '\t').ToLower();
                    if (h.Contains("drawing no") || h.Contains("drawing_no") || h.Contains("drawingno") || h.Contains("도면번호") || h.Contains("도면 번호"))
                        idxDrawingNo = i;
                    else if (h.Contains("품목코드")) 
                        idxPartNo = i;
                    else if (idxPartNo == -1 && (h.Contains("품목") || h.Contains("part") || h.Contains("code") || h.Contains("도번") || h.Contains("item") || h.Contains("품명"))) 
                        idxPartNo = i;
                    else if (h.Contains("선경") || h.Equals("d") || h.Contains("wire")) idxD = i;
                    else if (h.Contains("외경") || h.Contains("outer") || h.Contains("do") || h.Contains("d2")) idxDo = i;
                    else if (h.Contains("내경") || h.Contains("inner") || h.Contains("di") || h.Contains("d1")) idxDi = i;
                    else if (h.Contains("자유장") || h.Contains("자유고") || h.Contains("free") || h.Contains("hs") || h.Contains("l0")) idxHs = i;
                    else if (h.Contains("p2h") || h.Contains("device") || h.Contains("min") || h.Contains("취부") || h.Contains("장착")) idxP2h = i;
                    else if (h.Contains("총권수") || h.Contains("전권수") || h.Contains("nf")) idxNf = i;
                    else if (h.Contains("유효권수") || h.Contains("유효") || h.Contains("na")) idxNa = i;
                    else if (h.Contains("재료") || h.Contains("재질") || h.Contains("mat")) idxMat = i;
                }

                Func<int, string> getHName = idx => (idx >= 0 && idx < headers.Length) ? headers[idx].Trim(' ', '"', '\t') : "미검출";

                detectionReport = $"📊 [CSV 헤더 검출 현황 보고서]\n" +
                    $"· 헤더 행 번호: Line {headerRowIndex + 1}\n" +
                    $"· raw 헤더: \"{headerLine}\"\n\n" +
                    $"1. 품목코드: {(idxPartNo >= 0 ? $"✅ [{idxPartNo}열: '{getHName(idxPartNo)}']" : "❌ 미검출")}\n" +
                    $"2. Drawing No: {(idxDrawingNo >= 0 ? $"✅ [{idxDrawingNo}열: '{getHName(idxDrawingNo)}']" : "ℹ️ 미검출 (품목코드 값 사용)")}\n" +
                    $"3. 선경 (d): {(idxD >= 0 ? $"✅ [{idxD}열: '{getHName(idxD)}']" : (idxDo >= 0 && idxDi >= 0 ? "ℹ️ 외경-내경 관계식 d=(Do-Di)/2 자동 유도" : "❌ 미검출"))}\n" +
                    $"4. 외경 (Do): {(idxDo >= 0 ? $"✅ [{idxDo}열: '{getHName(idxDo)}']" : "❌ 미검출")}\n" +
                    $"5. 내경 (Di): {(idxDi >= 0 ? $"✅ [{idxDi}열: '{getHName(idxDi)}']" : "ℹ️ 외경-선경 관계식 Di=Do-2d 자동 유도")}\n" +
                    $"6. 자유장 (hs): {(idxHs >= 0 ? $"✅ [{idxHs}열: '{getHName(idxHs)}']" : "❌ 미검출")}\n" +
                    $"7. Min Device (P2h): {(idxP2h >= 0 ? $"✅ [{idxP2h}열: '{getHName(idxP2h)}']" : "❌ 미검출")}\n" +
                    $"8. 총권수 (Nf): {(idxNf >= 0 ? $"✅ [{idxNf}열: '{getHName(idxNf)}']" : "❌ 미검출")}\n" +
                    $"9. 유효권수 (Na): {(idxNa >= 0 ? $"✅ [{idxNa}열: '{getHName(idxNa)}']" : "❌ 미검출")}\n" +
                    $"10. 재료 (Material): {(idxMat >= 0 ? $"✅ [{idxMat}열: '{getHName(idxMat)}']" : "ℹ️ 미검출 (기본값 MusicWire 적용)")}";

                for (int i = headerRowIndex + 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim(' ', '\uFEFF', '\t');
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] cols = SplitCsvLine(line);
                    if (cols.Length < 3) continue;

                    var rec = new SpringCsvRecord();
                    Func<int, string> getCol = idx => (idx >= 0 && idx < cols.Length) ? cols[idx].Trim(' ', '"', '\t') : "";

                    rec.PartNumber = getCol(idxPartNo);
                    rec.DrawingNo = getCol(idxDrawingNo);
                    if (string.IsNullOrWhiteSpace(rec.DrawingNo))
                    {
                        rec.DrawingNo = rec.PartNumber;
                    }
                    if (double.TryParse(getCol(idxD), out double d)) rec.WireDiameter = d;
                    if (double.TryParse(getCol(idxDo), out double doVal)) rec.OuterDiameter = doVal;
                    if (double.TryParse(getCol(idxDi), out double diVal)) rec.InnerDiameter = diVal;
                    if (double.TryParse(getCol(idxHs), out double hs)) rec.FreeLength = hs;
                    if (double.TryParse(getCol(idxP2h), out double p2h)) rec.P2h = p2h;
                    if (double.TryParse(getCol(idxNf), out double nf)) rec.TotalCoils = nf;
                    if (double.TryParse(getCol(idxNa), out double na)) rec.ActiveCoils = na;

                    // [GUI와 100% 동일한 선경-외경-내경 관계식 수식 적용]
                    // 공식 1: 외경(Do)과 내경(Di)이 존재할 경우 선경 d = (Do - Di) / 2.0 자동 계산
                    if (rec.OuterDiameter > 0 && rec.InnerDiameter > 0)
                    {
                        rec.WireDiameter = Math.Round((rec.OuterDiameter - rec.InnerDiameter) / 2.0, 3);
                    }
                    // 공식 2: 외경(Do)과 선경(d)만 존재할 경우 내경 Di = Do - 2d 자동 계산
                    else if (rec.OuterDiameter > 0 && rec.WireDiameter > 0 && rec.InnerDiameter <= 0)
                    {
                        rec.InnerDiameter = Math.Round(rec.OuterDiameter - (2.0 * rec.WireDiameter), 3);
                    }
                    // 공식 3: 내경(Di)과 선경(d)만 존재할 경우 외경 Do = Di + 2d 자동 계산
                    else if (rec.InnerDiameter > 0 && rec.WireDiameter > 0 && rec.OuterDiameter <= 0)
                    {
                        rec.OuterDiameter = Math.Round(rec.InnerDiameter + (2.0 * rec.WireDiameter), 3);
                    }

                    string mStr = getCol(idxMat);
                    if (mStr.IndexOf("SUS", StringComparison.OrdinalIgnoreCase) >= 0) rec.Material = SpringMaterial.SUS;
                    else rec.Material = SpringMaterial.MusicWire;

                    list.Add(rec);
                }
            }
            catch { }

            return list;
        }

        private bool IsValidSolution(SpringDesignParameters p)
        {
            // 조건 0: 총권수(Nf)는 반드시 유효권수(Na)보다 커야 함 (물리적 한계 Nf >= Na + 1.0)
            if (p.TotalCoils < p.ActiveCoils + 1.0) return false;

            // 조건 1: 허용응력 20% ~ 30% 사이
            double saloPct = p.SAlo * 100.0;
            if (saloPct < 20.0 || saloPct > 30.0) return false;

            // 조건 2: Min Device > 밀착고 + LID Spring Gap
            if (p.P2h <= (p.FullComp + p.LID_gap)) return false;

            double pkg = p.PKGValue;
            if (pkg <= 0) return false;

            double tfMinPkg = p.TF_min / pkg;
            double tfNorPkg = p.TF_nor / pkg;
            double tfMaxPkg = p.TF_max / pkg;

            // 조건 3: (EF_max - 3) < (TF_min / PKG) < (EF_max + 2)
            if (tfMinPkg <= (p.EF_max - 3.0) || tfMinPkg >= (p.EF_max + 2.0)) return false;

            // 조건 4: (TF_nor / PKG) < (TF_min / PKG + 5)
            if (tfNorPkg >= (tfMinPkg + 5.0)) return false;

            // 조건 5: (TF_max / PKG) < (TF_min / PKG + 10)
            if (tfMaxPkg >= (tfMinPkg + 10.0)) return false;

            return true;
        }

        private static double[] GenerateWireDiameterRange()
        {
            List<double> list = new List<double>();
            // 0.6 미만: 0.05 단위 (0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55)
            for (double v = 0.2; v < 0.59; v += 0.05)
            {
                list.Add(Math.Round(v, 2));
            }
            // 0.6 이상: 0.1 단위 (0.60, 0.70, 0.80, 0.90, 1.00, 1.10, 1.20)
            for (double v = 0.6; v <= 1.2001; v += 0.1)
            {
                list.Add(Math.Round(v, 2));
            }
            return list.ToArray();
        }

        private static double[] GenerateRange(double min, double max, double step)
        {
            List<double> list = new List<double>();
            for (double v = min; v <= max + 0.0001; v += step)
            {
                list.Add(Math.Round(v, 3));
            }
            return list.ToArray();
        }

        private static int[] GenerateIntRange(int min, int max)
        {
            List<int> list = new List<int>();
            for (int v = min; v <= max; v++) list.Add(v);
            return list.ToArray();
        }

        private static int[] GenerateEvenIntRange(int min, int max)
        {
            List<int> list = new List<int>();
            int start = (min % 2 == 0) ? min : min + 1;
            for (int v = start; v <= max; v += 2)
            {
                list.Add(v);
            }
            return list.ToArray();
        }

        private static SpringDesignParameters CloneParameters(SpringDesignParameters p)
        {
            return new SpringDesignParameters
            {
                WireDiameter = p.WireDiameter,
                OuterDiameter = p.OuterDiameter,
                FreeLength = p.FreeLength,
                TotalCoils = p.TotalCoils,
                P2h = p.P2h,
                Material = p.Material,
                IsGrindingEnds = p.IsGrindingEnds,
                EF_min = p.EF_min,
                EF_nor = p.EF_nor,
                EF_max = p.EF_max,
                ET_min = p.ET_min,
                ET_nor = p.ET_nor,
                ET_max = p.ET_max,
                PT_min = p.PT_min,
                PT_nor = p.PT_nor,
                PT_max = p.PT_max,
                PKG = p.PKG,
                SPR_num = p.SPR_num,
                LID_gap = p.LID_gap
            };
        }

        public void NotifyCalculatedValues()
        {
            OnPropertyChanged(nameof(SpIdx));
            OnPropertyChanged(nameof(SModi));
            OnPropertyChanged(nameof(STor));
            OnPropertyChanged(nameof(SAlo));
            OnPropertyChanged(nameof(SAloPercent));
            OnPropertyChanged(nameof(MatK));
            OnPropertyChanged(nameof(Sten));

            // Socket 실시간 계산 결과 갱신
            OnPropertyChanged(nameof(EP_min));
            OnPropertyChanged(nameof(EP_nor));
            OnPropertyChanged(nameof(EP_max));

            OnPropertyChanged(nameof(SF_min));
            OnPropertyChanged(nameof(SF_nor));
            OnPropertyChanged(nameof(SF_max));

            OnPropertyChanged(nameof(TF_min));
            OnPropertyChanged(nameof(TF_nor));
            OnPropertyChanged(nameof(TF_max));

            // Design Result 6행 4열 표 갱신
            OnPropertyChanged(nameof(PinForce_min));
            OnPropertyChanged(nameof(PinForce_nor));
            OnPropertyChanged(nameof(PinForce_max));
            OnPropertyChanged(nameof(IsForceMinPass));
            OnPropertyChanged(nameof(IsForceNorPass));
            OnPropertyChanged(nameof(IsForceMaxPass));
            OnPropertyChanged(nameof(IsSAloPass));
            OnPropertyChanged(nameof(IsFullCompPass));
            OnPropertyChanged(nameof(ForceMinStatusText));
            OnPropertyChanged(nameof(ForceNorStatusText));
            OnPropertyChanged(nameof(ForceMaxStatusText));
            OnPropertyChanged(nameof(ForceMinSubText));
            OnPropertyChanged(nameof(ForceNorSubText));
            OnPropertyChanged(nameof(ForceMaxSubText));
            OnPropertyChanged(nameof(ForceMinEvaluationLine));
            OnPropertyChanged(nameof(ForceNorEvaluationLine));
            OnPropertyChanged(nameof(ForceMaxEvaluationLine));
            OnPropertyChanged(nameof(ForceMinPassText));
            OnPropertyChanged(nameof(ForceNorPassText));
            OnPropertyChanged(nameof(ForceMaxPassText));
            OnPropertyChanged(nameof(SAloPassText));
            OnPropertyChanged(nameof(FullCompPassText));

            OnPropertyChanged(nameof(ForceMinPassColor));
            OnPropertyChanged(nameof(ForceNorPassColor));
            OnPropertyChanged(nameof(ForceMaxPassColor));
            OnPropertyChanged(nameof(SAloPassColor));
            OnPropertyChanged(nameof(FullCompPassColor));

            OnPropertyChanged(nameof(IsGrindingEnds));
            OnPropertyChanged(nameof(GrindingEndsDisplayText));
            OnPropertyChanged(nameof(GrindingEndsTextColor));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
