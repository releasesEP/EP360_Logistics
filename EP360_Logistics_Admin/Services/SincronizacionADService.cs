using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using EP360_Logistics_Admin.DAL;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    public class SincronizacionADService
    {
        private const int MaximoCaracteresDetalle = 20000;

        // Cuentas tecnicas que Exchange / el sistema crean en AD y que no son personas
        // (ej. HealthMailbox-JZ-EXCHANGE-01-010, HealthMailboxfc973c19...). Se comparan por prefijo,
        // sin distinguir mayusculas, contra el usuario y el nombre.
        private static readonly string[] PrefijosCuentasDeSistema =
        {
            "HealthMailbox", "SystemMailbox", "DiscoverySearchMailbox", "FederatedEmail", "Migration.", "MSOL_", "AAD_", "krbtgt", "SM_"
        };

        private readonly SincronizacionDAL _dal = new SincronizacionDAL();
        private readonly ActiveDirectoryService _ad = new ActiveDirectoryService();

        public List<SincronizacionADModel> Historial(int maximo = 20) { return _dal.Historial(maximo); }

        // Se revisa el usuario, el nombre Y el correo: una de las cuentas de Exchange tiene el usuario
        // "SM_xxxx", el nombre "HealthMailboxfc97..." y el correo "HealthMailbox31e8...@...".
        private static bool EsCuentaDeSistema(UsuarioADModel u)
        {
            return PrefijosCuentasDeSistema.Any(p =>
                (u.SamAccountName ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
                (u.NombreCompleto ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
                (u.Correo ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        // Reglas del directorio: una persona aparece SOLO si cumple todas. Devuelve las que le faltan.
        // (La cuenta activa se revisa aparte porque se guarda tal cual en AD.)
        private static List<string> ReglasQueFaltan(UsuarioADModel u)
        {
            var faltan = new List<string>();
            if (!u.Habilitado) faltan.Add("cuenta activa");
            if (string.IsNullOrWhiteSpace(u.Puesto)) faltan.Add("puesto");
            if (string.IsNullOrWhiteSpace(u.Departamento)) faltan.Add("departamento");
            if (string.IsNullOrWhiteSpace(u.Sucursal)) faltan.Add("oficina");
            if (string.IsNullOrWhiteSpace(u.Compania)) faltan.Add("compañía");
            if (string.IsNullOrWhiteSpace(u.Jefe)) faltan.Add("jefe");
            return faltan;
        }

        // Si ya hay otra corrida en curso, Iniciar lanza SqlException 50031 (la muestra el controlador).
        public ResultadoSincronizacionModel Ejecutar(string ejecutadoPor)
        {
            var resultado = new ResultadoSincronizacionModel { IdSincronizacion = _dal.Iniciar(ejecutadoPor) };
            var detalle = new StringBuilder();

            try
            {
                string errorLectura;
                var usuarios = _ad.LeerUsuarios(out errorLectura);
                resultado.Leidos = usuarios.Count;

                if (errorLectura != null)
                {
                    resultado.Errores++;
                    detalle.AppendLine("Lectura de AD: " + errorLectura);
                }

                foreach (var usuario in usuarios)
                {
                    // Una cuenta de sistema NUNCA cumple, pero igual se manda a la BD: si una version anterior
                    // de la sincronizacion ya la habia creado, asi se le da de baja (si es nueva, se omite).
                    bool esSistema = EsCuentaDeSistema(usuario);
                    if (esSistema) resultado.CuentasDeSistema++;

                    var faltan = esSistema ? new List<string>() : ReglasQueFaltan(usuario);
                    bool cumple = !esSistema && faltan.Count == 0;
                    if (!cumple && !esSistema)
                    {
                        resultado.NoCumplenReglas++;
                        foreach (var regla in faltan)
                        {
                            int n; resultado.FaltantesPorRegla.TryGetValue(regla, out n);
                            resultado.FaltantesPorRegla[regla] = n + 1;
                        }
                    }

                    try
                    {
                        // Si no cumple, igual se manda: uno que YA existia se da de baja logica y uno nuevo se omite.
                        switch (_dal.SincronizarUsuario(usuario, cumple))
                        {
                            case "Alta": resultado.Altas++; break;
                            case "Actualizacion":
                                // Ya existia y ya no cumple: se dio de baja logica (no es una simple actualizacion).
                                if (cumple) resultado.Actualizaciones++; else resultado.Bajas++;
                                break;
                            default: resultado.Omitidos++; break;
                        }
                    }
                    catch (SqlException ex)
                    {
                        // Un conflicto de un usuario (mismo correo o samAccountName, etc.) no detiene a los demas.
                        resultado.Errores++;
                        if (detalle.Length < MaximoCaracteresDetalle) detalle.AppendLine(usuario.SamAccountName + ": " + ex.Message);
                    }
                }

                bool fallida = usuarios.Count == 0 && errorLectura != null;
                resultado.Estado = fallida ? "Fallida" : (resultado.Errores > 0 ? "ConErrores" : "Terminada");
            }
            catch (Exception ex)
            {
                resultado.Estado = "Fallida";
                resultado.Errores++;
                detalle.AppendLine(ex.Message);
            }

            resultado.DetalleErrores = detalle.Length == 0 ? null : detalle.ToString();
            _dal.Finalizar(resultado);
            return resultado;
        }
    }
}
