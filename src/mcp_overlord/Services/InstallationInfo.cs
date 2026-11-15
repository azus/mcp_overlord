using Microsoft.Win32;

namespace mcp_overlord.Services
{
    /// <summary>Reads the product code the installer wrote for this machine.</summary>
    public static class InstallationInfo
    {
        /// <summary>Registry key under HKLM that stores the installed product code.</summary>
        public const string RegistryPath = @"SOFTWARE\MCP Overlord";

        /// <summary>Value name that holds the MSI product code.</summary>
        public const string ProductCodeValueName = "ProductCode";

        /// <summary>
        /// Reads the product code from the 64-bit registry view. Missing, unreadable, and invalid values are null.
        /// </summary>
        /// <returns>The product code in <c>{GUID}</c> form, or null.</returns>
        public static string? ReadProductCode()
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using RegistryKey? key = baseKey.OpenSubKey(RegistryPath);
                return NormalizeProductCode(key?.GetValue(ProductCodeValueName) as string);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns <paramref name="value"/> as an uppercase <c>{GUID}</c>, or null when it is not a product code.
        /// </summary>
        /// <param name="value">Raw registry text.</param>
        /// <returns>The normalized product code, or null.</returns>
        public static string? NormalizeProductCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!Guid.TryParse(value.Trim(), out Guid productCode))
            {
                return null;
            }

            return productCode.ToString("B").ToUpperInvariant();
        }
    }
}
