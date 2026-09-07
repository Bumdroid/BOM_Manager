using System;
using System.IO;

namespace BOMManager.Core
{
    public class VaultConfig
    {
        public string Server { get; set; } = "192.168.150.105";
        public string VaultName { get; set; } = "ISC_Vault";
        public string LastUsername { get; set; } = "";
        public string Password { get; set; } = "";
        public bool AutoLogin { get; set; } = true;
    }

    public static class VaultConfigManager
    {
        public const string DefaultServer = "192.168.150.105";
        public const string DefaultVaultName = "ISC_Vault";
        public const string DefaultPassword = "";

        private static string GetConfigFilePath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "BOMManager");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                return Path.Combine(folder, "vault_config.json");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault_config.json");
            }
        }

        public static VaultConfig Load()
        {
            var config = new VaultConfig();
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = ParseSimpleJson(json);
                    if (loaded != null)
                    {
                        if (!string.IsNullOrEmpty(loaded.LastUsername)) config.LastUsername = loaded.LastUsername;
                        config.AutoLogin = loaded.AutoLogin;
                    }
                }
                else
                {
                    // Fallback local file check
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault_config.json");
                    if (File.Exists(localPath))
                    {
                        string json = File.ReadAllText(localPath);
                        var loaded = ParseSimpleJson(json);
                        if (loaded != null)
                        {
                            if (!string.IsNullOrEmpty(loaded.LastUsername)) config.LastUsername = loaded.LastUsername;
                            config.AutoLogin = loaded.AutoLogin;
                        }
                    }
                }
            }
            catch { }

            // Ensure server and vault constants
            config.Server = DefaultServer;
            config.VaultName = DefaultVaultName;
            config.Password = DefaultPassword;
            return config;
        }

        public static void SaveLastUsername(string username, bool autoLogin = true)
        {
            try
            {
                var config = Load();
                config.LastUsername = username?.Trim() ?? "";
                config.AutoLogin = autoLogin;

                string json = "{\r\n  \"Server\": \"" + EscapeJson(config.Server) + "\",\r\n  \"VaultName\": \"" + EscapeJson(config.VaultName) + "\",\r\n  \"LastUsername\": \"" + EscapeJson(config.LastUsername) + "\",\r\n  \"AutoLogin\": " + (config.AutoLogin ? "true" : "false") + "\r\n}";

                string path = GetConfigFilePath();
                File.WriteAllText(path, json);

                try
                {
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vault_config.json");
                    File.WriteAllText(localPath, json);
                }
                catch { }
            }
            catch { }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static VaultConfig? ParseSimpleJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var cfg = new VaultConfig();

            // Lightweight json parser without external dependencies
            foreach (var line in json.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("\"LastUsername\"", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = trimmed.IndexOf(':');
                    if (colon >= 0)
                    {
                        string val = trimmed.Substring(colon + 1).Trim().Trim('"', ' ', '\t');
                        cfg.LastUsername = val;
                    }
                }
                else if (trimmed.StartsWith("\"AutoLogin\"", StringComparison.OrdinalIgnoreCase))
                {
                    int colon = trimmed.IndexOf(':');
                    if (colon >= 0)
                    {
                        string val = trimmed.Substring(colon + 1).Trim().Trim('"', ' ', '\t');
                        if (bool.TryParse(val, out bool autoVal))
                        {
                            cfg.AutoLogin = autoVal;
                        }
                    }
                }
            }

            return cfg;
        }
    }
}
