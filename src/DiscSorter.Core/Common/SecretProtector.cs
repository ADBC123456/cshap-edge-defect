using System;
using System.Security.Cryptography;
using System.Text;

namespace DiscSorter.Core.Common
{
    /// <summary>
    /// 加密保存密码：用 Windows 自带的 DPAPI，只有当前 Windows 用户能解密。
    /// 这样数据库里存的是一串乱码，PLC 密码不会以明文出现在文件里。
    /// </summary>
    public static class SecretProtector
    {
        /// <summary>加密，返回 Base64 文本。空字符串返回空字符串。</summary>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return "";
            }
            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>解密。内容为空或无法解密（例如换了 Windows 用户）时返回空字符串。</summary>
        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
            {
                return "";
            }
            try
            {
                byte[] encrypted = Convert.FromBase64String(protectedText);
                byte[] data = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (FormatException)
            {
                return "";
            }
            catch (CryptographicException)
            {
                return "";
            }
        }
    }
}
