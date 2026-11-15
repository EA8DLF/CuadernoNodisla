using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>
/// Mira la firma Authenticode incrustada en un fichero con <c>WinVerifyTrust</c> y devuelve el
/// certificado que Windows ha usado para comprobarla.
/// </summary>
/// <remarks>
/// <para>
/// Se llama a <c>WinVerifyTrust</c> con la accion <c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>
/// (Authenticode), sin interfaz (<c>WTD_UI_NONE</c>), sin salir a la red a por listas de
/// revocacion (<c>WTD_REVOKE_NONE</c> + <c>WTD_CACHE_ONLY_URL_RETRIEVAL</c>) y conservando el
/// estado (<c>WTD_STATEACTION_VERIFY</c>) para leer despues el firmante con
/// <c>WTHelperProvDataFromStateData</c> → <c>WTHelperGetProvSignerFromChain</c> →
/// <c>WTHelperGetProvCertFromChain</c>. Asi el certificado que se compara es el mismo con el
/// que Windows ha comprobado la firma, no uno leido aparte del fichero.
/// </para>
/// <para>
/// <b>Como se distingue «integra pero de raiz no fiable» de «alterada».</b> El certificado de
/// NODISLA es autofirmado, asi que en un PC ajeno el resultado normal NO es 0 sino
/// <c>CERT_E_UNTRUSTEDROOT</c>. Segun <c>wintrust.h</c>, sin <c>WTD_SAFER_FLAG</c> la funcion
/// devuelve «cualquier error de verificacion del codigo firmado»; en las pruebas de integracion
/// un fichero alterado da <c>TRUST_E_BAD_DIGEST</c>, no el error de la raiz. Para no fiarse
/// solo del codigo de retorno se miran ademas los errores por paso (<c>CRYPT_PROVIDER_DATA.padwTrustStepErrors</c>):
/// los pasos del objeto (<c>TRUSTERROR_STEP_FINAL_OBJPROV</c>: abrir el fichero y calcular su
/// resumen) y de la firma (<c>TRUSTERROR_STEP_FINAL_SIGPROV</c>: comprobar el resumen firmado)
/// tienen que estar a cero. Solo entonces, y solo con retorno 0 o <c>CERT_E_UNTRUSTEDROOT</c>,
/// la firma cuenta como integra. Cualquier otro codigo es «no vale».
/// </para>
/// <para>
/// Documentacion: WinVerifyTrust
/// (https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust),
/// WINTRUST_DATA (https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_data),
/// CRYPT_PROVIDER_DATA (https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-crypt_provider_data),
/// el ejemplo «Verifying the Signature of a PE File»
/// (https://learn.microsoft.com/windows/win32/seccrypto/example-c-program--verifying-the-signature-of-a-pe-file)
/// y las cabeceras <c>wintrust.h</c>, <c>softpub.h</c> y <c>winerror.h</c> del SDK de Windows
/// (10.0.22621), de donde salen las constantes de abajo.
/// </para>
/// </remarks>
public sealed class VerificadorDeFirmaAuthenticode : IVerificadorDeFirma
{
    // winerror.h
    private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);

    // wintrust.h
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 1;
    private const uint WTD_STATEACTION_CLOSE = 2;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
    private const int TRUSTERROR_STEP_FINAL_OBJPROV = 32;
    private const int TRUSTERROR_STEP_FINAL_SIGPROV = 33;

    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    /// <inheritdoc />
    public ResultadoDeFirma Verificar(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta)) return new ResultadoDeFirma(EstadoDeFirma.NoValida);

        // softpub.h: WINTRUST_ACTION_GENERIC_VERIFY_V2 {00AAC56B-CD44-11d0-8CC2-00C04FC295EE}
        var accion = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var punteroRuta = IntPtr.Zero;
        var punteroFichero = IntPtr.Zero;
        try
        {
            punteroRuta = Marshal.StringToHGlobalUni(ruta);
            punteroFichero = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(
                new WINTRUST_FILE_INFO
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                    pcwszFilePath = punteroRuta,
                },
                punteroFichero,
                fDeleteOld: false);

            var datos = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = punteroFichero,
                dwStateAction = WTD_STATEACTION_VERIFY,
                dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
            };

            var codigo = WinVerifyTrust(INVALID_HANDLE_VALUE, ref accion, ref datos);
            try
            {
                return Interpretar(codigo, datos.hWVTStateData);
            }
            finally
            {
                // Hay que soltar el estado siempre, haya ido bien o mal la verificacion.
                datos.dwStateAction = WTD_STATEACTION_CLOSE;
                _ = WinVerifyTrust(INVALID_HANDLE_VALUE, ref accion, ref datos);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Un fallo al hablar con Windows no puede acabar en «vale».
            return new ResultadoDeFirma(EstadoDeFirma.NoValida);
        }
        finally
        {
            if (punteroFichero != IntPtr.Zero) Marshal.FreeHGlobal(punteroFichero);
            if (punteroRuta != IntPtr.Zero) Marshal.FreeHGlobal(punteroRuta);
        }
    }

    private static ResultadoDeFirma Interpretar(int codigo, IntPtr estado)
    {
        // wintrust.h: estos tres dicen que el fichero no lleva firma (o no es algo firmable).
        if (codigo is TRUST_E_NOSIGNATURE or TRUST_E_SUBJECT_FORM_UNKNOWN or TRUST_E_PROVIDER_UNKNOWN)
        {
            return new ResultadoDeFirma(EstadoDeFirma.SinFirma, Codigo: codigo);
        }

        // Solo dos resultados pueden ser una firma integra: todo bien (el certificado esta
        // instalado como raiz de confianza en este PC) o cadena que acaba en una raiz que
        // Windows no conoce (lo normal con un certificado autofirmado).
        if (codigo != 0 && codigo != CERT_E_UNTRUSTEDROOT) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);
        if (estado == IntPtr.Zero) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);

        var punteroDatos = WTHelperProvDataFromStateData(estado);
        if (punteroDatos == IntPtr.Zero) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);

        // Los pasos del objeto (resumen del fichero) y de la firma tienen que haber pasado.
        var cabecera = Marshal.PtrToStructure<CRYPT_PROVIDER_DATA_CABECERA>(punteroDatos);
        if (cabecera.padwTrustStepErrors == IntPtr.Zero || cabecera.cdwTrustStepErrors <= TRUSTERROR_STEP_FINAL_SIGPROV)
        {
            return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);
        }

        var errorDelObjeto = Marshal.ReadInt32(cabecera.padwTrustStepErrors, TRUSTERROR_STEP_FINAL_OBJPROV * sizeof(int));
        var errorDeLaFirma = Marshal.ReadInt32(cabecera.padwTrustStepErrors, TRUSTERROR_STEP_FINAL_SIGPROV * sizeof(int));
        if (errorDelObjeto != 0 || errorDeLaFirma != 0)
        {
            return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: errorDelObjeto != 0 ? errorDelObjeto : errorDeLaFirma);
        }

        // Firmante principal (indice 0, no contrafirma) y su certificado (indice 0 de la cadena).
        var punteroFirmante = WTHelperGetProvSignerFromChain(punteroDatos, 0, fCounterSigner: false, 0);
        if (punteroFirmante == IntPtr.Zero) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);

        var punteroCertificado = WTHelperGetProvCertFromChain(punteroFirmante, 0);
        if (punteroCertificado == IntPtr.Zero) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);

        var contexto = Marshal.PtrToStructure<CRYPT_PROVIDER_CERT_CABECERA>(punteroCertificado).pCert;
        if (contexto == IntPtr.Zero) return new ResultadoDeFirma(EstadoDeFirma.NoValida, Codigo: codigo);

        // El constructor duplica el contexto: el certificado sigue vivo al soltar el estado.
        using var certificado = new X509Certificate2(contexto);
        var huella = Convert.ToHexString(SHA256.HashData(certificado.RawData));
        return new ResultadoDeFirma(EstadoDeFirma.Integra, huella, certificado.Subject, codigo);
    }

    // wintrust.h (pshpack8): WINTRUST_FILE_INFO
    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    // wintrust.h (pshpack8): WINTRUST_DATA, con pSignatureSettings (Windows 8 en adelante).
    [StructLayout(LayoutKind.Sequential)]
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
        public IntPtr pSignatureSettings;
    }

    // wintrust.h (pshpack8): el principio de CRYPT_PROVIDER_DATA, hasta padwTrustStepErrors.
    [StructLayout(LayoutKind.Sequential)]
    private struct CRYPT_PROVIDER_DATA_CABECERA
    {
        public uint cbStruct;
        public IntPtr pWintrustData;
        public int fOpenedFile;
        public IntPtr hWndParent;
        public IntPtr pgActionID;
        public IntPtr hProv;
        public uint dwError;
        public uint dwRegSecuritySettings;
        public uint dwRegPolicySettings;
        public IntPtr psPfns;
        public uint cdwTrustStepErrors;
        public IntPtr padwTrustStepErrors;
    }

    // wintrust.h (pshpack8): el principio de CRYPT_PROVIDER_CERT.
    [StructLayout(LayoutKind.Sequential)]
    private struct CRYPT_PROVIDER_CERT_CABECERA
    {
        public uint cbStruct;
        public IntPtr pCert;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr pProvData,
        uint idxSigner,
        [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
        uint idxCounterSigner);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr pSgnr, uint idxCert);
}
