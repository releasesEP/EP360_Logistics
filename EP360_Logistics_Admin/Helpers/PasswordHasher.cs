using System;
using System.Security.Cryptography;

namespace EP360_Logistics_Admin.Helpers
{
    // MISMO formato y parametros que EP360 (Helpers/PasswordHasher.cs): PBKDF2-HMACSHA256, 250000 iteraciones,
    // formato "{iteraciones}.{saltBase64}.{hashBase64}". Los portales verifican la contrasena contra este hash
    // (EP360 la compara con VerificarHash), asi que el formato NO se debe cambiar sin cambiarlo en todos.
    // La contrasena nunca se guarda ni se manda a la BD: solo este hash.
    public static class PasswordHasher
    {
        private const int Iteraciones = 250000;
        private const int TamanoSalBytes = 16;
        private const int TamanoHashBytes = 32;

        public static string GenerarHash(string password)
        {
            byte[] sal = new byte[TamanoSalBytes];
            using (var generador = RandomNumberGenerator.Create())
            {
                generador.GetBytes(sal);
            }

            // Hay que usar la sobrecarga con HashAlgorithmName: la de 3 argumentos esta fija en HMAC-SHA1 en .NET Framework.
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password ?? "");
            using (var derivador = new Rfc2898DeriveBytes(passwordBytes, sal, Iteraciones, HashAlgorithmName.SHA256))
            {
                byte[] hash = derivador.GetBytes(TamanoHashBytes);
                return Iteraciones + "." + Convert.ToBase64String(sal) + "." + Convert.ToBase64String(hash);
            }
        }
    }
}
