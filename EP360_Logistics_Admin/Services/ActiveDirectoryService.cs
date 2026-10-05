using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using EP360_Logistics_Admin.Models;

namespace EP360_Logistics_Admin.Services
{
    internal class EntradaCachePerteneceAGrupo
    {
        public bool Resultado { get; set; }
        public DateTime Expira { get; set; }
    }

    // Misma regla que EP360: solo el grupo de AD configurado en AD_GrupoAdmins (por defecto
    // "ep360admins") administra el directorio global. Se revisa en vivo contra AD, con una
    // cache corta en memoria (90 s) para no ir al controlador de dominio en cada request.
    // Sacar a alguien del grupo surte efecto, como maximo, al vencer esa cache.
    public class ActiveDirectoryService
    {
        private static readonly ConcurrentDictionary<string, EntradaCachePerteneceAGrupo> _cachePerteneceAGrupo =
            new ConcurrentDictionary<string, EntradaCachePerteneceAGrupo>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DuracionCachePerteneceAGrupo = TimeSpan.FromSeconds(90);

        private static string Leer(string clave)
        {
            return Environment.GetEnvironmentVariable(clave, EnvironmentVariableTarget.Machine) ?? ConfigurationManager.AppSettings[clave];
        }

        private PrincipalContext CrearContexto()
        {
            string servidor = Leer("AD_Servidor");
            string usuario = Leer("AD_Usuario");
            string password = Leer("AD_Password");

            // Sin AD_Servidor se usa el dominio de la cuenta que ejecuta la app. Sin usuario y
            // password de servicio se usa la identidad del propio proceso (pool de IIS).
            bool conServidor = !string.IsNullOrWhiteSpace(servidor);
            bool conCredenciales = !string.IsNullOrWhiteSpace(usuario) && !string.IsNullOrWhiteSpace(password);

            if (conServidor && conCredenciales) return new PrincipalContext(ContextType.Domain, servidor, usuario, password);
            if (conServidor) return new PrincipalContext(ContextType.Domain, servidor);
            return new PrincipalContext(ContextType.Domain);
        }

        // IIS entrega "DOMINIO\usuario"; AD busca por el nombre corto (samAccountName).
        private static string ObtenerNombreCorto(string nombreUsuarioWindows)
        {
            if (string.IsNullOrWhiteSpace(nombreUsuarioWindows)) return null;
            return nombreUsuarioWindows.Contains("\\") ? nombreUsuarioWindows.Split('\\').Last() : nombreUsuarioWindows;
        }

        // Lee TODOS los usuarios del dominio (habilitados y deshabilitados). El filtrado (cuentas de
        // servicio, etc.) lo decide SincronizacionADService. Si la consulta truena a la mitad se
        // regresa lo ya leido y el motivo en errorLectura.
        public List<UsuarioADModel> LeerUsuarios(out string errorLectura)
        {
            errorLectura = null;
            var resultado = new List<UsuarioADModel>();
            try
            {
                using (var contexto = CrearContexto())
                using (var filtro = new UserPrincipal(contexto))
                using (var buscador = new PrincipalSearcher(filtro))
                {
                    var buscadorDirectorio = buscador.GetUnderlyingSearcher() as DirectorySearcher;
                    if (buscadorDirectorio != null) buscadorDirectorio.PageSize = 500;

                    foreach (var encontrado in buscador.FindAll())
                    {
                        using (var usuario = encontrado as UserPrincipal)
                        {
                            if (usuario == null || string.IsNullOrWhiteSpace(usuario.SamAccountName) || usuario.Sid == null) continue;

                            var sid = new byte[usuario.Sid.BinaryLength];
                            usuario.Sid.GetBinaryForm(sid, 0);
                            var entrada = (DirectoryEntry)usuario.GetUnderlyingObject();

                            resultado.Add(new UsuarioADModel
                            {
                                ObjectSid = sid,
                                SamAccountName = usuario.SamAccountName,
                                NombreCompleto = usuario.DisplayName ?? usuario.SamAccountName,
                                UserPrincipalName = usuario.UserPrincipalName,
                                Correo = usuario.EmailAddress,
                                Departamento = LeerPropiedad(entrada, "department"),
                                Sucursal = LeerPropiedad(entrada, "physicalDeliveryOfficeName"),
                                Puesto = LeerPropiedad(entrada, "title"),
                                Compania = LeerPropiedad(entrada, "company"),
                                Jefe = LeerPropiedad(entrada, "manager"),
                                Habilitado = usuario.Enabled != false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errorLectura = ex.Message;
            }
            return resultado;
        }

        private static string LeerPropiedad(DirectoryEntry entrada, string nombre)
        {
            return entrada.Properties.Contains(nombre) && entrada.Properties[nombre].Count > 0
                ? entrada.Properties[nombre][0].ToString()
                : null;
        }

        public bool PerteneceAlGrupoAdministradores(string nombreUsuarioWindows)
        {
            string nombreCorto = ObtenerNombreCorto(nombreUsuarioWindows);
            if (string.IsNullOrWhiteSpace(nombreCorto)) return false;

            if (_cachePerteneceAGrupo.TryGetValue(nombreCorto, out var entradaCache) && entradaCache.Expira > DateTime.UtcNow)
            {
                return entradaCache.Resultado;
            }

            string nombreGrupo = Leer("AD_GrupoAdmins");
            if (string.IsNullOrWhiteSpace(nombreGrupo)) nombreGrupo = "ep360admins";

            bool resultado;
            try
            {
                using (var contexto = CrearContexto())
                {
                    var usuario = UserPrincipal.FindByIdentity(contexto, IdentityType.SamAccountName, nombreCorto);
                    if (usuario == null)
                    {
                        resultado = false;
                    }
                    else
                    {
                        var grupo = GroupPrincipal.FindByIdentity(contexto, nombreGrupo);
                        resultado = grupo != null && usuario.IsMemberOf(grupo);
                    }
                }
            }
            catch
            {
                // Si AD no responde se niega el acceso (falla cerrada), pero ese resultado NO se
                // cachea: en cuanto AD vuelva, el siguiente request ya puede entrar.
                return false;
            }

            _cachePerteneceAGrupo[nombreCorto] = new EntradaCachePerteneceAGrupo
            {
                Resultado = resultado,
                Expira = DateTime.UtcNow.Add(DuracionCachePerteneceAGrupo)
            };
            return resultado;
        }
    }
}
