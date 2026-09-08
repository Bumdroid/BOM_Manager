using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BOMManager.Modules.SpringDesigner.Services
{
    /// <summary>
    /// 스프링 설계기 설정 파일(JSON) 관리 서비스 (.NET 4.8 호환 경량 JSON 지원)
    /// </summary>
    public static class SpringConfigService
    {
        public static string GetConfigDirectoryPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Common_Draw");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string ReadTextAutoEncoding(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0) return string.Empty;

                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
                }

                try
                {
                    var utf8Strict = new UTF8Encoding(false, true);
                    return utf8Strict.GetString(bytes);
                }
                catch
                {
                    try
                    {
                        return Encoding.GetEncoding(949).GetString(bytes);
                    }
                    catch
                    {
                        return Encoding.Default.GetString(bytes);
                    }
                }
            }
            catch
            {
                return File.ReadAllText(filePath, Encoding.Default);
            }
        }

        /// <summary>
        /// 단순 JSON 객체 문자열에서 특정 키의 문자열 값을 추출합니다.
        /// </summary>
        public static string GetJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key)) return string.Empty;
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return string.Empty;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return string.Empty;

            string after = json.Substring(colonIdx + 1).Trim();
            if (after.StartsWith("\""))
            {
                int nextQuote = after.IndexOf('"', 1);
                if (nextQuote > 0)
                {
                    return after.Substring(1, nextQuote - 1).Replace("\\\"", "\"").Replace("\\\\", "\\");
                }
            }
            else
            {
                // 숫자/불리언 등
                int comma = after.IndexOfAny(new[] { ',', '}', '\r', '\n' });
                if (comma >= 0)
                {
                    return after.Substring(0, comma).Trim().Trim('"', ' ', '\t');
                }
                return after.Trim().Trim('"', ' ', '\t');
            }
            return string.Empty;
        }

        /// <summary>
        /// 단순 JSON 객체 문자열에서 특정 키의 double 값을 추출합니다.
        /// </summary>
        public static double GetJsonDouble(string json, string key, double defaultValue = 0.0)
        {
            string val = GetJsonString(json, key);
            if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ||
                double.TryParse(val, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
            {
                return d;
            }
            return defaultValue;
        }

        /// <summary>
        /// 단순 JSON 객체 문자열에서 특정 키의 int 값을 추출합니다.
        /// </summary>
        public static int GetJsonInt(string json, string key, int defaultValue = 0)
        {
            string val = GetJsonString(json, key);
            if (int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out int i) ||
                int.TryParse(val, NumberStyles.Any, CultureInfo.CurrentCulture, out i))
            {
                return i;
            }
            return defaultValue;
        }

        /// <summary>
        /// 단순 JSON 객체 문자열에서 특정 키의 bool 값을 추출합니다.
        /// </summary>
        public static bool GetJsonBool(string json, string key, bool defaultValue = false)
        {
            string val = GetJsonString(json, key);
            if (bool.TryParse(val, out bool b))
            {
                return b;
            }
            return defaultValue;
        }

        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
