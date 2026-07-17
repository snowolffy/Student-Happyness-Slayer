using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace MyLabGuard.Client.Services;

/// <summary>
/// ผลลัพธ์การตรวจสอบ publisher ของไฟล์ .exe
/// </summary>
public record PublisherCheckResult(
    string? PublisherName,
    bool IsSignedMatch, // true = มาจาก digital signature (น่าเชื่อถือ), false = มาจาก metadata เท่านั้น
    bool HasPublisher    // false = หาไม่เจอเลยทั้งสองทาง (unsigned + ไม่มี metadata)
);

/// <summary>
/// เช็ค publisher ของไฟล์ .exe โดยใช้ digital signature (Authenticode) เป็นหลัก
/// และ fallback ไปที่ FileVersionInfo.CompanyName ถ้าไม่ signed
/// สำคัญ: metadata ปลอมง่ายมาก ต้องแยกผลลัพธ์ให้ชัดว่ามาจากทางไหน
/// </summary>
public static class PublisherChecker
{
    public static PublisherCheckResult Check(string filePath)
    {
        // 1) ลองเช็ค Authenticode signature ก่อน (น่าเชื่อถือกว่า)
        var signedPublisher = TryGetSignedPublisher(filePath);
        if (signedPublisher is not null)
        {
            return new PublisherCheckResult(signedPublisher, IsSignedMatch: true, HasPublisher: true);
        }

        // 2) ถ้าไม่ signed หรือเช็คไม่ได้ ลอง fallback ไป metadata (CompanyName)
        var metadataPublisher = TryGetMetadataPublisher(filePath);
        if (metadataPublisher is not null)
        {
            return new PublisherCheckResult(metadataPublisher, IsSignedMatch: false, HasPublisher: true);
        }

        return new PublisherCheckResult(null, IsSignedMatch: false, HasPublisher: false);
    }

    private static string? TryGetSignedPublisher(string filePath)
    {
        try
        {
            // ใช้ WinVerifyTrust เช็คว่า signature valid จริงไหม (ไม่ใช่แค่มี cert แปะอยู่)
            if (!WinTrust.IsFileSignedAndTrusted(filePath))
            {
                return null;
            }

            using var cert = X509Certificate.CreateFromSignedFile(filePath);
            using var cert2 = new X509Certificate2(cert);

            // ดึงชื่อ organization จาก Subject (เช่น "CN=Valve Corp, O=Valve Corporation, ...")
            var subject = cert2.Subject;
            var org = ExtractOrganization(subject) ?? cert2.GetNameInfo(X509NameType.SimpleName, false);
            return string.IsNullOrWhiteSpace(org) ? null : org;
        }
        catch
        {
            // ไฟล์ไม่ signed หรืออ่าน cert ไม่ได้ - ถือว่าไม่มี signed publisher
            return null;
        }
    }

    private static string? ExtractOrganization(string subject)
    {
        // subject เป็น string แบบ "CN=X, O=Organization Name, L=City, ..."
        var parts = subject.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("O=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[2..].Trim();
            }
        }
        return null;
    }

    private static string? TryGetMetadataPublisher(string filePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            var company = info.CompanyName;
            return string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// P/Invoke wrapper สำหรับ WinVerifyTrust API (เช็คว่า Authenticode signature valid จริง)
/// </summary>
internal static class WinTrust
{
    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static bool IsFileSignedAndTrusted(string filePath)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var winTrustData = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_NONE,
            dwUnionChoice = WTD_CHOICE_FILE,
            pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>()),
            dwStateAction = WTD_STATEACTION_VERIFY,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = IntPtr.Zero,
            dwProvFlags = WTD_SAFER_FLAG,
            dwUIContext = 0
        };

        try
        {
            Marshal.StructureToPtr(fileInfo, winTrustData.pFile, false);
            var guid = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = WinVerifyTrust(IntPtr.Zero, ref guid, ref winTrustData);
            return result == 0; // 0 = ERROR_SUCCESS = signature valid
        }
        finally
        {
            // ปิด state เพื่อไม่ให้ leak handle
            winTrustData.dwStateAction = WTD_STATEACTION_CLOSE;
            var guid = WINTRUST_ACTION_GENERIC_VERIFY_V2;
            WinVerifyTrust(IntPtr.Zero, ref guid, ref winTrustData);
            Marshal.FreeHGlobal(winTrustData.pFile);
        }
    }

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_SAFER_FLAG = 0x100;

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}