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
        private readonly SucursalDAL _sucursalDAL = new SucursalDAL();
        private readonly ActiveDirectoryService _ad = new ActiveDirectoryService();

        public List<SincronizacionADModel> Historial(int maximo = 20) { return _dal.Historial(maximo); }

        // Pantalla de detalle de una corrida, con filtro por tipo de movimiento y busqueda por nombre/usuario/valor.
        public SincronizacionADDetalleVistaModel Detalle(int idSincronizacion, string filtro, string buscar)
        {
            var corrida = _dal.Obtener(idSincronizacion);
            if (corrida == null) return null;
            var historial = _dal.Historial(500);
            int posicion = historial.FindIndex(h => h.IdSincronizacion == idSincronizacion);

            var renglones = _dal.Detalle(idSincronizacion);
            if (!string.IsNullOrWhiteSpace(filtro))
            {
                renglones = renglones.Where(r => GrupoDe(r) == filtro).ToList();
            }
            if (!string.IsNullOrWhiteSpace(buscar))
            {
                string b = buscar.Trim();
                Func<string, bool> tiene = t => t != null && t.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0;
                renglones = renglones.Where(r => tiene(r.Nombre) || tiene(r.Usuario) || tiene(r.Campo) || tiene(r.ValorAnterior) || tiene(r.ValorNuevo) || tiene(r.Motivo)).ToList();
            }

            return new SincronizacionADDetalleVistaModel
            {
                Corrida = corrida,
                Renglones = renglones,
                Filtro = filtro,
                Buscar = buscar,
                // El historial viene del mas nuevo al mas viejo: "anterior" es la corrida de antes en el tiempo.
                Anterior = posicion >= 0 && posicion + 1 < historial.Count ? historial[posicion + 1].IdSincronizacion : (int?)null,
                Siguiente = posicion > 0 ? historial[posicion - 1].IdSincronizacion : (int?)null
            };
        }

        // Todos los renglones de una corrida (para los conteos de las pestanas y el Excel).
        public List<SincronizacionADDetalleModel> RenglonesDetalle(int idSincronizacion) { return _dal.Detalle(idSincronizacion); }

        // Pestana a la que pertenece cada renglon del detalle.
        public static string GrupoDe(SincronizacionADDetalleModel r)
        {
            if (r.Entidad != "Persona") return "Catalogos";
            switch (r.Accion)
            {
                case "Alta": return "Altas";
                case "Cambio": return "Cambios";
                case "Baja": return "Bajas";
                case "Reactivacion": return "Reactivaciones";
                case "Error": return "Errores";
                default: return "Omitidos";
            }
        }

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

        // Al terminar de procesar a todas las personas: una sucursal que quedo sin personas ni cuentas (ej. una oficina mal escrita en AD
        // que luego se corrigio) se desactiva, y una que se desactivo asi y volvio a tener gente se reactiva. Lo que cambio queda en el
        // detalle de la corrida; los nombres para el aviso se calculan comparando la lista antes y despues.
        // Un fallo aqui no tira la sincronizacion: se reporta como error de la corrida.
        private void ReconciliarSucursales(ResultadoSincronizacionModel resultado, StringBuilder detalle)
        {
            try
            {
                var antes = _sucursalDAL.Listar().ToDictionary(s => s.IdSucursal, s => s.Activo);
                _sucursalDAL.Reconciliar(resultado.IdSincronizacion);
                foreach (var s in _sucursalDAL.Listar())
                {
                    bool estabaActiva;
                    if (!antes.TryGetValue(s.IdSucursal, out estabaActiva) || estabaActiva == s.Activo) continue;
                    (s.Activo ? resultado.SucursalesReactivadas : resultado.SucursalesDesactivadas).Add(s.Nombre);
                }
            }
            catch (SqlException ex)
            {
                resultado.Errores++;
                detalle.AppendLine("Sucursales sin uso: " + ex.Message);
            }
        }

        // Departamentos sin personas activas: se desactivan (se reactivan solos cuando AD vuelve a traerlos).
        private void ReconciliarDepartamentos(ResultadoSincronizacionModel resultado, StringBuilder detalle)
        {
            try { _dal.ReconciliarDepartamentos(resultado.IdSincronizacion); }
            catch (SqlException ex)
            {
                resultado.Errores++;
                detalle.AppendLine("Departamentos sin uso: " + ex.Message);
            }
        }

        // Personas de AD que ya no aparecen en el dominio: baja logica. Solo con una lectura completa (sin error de lectura);
        // el procedimiento ademas se niega (50033) si se leyeron menos usuarios que personas activas.
        private void DesactivarAusentes(ResultadoSincronizacionModel resultado, StringBuilder detalle)
        {
            try { _dal.DesactivarAusentes(resultado.IdSincronizacion, resultado.Leidos); }
            catch (SqlException ex)
            {
                resultado.Errores++;
                detalle.AppendLine("Personas que ya no estan en AD: " + ex.Message);
            }
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
                    if (!esSistema && faltan.Count > 0)
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
                        // El procedimiento deja en el detalle de la corrida cada valor que cambio (con antes y despues).
                        string motivo = esSistema ? "Es cuenta de sistema" : faltan.Count == 0 ? null : "Le falta: " + string.Join(", ", faltan);
                        switch (_dal.SincronizarUsuario(usuario, cumple, resultado.IdSincronizacion, motivo))
                        {
                            case "Alta": resultado.Altas++; break;
                            case "Actualizacion": resultado.Actualizaciones++; break;
                            case "Baja": resultado.Bajas++; break;
                            case "Reactivacion": resultado.Reactivaciones++; break;
                            case "SinCambios": resultado.SinCambios++; break;
                            default: resultado.Omitidos++; break;
                        }
                    }
                    catch (SqlException ex)
                    {
                        // Un conflicto de un usuario (mismo correo o samAccountName, etc.) no detiene a los demas.
                        resultado.Errores++;
                        if (detalle.Length < MaximoCaracteresDetalle) detalle.AppendLine(usuario.SamAccountName + ": " + ex.Message);
                        try { _dal.RegistrarDetalle(resultado.IdSincronizacion, usuario, "Error", ex.Message); }
                        catch (SqlException) { /* el error ya quedo en detalleErrores */ }
                    }
                }

                bool fallida = usuarios.Count == 0 && errorLectura != null;
                // Con una lectura incompleta NO se da de baja a nadie por ausencia: faltarian personas que si existen.
                if (!fallida && errorLectura == null) DesactivarAusentes(resultado, detalle);
                if (!fallida)
                {
                    ReconciliarSucursales(resultado, detalle);
                    ReconciliarDepartamentos(resultado, detalle);
                }
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
            try
            {
                var guardada = _dal.Obtener(resultado.IdSincronizacion);
                if (guardada != null) resultado.Ausentes = guardada.Ausentes;
            }
            catch (SqlException) { /* solo es para el aviso */ }
            return resultado;
        }
    }
}
